# App Launcher

Kendi yazdığınız uygulamaları (exe, bat, cmd, ps1, dll, py) bir web arayüzünden listeleyip elle çalıştırabileceğiniz, zamanlayabileceğiniz ve Windows Service olarak sürekli ayakta tutabileceğiniz bir .NET 8 uygulaması.

## Özellikler

- Web arayüzü: uygulama listesi, durum, sonraki çalışma zamanı, son sonuç
- Elle **Çalıştır / Durdur**, canlı log izleme, çalıştırma geçmişi
- Uygulama **ekleme / düzenleme / silme** (arayüzden veya `apps.json` dosyasını elle düzenleyerek)
- `apps.json` elle değiştirildiğinde **otomatik yeniden yüklenir**. Dosya bozuksa son geçerli hali kullanılır, hata arayüzde gösterilir
- Zamanlama tipleri: her gün, haftalık, ayın N. günü, ayın son günü, ayın 1./2./3./4./**son** X günü, serbest cron
- Zaman aşımı, eşzamanlı çalışmayı engelleme, ortam değişkenleri
- Windows Service olarak kurulum, hata halinde otomatik yeniden başlatma

## Gereksinimler

- .NET 8 SDK (geliştirme için) / .NET 8 ASP.NET Core Runtime (sunucu için)
- Visual Studio 2022: `AppLauncher.sln` dosyasını açın. Adım adım anlatım için [docs/VS2022-KURULUM.md](docs/VS2022-KURULUM.md) dosyasına bakın.

## Geliştirme ortamında çalıştırma

```powershell
cd src\AppLauncher
dotnet run
```

Tarayıcıda: http://localhost:5080

İlk çalıştırmada `src\AppLauncher\data\apps.json` örnek içerikle oluşturulur. Örneklerin çoğu kapalıdır; `deneme (ipconfig)` uygulamasını elle çalıştırarak hemen test edebilirsiniz.

## Windows Service olarak kurma

**Yönetici** olarak PowerShell açın:

```powershell
.\install-service.ps1                       # C:\Services\AppLauncher, port 5080
.\install-service.ps1 -InstallDir D:\Servisler\AppLauncher -Port 8080
```

Betik şunları yapar: projeyi yayınlar, servisi oluşturur (otomatik başlangıç), hata durumunda yeniden başlatma ayarını yapar ve servisi başlatır. Güncelleme için aynı betiği yeniden çalıştırın; `data` klasörü (apps.json, geçmiş, loglar) korunur.

Kaldırmak için: `.\uninstall-service.ps1`

## apps.json

Konum: uygulama klasöründeki `data\apps.json` (servis kurulumunda `C:\Services\AppLauncher\data\apps.json`).

```json
{
  "apps": [
    {
      "id": "aylik-rapor",
      "name": "Aylık Rapor",
      "description": "Her ayın 28'inde 09:00",
      "path": "C:\\Uygulamalar\\AylikRapor\\AylikRapor.exe",
      "arguments": "--mod aylik",
      "workingDirectory": null,
      "enabled": true,
      "timeoutMinutes": 60,
      "allowConcurrentRuns": false,
      "environmentVariables": { "ORTAM": "PROD" },
      "schedule": { "type": "Monthly", "dayOfMonth": 28, "time": "09:00" }
    }
  ]
}
```

| Alan | Açıklama |
|---|---|
| `id` | Benzersiz kısa ad (harf, rakam, `-` `_` `.`) |
| `path` | `.exe` doğrudan, `.bat/.cmd` cmd ile, `.ps1` PowerShell ile, `.dll` `dotnet` ile, `.py` python ile çalışır. `%ORTAM_DEGISKENI%` kullanılabilir |
| `workingDirectory` | Boşsa dosyanın bulunduğu klasör |
| `enabled` | `false` ise zamanlama çalışmaz, elle çalıştırma yine mümkündür |
| `timeoutMinutes` | `0` = sınırsız. Aşılırsa süreç ağacı sonlandırılır |
| `allowConcurrentRuns` | `false` iken önceki çalıştırma bitmeden yenisi başlamaz |

### Zamanlama örnekleri

```jsonc
// Her gün 22:00
{ "type": "Daily", "time": "22:00" }

// Her ayın 28'i 09:00
{ "type": "Monthly", "dayOfMonth": 28, "time": "09:00" }

// Her ayın son perşembesi 22:00
{ "type": "MonthlyWeekday", "week": "Last", "dayOfWeek": "Thursday", "time": "22:00" }

// Her ayın 2. salısı 08:30
{ "type": "MonthlyWeekday", "week": "2", "dayOfWeek": "Tuesday", "time": "08:30" }

// Ayın son günü 23:00
{ "type": "MonthlyLastDay", "time": "23:00" }

// Pazartesi, Çarşamba, Cuma 07:15
{ "type": "Weekly", "daysOfWeek": ["Monday", "Wednesday", "Friday"], "time": "07:15" }

// Serbest cron: hafta içi her 30 dakikada bir (dk saat gün ay haftagünü)
{ "type": "Cron", "cron": "*/30 8-18 * * 1-5" }
```

Gün adları İngilizce yazılır: `Monday` … `Sunday`. Zamanlama olmaması için `"schedule": null` kullanın.
`dayOfMonth: 31` seçilirse 31 çekmeyen aylarda çalışmaz; ay sonu için `MonthlyLastDay` kullanın.

## appsettings.json

```json
"Urls": "http://localhost:5080",
"Launcher": {
  "DataDirectory": "data",
  "TimeZone": "",
  "OutputEncoding": "utf-8",
  "HistoryLimit": 500,
  "CheckIntervalSeconds": 15
}
```

- **Urls**: Varsayılan olarak yalnızca bu makineden erişilebilir. Ağdan erişim için `http://0.0.0.0:5080` yapın. Arayüzde kimlik doğrulama **yoktur**, bu yüzden ağa açacaksanız güvenlik duvarı ile kısıtlayın veya Windows Authentication ekleyin.
- **TimeZone**: Boşsa sunucunun saat dilimi. Örnek: `Turkey Standard Time`.
- **OutputEncoding**: Log'larda Türkçe karakterler bozuk görünürse `ibm857` veya `windows-1254` deneyin.
- **HistoryLimit**: Tutulacak kayıt sayısı. Eski kayıtların log dosyaları da silinir.

