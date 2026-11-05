using AppLauncher.Models;

namespace AppLauncher.Services;

/// <summary>
/// Sürekli çalışan (KeepAlive) bir uygulama kapandığında ne zaman yeniden başlatılacağını hesaplar.
/// Hem zamanlayıcı hem de arayüz ("şu saatte yeniden başlatılacak") aynı hesabı kullanır.
///
/// Kural:
///  - Normalde kapanıştan KeepAliveDelaySeconds sonra yeniden başlatılır.
///  - Yalnızca son 10 dakika içinde art arda 3'ten fazla kez 60 sn dolmadan kapandıysa (gerçek bir
///    çökme döngüsü) bekleme her seferinde ikiye katlanır, en fazla 2 dakikaya çıkar.
///  - Elle durdurma ve servis kapanışı (yarıda kaldı) bu sayıma girmez.
/// </summary>
public static class KeepAlivePolicy
{
    public static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan CrashLoopWindow = TimeSpan.FromMinutes(10);
    public const int FreeRestarts = 3;
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

    /// <param name="recent">Uygulamanın son kayıtları, en yeni başta.</param>
    public static DateTimeOffset? NextStartAt(AppDefinition app, IReadOnlyList<RunRecord> recent, DateTimeOffset now)
    {
        // Çağıran, uygulamanın şu an çalışmadığını kontrol etmiş olmalı.
        var last = recent.Count > 0 ? recent[0] : null;
        if (last is null) return now;
        var ended = last.EndedAt ?? now;
        return ended + Delay(app, recent, now);
    }

    public static TimeSpan Delay(AppDefinition app, IReadOnlyList<RunRecord> recent, DateTimeOffset now)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Clamp(app.KeepAliveDelaySeconds, 1, 3600));

        var quickExits = recent
            .TakeWhile(r => r.EndedAt is { } e
                            && now - e <= CrashLoopWindow
                            && e - r.StartedAt < QuickExit
                            && r.Status is not (RunStatus.Interrupted or RunStatus.Stopped or RunStatus.Running))
            .Count();

        if (quickExits <= FreeRestarts) return baseDelay;

        var factor = Math.Pow(2, Math.Min(quickExits - FreeRestarts, 10));
        var cap = Math.Max(baseDelay.TotalSeconds, MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(Math.Min(baseDelay.TotalSeconds * factor, cap));
    }
}
