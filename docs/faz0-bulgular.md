# Faz 0 — Bulgular

> Durum: SSMS içi ilk test (2026-11-13, SQL login, `Cards` tablosu) başarılı. Windows auth testi bekliyor (`docs/testing/phase0-spike.md`).
> Tarih: 2026-11-13. Ortam: SSMS 22.12.12405.243, Visual Studio Community 2026 Insiders 18.13.

## Makinede doğrulananlar (SSMS açmadan)

| Konu | Bulgu | Sonuç |
|---|---|---|
| VS shell sürümü | `Microsoft.VisualStudio.Shell.Framework` assembly **18.0.0.0**, dosya 18.12.1603. SSMS 22.12 **VS 18 (2026) shell'i** üzerinde. | §2'deki çelişki çözüldü. `Microsoft.VisualStudio.SDK` **18.11.1036** (host'tan eski, `ExcludeAssets=runtime`), `Microsoft.VSSDK.BuildTools` 18.12.1606. |
| Build ortamı | VS 2026 Insiders + VSSDK bileşeni, .NET Framework 4.8 targeting pack mevcut. | `build.cmd` çalışıyor, VSIX üretiliyor. |
| Bağlantı API'si | `SqlWorkbench.Interfaces` içinde `ISqlEditorService` (servis tipi `SSqlEditorService`) var: `GetCurrentConnection()` → `IDbConnection`, `GetActiveEditorDetails()`, `GetUIConnectionInfoForSpecificQueryEditor(moniker)`, `ConnectionChanged` event'i. Eski yol `ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo` da duruyor. | Birincil yol `ISqlEditorService`, yedek `ScriptFactory`. |
| `UIConnectionInfo` | `ServerName`, `UserName`, `Password`, `InMemoryPassword` (SecureString), `AuthenticationType` (int), `AdvancedOptions`. | SQL login için parola alınabiliyor (çalışma anında doğrulanacak). |
| SQL dil servisi | `RadLangSvc.pkgdef`: dil servisi adı **`SQL`**, `.sql` uzantısı. | Editör content type'ı muhtemelen `SQL` (legacy dil servisi adından türetilir). Spike `text` için kayıtlı olup gerçek adı logluyor. |
| Paylaşılan DLL'ler | `Microsoft.Data.SqlClient` 6.0.0.0 (IDE kökü), `Microsoft.SqlServer.TransactSql.ScriptDom` 18.0 (`Extensions\Application`), SMO/ConnectionInfo 18.100, `Newtonsoft.Json` 13.0.4.0 (binding redirect var), `System.Text.Json` 10.0 (`PublicAssemblies`). | Yeni NuGet bağımlılığı gerekmiyor. JSON için `Newtonsoft.Json` güvenli. |
| VSIX içeriği | Yalnızca `SsmsToolkit.dll`, pkgdef, manifest, LICENSE. SSMS/VS DLL'leri pakette yok. | Kural 6 sağlanıyor. |

## SSMS içinde doğrulanacaklar (kabul kriteri)

| # | Soru | Spike'taki karşılığı | Sonuç |
|---|---|---|---|
| 1 | Boş paket SSMS 22.12'de yükleniyor mu? | Menü + log'da `Package loaded.` | ✅ VSIXInstaller manifest'i kabul etti, paket yüklendi. |
| 2 | SQL editöründe mouse processor tetikleniyor mu (content type)? | `Text view created` / `Ctrl+Click` log satırları | ✅ Content type **`SQL`**. Ctrl+Tık `Cards` adını yakaladı. |
| 3 | Aktif bağlantı ve DB okunabiliyor mu (SQL login + Windows auth)? | Rapor sekmesi + `Own connection: OK` | ✅ SQL login: `ISqlEditorService` yeterli (yedek yol gerekmedi), DB=`FMS`, kendi bağlantı 89 ms, `OBJECT_ID` → `U`. Editörde DB değiştirilince yeni DB okunuyor ✅. ⏳ Windows auth. |
| 4 | Doküman alanında tool window açılıyor mu? | *Spike: Open Test Window* | ✅ Rapor sekmesi açıldı. |

## İlk testten öğrenilenler

- `GetCurrentConnection()` SSMS'in **kapalı** (`state=Closed`) `Microsoft.Data.SqlClient.SqlConnection` nesnesini döndürüyor. Bu nesneyi kullanmıyoruz; yalnızca connection string ayarlarını kopyalıyoruz. Bu doğru yaklaşım.
- SSMS'in connection string'inde `Password` anahtarı **dolu** (SQL login). Bu yüzden asla loglanmamalı. Şu an yalnızca anahtar adları loglanıyor.
- `Encrypt` / `Trust Server Certificate` SSMS'ten aynen alındığı için sertifika hatası çıkmadı.
- `AdvancedOptions` içinde `ConnectedEngineType`, `ConnectedServerVersion` gibi alanlar var. Bunlar Faz 1'de Azure SQL / on-prem ayrımı için kullanılabilir.

## Açık riskler / notlar

- **Content type:** Ctrl+Tık yalnızca adında "sql" geçen content type'larda yakalanıyor. Faz 1'de `[ContentType("SQL")]` olarak daraltılacak.
- **Spike kodu geçici:** `Features/Spike/` ve `UI/SpikeToolWindow` Faz 1'de `FeatureRegistry` + Table Inspector ile değiştirilecek. Spike metinleri resource dosyasında değil (kural 8 Faz 1'de uygulanacak).
- Entra ID/MFA bağlantılarında kendi bağlantımızı açmıyoruz (etkileşimli giriş penceresi riski); "Unsupported" raporlanıyor.
