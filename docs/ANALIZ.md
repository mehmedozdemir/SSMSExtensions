# SSMS 22 Eklentisi — Ön Analiz

> Durum: **Analiz / onay bekliyor.** Bu doküman onaylanmadan geliştirmeye başlanmayacak.
> Hedef: SQL Server Management Studio 22.12 (x64)
> Çalışma klasörü: `C:\AI\SSMSExtensions`

---

## 1. Amaç

SSMS 22 için birden fazla özelliği barındıran, özellikleri tek tek açılıp kapatılabilen, kendi ayar ekranı olan bir eklenti (çalışma adı: **SsmsToolkit**, isim değiştirilebilir).

İlk özellik: **Table Inspector.** Sorgu editöründe bir tablo adına **Ctrl + Sol Tık** yapınca DBeaver'daki gibi tablo detay ekranı açılır. Bu ekranda yapı, indeksler, FK'lar, veri ve DDL gösterilir.

İleride eklenecekler: Result grid'e özel özellikler (INSERT/JSON/IN olarak kopyalama, seçim toplamları vb.).

---

## 2. Teknik fizibilite: **Mümkün (resmi desteksiz)**

| Konu | Bulgu |
|---|---|
| Resmi destek | Microsoft SSMS 21/22'de 3. parti eklentileri **resmi olarak desteklemiyor**. Resmi VSIX/marketplace desteği "araştırılıyor", tarih yok. |
| Pratikte | SSMS 22 için çalışan çok sayıda açık kaynak eklenti var: SSMSPlus, SqlAssist.Ssms22, MSSQL Tool, SSMS Schema Folders, T-SQL Analyzer. Topluluk galerileri de var (ErikEJ SSMS Gallery, Open SSMS VSIX Gallery). |
| Kurulum | SSMS 22, kendi `VSIXInstaller.exe` aracıyla `.vsix` kurabiliyor (`C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe`). Per-user kurulum yeri: `%LocalAppData%\Microsoft\SSMS\22.0_<id>\Extensions`. |
| Manifest | `InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[21.0,23.0)"`, amd64. |
| Framework | .NET Framework 4.8, VSSDK (in-proc `AsyncPackage`). |
| Benzer özellik var mı? | SqlAssist.Ssms22'de "object preview" ve "F12 → definition" var. SSMSPlus'ta result grid menüsü var. Yani hedeflediğimiz her şeyin bir örneği SSMS 22'de çalışıyor. |

**Sonuç:** Hem Ctrl+Tık → Tablo Detayı hem de ileride result grid özellikleri teknik olarak yapılabilir. Ana risk, belgelenmemiş SSMS iç API'lerinin her güncellemede değişebilmesi (bkz. §8).

> ⚠️ Doğrulanacak çelişki: Kaynakların bir kısmı SSMS 22'nin VS 2022 (17.x) shell'i üzerinde olduğunu, bir kısmı VS 18 (2026) shell'i üzerinde olduğunu söylüyor. Faz 0'da `SSMS.exe`/`Microsoft.VisualStudio.Shell.Framework.dll` sürümüne bakıp hangi `Microsoft.VisualStudio.SDK` NuGet sürümünü referans alacağımızı netleştireceğiz.

---

## 3. Mimari

```
SsmsToolkit.sln
├─ src/
│  ├─ SsmsToolkit/                  ← VSIX projesi (AsyncPackage, manifest, komutlar, MEF export'ları)
│  │   ├─ Package/                  ← ToolkitPackage, komut tablosu (.vsct), menüler
│  │   ├─ Core/                     ← IFeature, FeatureRegistry, Settings, Logger
│  │   ├─ SsmsInterop/              ← SSMS'e özel kırılgan kodun TEK yeri (bağlantı, aktif editör, grid)
│  │   ├─ Features/
│  │   │   ├─ TableInspector/       ← Ctrl+Tık, isim çözümleme, detay penceresi
│  │   │   └─ (ileride) ResultGrid/
│  │   └─ UI/                       ← Ayarlar penceresi, ortak WPF stilleri (SSMS tema renkleri)
│  ├─ SsmsToolkit.Data/             ← Saf ADO.NET/metadata sorguları (SSMS bağımsız, test edilebilir)
│  └─ SsmsToolkit.Playground/       ← SSMS açmadan UI denemek için küçük WPF uygulaması
├─ tests/SsmsToolkit.Tests/         ← İsim çözümleme + metadata sorguları testleri
├─ lib/ssms22/                      ← Derleme için SSMS'ten kopyalanan referans DLL'ler (VSIX'e eklenmez)
├─ docs/
└─ build.cmd                        ← vswhere + msbuild ile tek komut build
```

