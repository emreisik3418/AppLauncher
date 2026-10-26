using System.Text.Json;
using System.Text.RegularExpressions;
using AppLauncher.Models;
using Microsoft.Extensions.Options;

namespace AppLauncher.Services;

/// <summary>
/// data/apps.json dosyasını yönetir. Dosya elle (Notepad vb.) değiştirilirse otomatik yeniden yüklenir.
/// Dosya hatalıysa son geçerli yapılandırma kullanılmaya devam edilir ve hata arayüzde gösterilir.
/// </summary>
public sealed class AppStore : IDisposable
{
    private static readonly Regex IdRegex = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    private readonly object _lock = new();
    private readonly ILogger<AppStore> _log;
    private readonly FileSystemWatcher _watcher;
    private Timer? _debounce;
    private List<AppDefinition> _apps = new();

    public string FilePath { get; }
    public string? LastLoadError { get; private set; }
    public DateTimeOffset LastLoadedAt { get; private set; }

    public AppStore(IOptions<LauncherOptions> options, IHostEnvironment env, ILogger<AppStore> log)
    {
        _log = log;
        var dir = options.Value.ResolveDataDirectory(env.ContentRootPath);
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "apps.json");

        if (!File.Exists(FilePath)) WriteSample();
        Reload();

        _watcher = new FileSystemWatcher(dir, "apps.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };
        _watcher.Changed += (_, _) => ScheduleReload();
        _watcher.Created += (_, _) => ScheduleReload();
        _watcher.Renamed += (_, _) => ScheduleReload();
        _watcher.EnableRaisingEvents = true;
    }

    public IReadOnlyList<AppDefinition> GetAll()
    {
        lock (_lock) return _apps.ToList();
    }

    public AppDefinition? Get(string id)
    {
        lock (_lock) return _apps.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public List<string> Add(AppDefinition app)
    {
        lock (_lock)
        {
            var list = _apps.ToList();
            list.Add(app);
            return ValidateAndSave(list);
        }
    }

    public List<string> Update(string originalId, AppDefinition app)
    {
        lock (_lock)
        {
            var list = _apps.ToList();
            var idx = list.FindIndex(a => a.Id.Equals(originalId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return new() { $"'{originalId}' bulunamadı." };
            list[idx] = app;
            return ValidateAndSave(list);
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var list = _apps.ToList();
            if (list.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0) return false;
            ValidateAndSave(list);
            return true;
        }
    }

    public string ReadRaw()
    {
        lock (_lock) return File.ReadAllText(FilePath);
    }

    /// <summary>apps.json'u ham metin olarak kaydeder (biçim ve yorumlar korunur).</summary>
    public List<string> SaveRaw(string json)
    {
        AppsFile? parsed;
        try { parsed = JsonSerializer.Deserialize<AppsFile>(json, JsonDefaults.Options); }
        catch (JsonException ex) { return new() { "JSON hatası: " + ex.Message }; }
        if (parsed is null) return new() { "Boş yapılandırma." };

        var errors = Validate(parsed.Apps);
        if (errors.Count > 0) return errors;

        lock (_lock)
        {
            WriteAtomic(json);
            _apps = parsed.Apps;
            LastLoadError = null;
            LastLoadedAt = DateTimeOffset.Now;
        }
        return errors;
    }

    public static List<string> Validate(List<AppDefinition> apps)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in apps)
        {
            var label = string.IsNullOrWhiteSpace(a.Id) ? "(Id yok)" : a.Id;
            a.Id = a.Id?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(a.Name)) a.Name = a.Id;
            a.EnvironmentVariables ??= new();
            a.Arguments ??= "";
            a.Description ??= "";

            if (a.Id.Length == 0) errors.Add("Her uygulamanın bir Id değeri olmalı.");
            else if (!IdRegex.IsMatch(a.Id)) errors.Add($"{label}: Id yalnızca harf, rakam, '-', '_' ve '.' içerebilir.");
            else if (!seen.Add(a.Id)) errors.Add($"{label}: Bu Id birden fazla kez kullanılmış.");

            if (string.IsNullOrWhiteSpace(a.Path)) errors.Add($"{label}: Path (çalıştırılacak dosya) boş olamaz.");
            if (a.TimeoutMinutes < 0) errors.Add($"{label}: TimeoutMinutes negatif olamaz.");

            var schedErr = ScheduleHelper.Validate(a.Schedule);
            if (schedErr is not null) errors.Add($"{label}: {schedErr}");
        }
        return errors;
    }

