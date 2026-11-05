using AppLauncher.Models;
using Microsoft.Extensions.Options;

namespace AppLauncher.Services;

/// <summary>
/// Arka planda birkaç saniyede bir:
///  - zamanı gelen uygulamaları başlatır,
///  - "sürekli çalışsın" (KeepAlive) uygulamaları kapanmışsa yeniden başlatır.
/// Servis açılışında (StartupDelaySeconds kadar bekledikten sonra) bir kez:
///  - yeniden başlatma sırasında yarıda kalan çalıştırmaları baştan başlatır (ResumeInterrupted),
///  - servis kapalıyken kaçırılan zamanlamaları telafi eder (RunMissed).
/// </summary>
public sealed class SchedulerService : BackgroundService
{
    private static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly AppStore _store;
    private readonly ProcessRunner _runner;
    private readonly RunHistory _history;
    private readonly StateStore _state;
    private readonly ILogger<SchedulerService> _log;
    private readonly TimeZoneInfo _tz;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _startupDelay;

    public SchedulerService(AppStore store, ProcessRunner runner, RunHistory history, StateStore state,
        IOptions<LauncherOptions> options, ILogger<SchedulerService> log)
    {
        _store = store;
        _runner = runner;
        _history = history;
        _state = state;
        _log = log;
        _tz = options.Value.ResolveTimeZone();
        _interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.CheckIntervalSeconds, 2, 60));
        _startupDelay = TimeSpan.FromSeconds(Math.Clamp(options.Value.StartupDelaySeconds, 0, 600));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startedAt = DateTimeOffset.Now;
        var previousTick = _state.LastTickBeforeStartup;
        _log.LogInformation("Zamanlayıcı başladı. Saat dilimi: {Tz}. Son kontrol: {Last}. Başlangıç beklemesi: {Delay} sn",
            _tz.Id, previousTick?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(yok)", _startupDelay.TotalSeconds);

        try
        {
            // Sunucu yeni açıldıysa ağ, veritabanı vb. hazır olsun diye kısa bir süre bekle.
            await Task.Delay(_startupDelay, stoppingToken);

            try { Recover(previousTick, startedAt); }
            catch (Exception ex) { _log.LogError(ex, "Açılış kurtarma adımında hata."); }

            // Bekleme sırasında gelen zamanlamalar normal kontrolde yakalanır (lastCheck = açılış anı).
            var lastCheck = startedAt;
            using var timer = new PeriodicTimer(_interval);
            do
            {
                var now = DateTimeOffset.Now;
                CheckDue(lastCheck, now);
                CheckKeepAlive(now);
                _state.SetLastTick(now);
                lastCheck = now;
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // servis kapanıyor
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _state.SetLastTick(DateTimeOffset.Now, force: true);
        await base.StopAsync(cancellationToken);
        // Çalışan uygulamaları sonlandır ve "yarıda kaldı" olarak işaretle (açılışta yeniden başlatılabilsinler).
        await _runner.StopAllForShutdownAsync(TimeSpan.FromSeconds(15));
    }

    // ------------------------------------------------------------------ açılış kurtarma
    private void Recover(DateTimeOffset? previousTick, DateTimeOffset startedAt)
    {
        var interrupted = _history.TakeRecoverable();

        // Saat geri alınmışsa veya ilk kurulumsa kapalı kalma süresi bilinmez.
        TimeSpan? downtime = previousTick is { } p && p <= startedAt ? startedAt - p : null;
        var started = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Yarıda kalanlar
        foreach (var appId in interrupted.Select(r => r.AppId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var app = _store.Get(appId);
            if (app is null || !app.Enabled || app.KeepAlive || !app.ResumeInterrupted) continue;

            if (downtime is { } d && d > Window(app))
            {
                _log.LogWarning("{App} yarıda kalmıştı ama servis {Hours:0.#} saat kapalı kaldığı için yeniden başlatılmadı (sınır {Max} saat).",
                    app.Id, d.TotalHours, app.RecoveryWindowHours);
                continue;
            }

            _log.LogInformation("{App} yeniden başlatma sırasında yarıda kalmıştı, baştan başlatılıyor.", app.Id);
            if (TryStart(app, RunTrigger.Resume)) started.Add(app.Id);
        }

        // 2) Kaçırılan zamanlamalar
        if (previousTick is not { } from || from >= startedAt) return;

        foreach (var app in _store.GetAll())
        {
            if (!app.Enabled || app.KeepAlive || !app.RunMissed || started.Contains(app.Id)) continue;
            if (app.Schedule is null || app.Schedule.Type == ScheduleType.None) continue;

            try
            {
                var expr = ScheduleHelper.GetExpression(app.Schedule);
                if (expr is null) continue;

                var missed = expr.GetOccurrences(from, startedAt, _tz, fromInclusive: false, toInclusive: true)
                                 .LastOrDefault();
                if (missed == default) continue;

                if (startedAt - missed > Window(app))
                {
                    _log.LogWarning("{App} için {Missed} zamanlaması kaçırıldı ama {Max} saat sınırını aştığı için telafi edilmedi.",
                        app.Id, missed, app.RecoveryWindowHours);
                    continue;
                }

                _log.LogInformation("{App} için kaçırılan {Missed} zamanlaması telafi ediliyor.", app.Id, missed);
                TryStart(app, RunTrigger.CatchUp);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "{App} kaçırılan zamanlama kontrolünde hata.", app.Id);
            }
        }
    }

    private static TimeSpan Window(AppDefinition app) => TimeSpan.FromHours(Math.Clamp(app.RecoveryWindowHours, 1, 720));

    // ------------------------------------------------------------------ zamanlama
    private void CheckDue(DateTimeOffset from, DateTimeOffset to)
    {
        foreach (var app in _store.GetAll())
        {
            if (!app.Enabled || app.KeepAlive || app.Schedule is null || app.Schedule.Type == ScheduleType.None) continue;
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
                TryStart(app, RunTrigger.Schedule);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "{App} zamanlama kontrolünde hata.", app.Id);
            }
        }
    }

    // ------------------------------------------------------------------ sürekli çalışma
    private void CheckKeepAlive(DateTimeOffset now)
    {
        foreach (var app in _store.GetAll())
        {
            if (!app.Enabled || !app.KeepAlive) continue;
            if (_runner.IsRunning(app.Id) || _state.IsPaused(app.Id)) continue;

            try
            {
                var recent = _history.Query(app.Id, 12);
                var last = recent.FirstOrDefault();
                if (last?.EndedAt is { } ended)
                {
                    var wait = RestartDelay(app, recent);
                    if (now < ended + wait) continue;
                    if (wait > TimeSpan.FromSeconds(Math.Max(1, app.KeepAliveDelaySeconds)))
                        _log.LogWarning("{App} art arda hızlı kapanıyor; yeniden başlatma {Wait} sn geciktirildi.", app.Id, (int)wait.TotalSeconds);
                }

                _log.LogInformation("{App} çalışmıyor, sürekli çalışma için başlatılıyor.", app.Id);
                TryStart(app, RunTrigger.KeepAlive);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "{App} sürekli çalışma kontrolünde hata.", app.Id);
            }
        }
    }

    /// <summary>
    /// Uygulama 60 sn'den kısa sürede art arda kapanıyorsa (ör. hatalı yapılandırma) bekleme süresini
    /// her seferinde ikiye katlar, en fazla 5 dk. Bir kez düzgün çalışınca sıfırlanır.
    /// </summary>
    private static TimeSpan RestartDelay(AppDefinition app, List<RunRecord> recent)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Clamp(app.KeepAliveDelaySeconds, 1, 3600));
        var quickExits = recent.TakeWhile(r => r.EndedAt is { } e && e - r.StartedAt < QuickExit
                                               && r.Status != RunStatus.Interrupted).Count();
        if (quickExits <= 1) return baseDelay;
        var factor = Math.Pow(2, Math.Min(quickExits - 1, 10));
        var cap = Math.Max(baseDelay.TotalSeconds, MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(Math.Min(baseDelay.TotalSeconds * factor, cap));
    }

    private bool TryStart(AppDefinition app, RunTrigger trigger)
    {
        try
        {
            _runner.Start(app, trigger);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _log.LogInformation("{App} başlatılmadı: {Reason}", app.Id, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "{App} başlatılamadı.", app.Id);
            return false;
        }
    }
}
