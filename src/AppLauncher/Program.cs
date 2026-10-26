using System.Text;
using System.Text.Json.Serialization;
using AppLauncher;
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

builder.Host.UseWindowsService(o => o.ServiceName = "AppLauncher");

builder.Services.Configure<LauncherOptions>(builder.Configuration.GetSection("Launcher"));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<AppStore>();
builder.Services.AddSingleton<RunHistory>();
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddHostedService<SchedulerService>();

var app = builder.Build();

// Servisleri başlangıçta oluştur (config dosyası yoksa örnek oluşturulsun, hatalar erken görünsün)
app.Services.GetRequiredService<AppStore>();
app.Services.GetRequiredService<RunHistory>();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapLauncherApi();

app.Run();
