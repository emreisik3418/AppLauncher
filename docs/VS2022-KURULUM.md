# Visual Studio 2022 ile projeyi oluşturma

İki yol var. Ya repodaki hazır solution'ı açarsınız (A), ya da projeyi VS 2022'de sıfırdan oluşturup dosyaları yapıştırırsınız (B).

## Ön koşullar

- Visual Studio 2022 (17.8 veya üzeri)
- Visual Studio Installer'da **"ASP.NET and web development"** iş yükü seçili olmalı (.NET 8 SDK bununla gelir)
- Kontrol: Komut isteminde `dotnet --list-sdks` çalıştırın, listede `8.0.x` görünmeli

---

## A) Repodan hazır açma (önerilen)

1. VS 2022 → **Clone a repository** → `https://github.com/emreisik3418/AppLauncher` adresini girin.
   (Ya da GitHub'dan ZIP indirip açın.)
2. `AppLauncher.sln` dosyasını açın.
3. NuGet paketleri otomatik geri yüklenir. Yüklenmezse: Solution'a sağ tık → **Restore NuGet Packages**.
4. **F5** ile çalıştırın. Tarayıcı `http://localhost:5080` adresinde açılır.

---

## B) Sıfırdan oluşturup dosyaları yapıştırma

### 1. Projeyi oluşturun

1. VS 2022 → **Create a new project**
2. Şablon: **ASP.NET Core Empty** (C#) → Next
3. Project name: `AppLauncher`, Solution name: `AppLauncher` → Next
4. Framework: **.NET 8.0 (Long Term Support)**
   - Configure for HTTPS: **işareti kaldırın**
   - Enable container support: kapalı
   - Do not use top-level statements: **işaretlemeyin**
5. **Create**

### 2. NuGet paketlerini ekleyin

Projeye sağ tık → **Manage NuGet Packages** → Browse sekmesinde şunları arayıp kurun:

| Paket | Sürüm |
|---|---|
| `Cronos` | 0.8.4 |
| `Microsoft.Extensions.Hosting.WindowsServices` | 8.0.1 |
| `Microsoft.AspNetCore.Authentication.Negotiate` | 8.0.11 |

Alternatif: **Tools → NuGet Package Manager → Package Manager Console** açıp şunları çalıştırın:

```powershell
Install-Package Cronos -Version 0.8.4
Install-Package Microsoft.Extensions.Hosting.WindowsServices -Version 8.0.1
Install-Package Microsoft.AspNetCore.Authentication.Negotiate -Version 8.0.11
```

### 3. Klasörleri oluşturun

Projeye sağ tık → **Add → New Folder** ile şu klasörleri ekleyin:

```
Models
Security
Services
wwwroot
```

(`Properties` klasörü şablonla birlikte gelir.)

### 4. Dosyaları ekleyip içeriği yapıştırın

Her dosya için ilgili klasöre sağ tık → **Add → New Item**. C# dosyaları için **Class**, diğerleri için uygun türü seçin (JSON File, HTML Page, Style Sheet, JavaScript File). Ardından GitHub'daki içeriği olduğu gibi yapıştırın. Şablonun oluşturduğu dosyaların (`Program.cs`, `appsettings.json`, `launchSettings.json`) içeriğini tamamen silip repodakini yapıştırın.

| Dosya (projede) | GitHub'daki yeri |
|---|---|
| `Program.cs` (değiştirin) | `src/AppLauncher/Program.cs` |
| `Endpoints.cs` | `src/AppLauncher/Endpoints.cs` |
| `LauncherOptions.cs` | `src/AppLauncher/LauncherOptions.cs` |
| `appsettings.json` (değiştirin) | `src/AppLauncher/appsettings.json` |
| `Properties/launchSettings.json` (değiştirin) | `src/AppLauncher/Properties/launchSettings.json` |
| `Models/Models.cs` | `src/AppLauncher/Models/Models.cs` |
| `Security/WindowsAuth.cs` | `src/AppLauncher/Security/WindowsAuth.cs` |
| `Security/AuthEndpoints.cs` | `src/AppLauncher/Security/AuthEndpoints.cs` |
| `Services/AppStore.cs` | `src/AppLauncher/Services/AppStore.cs` |
| `Services/RunHistory.cs` | `src/AppLauncher/Services/RunHistory.cs` |
| `Services/ProcessRunner.cs` | `src/AppLauncher/Services/ProcessRunner.cs` |
| `Services/ScheduleHelper.cs` | `src/AppLauncher/Services/ScheduleHelper.cs` |
| `Services/SchedulerService.cs` | `src/AppLauncher/Services/SchedulerService.cs` |
| `wwwroot/index.html` | `src/AppLauncher/wwwroot/index.html` |
| `wwwroot/style.css` | `src/AppLauncher/wwwroot/style.css` |
| `wwwroot/app.js` | `src/AppLauncher/wwwroot/app.js` |
| `wwwroot/login.html` | `src/AppLauncher/wwwroot/login.html` |

> İpucu: GitHub'da dosyayı açınca sağ üstteki **Copy raw file** düğmesi içeriğin tamamını kopyalar.

> Şablon `appsettings.Development.json` dosyası da oluşturur. Bu dosya kalabilir, zararı yoktur.

### 5. `.csproj` dosyasını kontrol edin

Projeye çift tıklayın (veya sağ tık → **Edit Project File**). İçerik şöyle görünmeli. Aradaki `data` bölümünü elle ekleyin:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>AppLauncher</RootNamespace>
    <AssemblyName>AppLauncher</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Cronos" Version="0.8.4" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.Negotiate" Version="8.0.11" />
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.1" />
  </ItemGroup>

  <ItemGroup>
    <Content Remove="data\**" />
    <None Remove="data\**" />
    <Content Remove="appsettings.Local.json" />
    <None Remove="appsettings.Local.json" />
  </ItemGroup>

</Project>
```

### 6. Derleyin ve çalıştırın

1. **Build → Build Solution** (Ctrl+Shift+B). Hata listesi boş olmalı.
1. Proje klasörüne (`Program.cs` ile aynı yere) `appsettings.Local.json` dosyası ekleyip kendi sicilinizi yazın. Eklemezseniz giriş yaptığınızda login sayfasında "erişim yetkisi yok" mesajı ve eklemeniz gereken kullanıcı adı görünür:
   ```json
   { "Launcher": { "Auth": { "AllowedUsers": [ "A12345" ] } } }
   ```
2. **F5** → tarayıcıda `http://localhost:5080` açılır.
3. İlk çalıştırmada proje klasöründe `data\apps.json` dosyası örnek içerikle oluşur.
4. Test için **"Deneme (ipconfig)"** kartında **▶ Çalıştır** → **Son log** ile çıktıyı görün.

---

## Windows Service olarak yayınlama

### Komut satırından (önerilen)

Repo kök klasöründe **yönetici** PowerShell açıp şunu çalıştırın:

```powershell
.\install-service.ps1
```

Betik projeyi derleyip `C:\Services\AppLauncher` klasörüne kurar ve servisi başlatır.

> PowerShell betik çalıştırmaya izin vermiyorsa önce şunu çalıştırın:
> `Set-ExecutionPolicy -Scope Process Bypass`

### VS 2022 Publish ile

1. Projeye sağ tık → **Publish** → **Folder** → konum: `C:\Services\AppLauncher` → **Finish**
2. **Show all settings** bölümünde:
   - Configuration: `Release`
   - Target runtime: `win-x64`
   - Deployment mode: `Framework-dependent`
3. **Publish**
4. Yönetici komut isteminde servisi oluşturun:

```cmd
sc.exe create AppLauncher binPath= "\"C:\Services\AppLauncher\AppLauncher.exe\"" start= auto DisplayName= "App Launcher"
sc.exe failure AppLauncher reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start AppLauncher
```

Ardından `C:\Services\AppLauncher\appsettings.Local.json` dosyasını oluşturun (içeriği README'de var), sonra güvenlik duvarında portu açın:
`netsh advfirewall firewall add rule name="AppLauncher 5080" dir=in action=allow protocol=TCP localport=5080`

Güncelleme yaparken önce `sc.exe stop AppLauncher` ile servisi durdurun, yeniden **Publish** edin, sonra `sc.exe start AppLauncher` ile başlatın. `data` klasörü korunur.

---

## Sık karşılaşılan sorunlar

| Sorun | Çözüm |
|---|---|
| `Cronos` veya `UseWindowsService` bulunamadı | NuGet paketlerini kurun / Restore NuGet Packages |
| Port 5080 kullanımda | `appsettings.json` ve `launchSettings.json` içinde portu değiştirin |
| Servis başlamıyor | Olay Görüntüleyicisi → Windows Logs → Application → kaynak **AppLauncher** |
| Loglarda Türkçe karakterler bozuk | `appsettings.json` → `"OutputEncoding": "ibm857"` veya `"windows-1254"` |
| Uygulama servisten ağ paylaşımına erişemiyor | Servisi bir kullanıcı hesabıyla çalıştırın (services.msc → AppLauncher → Oturum Aç) |
