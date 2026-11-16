# PassDPI

[English](README.md) · **Türkçe**

Servis sağlayıcıların uyguladığı **DPI tabanlı site engellerini**
[GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) ile aşan, hafif bir Windows aracı.
**Ayarla** tuşuna basınca PassDPI bağlantınızda GoodbyeDPI profillerini test eder, çalışanı seçer
ve her ağ için hatırlar.

- Kurulumda bir kez yönetici izni ister, sonra bir daha sormaz.
- GoodbyeDPI'ı küçük bir Windows servisi arka planda çalıştırır. Boştayken CPU kullanımı ~%0'dır
  (olay tabanlı, polling yok).
- Küçük bir pencereden oluşur, sistem tepsisi simgesi yoktur. Pencereyi kapatınca uygulama
  tamamen kapanır.
- Ağ başına hafıza: Wi-Fi değişince doğru profil otomatik uygulanır ve hızlıca doğrulanır.
- Türkçe ve İngilizce, koyu ve açık tema.
- Telemetri yok. Tek ağ trafiği test sitelerine yapılan bağlantılar ve GitHub'dan bir kez yapılan
  motor indirmesidir.

## Nasıl çalışır

```
PassDPI.exe (arayüz, standart kullanıcı)  ──named pipe──►  PassDPI servisi (LocalSystem)
                                                              ├─ goodbyedpi.exe'yi çalıştırır (+ WinDivert sürücüsü)
                                                              ├─ profil testi (HTTPS denemeleri)
                                                              └─ ağ izleyici (NetworkChange olayları)
```

**Otomatik test.** PassDPI önce motor kapalıyken test sitelerini ölçer. Kontrol siteleri
açılmıyorsa sorun DPI değil internet bağlantısıdır. Her şey zaten açılıyorsa engel yoktur.
Aksi halde her profili sırayla dener: GoodbyeDPI'ı başlat → bekle → test sitelerine HTTPS
bağlantısı aç → durdur. Bütün siteleri açan ilk profilde test durur.

Bir sitenin "açıldı" sayılması için TLS el sıkışmasının o alan adına ait geçerli bir sertifikayla
tamamlanması gerekir. Bu yüzden ISS uyarı sayfaları ve zehirlenmiş DNS cevapları başarısız sayılır.
Hiçbir profil işe yaramazsa motor **kapalı** bırakılır ve bağlantınız olduğu gibi kalır.

**Profiller** resmi GoodbyeDPI README'sinden alınmıştır: `-9`, `-5`…`-8` (modern) ve `-1`…`-4`
(eski) modları. Her mod ayrıca DNS yönlendirmesiyle de denenir: önce en hızlısı olan Cloudflare
DNS, sonra GoodbyeDPI'ın kendi `2_any_country_dnsredir.cmd` dosyasındaki gibi 1253 portunda Yandex
DNS. Yandex, ISS 53 portunu ele geçirdiğinde de çalışır.
Toplam 27 profil vardır ve en olasıdan en az olasıya doğru test edilir.

**Ağ hafızası.** Sonuçlar ağ başına saklanır. Ağ anahtarı; Wi-Fi adı (SSID), ağ geçidi IP'si ve
ağ geçidi MAC adresinin tuzlanmış SHA-256 özetidir. SSID'ler hiçbir zaman düz metin olarak
saklanmaz ve hiçbir yere bir şey gönderilmez.

## Kurulum

1. [Releases](../../releases) sayfasından `PassDPI-<sürüm>-win-x64.zip` dosyasını indirip açın.
   `PassDPI.exe` ile `PassDpi.Service.exe` aynı klasörde kalmalı.
2. `PassDPI.exe`'yi çalıştırıp **Kur**'a tıklayın. Windows bir kez yönetici onayı ister.
   - Kurulum PassDPI'ı `C:\Program Files\PassDPI` klasörüne kopyalar.
   - GoodbyeDPI `0.2.3rc3` sürümünü resmi GitHub release'inden indirir ve SHA-256 ile doğrular.
     İsterseniz **Mevcut GoodbyeDPI klasöründen kur** seçeneğini kullanabilirsiniz. Yalnızca bu
     sürüm kabul edilir ve dosyalar korumalı klasöre kopyalanır.
   - `PassDPI` servisini kaydedip başlatır.
3. **Ayarla**'ya basın.

