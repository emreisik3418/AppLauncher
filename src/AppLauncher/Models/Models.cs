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

public enum RunTrigger { Manual, Schedule }

public enum RunStatus { Running, Succeeded, Failed, Stopped, TimedOut, Interrupted }

public sealed class RunRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AppId { get; set; } = "";
    public string AppName { get; set; } = "";
    public RunTrigger Trigger { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? ExitCode { get; set; }
    public int? ProcessId { get; set; }
    public string? LogFile { get; set; }
    public string? Error { get; set; }
}
