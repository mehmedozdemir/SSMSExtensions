# Faz 3.1 — SSMS grid araçları manuel test listesi

Build: `0.3.0` (`feature/phase3-grid-tools`). Faz 1 ve 2 listelerinin üzerine yapılır.

## Hazırlık

1. SSMS'i kapat.
2. Repo kökünde:
   ```powershell
   build.cmd Release
   powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1
   ```
3. SSMS'i aç, bir sunucuya bağlan.
4. **Extensions > SsmsToolkit > Settings** → **Result Grid Tools**'u aç → Kaydet. *(Varsayılan kapalı.)*

Örnek sorgu (yeni sorgu penceresinde, sonuç **Grid** modunda):

```sql
SELECT TOP (20)
    o.object_id, o.name, o.type_desc, o.create_date, o.is_ms_shipped,
    CAST(o.object_id AS decimal(18,2)) / 7 AS ratio,
    NEWID() AS guid_col, CAST(NULL AS int) AS null_col,
    N'O''Brien, "Bob"' AS tricky_text, 0x00FF AS bin_col
FROM sys.objects AS o;
```

## A. Sağ tık menüsü

- [ ] Grid'de birkaç hücre seç → sağ tık → menünün altında **Copy As (SsmsToolkit)** alt menüsü var: CSV, JSON, Markdown Table, INSERT Statements, IN (...) List. *(Yoksa bana yaz; menü ID'si SSMS içinden ilk kez deneniyor.)*
- [ ] Ayarlardan özelliği kapat → alt menü görünmüyor.
- [ ] SSMS'in kendi menü öğeleri (Copy, Copy with Headers, Save Results As...) eskisi gibi çalışıyor.

## B. Kopyalama formatları

Her biri için seç → kopyala → yeni bir sorgu penceresine veya Not Defteri'ne yapıştır. Durum çubuğunda "N satır X olarak kopyalandı" görünmeli.

- [ ] **CSV:** başlık satırı var; `tricky_text` çift tırnakla sarılı ve içteki `"` ikilenmiş; NULL boş; tarih `2026-...T...` biçiminde.
- [ ] **JSON:** sayılar tırnaksız, `is_ms_shipped` `true/false`, `null_col` `null`, metin kaçışları doğru.
- [ ] **Markdown:** tablo bir Markdown önizlemesinde (ör. VS Code) düzgün görünüyor; sayısal kolonlar sağa yaslı.
- [ ] **IN (...):** tek kolon (ör. `object_id`) seçince `IN (1, 2, ...)`; tekrar edenler bir kez; NULL yok. Metin kolonunda `IN (N'a', N'b')`.
- [ ] Kolonları sürükleyip sırasını değiştir → kopyalanan kolon sırası grid'deki sırayla aynı.
- [ ] Birden çok sonuç seti (iki SELECT) → ikinci grid'de seçim → kopyalanan veri ikinci grid'den.
- [ ] Hiç hücre seçmeden (veya sadece satır numarası kolonu) → "Önce bir sonuç grid'inde hücre seçin".

## B2. INSERT ifadeleri penceresi

Tek tablodan bir sorgu çalıştır (ör. `SELECT TOP (50) * FROM dbo.BirTablo`), birkaç satır seç → sağ tık → *Copy As (SsmsToolkit) > INSERT Statements...*

- [ ] Pencere açılıyor, tema SSMS ile uyumlu (koyu/açık).
- [ ] **Hedef tablo** sorgudaki tabloyla dolu geliyor; açılır listede sorgudaki diğer FROM tabloları var; elle değiştirilebiliyor (ör. `dbo.Kopya`) ve önizleme ~0,3 sn sonra güncelleniyor.
- [ ] **Satırlar:** *Seçili hücreler* ve *Sonucun tamamı* arasında geçiş önizlemeyi değiştiriyor.
- [ ] **INSERT başına satır** 1 → her satıra bir `INSERT`; 100 → çok satırlı `VALUES`.
- [ ] Identity kolonlu tabloda: *Identity / hesaplanan kolonları hariç tut* işaretliyken identity kolonu yok; işareti kaldırınca var ve *SET IDENTITY_INSERT ON/OFF ile sar* seçilebiliyor.
- [ ] *Adları [ ] ile tırnakla* kapatılınca köşeli parantezler kalkıyor.
- [ ] Metin `N'...'`, tarih `'...'`, `O''Brien` kaçışı, binary `0x...`, NULL doğru. Script **parse** ediliyor (Ctrl+F5).
- [ ] **Kopyala** → panoda tam script; durum satırında "N satır panoya kopyalandı".
- [ ] **Yeni Sorguda Aç** → yeni sorgu penceresi aynı sunucu ve veritabanına **bağlı** açılıyor, script içinde.
- [ ] **Dosyaya Kaydet...** → önerilen ad tablo adından (`dbo.Kopya.sql`); dosya UTF-8, Türkçe karakterler bozulmuyor.
- [ ] **İlerleme:** büyük bir sonuçta (ör. `SELECT * FROM sys.all_columns`, *Sonucun tamamı*) Kopyala → ilerleme çubuğu ve "Oluşturuluyor... x / y satır"; SSMS donmuyor; **İptal** çalışıyor.
- [ ] 5.000.000 hücreden büyük sonuçta uyarı çıkıyor, script oluşturulmuyor.

## C. Seçim istatistikleri (durum çubuğu)

- [ ] Sayısal kolonda birkaç hücre seç → SSMS'in **en alttaki** durum çubuğunda: `Adet: N   Farklı: N   Toplam: ...   Ort.: ...   Min: ...   Maks: ...`.
- [ ] Metin kolonunda yalnızca Adet ve Farklı görünüyor.
- [ ] NULL içeren seçimde `Adet: 3 / 5` (NULL olmayan / toplam).
- [ ] Tek hücre seçince istatistik yazılmıyor.
- [ ] Sürükleyerek seçerken takılma yok; bırakınca ~0,2 sn içinde güncelleniyor.
- [ ] Büyük sonuçta (ör. `SELECT * FROM sys.all_columns`) kolon başlığına tıkla veya tümünü seç → SSMS donmuyor; çok büyükse sonunda `(ilk 100.000 hücre)` yazıyor.
- [ ] Sayı biçimi Windows bölge ayarına uyuyor (TR: `1.234,5`).

## D. Kararlılık

- [ ] Sorguyu tekrar çalıştır (grid yeniden oluşur) → menü ve istatistikler yeni grid'de çalışıyor.
- [ ] Sorgu penceresini kapat/aç, sonuçları **Text** moduna al → hata yok.
- [ ] Log'da (`%LocalAppData%\SsmsToolkit\logs`) `result-grid` için hata yok.

## Bilinen sınırlar

- INSERT penceresinde tablo adı önerisi sorgu metninden gelir; sorgu birden çok tablo okuyorsa ilki seçilir, listeden değiştirin. Ad boşsa `[TableName]` yazılır.
- Identity / hesaplanan kolon bilgisi SSMS'in sonuç şemasından gelir; SSMS bu bilgiyi vermezse kolon normal kolon gibi görünür.
- Değerler SSMS'in grid'de tuttuğu haliyle kopyalanır: SSMS ayarındaki "en fazla karakter" sınırını aşan uzun metinler kesik gelir.
- Birden çok ayrı blok seçilirse (Ctrl ile), seçili satır ve kolonların kesişimi kopyalanır.
- 500.000 hücreden büyük seçim kopyalanmaz.

## Kaldırma

SSMS kapalıyken: `powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1 -Uninstall`
