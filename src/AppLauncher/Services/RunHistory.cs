using System.Text.Json;
using AppLauncher.Models;
using Microsoft.Extensions.Options;

namespace AppLauncher.Services;

/// <summary>Çalıştırma geçmişini data/history.json içinde tutar.</summary>
public sealed class RunHistory
{
    private readonly object _lock = new();
    private readonly List<RunRecord> _runs;
    private readonly string _file;
    private readonly int _limit;
    private readonly ILogger<RunHistory> _log;

    public string LogsDirectory { get; }

    public RunHistory(IOptions<LauncherOptions> options, IHostEnvironment env, ILogger<RunHistory> log)
    {
        _log = log;
        var dir = options.Value.ResolveDataDirectory(env.ContentRootPath);
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "history.json");
        LogsDirectory = Path.Combine(dir, "logs");
        Directory.CreateDirectory(LogsDirectory);
        _limit = Math.Max(10, options.Value.HistoryLimit);

        _runs = new List<RunRecord>();
        if (File.Exists(_file))
        {
            try
            {
                _runs = JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(_file), JsonDefaults.Options) ?? new();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "history.json okunamadı, geçmiş sıfırlanıyor.");
            }
        }

        // Servis kapanırken yarıda kalan çalıştırmalar
        foreach (var r in _runs.Where(r => r.Status == RunStatus.Running))
        {
            r.Status = RunStatus.Interrupted;
            r.EndedAt ??= DateTimeOffset.Now;
            r.Error ??= "Servis yeniden başlatıldığı için takip edilemedi.";
        }
        Save();
    }

    public void Add(RunRecord record)
    {
        lock (_lock)
        {
            _runs.Add(record);
            Trim();
        }
        Save();
    }

    /// <summary>Kayıt nesnesi değiştikten sonra dosyaya yazmak için çağrılır.</summary>
    public void Updated(RunRecord record) => Save();

    public RunRecord? Get(string id)
    {
        lock (_lock) return _runs.FirstOrDefault(r => r.Id == id);
    }

    public RunRecord? Last(string appId)
    {
        lock (_lock)
            return _runs.Where(r => r.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase))
                        .MaxBy(r => r.StartedAt);
    }

    public List<RunRecord> Query(string? appId, int take)
    {
        lock (_lock)
            return _runs.Where(r => appId is null || r.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(r => r.StartedAt)
                        .Take(Math.Clamp(take, 1, 1000))
                        .ToList();
    }

    private void Trim()
    {
        var excess = _runs.Count - _limit;
        if (excess <= 0) return;
        var old = _runs.Where(r => r.Status != RunStatus.Running).OrderBy(r => r.StartedAt).Take(excess).ToList();
        foreach (var r in old)
        {
            _runs.Remove(r);
            try { if (r.LogFile is not null && File.Exists(r.LogFile)) File.Delete(r.LogFile); } catch { /* yoksay */ }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            try
            {
                var tmp = _file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_runs, JsonDefaults.Options));
                File.Move(tmp, _file, overwrite: true);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "history.json yazılamadı.");
            }
        }
    }
}
