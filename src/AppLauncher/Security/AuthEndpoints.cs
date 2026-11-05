using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;

namespace AppLauncher.Security;

public sealed record LoginRequest(string? Username, string? Password, string? ReturnUrl);

public static class AuthEndpoints
{
    public const string MethodClaim = "launcher:auth_method";
    public const string LoginAtClaim = "launcher:login_at";

    public static void MapLauncherAuth(this WebApplication app, bool authEnabled)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AppLauncher.Auth");

        // ---- Login sayfası ----
        app.MapGet("/login", (IWebHostEnvironment env, HttpContext ctx) =>
        {
            if (!authEnabled) return Results.Redirect("/");
            ctx.Response.Headers.CacheControl = "no-store";
            var file = env.WebRootFileProvider.GetFileInfo("login.html");
            return Results.File(file.PhysicalPath!, "text/html; charset=utf-8");
        }).AllowAnonymous();

        var auth = app.MapGroup("/api/auth").AllowAnonymous();

        // Login sayfasının ihtiyaç duyduğu bilgiler
        auth.MapGet("/info", (HttpContext ctx, IOptionsMonitor<LauncherOptions> opt) =>
        {
            var o = opt.CurrentValue.Auth;
            var user = ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null;
            var allowed = user is not null && AllowedUserHandler.IsAllowed(ctx.User, o);
            var domain = WindowsLogon.ResolveDomain(o.Domain);
            return new
            {
                authEnabled,
                passwordLogin = o.AllowPasswordLogin,
                windowsLogin = o.AllowWindowsLogin,
                domain = domain == "." ? null : domain,
                machine = Environment.MachineName,
                user = allowed ? user : null
            };
        });