### 3.1 Özellik altyapısı (açma/kapama)

```csharp
interface IFeature {
    string Id { get; }            // "table-inspector"
    string DisplayName { get; }
    string Description { get; }
    bool IsEnabled { get; }
    Task ActivateAsync(...);      // event/komut bağlar
    Task DeactivateAsync();       // bağları çözer
    FrameworkElement? CreateSettingsView(); // özelliğe özel ayarlar
}
```

- `FeatureRegistry` paket yüklenince ayarları okur ve açık olan özellikleri aktive eder.
- Bir özellik çalışırken beklenmeyen bir hata fırlatırsa **o özellik otomatik devre dışı kalır**, log'a yazılır ve kullanıcıya bir kez bilgi verilir. SSMS çökmez.
- Ayarlar JSON dosyasında tutulur: `%LocalAppData%\SsmsToolkit\settings.json`. SSMS'in kendi ayar deposuna bağımlı değiliz.

### 3.2 Ekranlar

1. **Menü:** `Extensions > SsmsToolkit` → *Ayarlar*, *Log*, *Hakkında*. Ayrıca özellik komutları burada olur.
2. **Ayarlar penceresi** (tool window): Sol tarafta özellik listesi ve aç/kapa anahtarları. Sağ tarafta seçili özelliğin ayarları. Genel ayarlar: log seviyesi, prod uyarısı.
3. **Tools > Options > SsmsToolkit:** Ayarlar penceresine giden kısa bir sayfa. İsteğe bağlı.
4. Arayüz SSMS'in açık/koyu temasına uyumlu olur (`VsBrushes` / `EnvironmentColors`).

---

## 4. Özellik 1 — Table Inspector

### 4.1 Akış

```
Ctrl+Sol Tık (SQL editörü)
  → tıklanan konumdaki tanımlayıcıyı bul
  → [db].[schema].[obje] veya alias çözümle
  → aktif editörün bağlantısını al (server, auth, current DB)
  → sys.objects ile doğrula (tablo / view / synonym)
  → Detay penceresini aç (yeni sekme)
  → sekmeleri arka planda, async doldur
```

### 4.2 Bileşenler

