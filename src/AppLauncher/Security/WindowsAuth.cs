using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace AppLauncher.Security;

/// <summary>appsettings içindeki "Launcher:Auth" bölümü.</summary>
public sealed class AuthOptions
{
    /// <summary>false ise giriş kontrolü yapılmaz (yalnızca localhost'ta test için).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Girebilecek Windows kullanıcıları (sicil). "A12345" yazılırsa hangi domain'den gelirse gelsin eşleşir,
    /// "DOMAIN\A12345" yazılırsa yalnızca o domain'deki kullanıcı eşleşir. Büyük/küçük harf duyarsızdır.
    /// </summary>
    public List<string> AllowedUsers { get; set; } = new();

    /// <summary>İsteğe bağlı: bu Windows/AD gruplarının üyeleri de girebilir. Örn. "DOMAIN\AppLauncherYoneticileri".</summary>
    public List<string> AllowedGroups { get; set; } = new();
}

public sealed class AllowedUserRequirement : IAuthorizationRequirement { }

/// <summary>Kullanıcının izinli listede olup olmadığını kontrol eder. Liste her istekte güncel config'den okunur.</summary>
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

        foreach (var group in auth.AllowedGroups)
        {
            var g = group?.Trim();
            if (string.IsNullOrEmpty(g)) continue;
            try { if (user.IsInRole(g)) return true; }
            catch { /* grup çözümlenemezse yoksay */ }
        }
        return false;
    }
}

/// <summary>Yetkisi olmayan kullanıcıya kim olduğunu ve ne yapması gerektiğini söyleyen 403 sayfası.</summary>
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
            _log.LogWarning("Yetkisiz erişim denemesi: {User} {Path}", name, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                await context.Response.WriteAsJsonAsync(new { error = $"'{name}' kullanıcısının bu uygulamaya erişim yetkisi yok." });
                return;
            }

            var n = WebUtility.HtmlEncode(name);
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync($$"""
                <!doctype html><html lang="tr"><head><meta charset="utf-8"><title>Erişim yok</title>
                <style>body{font:15px/1.5 "Segoe UI",sans-serif;max-width:640px;margin:80px auto;padding:0 16px;color:#1d2330}
                code{background:#f0f2f5;padding:2px 6px;border-radius:4px}</style></head><body>
                <h2>Erişim yetkiniz yok</h2>
                <p>Windows kullanıcınız: <code>{{n}}</code></p>
                <p>Erişim için yöneticinin bu sicili <code>appsettings.Local.json</code> dosyasındaki
                <code>Launcher:Auth:AllowedUsers</code> listesine eklemesi gerekir.</p>
                </body></html>
                """);
            return;
        }

        await _default.HandleAsync(next, context, policy, result);
    }
}

public static class WindowsAuthExtensions
{
    public static void AddLauncherWindowsAuth(this IServiceCollection services)
    {
        services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
        services.AddSingleton<IAuthorizationHandler, AllowedUserHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenResultHandler>();
        services.AddAuthorization(o =>
        {
            // Hiçbir endpoint/dosya için ayrıca [Authorize] yazmaya gerek yok: her şey bu politikadan geçer.
            o.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new AllowedUserRequirement())
                .Build();
        });
    }
}
