using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using AppLauncher.Models;
using Microsoft.Extensions.Options;

namespace AppLauncher.Services;

/// <summary>Uygulamaları ayrı süreç olarak başlatır, çıktısını log dosyasına yazar, durdurur.</summary>
public sealed class ProcessRunner
{
    private sealed class ActiveRun
    {
        public required RunRecord Record { get; init; }
        public required Process Process { get; init; }
        public required StreamWriter Writer { get; init; }
        public volatile bool StopRequested;
        public volatile bool TimedOut;
    }

    private readonly ConcurrentDictionary<string, ActiveRun> _active = new();
    private readonly object _startLock = new();
    private readonly RunHistory _history;
    private readonly ILogger<ProcessRunner> _log;
    private readonly Encoding _outputEncoding;

    public ProcessRunner(RunHistory history, IOptions<LauncherOptions> options, ILogger<ProcessRunner> log)
    {
        _history = history;
        _log = log;
        _outputEncoding = options.Value.ResolveOutputEncoding();
    }

    public bool IsRunning(string appId) =>
        _active.Values.Any(a => a.Record.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));

    public List<RunRecord> ActiveRuns(string appId) =>
        _active.Values.Where(a => a.Record.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase))
                      .Select(a => a.Record).ToList();

    public RunRecord Start(AppDefinition app, RunTrigger trigger)
    {
        lock (_startLock)
        {
            if (!app.AllowConcurrentRuns && IsRunning(app.Id))
                throw new InvalidOperationException($"'{app.Name}' zaten çalışıyor.");

            var now = DateTimeOffset.Now;
            var record = new RunRecord
            {
                AppId = app.Id,
                AppName = app.Name,
                Trigger = trigger,
                StartedAt = now,
                Status = RunStatus.Running
            };

            var logDir = Path.Combine(_history.LogsDirectory, app.Id);
            Directory.CreateDirectory(logDir);
            record.LogFile = Path.Combine(logDir, $"{now:yyyyMMdd_HHmmss}_{record.Id[..6]}.log");

            var writer = new StreamWriter(
                new FileStream(record.LogFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false)) { AutoFlush = true };

            ProcessStartInfo psi;
            try
            {
                psi = BuildStartInfo(app);
            }
            catch (Exception ex)
            {
                return FailImmediately(record, writer, ex);
            }

            WriteLine(writer, $"=== {app.Name} | {(trigger == RunTrigger.Manual ? "Elle" : "Zamanlanmış")} | {now:yyyy-MM-dd HH:mm:ss}");
            WriteLine(writer, $"=== Komut: {psi.FileName} {psi.Arguments}");
            WriteLine(writer, $"=== Çalışma klasörü: {psi.WorkingDirectory}");

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) WriteLine(writer, e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) WriteLine(writer, "[ERR] " + e.Data); };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                process.Dispose();
                return FailImmediately(record, writer, ex);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            record.ProcessId = process.Id;

            var active = new ActiveRun { Record = record, Process = process, Writer = writer };
            _active[record.Id] = active;
            _history.Add(record);
            _log.LogInformation("{App} başlatıldı (PID {Pid}, {Trigger}).", app.Id, process.Id, trigger);

            _ = Task.Run(() => MonitorAsync(active, app.TimeoutMinutes));
            return record;
        }
    }

    public bool Stop(string runId)
    {
        if (!_active.TryGetValue(runId, out var active)) return false;
        active.StopRequested = true;
        WriteLine(active.Writer, "=== Kullanıcı tarafından durduruluyor...");
        Kill(active.Process);
        return true;
    }

    private async Task MonitorAsync(ActiveRun active, int timeoutMinutes)
    {
        var p = active.Process;
        var rec = active.Record;
        try
        {
            using var cts = timeoutMinutes > 0
                ? new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes))
                : new CancellationTokenSource();
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                active.TimedOut = true;
                WriteLine(active.Writer, $"=== Süre aşımı ({timeoutMinutes} dk), süreç sonlandırılıyor...");
                Kill(p);
                await Task.WhenAny(p.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(30)));
            }

            rec.ExitCode = p.HasExited ? p.ExitCode : null;
            rec.Status = active.StopRequested ? RunStatus.Stopped
                       : active.TimedOut ? RunStatus.TimedOut
                       : rec.ExitCode == 0 ? RunStatus.Succeeded
                       : RunStatus.Failed;
        }
        catch (Exception ex)
        {
            rec.Status = RunStatus.Failed;
            rec.Error = ex.Message;
            _log.LogError(ex, "{App} izlenirken hata.", rec.AppId);
        }
        finally
        {
            rec.EndedAt = DateTimeOffset.Now;
            WriteLine(active.Writer,
                $"=== Bitti: {rec.Status} | Çıkış kodu: {rec.ExitCode?.ToString() ?? "-"} | Süre: {(rec.EndedAt.Value - rec.StartedAt):hh\\:mm\\:ss}");
            lock (active.Writer) active.Writer.Dispose();
            p.Dispose();
            _active.TryRemove(rec.Id, out _);
            _history.Updated(rec);
            _log.LogInformation("{App} bitti: {Status} ({Exit}).", rec.AppId, rec.Status, rec.ExitCode);
        }
    }

    private RunRecord FailImmediately(RunRecord record, StreamWriter writer, Exception ex)
    {
        WriteLine(writer, "=== Başlatılamadı: " + ex.Message);
        writer.Dispose();
        record.Status = RunStatus.Failed;
        record.Error = ex.Message;
        record.EndedAt = DateTimeOffset.Now;
        _history.Add(record);
        _log.LogError(ex, "{App} başlatılamadı.", record.AppId);
        return record;
    }

    private ProcessStartInfo BuildStartInfo(AppDefinition app)
    {
        var path = System.Environment.ExpandEnvironmentVariables(app.Path.Trim().Trim('"'));
        var args = System.Environment.ExpandEnvironmentVariables(app.Arguments ?? "");
        var ext = Path.GetExtension(path).ToLowerInvariant();

        string fileName, arguments;
        switch (ext)
        {
            case ".bat":
            case ".cmd":
                fileName = "cmd.exe";
                arguments = $"/c \"\"{path}\" {args}\"";
                break;
            case ".ps1":
                fileName = "powershell.exe";
                arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{path}\" {args}";
                break;
            case ".dll":
                fileName = "dotnet";
                arguments = $"\"{path}\" {args}";
                break;
            case ".py":
                fileName = "python";
                arguments = $"\"{path}\" {args}";
                break;
            default:
                fileName = path;
                arguments = args;
                break;
        }

        var workDir = app.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workDir))
            workDir = Path.IsPathRooted(path) ? Path.GetDirectoryName(path) : AppContext.BaseDirectory;
        else
            workDir = System.Environment.ExpandEnvironmentVariables(workDir);

        if (!string.IsNullOrEmpty(workDir) && !Directory.Exists(workDir))
            throw new DirectoryNotFoundException($"Çalışma klasörü bulunamadı: {workDir}");
        if (Path.IsPathRooted(path) && !File.Exists(path))
            throw new FileNotFoundException($"Dosya bulunamadı: {path}");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments.Trim(),
            WorkingDirectory = workDir ?? "",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = _outputEncoding,
            StandardErrorEncoding = _outputEncoding
        };
        foreach (var (key, value) in app.EnvironmentVariables)
            psi.Environment[key] = value;

        return psi;
    }

    private static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* zaten bitmiş olabilir */ }
    }

    private static void WriteLine(StreamWriter w, string line)
    {
        lock (w)
        {
            try { w.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}"); }
            catch (ObjectDisposedException) { }
        }
    }
}
