using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppLauncher;

public sealed class LauncherOptions
{
    /// <summary>apps.json, history.json ve logs klasörünün yeri. Göreli ise uygulama klasörüne göredir.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>Zamanlamaların hangi saat dilimine göre yorumlanacağı. Boşsa sunucunun yerel saati.</summary>
    public string? TimeZone { get; set; }

    /// <summary>Çalıştırılan uygulamaların çıktı kodlaması (utf-8, ibm857, windows-1254 ...).</summary>
    public string OutputEncoding { get; set; } = "utf-8";

    /// <summary>Saklanacak en fazla çalıştırma kaydı. Eski kayıtların log dosyaları da silinir.</summary>
    public int HistoryLimit { get; set; } = 500;

    /// <summary>Zamanlayıcının kontrol aralığı (saniye).</summary>
    public int CheckIntervalSeconds { get; set; } = 5;

    /// <summary>Servis açıldıktan sonra kurtarma / sürekli çalışma başlatmadan önce beklenecek süre (sn).
    /// Sunucu yeni açıldığında ağ, veritabanı vb. hazır olsun diye.</summary>
    public int StartupDelaySeconds { get; set; } = 30;

    /// <summary>Çıktıda hata satırı algılama (tüm uygulamalar için varsayılan).</summary>
    public Services.ErrorDetectionOptions ErrorDetection { get; set; } = new();

    /// <summary>Windows Authentication ve izinli siciller.</summary>
    public Security.AuthOptions Auth { get; set; } = new();

    public string ResolveDataDirectory(string contentRoot) =>
        Path.IsPathRooted(DataDirectory) ? DataDirectory : Path.Combine(contentRoot, DataDirectory);

    public TimeZoneInfo ResolveTimeZone()
    {
        if (string.IsNullOrWhiteSpace(TimeZone)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
        catch { return TimeZoneInfo.Local; }
    }

    public Encoding ResolveOutputEncoding()
    {
        try { return Encoding.GetEncoding(OutputEncoding); }
        catch { return new UTF8Encoding(false); }
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