Sonraki seferlerde PassDPI'ı Başlat menüsünden açabilirsiniz.

> **SmartScreen / antivirüs uyarısı.** PassDPI kod imzalı değildir. Bu yüzden Windows SmartScreen
> "Windows bilgisayarınızı korudu" diyebilir. *Ek bilgi → Yine de çalıştır* seçeneğini kullanın.
> Birçok antivirüs programı da GoodbyeDPI ve WinDivert'i ağ paketlerini yakaladıkları için
> "riskware" ya da "hacktool" olarak işaretler. Bu tür araçlar için bu beklenen bir durumdur.
> İsterseniz kaynak koddan kendiniz derleyebilirsiniz.

### Ayarlar

- **Windows ile başlat.** Servisin başlangıç türünü Otomatik ve El ile arasında değiştirir.
  El ile modunda uygulamayı açtığınızda servis yönetici izni istemeden başlar. Servisi yalnızca
  kurulumu yapan kullanıcı başlatabilir.
- **El ile mod.** *Profiller* sekmesinden herhangi bir hazır profili sabitleyebilir ya da otomatik
  moda dönebilirsiniz.
- **Özel parametreler.** İsteğe bağlıdır. Yalnızca GoodbyeDPI README'sinde belgelenmiş bayraklar,
  doğrulanmış değerlerle kabul edilir. `--blacklist` gibi dosya yolu alan bayraklar reddedilir.
- **Test siteleri.** Düzenlenebilir. En az bir tane engelsiz *Kontrol* sitesi bırakın.
- **Kaldır.** Servisi, motor dosyalarını, ayarları ve ağ hafızasını siler.

## Güvenlik modeli

Servis LocalSystem olarak çalıştığı için standart bir kullanıcının kötüye kullanabileceği hiçbir
açık bırakmayacak şekilde tasarlandı:

- Çalıştırılabilir dosyalar `C:\Program Files\PassDPI` klasöründedir; bu klasöre yalnızca
  Administrators ve SYSTEM yazabilir. Veriler, kullanıcı erişimi olmayan korumalı ACL'li
  `C:\ProgramData\PassDPI` klasöründedir. Bu ACL'ler gevşetilirse servis onları onarır.
- Motor dosyaları kurulumda **ve her başlatmadan önce** sabitlenmiş SHA-256 değerleriyle doğrulanır.
- Kontrol pipe'ının DACL'i yalnızca kurulumu yapan kullanıcıya (ve SYSTEM'e) izin verir. Ağ
  oturumları reddedilir. Arayüz, pipe sunucusunun gerçekten PassDPI servis süreci olduğunu kontrol
  eder. İstekler sabit bir komut kümesinden oluşur (`StartTest`, `Apply`, `Stop`, `SetAutoStart`,
  `SetTestSites`, `SetCustomArgs`, `Uninstall`, …). Servis dosya yolu ya da komut satırı kabul
  etmez ve mesaj boyutu 64 KB ile sınırlıdır.
- GoodbyeDPI bir Windows job object içinde çalışır. Servis kapanırsa motor da onunla birlikte
  kapanır ve normal ağ bağlantısı geri gelir.
- PassDPI; Defender'a, güvenlik duvarı kurallarına, sistem DNS ayarlarına ya da başka herhangi bir
  sistem ayarına dokunmaz.

## Derleme

Gereksinimler: Windows 10/11 x64 ve .NET 8 SDK (daha yeni SDK'lar da çalışır).

```powershell
dotnet build PassDpi.sln -c Release
dotnet test tests/PassDpi.Tests -c Release
pwsh build/publish.ps1          # → artifacts/PassDPI-<sürüm>-win-x64.zip
```

Servis trimmed, self-contained ve tek dosya olarak yayınlanır. WPF uygulaması da self-contained ve
tek dosyadır, ancak WPF trimming'i desteklemediği için trimmed değildir.

## Lisans

PassDPI MIT lisanslıdır (bkz. `LICENSE`). GoodbyeDPI Apache-2.0, WinDivert ise LGPLv3/GPLv2
lisanslıdır. İkisi de kurulum sırasında indirilir ve bu deponun parçası değildir. Ayrıntılar için
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) dosyasına bakın.

PassDPI'ın GoodbyeDPI ya da WinDivert projeleriyle bir bağlantısı yoktur. Engel aşma araçlarını
kullanmadan önce ülkenizdeki yasaları kontrol edin.
