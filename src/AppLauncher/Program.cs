using System.Text;
using System.Text.Json.Serialization;
using AppLauncher;
using AppLauncher.Security;
using AppLauncher.Services;
using Microsoft.Extensions.Hosting.WindowsServices;

// cp857 / windows-1254 gibi eski kod sayfalarını kullanabilmek için
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var options = new WebApplicationOptions
{
    Args = args,
    // Windows Service olarak çalışırken çalışma dizini C:\Windows\System32 olur;
    // içerik kökünü exe'nin bulunduğu klasöre sabitliyoruz.
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
};

var builder = WebApplication.CreateBuilder(options);

// Sunucuya özel ayarlar (adres, izinli siciller). Bu dosya publish ile ezilmez, en son okunur ve
// değiştirildiğinde otomatik yeniden yüklenir.
builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json"),
    optional: true, reloadOnChange: true);

builder.Host.UseWindowsService(o => o.ServiceName = "AppLauncher");

builder.Services.Configure<LauncherOptions>(builder.Configuration.GetSection("Launcher"));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Giriş (login sayfası + Windows/AD doğrulama). Enabled değişikliği için servisi yeniden başlatmak gerekir;
// izinli sicil listesi ise anında uygulanır.
var authEnabled = builder.Configuration.GetValue("Launcher:Auth:Enabled", true);
if (authEnabled)
{
    var launcherOptions = builder.Configuration.GetSection("Launcher").Get<LauncherOptions>() ?? new LauncherOptions();
    var dataDir = launcherOptions.ResolveDataDirectory(builder.Environment.ContentRootPath);
    builder.Services.AddLauncherAuth(builder.Configuration, dataDir);
}

builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<AppStore>();
builder.Services.AddSingleton<RunHistory>();
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddHostedService<SchedulerService>();

var app = builder.Build();

// Servisleri başlangıçta oluştur (config dosyası yoksa örnek oluşturulsun, hatalar erken görünsün)
app.Services.GetRequiredService<AppStore>();
app.Services.GetRequiredService<RunHistory>();

app.UseLauncherCsrfGuard();

if (authEnabled)
{
    // Statik dosyalardan (arayüz) ÖNCE olmalı ki arayüz de korunsun
    app.UseAuthentication();
    app.UseAuthorization();
}
else
{
    app.Logger.LogWarning("Windows Authentication KAPALI. Adrese erişebilen herkes uygulamayı kullanabilir.");
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapLauncherAuth(authEnabled);
app.MapLauncherApi(authEnabled);

app.Run();
