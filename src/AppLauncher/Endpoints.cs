using System.Security.Claims;
using AppLauncher.Models;
using AppLauncher.Services;
using Microsoft.Extensions.Options;

namespace AppLauncher;

public static class Endpoints
{
    private static string? UserName(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.Identity.Name : null;

    public static void MapLauncherApi(this WebApplication app, bool authEnabled)
    {
        var api = app.MapGroup("/api");
        var audit = app.Logger;

        api.MapGet("/me", (ClaimsPrincipal user) => new
        {
            authEnabled,
            name = UserName(user),
            method = user.FindFirst(AppLauncher.Security.AuthEndpoints.MethodClaim)?.Value,
            loginAt = user.FindFirst(AppLauncher.Security.AuthEndpoints.LoginAtClaim)?.Value
        });

        api.MapGet("/status", (AppStore store, IOptions<LauncherOptions> opt) => new
        {
            configFile = store.FilePath,
            configError = store.LastLoadError,
            configLoadedAt = store.LastLoadedAt,
            timeZone = opt.Value.ResolveTimeZone().Id,
            serverTime = DateTimeOffset.Now
        });

        // ---- Uygulamalar ----
        api.MapGet("/apps", (AppStore store, ProcessRunner runner, RunHistory history, StateStore state, IOptions<LauncherOptions> opt) =>
        {
            var tz = opt.Value.ResolveTimeZone();
            return store.GetAll().Select(a => BuildView(a, runner, history, state, tz));
        });

        api.MapGet("/apps/{id}", (string id, AppStore store) =>
            store.Get(id) is { } a ? Results.Ok(a) : Results.NotFound());

        api.MapPost("/apps", (AppDefinition def, AppStore store, ClaimsPrincipal user) =>
        {
            var errors = store.Add(def);
            if (errors.Count == 0) audit.LogInformation("{User} uygulama ekledi: {App}", UserName(user), def.Id);
            return errors.Count == 0 ? Results.Ok(def) : Results.BadRequest(new { errors });
        });

        api.MapPut("/apps/{id}", (string id, AppDefinition def, AppStore store, ClaimsPrincipal user) =>
        {
            var errors = store.Update(id, def);
            if (errors.Count == 0) audit.LogInformation("{User} uygulamayı güncelledi: {App}", UserName(user), id);
            return errors.Count == 0 ? Results.Ok(def) : Results.BadRequest(new { errors });
        });

        api.MapDelete("/apps/{id}", (string id, AppStore store, ClaimsPrincipal user) =>
        {
            if (!store.Delete(id)) return Results.NotFound();
            audit.LogInformation("{User} uygulamayı sildi: {App}", UserName(user), id);
            return Results.NoContent();
        });

        api.MapPost("/apps/{id}/run", (string id, AppStore store, ProcessRunner runner, StateStore state, ClaimsPrincipal user) =>
        {
            var def = store.Get(id);
            if (def is null) return Results.NotFound();
            state.Resume(def.Id); // sürekli çalışma beklemedeyse tekrar devreye al
            try
            {
                return Results.Ok(runner.Start(def, RunTrigger.Manual, UserName(user)));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // ---- Çalıştırmalar ----
        api.MapGet("/runs", (string? appId, int? take, RunHistory history) =>
            history.Query(appId, take ?? 50));

        api.MapPost("/runs/{runId}/stop", (string runId, ProcessRunner runner, RunHistory history, AppStore store,
            StateStore state, ClaimsPrincipal user) =>
        {
            // Sürekli çalışan uygulama elle durdurulursa, tekrar "Çalıştır" denene kadar yeniden başlatılmaz.
            var rec = history.Get(runId);
            if (rec is not null && store.Get(rec.AppId)?.KeepAlive == true) state.Pause(rec.AppId);
            return runner.Stop(runId, UserName(user)) ? Results.Ok() : Results.NotFound(new { error = "Çalışan bir süreç bulunamadı." });
        });

        api.MapGet("/runs/{runId}/log", (string runId, RunHistory history) =>
        {
            var rec = history.Get(runId);
            if (rec?.LogFile is null || !File.Exists(rec.LogFile)) return Results.Text("(log bulunamadı)");

            const long maxBytes = 512 * 1024; // son 512 KB
            using var fs = new FileStream(rec.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var truncated = fs.Length > maxBytes;
            if (truncated) fs.Seek(-maxBytes, SeekOrigin.End);
            using var sr = new StreamReader(fs);
            var text = sr.ReadToEnd();
            if (truncated) text = "... (log kırpıldı, yalnızca son 512 KB gösteriliyor)\n" + text;
            return Results.Text(text, "text/plain; charset=utf-8");
        });

        // ---- Ham yapılandırma (apps.json) ----
        api.MapGet("/config", (AppStore store) => Results.Text(store.ReadRaw(), "application/json; charset=utf-8"));

        api.MapPut("/config", async (HttpRequest request, AppStore store, ClaimsPrincipal user) =>
        {
            using var reader = new StreamReader(request.Body);
            var json = await reader.ReadToEndAsync();
            var errors = store.SaveRaw(json);
            if (errors.Count == 0) audit.LogInformation("{User} apps.json dosyasını kaydetti.", UserName(user));
            return errors.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors });
        });

        // ---- Zamanlama önizleme ----
        api.MapPost("/schedule/preview", (ScheduleDefinition schedule, IOptions<LauncherOptions> opt) =>
        {
            try
            {
                var expr = ScheduleHelper.GetExpression(schedule);
                var tz = opt.Value.ResolveTimeZone();
                return Results.Ok(new
                {
                    cron = ScheduleHelper.ToCron(schedule),
                    description = ScheduleHelper.Describe(schedule),
                    next = expr is null
                        ? new List<DateTimeOffset>()
                        : ScheduleHelper.NextOccurrences(expr, tz, DateTimeOffset.Now, 5)
                });
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static object BuildView(AppDefinition a, ProcessRunner runner, RunHistory history, StateStore state, TimeZoneInfo tz)
    {
        string? cron = null;
        DateTimeOffset? nextRun = null;
        string? scheduleError = null;
        try
        {
            cron = ScheduleHelper.ToCron(a.Schedule);
            var expr = ScheduleHelper.GetExpression(a.Schedule);
            if (expr is not null && a.Enabled && !a.KeepAlive) nextRun = expr.GetNextOccurrence(DateTimeOffset.Now, tz);
        }
        catch (FormatException ex)
        {
            scheduleError = ex.Message;
        }

        return new
        {
            app = a,
            cron,
            scheduleText = a.KeepAlive ? "Sürekli çalışır (kapanırsa yeniden başlatılır)" : ScheduleHelper.Describe(a.Schedule),
            keepAlivePaused = a.KeepAlive && state.IsPaused(a.Id),
            keepAliveNextStart = a.KeepAlive && a.Enabled && !state.IsPaused(a.Id) && !runner.IsRunning(a.Id)
                ? KeepAlivePolicy.NextStartAt(a, history.Query(a.Id, 20), DateTimeOffset.Now)
                : null,
            scheduleError,
            nextRun,
            activeRuns = runner.ActiveRuns(a.Id),
            lastRun = history.Last(a.Id)
        };
    }
}