        // ---- Sicil + şifre ile giriş ----
        auth.MapPost("/login", async (LoginRequest req, HttpContext ctx, IOptionsMonitor<LauncherOptions> opt,
            [FromServices] LoginThrottle throttle) =>
        {
            var o = opt.CurrentValue.Auth;
            if (!authEnabled || !o.AllowPasswordLogin)
                return Results.BadRequest(new { error = "Şifre ile giriş kapalı." });

            var username = (req.Username ?? "").Trim();
            var password = req.Password ?? "";
            if (username.Length == 0 || password.Length == 0)
                return Results.BadRequest(new { error = "Sicil ve şifre girin." });
            if (!OperatingSystem.IsWindows())
                return Results.BadRequest(new { error = "Şifre ile giriş yalnızca Windows sunucuda çalışır." });

            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            var wait = throttle.GetLockout(username, ip, o);
            if (wait is not null)
            {
                log.LogWarning("Giriş engellendi (çok fazla deneme): {User} {Ip}", username, ip);
                return Results.Json(new { error = $"Çok fazla hatalı deneme. {Math.Ceiling(wait.Value.TotalMinutes)} dakika sonra tekrar deneyin." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var result = WindowsLogon.Validate(username, password, o.Domain, o.AllowedGroups);
            if (!result.Success)
            {
                throttle.RecordFailure(username, ip);
                log.LogWarning("Hatalı giriş: {User} {Ip} ({Code})", username, ip, result.ErrorCode);
                await Task.Delay(Random.Shared.Next(400, 800)); // deneme hızını yavaşlat
                return Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }

            throttle.Reset(username, ip);
            var principal = result.Principal!;
            if (!AllowedUserHandler.IsAllowed(principal, o))
            {
                log.LogWarning("Yetkisiz kullanıcı giriş denedi: {User} {Ip}", result.Name, ip);
                return Results.Json(new { error = $"'{result.Name}' kullanıcısının bu uygulamaya erişim yetkisi yok.", forbidden = true },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await SignInAsync(ctx, result.Name!, "Şifre", AllowedUserHandler.MatchingGroups(principal, o).ToList());
            log.LogInformation("Giriş yapıldı (şifre): {User} {Ip}", result.Name, ip);
            return Results.Ok(new { redirect = SafeReturnUrl(req.ReturnUrl) });
        });

        // ---- Çıkış ----
        auth.MapPost("/logout", async (HttpContext ctx) =>
        {
            if (authEnabled)
            {
                log.LogInformation("Çıkış yapıldı: {User}", ctx.User.Identity?.Name);
                await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
            return Results.Ok();
        });

        // ---- Windows oturumu ile giriş (Kerberos/NTLM) ----
        app.MapGet("/auth/windows", async (HttpContext ctx, IOptionsMonitor<LauncherOptions> opt) =>
        {
            var o = opt.CurrentValue.Auth;
            if (!authEnabled) return Results.Redirect("/");
            if (!o.AllowWindowsLogin) return Results.Redirect("/login");

            var returnUrl = SafeReturnUrl(ctx.Request.Query["returnUrl"]);
            var result = await ctx.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
            if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
            {
                // Tarayıcıdan Windows kimliğini iste. Tarayıcı veremezse (iptal vb.) bu sayfa görünür.
                await ctx.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(
                    "<!doctype html><meta charset=utf-8><meta http-equiv=refresh content=\"0;url=/login?error=windows\">" +
                    "<a href=\"/login?error=windows\">Giriş sayfasına dön</a>");
                return Results.Empty;
            }

            var principal = result.Principal;
            var name = principal.Identity!.Name ?? "?";
            if (!AllowedUserHandler.IsAllowed(principal, o))
            {
                log.LogWarning("Yetkisiz kullanıcı Windows ile giriş denedi: {User}", name);
                return Results.Redirect("/login?error=forbidden&user=" + Uri.EscapeDataString(name));
            }

            await SignInAsync(ctx, name, "Windows", AllowedUserHandler.MatchingGroups(principal, o).ToList());
            log.LogInformation("Giriş yapıldı (Windows): {User}", name);
            return Results.Redirect(returnUrl);
        }).AllowAnonymous();
    }

    private static Task SignInAsync(HttpContext ctx, string name, string method, List<string> groups)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, name),
            new(MethodClaim, method),
            new(LoginAtClaim, DateTimeOffset.Now.ToString("o"))
        };
        claims.AddRange(groups.Select(g => new Claim(ClaimTypes.Role, g)));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
    }

    /// <summary>Yalnızca bu sitedeki adreslere yönlendir (açık yönlendirme açığını önler).</summary>
    public static string SafeReturnUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "/";
        if (!url.StartsWith('/') || url.StartsWith("//") || url.StartsWith("/\\")) return "/";
        if (url.StartsWith("/login", StringComparison.OrdinalIgnoreCase)) return "/";
        return url;
    }
}

/// <summary>Sicil + şifreyi Windows/AD'ye karşı doğrular (LogonUser).</summary>
public static class WindowsLogon
{
    public sealed record Result(bool Success, string? Name, ClaimsPrincipal? Principal, int ErrorCode, string Message);

    private const int LOGON32_LOGON_NETWORK = 3;
    private const int LOGON32_PROVIDER_DEFAULT = 0;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LogonUserW")]
    private static extern bool LogonUser(string userName, string? domain, string password,
        int logonType, int logonProvider, out SafeAccessTokenHandle token);

    public static string ResolveDomain(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        var d = Environment.UserDomainName;
        if (!string.IsNullOrEmpty(d)
            && !d.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            && !d.Equals("NT AUTHORITY", StringComparison.OrdinalIgnoreCase)
            && !d.Equals("WORKGROUP", StringComparison.OrdinalIgnoreCase))
            return d;

        try
        {
            var dns = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            if (!string.IsNullOrEmpty(dns)) return dns;
        }
        catch { /* yoksay */ }

        return "."; // domain yok: yerel hesaplar
    }

