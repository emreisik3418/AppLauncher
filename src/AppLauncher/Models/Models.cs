namespace AppLauncher.Models;

public sealed class AppsFile
{
    public List<AppDefinition> Apps { get; set; } = new();
}

public sealed class AppDefinition
{
    /// <summary>Benzersiz kısa ad (harf, rakam, - _ .)</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>.exe, .bat, .cmd, .ps1, .dll (dotnet ile), .py veya başka bir çalıştırılabilir.</summary>
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string? WorkingDirectory { get; set; }

    /// <summary>false ise zamanlama devre dışı (elle çalıştırma yine yapılabilir).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>0 = süre sınırı yok. Aşılırsa süreç ağacı sonlandırılır.</summary>
    public int TimeoutMinutes { get; set; }

    /// <summary>true ise önceki çalıştırma bitmeden yenisi başlatılabilir.</summary>
    public bool AllowConcurrentRuns { get; set; }

    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    // ---- Süreklilik / yeniden başlatma ----

    /// <summary>
    /// true ise uygulama hep çalışır durumda tutulur: servis açılınca başlatılır, kapanır/çökerse
    /// KeepAliveDelaySeconds sonra yeniden başlatılır. Elle "Durdur" denirse tekrar "Çalıştır" denene kadar beklemeye alınır.
    /// Zamanlama ile birlikte kullanılamaz.
    /// </summary>
    public bool KeepAlive { get; set; }

    /// <summary>KeepAlive uygulaması kapandıktan sonra yeniden başlatmadan önce beklenecek süre (sn).</summary>
    public int KeepAliveDelaySeconds { get; set; } = 10;

    /// <summary>Sunucu/servis yeniden başlarken yarıda kalan çalıştırma, servis açılınca baştan bir kez daha çalıştırılır.</summary>
    public bool ResumeInterrupted { get; set; }

    /// <summary>Servis kapalıyken gelen zamanlama, servis açılınca bir kez çalıştırılır.</summary>
    public bool RunMissed { get; set; }

    // ---- Hata algılama ----

    /// <summary>false ise bu uygulamanın çıktısında hata satırı aranmaz.</summary>
    public bool DetectErrors { get; set; } = true;

    /// <summary>null: genel ayar (Launcher:ErrorDetection:StdErrIsError) kullanılır.</summary>
    public bool? StdErrIsError { get; set; }

    /// <summary>Genel kalıplara ek olarak hata sayılacak satırlar (regex).</summary>
    public List<string> ErrorPatterns { get; set; } = new();

    /// <summary>Bu kalıplara uyan satırlar hata sayılmaz (regex), ör. "0 error".</summary>
    public List<string> IgnoreErrorPatterns { get; set; } = new();

    /// <summary>ResumeInterrupted / RunMissed için sınır: bundan daha eski kesinti/kaçırmalar telafi edilmez (saat).</summary>
    public int RecoveryWindowHours { get; set; } = 6;

    public ScheduleDefinition? Schedule { get; set; }
}

public enum ScheduleType
{
    None,
    Daily,           // her gün HH:mm
    Weekly,          // seçili günlerde HH:mm
    Monthly,         // her ayın N. günü HH:mm
    MonthlyLastDay,  // her ayın son günü HH:mm
    MonthlyWeekday,  // her ayın 1./2./3./4./son X günü HH:mm
    Cron             // serbest cron ifadesi
}

public sealed class ScheduleDefinition
{
    public ScheduleType Type { get; set; } = ScheduleType.None;

    /// <summary>"HH:mm" (24 saat)</summary>
    public string Time { get; set; } = "00:00";

    /// <summary>Weekly için.</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    /// <summary>Monthly için 1-31.</summary>
    public int DayOfMonth { get; set; } = 1;

    /// <summary>MonthlyWeekday için: "1","2","3","4","5" veya "Last".</summary>
    public string Week { get; set; } = "Last";

    /// <summary>MonthlyWeekday için.</summary>
    public DayOfWeek DayOfWeek { get; set; } = System.DayOfWeek.Monday;

    /// <summary>Cron için: 5 alan (dk saat gün ay haftagünü) veya 6 alan (sn ile).</summary>
    public string? Cron { get; set; }
}

public enum RunTrigger
{
    Manual,     // arayüzden elle
    Schedule,   // zamanlama
    KeepAlive,  // sürekli çalışma (kapandığı için yeniden)
    Resume,     // yeniden başlatmada yarıda kaldığı için baştan
    CatchUp     // servis kapalıyken kaçırılan zamanlamanın telafisi
}

public enum RunStatus
{
    Running,
    Succeeded,
    Failed,
    Stopped,
    TimedOut,
    Interrupted,
    CompletedWithErrors // çıkış kodu 0 ama çıktıda hata satırları var
}

public sealed class ErrorLine
{
    public DateTimeOffset At { get; set; }
    public string Line { get; set; } = "";
}

public sealed class RunRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AppId { get; set; } = "";
    public string AppName { get; set; } = "";
    public RunTrigger Trigger { get; set; }
    /// <summary>Elle başlatıldıysa başlatan Windows kullanıcısı.</summary>
    public string? StartedBy { get; set; }
    public string? StoppedBy { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? ExitCode { get; set; }
    public int? ProcessId { get; set; }
    public string? LogFile { get; set; }
    public string? Error { get; set; }

    // ---- Çıktıda görülen hatalar ----
    public int ErrorCount { get; set; }
    public DateTimeOffset? FirstErrorAt { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }

    /// <summary>Son birkaç hata satırı (en yeni sonda).</summary>
    public List<ErrorLine>? RecentErrors { get; set; }

    /// <summary>"Gördüm" denildiği andaki hata sayısı; kartta yalnızca bundan sonrakiler uyarı olarak gösterilir.</summary>
    public int AcknowledgedErrors { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>Sunucu/servis kapanırken yarıda kaldı ve henüz açılışta değerlendirilmedi.</summary>
    public bool Recoverable { get; set; }
}
