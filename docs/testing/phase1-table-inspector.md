# Faz 1 — Table Inspector manuel test listesi

Build: `0.1.0` (`feature/phase1-table-inspector`). SSMS 22.12 üzerinde yapılır.

## Hazırlık

1. **SSMS'i kapat.**
2. Repo kökünde:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\copy-ssms-refs.ps1   # yeni DLL eklendi (SqlEnum): bir kez daha çalıştır
   build.cmd Release
   powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1
   ```
3. SSMS'i aç, `FMS - Test` sunucusuna bağlan, yeni bir sorgu penceresi aç.

Sorun olursa log: **Extensions > SsmsToolkit > Open Log Folder**.

## A. Menü ve ayarlar

- [ ] **Extensions** (veya **Tools**) > **SsmsToolkit** altında: *Settings...*, *Open Log Folder*, *About SsmsToolkit*.
- [ ] *About* → sürüm `0.1.0`, SSMS sürümü ve log/ayar yolları görünüyor.
- [ ] *Settings...* → doküman alanında ayarlar sekmesi açılıyor. Solda **Table Inspector** (işaretli), sağda ayarları ve **Genel** bölümü var.
- [ ] Açılır kutular ve onay kutuları koyu temada okunaklı.
- [ ] Bir ayarı değiştir → **Kaydet** → "Ayarlar kaydedildi." mesajı. `%LocalAppData%\SsmsToolkit\settings.json` güncellendi.

## B. Ctrl+Tık

`FMS` veritabanında:

```sql
SELECT p.Id, c.*
FROM dbo.PersonelCardDetails p
JOIN Cards c ON c.Id = p.CardId
WHERE p.Id > 0
```

- [ ] `PersonelCardDetails` üzerine **Ctrl+Tık** → yeni bir doküman sekmesi `dbo.PersonelCardDetails` açılıyor. Kelime seçimi olmuyor.
- [ ] `p.Id` içindeki **`p`** üzerine Ctrl+Tık → aynı tablo (yeni sekme açılmaz, mevcut sekme öne gelir).
- [ ] **`c`** (alias) üzerine Ctrl+Tık → `Cards` açılıyor.
- [ ] Yarım sorguda dene: `SELECT * FROM dbo.Cards x WHERE x.` → `x` üzerine Ctrl+Tık → `Cards`.
- [ ] Var olmayan bir ad (`dbo.Yok`) → durum çubuğunda "tablo veya view bulunamadı".
- [ ] `#tmp` → durum çubuğunda "temp tablolar ... desteklenmiyor".
- [ ] Bir stored procedure adı → durum çubuğunda "bir tablo veya view değil (SQL_STORED_PROCEDURE)".
- [ ] Bir **view** üzerinde → açılıyor, başlıkta "View", DDL sekmesinde view tanımı.
- [ ] Bağlı olmayan bir sorgu penceresinde → "sorgu editörü bir sunucuya bağlı değil".

## C. Sağ tık menüsü ve kısayol

- [ ] Tablo adının üzerinde sağ tık → **Inspect Table** görünüyor ve çalışıyor. *(Görünmüyorsa bana yaz: SSMS'in sağ tık menü ID'si doğrulanacak.)*
- [ ] İmleci tablo adına koy → **Ctrl+K, Ctrl+T** → inspector açılıyor.

## D. Sekmeler

- [ ] **Özet:** sunucu, DB, şema, tip, yaklaşık satır sayısı, boyut, tarih, açıklama.
- [ ] **Kolonlar:** tip (`nvarchar(50)` gibi), PK ✓, Null ✓, identity, default, computed, açıklama.
- [ ] **Anahtarlar:** yalnızca PK ve UNIQUE constraint'ler (PK, UQ), tipleri ve kolonları.
- [ ] **İndeksler:** yalnızca diğer indeksler; PK/UQ burada tekrar görünmüyor. Anahtar ve include kolonları, filtre.
- [ ] **Constraint'ler:** CHECK ve DEFAULT constraint'ler; ad, kolon (tablo seviyesi CHECK'te boş), tanım, durum (kapalı / güvenilmez / sistem adı).
- [ ] **SQL paneli:** Kolonlar, Anahtarlar, İndeksler, Constraint'ler ve Foreign Key'ler sekmelerinde bir satır seç → altta o nesnenin scripti (ör. `CREATE NONCLUSTERED INDEX ...`, `ALTER TABLE ... ADD CONSTRAINT ...`). Ayraç sürüklenerek panel büyütülüp küçültülebiliyor. *Kopyala* ve *Yeni Sorguda Aç* çalışıyor. FK sekmesinde giden ve gelen listeler aynı paneli kullanıyor.
- [ ] **Foreign Key'ler:** giden ve gelen FK'lar. Tablo adına tıklayınca o tablo yeni sekmede açılıyor.
- [ ] **Veri:** ilk 200 satır. Kolon başlığına tık → sunucuda sıralıyor (ok işareti, ikinci tık ters sıralama).
- [ ] **Veri / WHERE:** `Id < 10` + Enter → filtreli. `1=1; DELETE FROM x` → hata mesajı, sorgu **çalışmaz**.
- [ ] **Veri / Yeni Sorguda Aç:** aynı sunucu/DB'ye bağlı yeni sorgu penceresi, içinde `SELECT TOP (200) * FROM [FMS].[dbo].[...]`.
- [ ] **DDL:** `CREATE TABLE` scripti (PK, FK, indeksler). *Kopyala* ve *Yeni Sorguda Aç* çalışıyor.
- [ ] **İptal / Yenile** butonları çalışıyor. Yükleme sırasında SSMS donmuyor.

## E. Ayarlar etkisi

- [ ] *Açılış şekli = Yüzen pencere* → Ctrl+Tık ayrı bir pencere açıyor.
- [ ] *Fare tetikleyicisi = Ctrl + Alt + Tık* → sadece Ctrl+Tık artık açmıyor, Ctrl+Alt+Tık açıyor.
- [ ] *Veri sekmesi satır limiti = 50* → Veri sekmesinde 50 satır.
- [ ] *Üretim sunucusu adı kalıpları* alanına test sunucusunun adına uyan bir kalıp yaz (ör. `*test*`) → yeni açılan inspector'da kırmızı **ÜRETİM SUNUCUSU** şeridi. Sonra eski haline getir.
- [ ] *Dil = Türkçe / English* → kaydettikten sonra açılan pencereler o dilde.
- [ ] Table Inspector onay kutusunu kapat → Kaydet → Ctrl+Tık hiçbir şey yapmıyor, sağ tık menüsünde *Inspect Table* görünmüyor. Tekrar aç.

## F. Tema

- [ ] Koyu ve açık temada (Tools > Theme) inspector ve ayarlar okunaklı. Tema değişince açık pencereler de güncelleniyor.

## Geri bildirim

Her kutu için evet/hayır ve sorunlu durumlarda log dosyası.

## Kaldırma

SSMS kapalıyken: `powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1 -Uninstall`
