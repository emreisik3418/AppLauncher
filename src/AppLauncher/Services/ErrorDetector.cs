using System.Text.RegularExpressions;
using AppLauncher.Models;

namespace AppLauncher.Services;

/// <summary>appsettings içindeki "Launcher:ErrorDetection" bölümü (tüm uygulamalar için varsayılan).</summary>
public sealed class ErrorDetectionOptions
{
    /// <summary>Boş bırakılırsa <see cref="ErrorDetector.DefaultPatterns"/> kullanılır.</summary>
    public List<string> Patterns { get; set; } = new();

    /// <summary>Bu kalıplara uyan satırlar hiçbir zaman hata sayılmaz (ör. "0 errors").</summary>
    public List<string> IgnorePatterns { get; set; } = new();

    public bool IgnoreCase { get; set; } = true;

    /// <summary>Uygulamanın hata akışına (stderr, logda [ERR] ile görünen) yazdığı her satır hata sayılsın mı.</summary>
    public bool StdErrIsError { get; set; } = true;
}

/// <summary>
/// Bir çıktı satırının hata satırı olup olmadığına karar verir.
/// Genel ayarlar (appsettings) + uygulamaya özel ek/yok sayılacak kalıplar birlikte kullanılır.
/// </summary>
public sealed class ErrorDetector
{
    public static readonly string[] DefaultPatterns =
    {
        @"\b(ERR|ERROR|FATAL|CRITICAL)\b",   // ERR, ERROR, [ERROR], error:, FATAL ...
        @"\b\w+Exception\b",                 // NullReferenceException, System.IO.IOException ...
        @"\bUnhandled exception\b",
        @"\bTraceback \(most recent call last\)"
    };

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly bool _enabled;
    private readonly bool _stdErrIsError;
    private readonly Regex[] _match;
    private readonly Regex[] _ignore;

    public static readonly ErrorDetector Disabled = new(false, false, Array.Empty<Regex>(), Array.Empty<Regex>());

    private ErrorDetector(bool enabled, bool stdErrIsError, Regex[] match, Regex[] ignore)
    {
        _enabled = enabled;
        _stdErrIsError = stdErrIsError;
        _match = match;
        _ignore = ignore;
    }

    public static ErrorDetector Create(ErrorDetectionOptions global, AppDefinition? app)
    {
        if (app is { DetectErrors: false }) return Disabled;

        var opts = global.IgnoreCase ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant;
        var patterns = (global.Patterns.Count > 0 ? global.Patterns : DefaultPatterns.ToList())
            .Concat(app?.ErrorPatterns ?? new());
        var ignores = global.IgnorePatterns.Concat(app?.IgnoreErrorPatterns ?? new());

        return new ErrorDetector(true, app?.StdErrIsError ?? global.StdErrIsError,
            Compile(patterns, opts), Compile(ignores, opts));
    }

    /// <summary>Geçersiz kalıp varsa hata mesajı döner (yapılandırma doğrulaması için).</summary>
    public static string? ValidatePattern(string pattern)
    {
        try { _ = new Regex(pattern, RegexOptions.None, RegexTimeout); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }

    /// <param name="line">Uygulamanın yazdığı ham satır.</param>
    /// <param name="stderr">Satır hata akışından (stderr) mı geldi.</param>
    public bool IsError(string line, bool stderr)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(line)) return false;
        if (_ignore.Any(r => SafeMatch(r, line))) return false;
        if (stderr && _stdErrIsError) return true;
        return _match.Any(r => SafeMatch(r, line));
    }

    /// <summary>
    /// Log dosyasındaki bir satırı değerlendirir. Biçim: "[HH:mm:ss] içerik" veya "[HH:mm:ss] [ERR] içerik".
    /// App Launcher'ın kendi "=== ..." satırları hata sayılmaz.
    /// </summary>
    public bool IsLogLineError(string logLine)
    {
        var content = logLine;
        if (content.Length > 11 && content[0] == '[' && content[9] == ']') content = content[11..];
        if (content.StartsWith("=== ", StringComparison.Ordinal)) return false;
        var stderr = content.StartsWith("[ERR] ", StringComparison.Ordinal);
        if (stderr) content = content[6..];
        return IsError(content, stderr);
    }

    private static Regex[] Compile(IEnumerable<string> patterns, RegexOptions opts) =>
        patterns.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p =>
                {
                    try { return new Regex(p.Trim(), opts | RegexOptions.Compiled, RegexTimeout); }
                    catch (ArgumentException) { return null; } // doğrulamada yakalanır; çalışma anında yoksay
                })
                .OfType<Regex>()
                .ToArray();

    private static bool SafeMatch(Regex r, string line)
    {
        try { return r.IsMatch(line); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