| Bileşen | Yaklaşım |
|---|---|
| **Tıklama yakalama** | MEF `IMouseProcessorProvider`, SQL content type'ı için (SSMS'teki gerçek content type adı Faz 0'da doğrulanacak). `PreprocessMouseLeftButtonDown` içinde Ctrl kontrolü yapılır, olay "handled" yapılır. |
| **Hover görünümü** (Faz 2) | Ctrl basılıyken tablo adının altı çizilir, imleç el olur (VS'deki "Go to Definition" hissi). `IKeyProcessor` ve adornment kullanılır. |
| **Alternatif tetikleyiciler** | Ctrl+Tık başka bir SSMS davranışıyla çakışabilir. Bu yüzden ayrıca editör sağ tık menüsünde **"Tabloyu İncele"** ve atanabilir bir klavye kısayolu olacak. Tetikleyici ayardan seçilebilir. |
| **İsim çözümleme** | `Microsoft.SqlServer.TransactSql.ScriptDom` (SSMS içinde zaten var) ile imlecin bulunduğu statement parse edilir. `NamedTableReference` ve alias'lar bulunur. Örnek: `FROM PersonelCardDetails p ... p.Id` içinde `p`'ye tıklanınca tablo açılır. Parse hatalı/yarım sorguda token tabanlı bir yedek çözümleyici devreye girer. |
| **Bağlantı bilgisi** | SSMS 21+ için `ISqlEditorService`, olmazsa `ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo` kullanılır (SSMSPlus aynı yolu izliyor). Buradan `UIConnectionInfo` ve aktif DB alınır. Kod `SsmsInterop` katmanında izole edilir. |
| **DB erişimi** | Eklenti kendi kısa ömürlü bağlantısını açar (SSMS'in sorgu bağlantısını kullanmaz, kullanıcının oturumunu kilitlemez). SSMS'in yüklediği `Microsoft.Data.SqlClient` kullanılır; ayrı bir kopya paketlenmez, bu sürüm çakışmasını önler. |
| **Detay penceresi** | Çoklu instance `ToolWindowPane`, doküman alanında yeni sekme olarak açılır. Her tablo kendi sekmesinde; aynı tablo tekrar açılırsa mevcut sekme öne gelir. İstenirse "yüzen pencere (popup)" modu ayardan seçilir. |

### 4.3 Detay ekranı sekmeleri

| Sekme | İçerik | Kaynak | Faz |
|---|---|---|---|
| **Özet** | Schema, tip, satır sayısı (yaklaşık), boyut, oluşturma/değişme tarihi, açıklama | `sys.objects`, `sys.dm_db_partition_stats`, extended properties | 1 |
| **Kolonlar** | Ad, tip(uzunluk), NULL, default, identity, computed, collation, PK işareti, açıklama | `sys.columns`, `sys.types`, `sys.default_constraints`, `sys.extended_properties` | 1 |
| **Anahtarlar** | PK ve UNIQUE constraint'ler, kolonlar, clustered/nonclustered | `sys.indexes`, `sys.index_columns` | 1 |
| **İndeksler** | Constraint'e bağlı olmayan indeksler: kolon sırası, include kolonları, filtre, tip | `sys.indexes`, `sys.index_columns` | 1 |
| **Constraint'ler** | CHECK ve DEFAULT constraint'ler: ad, kolon, tanım, kapalı/güvenilmez | `sys.check_constraints`, `sys.default_constraints` | 1 |
| **Foreign Key'ler** | Giden ve gelen FK'lar. Hedef tabloya tıklanınca o tablo açılır | `sys.foreign_keys`, `sys.foreign_key_columns` | 1 |
| **Veri** | `TOP N` grid (salt okunur), kolon başlığına göre sıralama, basit WHERE filtresi, "Sorgu olarak aç" | Kendi bağlantı, parametreli | 1 |
| **DDL** | `CREATE TABLE` scripti, kopyala / yeni sorgu penceresinde aç | SMO `Scripter` (SSMS içinde mevcut) | 1 |
| **Trigger'lar** | Trigger listesi ve tanımı | `sys.triggers`, `sys.sql_modules` | 2 |
| **Bağımlılıklar** | Bu tabloyu kullanan SP/view/function'lar ve tablonun bağımlılıkları | `sys.sql_expression_dependencies` | 2 |
| **İstatistikler / Partition** | İsteğe bağlı | `sys.stats`, `sys.partitions` | 3 |

### 4.4 Güvenlik ve performans kuralları

- **Salt okunur:** v1'de veri düzenleme yok.
- **Satır limiti:** Veri sekmesi varsayılan `TOP 200`, ayarlanabilir. Satır sayısı `COUNT(*)` ile değil, DMV'den yaklaşık alınır; büyük tablolarda tarama yapılmaz.
- **Sorgu ayarları:** Sorgularda `CommandTimeout` (varsayılan 15 sn) ve iptal butonu olur. Her şey async çalışır, SSMS UI thread'i bloklanmaz.
- **Kilit davranışı:** "Veri sekmesinde READ UNCOMMITTED kullan" ayarı varsayılan olarak **kapalı**.
- **Prod uyarısı:** Sunucu adı belirli kalıplara uyuyorsa (ör. `*Prod*`, ayarlanabilir) pencere başlığında renkli uyarı şeridi gösterilir.
- **Gizli bilgiler:** Parola/bağlantı bilgisi diske veya log'a yazılmaz.
- **Kapsam dışı:** Temp tablolar (`#x`) ve table variable'lar v1'de desteklenmez, "desteklenmiyor" bilgisi gösterilir.

---

## 5. İleride: Result Grid özellikleri (Faz 3+)

Örnek fikirler:
- Seçili hücreleri **INSERT / JSON / CSV / Markdown / `IN (...)` listesi** olarak kopyalama
- Seçimde toplam, ortalama, min/max, adet (durum çubuğunda)
- Seçili kolon değerine göre **Table Inspector'ı açma**: bir FK değerinden ilgili satıra gitmek

Teknik not: Grid'e erişim `Microsoft.SqlServer.GridControl` ve yansıma (reflection) ile olur. SSMSPlus bunu SSMS 22'de yapıyor. Bu kısım en kırılgan alan olduğu için ayrı bir özellik modülü olur ve varsayılan olarak kapalı başlar.

---

## 6. Geliştirme ortamı gereksinimleri (senin PC'n)

| Gereksinim | Not |
|---|---|
| Windows x64, **SSMS 22.12** | Mevcut. |
| **Visual Studio 2022 (17.14+) veya VS 2026** + **"Visual Studio extension development"** workload | VSSDK, MSBuild ve .NET Framework 4.8 targeting pack'i sağlar. Kurulu olup olmadığını teyit etmemiz gerekiyor. |
| SSMS DLL'lerinin `lib/ssms22/` altına kopyalanması | `SqlWorkbench.Interfaces`, `SqlPackageBase`, `Microsoft.SqlServer.ConnectionInfo`, `Microsoft.SqlServer.RegSvrEnum`, `Microsoft.SqlServer.GridControl`, `Microsoft.SqlServer.TransactSql.ScriptDom`, SMO. `tools/copy-ssms-refs.ps1` scripti bunları SSMS kurulumundan otomatik kopyalar. VSIX'e eklenmez, çalışma anında SSMS'ten yüklenir. **Microsoft DLL'leri olduğu için public repoya commit edilmez** (`.gitignore`). |
| Debug | VS'de debug hedefi `SSMS.exe` olur. Build sonrası VSIX, SSMS'in `VSIXInstaller.exe` aracıyla per-user kurulur (admin gerektirmez). Log dosyası `%LocalAppData%\SsmsToolkit\logs`. |

### Claude Code ile çalışma modeli
- Claude Code bu klasörde kodu yazar, `build.cmd` ile derler, unit testleri çalıştırır ve VSIX'i kurar.
- **SSMS içindeki davranış testi sende:** SSMS'i açıp Ctrl+Tık yapmak ve ekranı kontrol etmek. Claude Code SSMS arayüzünü kendisi deneyemez. Bu yüzden her fazın sonunda kısa bir **manuel test listesi** olur.
- Repoya bir `CLAUDE.md` eklenir: mimari kurallar, `SsmsInterop` izolasyonu, build ve kurulum komutları, "SSMS DLL'lerini VSIX'e koyma" gibi kurallar.

---

## 7. Fazlar

| Faz | Kapsam | Çıktı / Kabul kriteri |
|---|---|---|
| **0 — Spike (doğrulama)** | Boş paket SSMS 22.12'de yükleniyor mu? SQL editöründe mouse processor tetikleniyor mu (content type)? Aktif bağlantı ve DB okunabiliyor mu (SQL login + Windows auth)? Doküman alanında tool window açılıyor mu? | 4 sorunun hepsine "evet" ise devam edilir. Değilse alternatif yol (ör. sadece sağ tık/kısayol) değerlendirilir. **1–2 gün** |
| **1 — Altyapı + Table Inspector MVP** | Feature registry, ayarlar penceresi, log; Ctrl+Tık + sağ tık + kısayol; sekmeler: Özet, Kolonlar, İndeksler, FK, Veri, DDL | `FMS - Test` üzerinde `PersonelCardDetails` tıklanınca detay ekranı açılıyor. |
| **2 — Zenginleştirme** | Hover alt çizgi, alias çözümleme iyileştirmeleri, Trigger/Bağımlılık sekmeleri, Object Explorer sağ tık menüsünde "Tabloyu İncele" | |
| **3 — Result Grid modülü** | Kopyalama formatları, seçim istatistikleri | Varsayılan kapalı özellik olarak gelir. |
| **Sürekli** | SSMS güncellemesi sonrası smoke test listesi | |

---

## 8. Riskler

| Risk | Etki | Önlem |
|---|---|---|
| SSMS güncellemesi iç API'leri değiştirir | Özellik çalışmaz | Tüm SSMS'e özel kod `SsmsInterop` katmanında; hata olursa özellik kendini kapatır; test edilen SSMS sürümü README'de yazılır; güncelleme sonrası smoke test yapılır. |
| Microsoft resmi destek vermiyor | SSMS sorunlarında MS yardımı olmaz | Eklenti tek tıkla devre dışı bırakılabilir / kaldırılabilir. |
| Ctrl+Tık SSMS'te başka bir şeye bağlı | Çakışma | Tetikleyici ayarlanabilir; sağ tık ve kısayol alternatifleri olur. |
| Entra ID / MFA gibi auth tiplerinde ayrı bağlantı açmak zor | O bağlantılarda veri çekilemez | v1 hedefi SQL login + Windows auth (ekrandaki `webLogin` SQL login). Diğerleri Faz 0'da test edilir, gerekirse desteklenmiyor mesajı gösterilir. |
| Prod'da ağır sorgu | Performans | DMV tabanlı sayım, TOP limiti, timeout, iptal, prod uyarısı. |
| Assembly sürüm çakışması (SqlClient, Newtonsoft vb.) | Paket yüklenmez | SSMS'te bulunan DLL'ler paketlenmez; ek bağımlılık minimumda tutulur; JSON için `System.Text.Json` yerine SSMS'te mevcut olan kullanılır (Faz 0'da kontrol edilir). |
| Kurumsal makinelerde VSIX kurulum kısıtı | Dağıtım | Per-user kurulum (admin gerektirmez); gerekirse zip ile manuel kurulum. |

---

## 9. Alınan kararlar (2026-11-13)

| # | Konu | Karar |
|---|---|---|
| 1 | İsim | **SsmsToolkit** |
| 2 | Açılış şekli | Varsayılan **yeni doküman sekmesi**; ayardan **popup** seçilebilir |
| 3 | Faz 1 sekmeleri | Özet, Kolonlar, Anahtarlar, İndeksler, Constraint'ler, FK, Veri, DDL (2026-11-13: Anahtar/İndeks ayrımı ve Constraint sekmesi Faz 2'den Faz 1'e alındı; FK'dan tabloya gezinme de Faz 1'de yapıldı) |
| 4 | Veri sekmesi | **Salt okunur**; ayarlanabilir satır limiti, varsayılan **200** (`TOP (@n)`) |
| 5 | Hedef sürüm | Yalnızca **SSMS 22** (`[22.0,23.0)`) |
| 6 | Dağıtım | Şimdilik kişisel/ekip; **ileride açık kaynak**. Kod, yorum ve commit'ler İngilizce; kullanıcıya görünen metinler kaynak dosyasında (TR/EN eklenebilir). Lisans: **MIT** (değiştirilebilir) |
| 7 | Repo | **GitHub public repo**; sürüm yönetimi orada (SemVer, tag + GitHub Release, VSIX release asset olarak) |
| 8 | Ortam | Visual Studio + "Visual Studio extension development" workload **kurulu** |

### Sürüm yönetimi
- **SemVer:** `MAJOR.MINOR.PATCH`. Faz 1 bitene kadar `0.x`.
- **Branch'ler:** `main` her zaman build alır. Geliştirme `feature/<kısa-ad>` branch'lerinde yapılır, PR ile `main`'e alınır.
- **Commit mesajları:** Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`...).
- **Sürüm çıkarma:** `vX.Y.Z` tag'i atılır, `CHANGELOG.md` güncellenir, GitHub Release'e `.vsix` eklenir. GitHub Actions ile otomatik build Faz 1 sonunda değerlendirilir (SSMS DLL'leri repoda olmadığı için CI'da derleme ayrıca çözülmeli).

---

## Kaynaklar

- [SSMS FAQ – eklenti desteği durumu](https://learn.microsoft.com/ssms/faq)
- [SSMSPlus (SSMS 22 sürümü) – mimari ve build notları](https://github.com/alint997/SSMSPlus)
- [SSMSPlus 7.0 release – SSMS 22 kurulum](https://github.com/MishaT/SSMSPlus/releases/tag/7.0)
- [SqlAssist.Ssms22 – object preview, result grid](https://github.com/a73013110/SqlAssist.Ssms22)
- [SSMS SQL Formatter – manifest hedefi, uyarılar](https://github.com/ClaneSharke/SSMSSQLFormatter)
- [SSMS Schema Folders – SSMS 22 kurulum yeri](https://github.com/nicholas-ross/SSMS-Schema-Folders)
- [ErikEJ SSMS Extension Gallery](https://erikej.github.io/SsmsExtensions/)
- [Open SSMS VSIX Gallery – dev guide](https://ssmsgallery.azurewebsites.net/devguide)
- [ScriptFactory.CurrentlyActiveWndConnectionInfo (eski SSMS API dokümanı)](https://learn.microsoft.com/en-us/previous-versions/sql/sql-server-2012/ee636230(v=sql.110))
