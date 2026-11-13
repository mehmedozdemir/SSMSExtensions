# Faz D1 — Veritabanı paneli manuel test listesi

Build: `0.4.0` (`feature/db-dashboard`). Önceki fazların listelerinin üzerine yapılır.

## Hazırlık

1. SSMS'i kapat.
2. Repo kökünde:
   ```powershell
   build.cmd Release
   powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1
   ```
3. SSMS'i aç, bir sunucuya bağlan. Özellik varsayılan olarak **açık** (Ayarlar > Database Dashboard).

## A. Açma

- [ ] Object Explorer'da bir **veritabanına** sağ tık → menüde **Database Dashboard** var. *(Yoksa bana yaz; log'da "Object Explorer" satırları olacak.)*
- [ ] Tıklayınca belge alanında "VeritabanıAdı — Panel" sekmesi açılıyor.
- [ ] Aynı veritabanı için tekrar aç → yeni sekme açılmıyor, mevcut sekme öne geliyor.
- [ ] Başka bir veritabanı → ayrı sekme.
- [ ] Object Explorer'da bir **tablo** seçiliyken *Extensions > SsmsToolkit > Database Dashboard* → o tablonun veritabanı açılıyor.
- [ ] Bir sorgu penceresi aktifken aynı komut → sorgu penceresinin veritabanı açılıyor.
- [ ] Üretim kalıbına uyan sunucuda üstte kırmızı şerit var.

## B. Genel Bakış

- [ ] Kartlar: toplam boyut, veri ve log doluluk yüzdesi (çubuklu), recovery model, uyumluluk seviyesi, sağlık özeti.
- [ ] Sağlık kartındaki *Ayrıntıları göster* → Sağlık sekmesine geçiyor.
- [ ] Dosyalar tablosu: boyut, dolu (çubuk), büyüme, maks. boyut, yol doğru (SSMS'teki *Properties > Files* ile karşılaştır).
- [ ] Özellikler listesi doğru; tarih ve sayılar Türkçe bölge biçiminde.
- [ ] Koyu ve açık temada yazılar okunuyor.

## C. Depolama

- [ ] En büyük tablolar listesi; sütun başlığına tıklayınca sıralama (Satır, Ayrılan...) çalışıyor.
- [ ] Değerler `sp_spaceused 'dbo.Tablo'` ile yaklaşık aynı.
- [ ] Tablo adına tıkla → Table Inspector o tabloyla açılıyor.
- [ ] Yedek alınan bir veritabanında altta büyüme grafiği var; çubuğun üzerine gelince tarih ve boyut.
- [ ] Yedeği olmayan veritabanında "Son 365 günde full yedek yok".

## D. Sağlık

- [ ] Kontroller kritik → uyarı → bilgi → kontrol edilemedi → tamam sırasında.
- [ ] *Ayrıntılar* açılıyor; nesne adları seçilip kopyalanabiliyor.
- [ ] Yedek durumu SSMS'teki son yedek tarihleriyle uyumlu (*Ayrıntılar*'da FULL / DIFF / LOG).
- [ ] Yetkisi kısıtlı bir login ile (varsa) aç → bazı kontroller "Kontrol edilemedi: ..." diyor, sayfa yine açılıyor.
- [ ] Büyük bir veritabanında sayfa makul sürede yükleniyor; SSMS donmuyor; **Yenile** çalışıyor.

## E. Nesneler

- [ ] Nesne sayıları (prosedür, tablo, view...) doğru.
- [ ] Son değişen nesneler en yeniden eskiye; tablo ve view adları tıklanabilir.

## Bilinen sınırlar

- Satır sayıları `sys.partitions`'tan, yaklaşıktır (sp_spaceused gibi).
- Büyüme grafiği yalnızca msdb'deki full yedek geçmişinden gelir; yedek başka sunucuda/replikada alınıyorsa boş görünür.
- VLF sayısı SQL Server 2016 SP2+ ve `VIEW DATABASE STATE` ister; yoksa "kontrol edilemedi".
- Sahipsiz kullanıcı kontrolü yalnızca `VIEW ANY DEFINITION` (veya sysadmin) ile yapılır; yoksa yanlış alarm vermemek için atlanır.
- SQL Server 2016 öncesi sürümler desteklenmiyor.
- Canlı aktivite ve performans bölümleri D2'de.

## Kaldırma

SSMS kapalıyken: `powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1 -Uninstall`
