using System.ComponentModel;
using System.Globalization;

namespace PassDpi.App;

/// <summary>Tiny TR/EN string table. XAML binds with {Binding [Key], Source={x:Static app:Loc.I}}.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Lang { get; private set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    public void SetLanguage(string lang)
    {
        Lang = lang == "tr" ? "tr" : "en";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
    }

    public string this[string key] =>
        (Lang == "tr" ? Tr : En).TryGetValue(key, out var v) ? v : En.TryGetValue(key, out var e) ? e : key;

    public string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);

    private static readonly Dictionary<string, string> Tr = new()
    {
        ["Tab.Home"] = "Ana",
        ["Tab.Profiles"] = "Profiller",
        ["Tab.Sites"] = "Siteler",
        ["Tab.Log"] = "Log",
        ["Tab.Settings"] = "Ayarlar",

        ["Home.Setup"] = "Ayarla",
        ["Home.Testing"] = "Test ediliyor",
        ["Home.Cancel"] = "İptal",
        ["Home.Protection"] = "Koruma",
        ["Home.Profile"] = "Profil",
        ["Home.Network"] = "Ağ",
        ["Home.Mode"] = "Mod",
        ["Home.ModeAuto"] = "Otomatik",
        ["Home.ModeManual"] = "El ile",
        ["Home.None"] = "—",
        ["Home.Hint"] = "Ayarla'ya bas: PassDPI bu ağda çalışan GoodbyeDPI profilini test edip seçer.",

        ["State.Off"] = "Kapalı",
        ["State.Starting"] = "Başlatılıyor",
        ["State.Running"] = "Çalışıyor",
        ["State.Testing"] = "Test ediliyor",
        ["State.Error"] = "Hata",

        ["Code.Disabled"] = "Koruma kapalı.",
        ["Code.EngineMissing"] = "GoodbyeDPI dosyaları eksik ya da doğrulanamadı. PassDPI'ı yeniden kurun.",
        ["Code.ProfileInvalid"] = "Seçili profil geçersiz.",
        ["Code.NoNetwork"] = "Ağ bağlantısı yok.",
        ["Code.NoBlocking"] = "Bu ağda engel bulunamadı; motor gerekmiyor.",
        ["Code.OtherEngine"] = "Bilgisayarda başka bir GoodbyeDPI çalışıyor. Onu kapatıp testi tekrar çalıştırın.",
        ["Code.Verified"] = "Bu ağ için kayıtlı profil uygulandı ve doğrulandı.",
        ["Code.Testing"] = "Profiller test ediliyor…",
        ["Code.Found"] = "En iyi profil bulundu ve uygulandı.",
        ["Code.Cancelled"] = "Test iptal edildi.",
        ["Code.AllFailed"] = "Hiçbir profil bu ağda işe yaramadı. Motor kapalı bırakıldı, bağlantınız değişmedi.",
        ["Code.NoInternet"] = "Kontrol siteleri motor kapalıyken bile açılmıyor. İnternet bağlantınızı kontrol edin.",
        ["Code.EngineBroken"] = "GoodbyeDPI başlatılamadı (WinDivert sürücüsü yüklenemedi). Log'a bakın.",
        ["Code.TestError"] = "Test sırasında hata oluştu. Log'a bakın.",
        ["Code.Starting"] = "Motor başlatılıyor…",
        ["Code.Active"] = "Motor çalışıyor.",
        ["Code.StartFailed"] = "GoodbyeDPI başlatılamadı. Log'a bakın.",
        ["Code.CrashRestart"] = "Motor kapandı, yeniden başlatılıyor…",
        ["Code.CrashGiveUp"] = "Motor sürekli çöküyor; yeniden deneme durduruldu. Testi tekrar çalıştırın.",

        ["Progress.Baseline"] = "Motor kapalıyken bağlantı ölçülüyor",
        ["Progress.Profile"] = "Profil {0}/{1}: {2}",
        ["Progress.Result"] = "{0}: {1}/{2} site açıldı",

        ["Svc.Connecting"] = "Servise bağlanılıyor…",
        ["Svc.NotInstalled"] = "PassDPI servisi kurulu değil.",
        ["Svc.NotInstalledHint"] = "Kurulum bir kez yönetici izni ister. GoodbyeDPI resmi GitHub sürümünden indirilir ve SHA-256 ile doğrulanır.",
        ["Svc.Install"] = "Kur",
        ["Svc.InstallFromFolder"] = "Mevcut GoodbyeDPI klasöründen kur…",
        ["Svc.Stopped"] = "PassDPI servisi çalışmıyor.",
        ["Svc.Start"] = "Servisi başlat",
        ["Svc.Reinstall"] = "Yeniden kur",
        ["Svc.Installing"] = "Kuruluyor… (yönetici onayı gerekebilir)",
        ["Svc.InstallFailed"] = "Kurulum tamamlanamadı (çıkış kodu {0}).",
        ["Svc.InstallCancelled"] = "Yönetici izni verilmedi.",
        ["Svc.StartFailed"] = "Servis başlatılamadı: {0}",
        ["Svc.PickFolderTitle"] = "GoodbyeDPI klasörünü seçin (0.2.3rc3)",

        ["Profiles.Title"] = "Hazır profiller",
        ["Profiles.Hint"] = "Otomatik mod, her ağ için test edip en iyi profili hatırlar. Bir profili el ile sabitlemek için seçip Uygula'ya basın.",
        ["Profiles.Auto"] = "Otomatik moda dön",
        ["Profiles.Apply"] = "Uygula",
        ["Profiles.Custom"] = "Özel parametreler (isteğe bağlı)",
        ["Profiles.CustomHint"] = "Yalnızca GoodbyeDPI README'deki bayraklar kabul edilir. --blacklist gibi dosya yolu alan bayraklar reddedilir.",
        ["Profiles.CustomSave"] = "Kaydet",
        ["Profiles.CustomClear"] = "Temizle",
        ["Profiles.CustomSaved"] = "Özel parametreler kaydedildi. Profil listesinde 'Custom' olarak görünür.",

        ["Sites.Title"] = "Test siteleri",
        ["Sites.Hint"] = "Engelli olduğunu düşündüğünüz siteleri ekleyin. 'Kontrol' işaretli siteler engelsiz olmalı; bağlantının gerçekten çalıştığını doğrulamak için kullanılır.",
        ["Sites.Control"] = "Kontrol",
        ["Sites.Add"] = "Ekle",
        ["Sites.Remove"] = "Sil",
        ["Sites.Defaults"] = "Varsayılanlar",
        ["Sites.Save"] = "Kaydet",
        ["Sites.Saved"] = "Site listesi kaydedildi.",
        ["Sites.Invalid"] = "Geçersiz adres. Yalnızca https:// ile alan adı kabul edilir.",
        ["Sites.Placeholder"] = "ornek.com",

        ["Log.Title"] = "Log",
        ["Log.Copy"] = "Kopyala",
        ["Log.Refresh"] = "Yenile",

        ["Settings.Language"] = "Dil",
        ["Settings.Theme"] = "Tema",
        ["Settings.Dark"] = "Koyu",
        ["Settings.Light"] = "Açık",
        ["Settings.AutoStart"] = "Windows ile başlat",
        ["Settings.AutoStartHint"] = "Açıkken servis Windows açılışında başlar. Kapalıyken arayüzü açınca başlatılır.",
        ["Settings.Uninstall"] = "PassDPI'ı kaldır",
        ["Settings.UninstallHint"] = "Servisi, GoodbyeDPI dosyalarını, ayarları ve ağ hafızasını siler.",
        ["Settings.UninstallConfirm"] = "PassDPI tamamen kaldırılsın mı? Servis, motor dosyaları ve tüm ayarlar silinecek.",
        ["Settings.Uninstalled"] = "PassDPI kaldırıldı. Pencere kapanacak.",
        ["Settings.Version"] = "Sürüm",
        ["Settings.About"] = "PassDPI, GoodbyeDPI'ı (Apache-2.0) ve WinDivert'i kullanır. Telemetri yok; tek ağ trafiği test siteleri ve motor indirmesidir.",

        ["Err.Busy"] = "Zaten bir test çalışıyor.",
        ["Err.Generic"] = "İşlem başarısız: {0}",
        ["Err.NotConnected"] = "Servise bağlı değil.",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["Tab.Home"] = "Home",
        ["Tab.Profiles"] = "Profiles",
        ["Tab.Sites"] = "Sites",
        ["Tab.Log"] = "Log",
        ["Tab.Settings"] = "Settings",

        ["Home.Setup"] = "Set up",
        ["Home.Testing"] = "Testing",
        ["Home.Cancel"] = "Cancel",
        ["Home.Protection"] = "Protection",
        ["Home.Profile"] = "Profile",
        ["Home.Network"] = "Network",
        ["Home.Mode"] = "Mode",
        ["Home.ModeAuto"] = "Automatic",
        ["Home.ModeManual"] = "Manual",
        ["Home.None"] = "—",
        ["Home.Hint"] = "Press Set up: PassDPI tests GoodbyeDPI profiles and picks the one that works on this network.",

        ["State.Off"] = "Off",
        ["State.Starting"] = "Starting",
        ["State.Running"] = "Running",
        ["State.Testing"] = "Testing",
        ["State.Error"] = "Error",

        ["Code.Disabled"] = "Protection is off.",
        ["Code.EngineMissing"] = "GoodbyeDPI files are missing or failed verification. Reinstall PassDPI.",
        ["Code.ProfileInvalid"] = "The selected profile is invalid.",
        ["Code.NoNetwork"] = "No network connection.",
        ["Code.NoBlocking"] = "No blocking detected on this network; the engine isn't needed.",
        ["Code.OtherEngine"] = "Another GoodbyeDPI is running on this PC. Close it and run the test again.",
        ["Code.Verified"] = "Applied and verified the profile remembered for this network.",
        ["Code.Testing"] = "Testing profiles…",
        ["Code.Found"] = "Found and applied the best profile.",
        ["Code.Cancelled"] = "Test cancelled.",
        ["Code.AllFailed"] = "No profile worked on this network. The engine was left off; your connection is unchanged.",
        ["Code.NoInternet"] = "Control sites don't open even with the engine off. Check your Internet connection.",
        ["Code.EngineBroken"] = "GoodbyeDPI could not start (the WinDivert driver failed to load). See the log.",
        ["Code.TestError"] = "The test failed. See the log.",
        ["Code.Starting"] = "Starting the engine…",
        ["Code.Active"] = "The engine is running.",
        ["Code.StartFailed"] = "GoodbyeDPI failed to start. See the log.",
        ["Code.CrashRestart"] = "The engine exited; restarting…",
        ["Code.CrashGiveUp"] = "The engine keeps crashing; stopped retrying. Run the test again.",

        ["Progress.Baseline"] = "Measuring with the engine off",
        ["Progress.Profile"] = "Profile {0}/{1}: {2}",
        ["Progress.Result"] = "{0}: {1}/{2} sites opened",

        ["Svc.Connecting"] = "Connecting to the service…",
        ["Svc.NotInstalled"] = "The PassDPI service isn't installed.",
        ["Svc.NotInstalledHint"] = "Installing asks for administrator rights once. GoodbyeDPI is downloaded from the official GitHub release and verified with SHA-256.",
        ["Svc.Install"] = "Install",
        ["Svc.InstallFromFolder"] = "Install from an existing GoodbyeDPI folder…",
        ["Svc.Stopped"] = "The PassDPI service isn't running.",
        ["Svc.Start"] = "Start service",
        ["Svc.Reinstall"] = "Reinstall",
        ["Svc.Installing"] = "Installing… (administrator approval may be required)",
        ["Svc.InstallFailed"] = "Installation did not complete (exit code {0}).",
        ["Svc.InstallCancelled"] = "Administrator approval was not given.",
        ["Svc.StartFailed"] = "Could not start the service: {0}",
        ["Svc.PickFolderTitle"] = "Select the GoodbyeDPI folder (0.2.3rc3)",

        ["Profiles.Title"] = "Built-in profiles",
        ["Profiles.Hint"] = "Automatic mode tests each network and remembers the best profile. To pin a profile manually, select it and press Apply.",
        ["Profiles.Auto"] = "Back to automatic",
        ["Profiles.Apply"] = "Apply",
        ["Profiles.Custom"] = "Custom arguments (optional)",
        ["Profiles.CustomHint"] = "Only flags from the GoodbyeDPI README are accepted. Flags that take file paths, like --blacklist, are rejected.",
        ["Profiles.CustomSave"] = "Save",
        ["Profiles.CustomClear"] = "Clear",
        ["Profiles.CustomSaved"] = "Custom arguments saved. They appear as 'Custom' in the profile list.",

        ["Sites.Title"] = "Test sites",
        ["Sites.Hint"] = "Add sites you believe are blocked. Sites marked 'Control' must be unblocked; they confirm the connection really works.",
        ["Sites.Control"] = "Control",
        ["Sites.Add"] = "Add",
        ["Sites.Remove"] = "Remove",
        ["Sites.Defaults"] = "Defaults",
        ["Sites.Save"] = "Save",
        ["Sites.Saved"] = "Site list saved.",
        ["Sites.Invalid"] = "Invalid address. Only https:// with a domain name is accepted.",
        ["Sites.Placeholder"] = "example.com",

        ["Log.Title"] = "Log",
        ["Log.Copy"] = "Copy",
        ["Log.Refresh"] = "Refresh",

        ["Settings.Language"] = "Language",
        ["Settings.Theme"] = "Theme",
        ["Settings.Dark"] = "Dark",
        ["Settings.Light"] = "Light",
        ["Settings.AutoStart"] = "Start with Windows",
        ["Settings.AutoStartHint"] = "When on, the service starts at boot. When off, it starts when you open the app.",
        ["Settings.Uninstall"] = "Uninstall PassDPI",
        ["Settings.UninstallHint"] = "Removes the service, the GoodbyeDPI files, settings and network memory.",
        ["Settings.UninstallConfirm"] = "Completely remove PassDPI? The service, engine files and all settings will be deleted.",
        ["Settings.Uninstalled"] = "PassDPI was removed. This window will close.",
        ["Settings.Version"] = "Version",
        ["Settings.About"] = "PassDPI uses GoodbyeDPI (Apache-2.0) and WinDivert. No telemetry; the only network traffic is the test sites and the engine download.",

        ["Err.Busy"] = "A test is already running.",
        ["Err.Generic"] = "Operation failed: {0}",
        ["Err.NotConnected"] = "Not connected to the service.",
    };
}
