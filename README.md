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
- **Kurumsal login sayfası**: sicil + domain şifresi veya tek tıkla Windows oturumu ile giriş, çıkış yap
- Yalnızca config'te listelenen siciller (veya AD grupları) girebilir; kim neyi başlattı/durdurdu kaydedilir

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

### sc.exe komutlarıyla kurma

Hazır betik: repo klasöründeki `install-service.cmd` dosyasına sağ tıklayıp **Yönetici olarak çalıştır** seçin. Kaldırmak için `uninstall-service.cmd` dosyasını aynı şekilde çalıştırın.

Komutları elle çalıştırmak isterseniz, **yönetici** komut isteminde (cmd):

```cmd
:: 1) Yayınla
dotnet publish src\AppLauncher\AppLauncher.csproj -c Release -r win-x64 --self-contained false -o C:\Services\AppLauncher

:: 2) Servisi oluştur  (binPath= / start= sonrasındaki BOŞLUK zorunlu)
sc.exe create AppLauncher binPath= "\"C:\Services\AppLauncher\AppLauncher.exe\"" start= auto DisplayName= "App Launcher"
sc.exe description AppLauncher "Uygulamalari web arayuzunden yonetir ve zamanlanmis calistirir."

:: 3) Hata alırsa 1 dk sonra yeniden başlat
sc.exe failure AppLauncher reset= 86400 actions= restart/60000/restart/60000/restart/60000

:: 4) Başlat ve kontrol et
sc.exe start AppLauncher
sc.exe query AppLauncher
```

Diğer komutlar: `sc.exe stop AppLauncher`, `sc.exe delete AppLauncher` (kaldırır), `sc.exe qc AppLauncher` (ayarları gösterir).

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

## Giriş yetkisi ve ağ erişimi (appsettings.Local.json)

Sunucuya özel ayarlar kurulum klasöründeki **`appsettings.Local.json`** dosyasında tutulur (örn. `C:\Services\AppLauncher\appsettings.Local.json`). Kurulum betiği bu dosyayı ilk kurulumda, kurulumu yapan kullanıcının sicili ile oluşturur. Güncelleme (yeniden publish) sırasında **ezilmez**.

```json
{
  "Urls": "http://0.0.0.0:5080",
  "Launcher": {
    "Auth": {
      "Enabled": true,
      "AllowedUsers": [ "A12345", "B67890", "SIRKET\\C11111" ],
      "AllowedGroups": [ "SIRKET\\AppLauncher-Yoneticiler" ]
    }
  }
}
```

- **Urls**: `0.0.0.0` makinenin tüm ağ adreslerini dinler. Diğer makinelerden `http://MAKINEADI:5080` ile erişilir.
- **AllowedUsers**: Girebilecek Windows kullanıcı adları (siciller). `A12345` yazarsanız domain fark etmeksizin eşleşir. `SIRKET\\A12345` yazarsanız yalnızca o domain eşleşir. JSON içinde `\` iki kez yazılır. Büyük/küçük harf fark etmez.
- **AllowedGroups** (isteğe bağlı): Bu AD gruplarının üyeleri de girebilir.
- Listeye ekleme/çıkarma **servisi yeniden başlatmadan** hemen geçerli olur. `Enabled` değiştirilirse servisi yeniden başlatın.
- Yetkisi olmayan kullanıcı, login sayfasında kendi Windows kullanıcı adını gösteren bir "erişim yetkisi yok" mesajı görür. Bu adı listeye eklemeniz yeterli. Listeden çıkarılan kullanıcının açık oturumu bir sonraki işleminde kapanır.

### Login sayfası

`http://MAKINEADI:5080` açıldığında oturum yoksa `/login` sayfası gelir. İki giriş yolu vardır:

1. **Sicil + şifre**: Kullanıcı domain sicilini ve Windows şifresini yazar. Şifre sunucuda Windows/AD'ye (LogonUser) karşı doğrulanır, uygulama şifreyi saklamaz. Sicil `A12345`, `SIRKET\A12345` veya `a12345@sirket.local` biçiminde yazılabilir.
2. **Windows oturumumla giriş yap**: Bilgisayardaki oturum ile şifre sorulmadan girer (Kerberos/NTLM).

Giriş sonrası oturum cookie ile tutulur. Sağ üstteki kullanıcı menüsünden **Çıkış yap** seçilebilir.

| Ayar (`Launcher:Auth`) | Varsayılan | Açıklama |
|---|---|---|
| `Domain` | boş | Sicil + şifre girişinde kullanılacak domain. Boşsa sunucunun domain'i otomatik bulunur. Login sayfasında "Domain: …" olarak görünür |
| `AllowPasswordLogin` | `true` | Sicil + şifre formunu göster |
| `AllowWindowsLogin` | `true` | "Windows oturumumla giriş yap" düğmesini göster |
| `SessionMinutes` | `480` | Bu süre boyunca işlem yapılmazsa oturum düşer |
| `MaxFailedAttempts` / `LockoutMinutes` | `5` / `10` | Bir sicil 10 dakika içinde 5 kez hatalı şifre girerse giriş 10 dakika engellenir. Bu, AD hesabının kilitlenmesini önlemeye yardımcı olur |

Oturum anahtarları `data\keys` klasöründe (DPAPI ile şifreli) tutulur. Bu sayede servis yeniden başlasa da kullanıcıların oturumu düşmez.
- Giriş, ekleme/silme, çalıştırma/durdurma ve yetkisiz erişim denemeleri Olay Görüntüleyicisi'ne (Application → AppLauncher) yazılır. Geçmiş ekranında da "Kullanıcı" sütununda görünür.

**Tarayıcı:** "Windows oturumumla giriş yap" düğmesine basıldığında Edge/Chrome, domain'deki makinelerde şifre sormadan girer. Tarayıcı kendi kullanıcı adı/şifre penceresini açarsa adresi Internet Seçenekleri → Güvenlik → **Yerel intranet** → Siteler → Gelişmiş bölümüne `http://MAKINEADI` olarak ekleyin (GPO ile de dağıtılabilir). Firefox için `about:config` → `network.negotiate-auth.trusted-uris` ayarına makine adını ekleyin.

**Geliştirme (F5):** Proje klasörüne kendi `appsettings.Local.json` dosyanızı koyun ve sicilinizi yazın. Bu dosya git'e ve publish'e dahil edilmez.

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

Bu dosyadaki değerler varsayılanlardır. Sunucuya özel değerleri (Urls, Auth) `appsettings.Local.json` dosyasına yazın, çünkü `appsettings.json` her publish'te üzerine yazılır.

- **Urls**: Varsayılan `localhost`. Ağ erişimi için yukarıdaki bölüme bakın.
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
  Security/WindowsAuth.cs    Cookie + Windows kimlik doğrulama, izinli sicil kontrolü
  Security/AuthEndpoints.cs  Login/logout uçları, AD şifre doğrulama, hatalı deneme sınırı
  wwwroot/login.html         Login sayfası
  wwwroot/                   Arayüz (HTML/CSS/JS, derleme gerektirmez)
```
