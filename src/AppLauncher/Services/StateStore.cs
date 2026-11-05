using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AppLauncher.Services;

/// <summary>
/// Servis yeniden başlasa da korunması gereken küçük durum bilgileri (data/state.json):
/// - Zamanlayıcının en son çalıştığı an (kapalıyken kaçırılan zamanlamaları bulmak için)
/// - Elle durdurulduğu için "sürekli çalışma"sı beklemeye alınan uygulamalar
/// </summary>
public sealed class StateStore
{
    private sealed class StateFile
    {
        public DateTimeOffset? LastTick { get; set; }
        public List<string> PausedKeepAlive { get; set; } = new();
    }

    private readonly object _lock = new();
    private readonly string _file;
    private readonly ILogger<StateStore> _log;
    private readonly StateFile _state;
    private DateTimeOffset _lastSaved = DateTimeOffset.MinValue;

    /// <summary>Servis bu sefer açılmadan önceki son zamanlayıcı kontrolü (yoksa ilk kurulum).</summary>
    public DateTimeOffset? LastTickBeforeStartup { get; }

    public StateStore(IOptions<LauncherOptions> options, IHostEnvironment env, ILogger<StateStore> log)
    {
        _log = log;
        var dir = options.Value.ResolveDataDirectory(env.ContentRootPath);
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "state.json");

        _state = new StateFile();
        if (File.Exists(_file))
        {
            try
            {
                _state = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(_file), JsonDefaults.Options) ?? new();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "state.json okunamadı, sıfırlanıyor.");
            }
        }
        LastTickBeforeStartup = _state.LastTick;
    }

    /// <summary>Her zamanlayıcı turunda çağrılır; diske en fazla 30 sn'de bir yazar.</summary>
    public void SetLastTick(DateTimeOffset now, bool force = false)
    {
        lock (_lock)
        {
            _state.LastTick = now;
            if (force || now - _lastSaved >= TimeSpan.FromSeconds(30)) Save();
        }
    }

    public bool IsPaused(string appId)
    {
        lock (_lock) return _state.PausedKeepAlive.Contains(appId, StringComparer.OrdinalIgnoreCase);
    }

    public void Pause(string appId)
    {
        lock (_lock)
        {
            if (_state.PausedKeepAlive.Contains(appId, StringComparer.OrdinalIgnoreCase)) return;
            _state.PausedKeepAlive.Add(appId);
            Save();
        }
    }

    public void Resume(string appId)
    {
        lock (_lock)
        {
            if (_state.PausedKeepAlive.RemoveAll(x => x.Equals(appId, StringComparison.OrdinalIgnoreCase)) > 0) Save();
        }
    }

    private void Save()
    {
        try
        {
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_state, JsonDefaults.Options));
            File.Move(tmp, _file, overwrite: true);
            _lastSaved = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "state.json yazılamadı.");
        }
    }
}