    [SupportedOSPlatform("windows")]
    public static Result Validate(string username, string password, string? configuredDomain,
        IEnumerable<string> groupsToCheck)
    {
        string user = username;
        string? domain;
        var slash = username.IndexOf('\\');
        if (slash > 0)
        {
            domain = username[..slash];
            user = username[(slash + 1)..];
        }
        else if (username.Contains('@'))
        {
            domain = null; // UPN biçimi: kullanici@sirket.local
        }
        else
        {
            domain = ResolveDomain(configuredDomain);
        }

        if (!LogonUser(user, domain, password, LOGON32_LOGON_NETWORK, LOGON32_PROVIDER_DEFAULT, out var token))
        {
            var code = Marshal.GetLastWin32Error();
            token.Dispose();
            return new Result(false, null, null, code, ErrorMessage(code));
        }

        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            // Grup üyelikleri WindowsIdentity açıkken kontrol edilir ve Role claim'i olarak kopyalanır.
            var windows = new WindowsPrincipal(identity);
            var claims = new List<Claim> { new(ClaimTypes.Name, identity.Name) };
            foreach (var g in groupsToCheck)
            {
                var group = g?.Trim();
                if (string.IsNullOrEmpty(group)) continue;
                try { if (windows.IsInRole(group)) claims.Add(new Claim(ClaimTypes.Role, group)); }
                catch { /* grup çözümlenemezse yoksay */ }
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Windows"));
            return new Result(true, identity.Name, principal, 0, "");
        }
    }

    private static string ErrorMessage(int code) => code switch
    {
        1326 => "Sicil veya şifre hatalı.",
        1909 => "Hesabınız kilitlenmiş. BT ile görüşün.",
        1330 => "Şifrenizin süresi dolmuş. Önce Windows'ta şifrenizi değiştirin.",
        1907 => "Şifrenizi değiştirmeniz gerekiyor. Önce Windows'ta şifrenizi değiştirin.",
        1331 => "Hesabınız devre dışı.",
        1793 => "Hesabınızın süresi dolmuş.",
        1327 or 1328 => "Hesabınız için bu saatte / bu makineden oturum açma kısıtlı.",
        1385 => "Hesabınızın bu sunucuya ağ üzerinden oturum açma izni yok.",
        1311 or 1355 => "Domain'e ulaşılamıyor. Sunucunun ağ/domain bağlantısını kontrol edin.",
        _ => $"Giriş yapılamadı ({new Win32Exception(code).Message})."
    };
}

/// <summary>
/// Hatalı giriş denemelerini sayar. Aynı sicil için MaxFailedAttempts, aynı IP için bunun 4 katı hatalı
/// denemeden sonra LockoutMinutes boyunca giriş reddedilir. Böylece AD hesabı kilitlenmeden önce durdurulur.
/// </summary>
public sealed class LoginThrottle
{
    private sealed class Entry
    {
        public int Count;
        public DateTimeOffset WindowStart;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan? GetLockout(string username, string ip, AuthOptions o)
    {
        var window = TimeSpan.FromMinutes(Math.Max(1, o.LockoutMinutes));
        var max = Math.Max(1, o.MaxFailedAttempts);
        var w1 = Check("u:" + Normalize(username), max, window);
        var w2 = Check("ip:" + ip, max * 4, window);
        return w1 ?? w2;
    }

    public void RecordFailure(string username, string ip)
    {
        Bump("u:" + Normalize(username));
        Bump("ip:" + ip);
        if (_entries.Count > 10_000) Cleanup();
    }

    public void Reset(string username, string ip)
    {
        _entries.TryRemove("u:" + Normalize(username), out _);
    }

    private TimeSpan? Check(string key, int max, TimeSpan window)
    {
        if (!_entries.TryGetValue(key, out var e)) return null;
        lock (e)
        {
            var elapsed = DateTimeOffset.UtcNow - e.WindowStart;
            if (elapsed >= window) { _entries.TryRemove(key, out _); return null; }
            return e.Count >= max ? window - elapsed : null;
        }
    }

    private void Bump(string key)
    {
        var e = _entries.GetOrAdd(key, _ => new Entry { WindowStart = DateTimeOffset.UtcNow });
        lock (e) e.Count++;
    }

    private void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        foreach (var (k, v) in _entries)
            if (v.WindowStart < cutoff) _entries.TryRemove(k, out _);
    }

    /// <summary>"SIRKET\\A12345", "a12345@sirket.local" ve "A12345" aynı sicil sayılır.</summary>
    private static string Normalize(string username)
    {
        var u = username.Trim();
        var slash = u.LastIndexOf('\\');
        if (slash >= 0) u = u[(slash + 1)..];
        var at = u.IndexOf('@');
        if (at > 0) u = u[..at];
        return u.ToLowerInvariant();
    }
}
