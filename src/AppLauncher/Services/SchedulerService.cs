using AppLauncher.Models;
using Microsoft.Extensions.Options;

namespace AppLauncher.Services;

/// <summary>
/// Belirli aralıklarla (varsayılan 15 sn) tüm uygulamaların zamanlamasını kontrol eder.
/// Son kontrol ile şimdiki an arasına düşen bir çalıştırma zamanı varsa uygulamayı başlatır.
/// Not: Servis kapalıyken kaçırılan çalıştırmalar sonradan telafi edilmez.
/// </summary>
public sealed class SchedulerService : BackgroundService
{
    private readonly AppStore _store;
    private readonly ProcessRunner _runner;
    private readonly ILogger<SchedulerService> _log;
    private readonly TimeZoneInfo _tz;
    private readonly TimeSpan _interval;

    public SchedulerService(AppStore store, ProcessRunner runner, IOptions<LauncherOptions> options,
        ILogger<SchedulerService> log)
    {
        _store = store;
        _runner = runner;
        _log = log;
        _tz = options.Value.ResolveTimeZone();
        _interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.CheckIntervalSeconds, 5, 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Zamanlayıcı başladı. Saat dilimi: {Tz}", _tz.Id);
        var lastCheck = DateTimeOffset.Now;
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTimeOffset.Now;
                CheckDue(lastCheck, now);
                lastCheck = now;
            }
        }
        catch (OperationCanceledException)
        {
            // servis kapanıyor
        }
    }

    private void CheckDue(DateTimeOffset from, DateTimeOffset to)
    {
        foreach (var app in _store.GetAll())
        {
            if (!app.Enabled || app.Schedule is null || app.Schedule.Type == ScheduleType.None) continue;
            try
            {
                var expr = ScheduleHelper.GetExpression(app.Schedule);
                var next = expr?.GetNextOccurrence(from, _tz);
                if (next is null || next.Value > to) continue;

                if (!app.AllowConcurrentRuns && _runner.IsRunning(app.Id))
                {
                    _log.LogWarning("{App} zamanı geldi ama hâlâ çalışıyor, bu tetikleme atlandı.", app.Id);
                    continue;
                }

                _log.LogInformation("{App} zamanlama ile başlatılıyor ({When}).", app.Id, next.Value);
                _runner.Start(app, RunTrigger.Schedule);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "{App} zamanlama kontrolünde hata.", app.Id);
            }
        }
    }
}