## Bilmeniz gerekenler

- **Servis hesabı:** Servis varsayılan olarak `LocalSystem` ile çalışır. Uygulamalarınız ağ paylaşımına, veritabanına (Windows auth) veya kullanıcı profiline erişiyorsa servisi bir kullanıcı hesabıyla çalıştırın. `services.msc` → AppLauncher → Oturum Aç sekmesini kullanabilir ya da `sc.exe config AppLauncher obj= "DOMAIN\kullanici" password= "..."` komutunu çalıştırabilirsiniz.
- **Pencereli uygulamalar:** Servisler masaüstü oturumunda pencere açamaz (Session 0). Konsol veya arka plan uygulamaları sorunsuz çalışır. Ekranda pencere göstermesi gereken uygulamalar görünmez.
- **Kaçırılan çalıştırmalar:** Servis kapalıyken veya makine kapalıyken geçen zamanlamalar sonradan telafi edilmez.
- **Loglar:** `data\logs\<id>\` altında her çalıştırma için bir dosya bulunur. Servisin kendi logları Windows Olay Görüntüleyicisi'ndeki (Application) AppLauncher kaynağında tutulur.
- **Çıkış kodu:** `0` başarılı, diğer değerler hatalı kabul edilir.

## Proje yapısı

```
src/AppLauncher/
  Program.cs                 Host, Windows Service, DI
  Endpoints.cs               REST API (/api/...)
  LauncherOptions.cs         Ayarlar
  Models/Models.cs           AppDefinition, ScheduleDefinition, RunRecord
  Services/AppStore.cs       apps.json okuma/yazma + dosya izleme
  Services/ScheduleHelper.cs Zamanlama → cron dönüşümü (Cronos)
  Services/SchedulerService.cs  Arka planda zamanı gelenleri başlatır
  Services/ProcessRunner.cs  Süreç başlatma/durdurma, loglama, zaman aşımı
  Services/RunHistory.cs     Çalıştırma geçmişi (data/history.json)
  wwwroot/                   Arayüz (HTML/CSS/JS, derleme gerektirmez)
```
