using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AppLauncher.Security;

/// <summary>appsettings içindeki "Launcher:Auth" bölümü.</summary>
public sealed class AuthOptions
{
    /// <summary>false ise giriş kontrolü yapılmaz (yalnızca localhost'ta test için). Değişiklik için yeniden başlatma gerekir.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Girebilecek Windows kullanıcıları (sicil). "A12345" yazılırsa hangi domain'den gelirse gelsin eşleşir,
    /// "DOMAIN\A12345" yazılırsa yalnızca o domain'deki kullanıcı eşleşir. Büyük/küçük harf duyarsızdır.
    /// </summary>
    public List<string> AllowedUsers { get; set; } = new();

    /// <summary>İsteğe bağlı: bu Windows/AD gruplarının üyeleri de girebilir. Örn. "DOMAIN\AppLauncherYoneticileri".</summary>
    public List<string> AllowedGroups { get; set; } = new();

    /// <summary>Sicil + şifre ile girişte kullanılacak domain. Boşsa makinenin domain'i otomatik bulunur.</summary>
    public string? Domain { get; set; }

    /// <summary>Login sayfasında sicil + şifre formu gösterilsin mi.</summary>
    public bool AllowPasswordLogin { get; set; } = true;

    /// <summary>Login sayfasında "Windows oturumumla giriş yap" düğmesi gösterilsin mi.</summary>
    public bool AllowWindowsLogin { get; set; } = true;

    /// <summary>İşlem yapılmazsa oturumun düşeceği süre (dakika).</summary>
    public int SessionMinutes { get; set; } = 480;

    /// <summary>Aynı sicil için bu kadar hatalı denemeden sonra giriş geçici olarak engellenir (AD hesabının kilitlenmesini önler).</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 10;
}

public sealed class AllowedUserRequirement : IAuthorizationRequirement { }

/// <summary>Kullanıcının izinli listede olup olmadığını her istekte güncel config'e göre kontrol eder.</summary>
public sealed class AllowedUserHandler : AuthorizationHandler<AllowedUserRequirement>
{
    private readonly IOptionsMonitor<LauncherOptions> _options;

    public AllowedUserHandler(IOptionsMonitor<LauncherOptions> options) => _options = options;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AllowedUserRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && IsAllowed(context.User, _options.CurrentValue.Auth))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Hem Windows (Negotiate) kimliği hem de cookie kimliği için çalışır. Cookie'de grup üyelikleri
    /// giriş anında Role claim'i olarak saklanır.
    /// </summary>
    public static bool IsAllowed(ClaimsPrincipal user, AuthOptions auth)
    {
        var fullName = user.Identity?.Name ?? "";
        if (fullName.Length == 0) return false;
        var slash = fullName.LastIndexOf('\\');
        var shortName = slash >= 0 ? fullName[(slash + 1)..] : fullName;

        foreach (var entry in auth.AllowedUsers)
        {
            var v = entry?.Trim();
            if (string.IsNullOrEmpty(v)) continue;
            var match = v.Contains('\\')
                ? string.Equals(v, fullName, StringComparison.OrdinalIgnoreCase)
                : string.Equals(v, shortName, StringComparison.OrdinalIgnoreCase);
            if (match) return true;
        }

        return MatchingGroups(user, auth).Any();
    }

    public static IEnumerable<string> MatchingGroups(ClaimsPrincipal user, AuthOptions auth)
    {
        foreach (var group in auth.AllowedGroups)
        {
            var g = group?.Trim();
            if (string.IsNullOrEmpty(g)) continue;
            bool inRole;
            try { inRole = user.IsInRole(g); }
            catch { inRole = false; } // grup çözümlenemezse yoksay
            if (inRole) yield return g;
        }
    }
}

/// <summary>
/// Yetkisi kaldırılmış (listeden çıkarılmış) kullanıcının oturumunu kapatıp login sayfasına yönlendirir.
/// API çağrılarında 403 JSON döner.
/// </summary>
public sealed class ForbiddenResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();
    private readonly ILogger<ForbiddenResultHandler> _log;

    public ForbiddenResultHandler(ILogger<ForbiddenResultHandler> log) => _log = log;

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult result)
    {
        if (result.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var name = context.User.Identity.Name ?? "?";
            _log.LogWarning("Yetkisiz erişim: {User} {Path}", name, context.Request.Path);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = $"'{name}' kullanıcısının erişim yetkisi yok." });
                return;
            }

            context.Response.Redirect("/login?error=forbidden&user=" + Uri.EscapeDataString(name));
            return;
        }

        await _default.HandleAsync(next, context, policy, result);
    }
}

public static class WindowsAuthExtensions
{
    public const string CsrfHeader = "X-Launcher";

    public static void AddLauncherAuth(this IServiceCollection services, IConfiguration config, string dataDirectory)
    {
        var sessionMinutes = Math.Clamp(config.GetValue("Launcher:Auth:SessionMinutes", 480), 5, 7 * 24 * 60);

        // Oturum cookie'lerini şifreleyen anahtarlar data\keys altında saklanır; servis yeniden
        // başlasa da kullanıcıların oturumu düşmez.
        var dp = services.AddDataProtection()
            .SetApplicationName("AppLauncher")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
        if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi(protectToLocalMachine: true);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = ".AppLauncher.Auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(sessionMinutes);
                o.SlidingExpiration = true;
                o.LoginPath = "/login";
                o.AccessDeniedPath = "/login";
                o.Events.OnRedirectToLogin = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = 401;
                    else ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = 403;
                    else ctx.Response.Redirect("/login?error=forbidden");
                    return Task.CompletedTask;
                };
            })
            .AddNegotiate(); // "Windows oturumumla giriş" düğmesi için

        services.AddSingleton<IAuthorizationHandler, AllowedUserHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenResultHandler>();
        services.AddAuthorization(o =>
        {
            // Login sayfası ve /api/auth dışındaki her şey (arayüz dahil) bu politikadan geçer.
            o.FallbackPolicy = new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new AllowedUserRequirement())
                .Build();
        });
    }

    /// <summary>
    /// Başka bir sitenin, tarayıcıdaki oturumu kullanarak API'yi tetiklemesini (CSRF) engeller:
    /// veri değiştiren her API isteği özel bir başlık taşımalıdır. Tarayıcılar bu başlığı başka
    /// sitelerden izinsiz gönderemez.
    /// </summary>
    public static void UseLauncherCsrfGuard(this WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")
                && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
                && !ctx.Request.Headers.ContainsKey(CsrfHeader))
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new { error = "Geçersiz istek (güvenlik başlığı eksik)." });
                return;
            }
            await next();
        });
    }
}
