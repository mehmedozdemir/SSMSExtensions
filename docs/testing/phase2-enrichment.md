# Faz 2 — Zenginleştirme manuel test listesi

Build: `0.2.0` (`feature/phase2-enrichment`). Faz 1 listesinin üzerine yapılır.

## Hazırlık

1. SSMS'i kapat.
2. Repo kökünde:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\copy-ssms-refs.ps1   # yeni DLL eklendi (Sdk.SqlStudio)
   build.cmd Release
   powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1
   ```
3. SSMS'i aç, `FMS - Test`'e bağlan.

## A. Ctrl basılıyken alt çizgi

- [ ] Sorgu editöründe **Ctrl**'yi basılı tutup fareyi bir tablo adının üzerine getir → kelimenin altı çiziliyor, imleç el oluyor.
- [ ] Fareyi kıpırdatmadan Ctrl'yi bırak → alt çizgi kayboluyor. Tekrar bas → geri geliyor.
- [ ] Satır sonundaki boş alanda, string veya yorum içinde alt çizgi **çıkmıyor** (yorum/string ayrımı yapılmıyor; kelime varsa çizilebilir, bu bilinen sınır).
- [ ] Ayarlarda tetikleyici *Ctrl + Alt + Tık* ise alt çizgi yalnızca Ctrl+Alt ile çıkıyor; *Kapalı* ise hiç çıkmıyor.
- [ ] Yazarken veya kaydırırken takılma/titreme yok.

## B. Trigger'lar sekmesi

- [ ] Trigger'ı olan bir tabloda **Trigger'lar** sekmesi: ad, AFTER / INSTEAD OF, olaylar (INSERT, UPDATE, DELETE), kapalı mı.
- [ ] Satır seçince altta trigger'ın `CREATE TRIGGER` tanımı; kapalıysa sonunda `DISABLE TRIGGER ...`.
- [ ] Trigger'ı olmayan tabloda liste boş, hata yok.

## C. Bağımlılıklar sekmesi

- [ ] **Bunu kullananlar:** tabloyu kullanan prosedür/view/fonksiyonlar listeleniyor (şemasız yazılmış `FROM Cards` gibi referanslar dahil).
- [ ] Satır seçince altta o nesnenin tanımı. *Yeni Sorguda Aç* çalışıyor.
- [ ] Listede bir **view veya tablo** adı link olarak görünüyor; tıklayınca o nesne yeni inspector'da açılıyor.
- [ ] Bir **view** incelerken **Bunun kullandıkları** bölümünde altındaki tablolar var.
- [ ] Not: dinamik SQL (`EXEC('...')`) içindeki kullanımlar görünmez — beklenen davranış.

## D. Object Explorer

- [ ] Object Explorer'da bir tabloya sağ tık → menüde **Inspect Table** var. *(Yoksa bana yaz; log'da "Object Explorer" satırları olacak.)*
- [ ] Tıklayınca inspector açılıyor: doğru sunucu, doğru veritabanı, doğru tablo.
- [ ] Bir **view** üzerinde de çalışıyor.
- [ ] Object Explorer'da tablo seçiliyken **Ctrl+K, Ctrl+T** → inspector açılıyor.
- [ ] OE'den açılan inspector'da Veri sekmesi → **Yeni Sorguda Aç** → yeni sorgu penceresi aynı sunucuya ve veritabanına **bağlı** açılıyor (bağlantı penceresi çıkmamalı).
- [ ] Farklı bir sunucuya da bağlanıp (ör. ikinci sunucu) OE'den aç → o sunucunun tablosu açılıyor.

## E. Koyu tema scrollbar'ları

- [ ] Koyu temada inspector'daki (Veri sekmesi, DDL, SQL paneli) ve ayarlardaki scrollbar'lar koyu.

## Kaldırma

SSMS kapalıyken: `powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1 -Uninstall`