    private List<string> ValidateAndSave(List<AppDefinition> list)
    {
        var errors = Validate(list);
        if (errors.Count > 0) return errors;
        var json = JsonSerializer.Serialize(new AppsFile { Apps = list }, JsonDefaults.Options);
        WriteAtomic(json);
        _apps = list;
        LastLoadError = null;
        LastLoadedAt = DateTimeOffset.Now;
        return errors;
    }

    private void WriteAtomic(string content)
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, FilePath, overwrite: true);
    }

    private void ScheduleReload()
    {
        // Editörler dosyayı birkaç adımda yazar; kısa bir bekleme ile tek seferde yükle.
        lock (_lock)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ => Reload(), null, 700, Timeout.Infinite);
        }
    }

    private void Reload()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                string json;
                using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                    json = sr.ReadToEnd();

                var parsed = JsonSerializer.Deserialize<AppsFile>(json, JsonDefaults.Options) ?? new AppsFile();
                var errors = Validate(parsed.Apps);
                lock (_lock)
                {
                    if (errors.Count > 0)
                    {
                        LastLoadError = string.Join(Environment.NewLine, errors);
                        _log.LogError("apps.json geçersiz, önceki yapılandırma kullanılıyor: {Errors}", LastLoadError);
                        return;
                    }
                    _apps = parsed.Apps;
                    LastLoadError = null;
                    LastLoadedAt = DateTimeOffset.Now;
                }
                _log.LogInformation("apps.json yüklendi ({Count} uygulama).", parsed.Apps.Count);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(300); // dosya başka bir program tarafından kilitli olabilir
            }
            catch (JsonException ex)
            {
                lock (_lock) LastLoadError = "JSON hatası: " + ex.Message;
                _log.LogError(ex, "apps.json okunamadı, önceki yapılandırma kullanılıyor.");
                return;
            }
        }
    }

    private void WriteSample()
    {
        var sample = new AppsFile
        {
            Apps =
            {
                new AppDefinition
                {
                    Id = "aylik-rapor",
                    Name = "Aylık Rapor",
                    Description = "Her ayın 28'inde 09:00'da çalışır.",
                    Path = @"C:\Uygulamalar\AylikRapor\AylikRapor.exe",
                    Arguments = "--mod aylik",
                    Enabled = false,
                    Schedule = new ScheduleDefinition { Type = ScheduleType.Monthly, DayOfMonth = 28, Time = "09:00" }
                },
                new AppDefinition
                {
                    Id = "son-persembe-yedek",
                    Name = "Son Perşembe Yedekleme",
                    Description = "Her ayın son perşembesi 22:00'de çalışır.",
                    Path = @"C:\Scripts\yedekle.ps1",
                    Enabled = false,
                    TimeoutMinutes = 120,
                    Schedule = new ScheduleDefinition
                    {
                        Type = ScheduleType.MonthlyWeekday, Week = "Last", DayOfWeek = DayOfWeek.Thursday, Time = "22:00"
                    }
                },
                new AppDefinition
                {
                    Id = "gunluk-temizlik",
                    Name = "Günlük Temizlik",
                    Description = "Her gün 22:00'de çalışır.",
                    Path = @"C:\Scripts\temizlik.bat",
                    Enabled = false,
                    Schedule = new ScheduleDefinition { Type = ScheduleType.Daily, Time = "22:00" }
                },
                new AppDefinition
                {
                    Id = "deneme",
                    Name = "Deneme (ipconfig)",
                    Description = "Zamanlaması yok; arayüzden elle çalıştırıp logu görebilirsiniz.",
                    Path = "ipconfig.exe",
                    Arguments = "/all"
                }
            }
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(sample, JsonDefaults.Options));
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce?.Dispose();
    }
}
