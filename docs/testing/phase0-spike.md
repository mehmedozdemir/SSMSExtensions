# Faz 0 — Spike manuel test listesi

Build: `0.0.1-spike` (`feature/phase0-spike`). SSMS 22.12 üzerinde yapılır.

## Hazırlık

1. **SSMS'i kapat.**
2. Repo kökünde:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\copy-ssms-refs.ps1   # sadece ilk sefer
   build.cmd Release
   powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1
   ```
   Çıkış kodu `0` olmalı. Değilse `%TEMP%\dd_VSIXInstaller_*.log` dosyasının son kısmını bana gönder.
3. SSMS'i aç. Log dosyası: `%LocalAppData%\SsmsToolkit\logs\ssmstoolkit-YYYYMMDD.log`

## Soru 1 — Paket SSMS 22.12'de yükleniyor mu?

- [ ] SSMS hatasız açıldı (eklenti hata penceresi yok).
- [ ] **Extensions** (veya **Tools**) menüsünde **SsmsToolkit** alt menüsü var: *Spike: Show Active Connection*, *Spike: Open Test Window*, *Open Log Folder*.
- [ ] Log'da `Package loaded.` satırı ve altında yüklü DLL sürümleri var.

## Soru 2 — Doküman alanında tool window açılıyor mu?

- [ ] *SsmsToolkit > Spike: Open Test Window* → doküman alanında (sorgu sekmelerinin yanında) yeni bir sekme açılıyor.
- [ ] Komutu ikinci kez çalıştır → **ayrı** ikinci bir sekme açılıyor.
- [ ] Açık ve koyu temada okunaklı (Tools > Theme ile dene).

## Soru 3 — SQL editöründe mouse processor tetikleniyor mu?

1. Bir sunucuya bağlan, *New Query* aç, örn. `SELECT * FROM dbo.PersonelCardDetails` yaz.
2. Log'da `Text view created: contentType='...'` satırını bul. **Bu satırı bana gönder** (gerçek content type adı).
3. Tablo adının üzerine **Ctrl + Sol Tık** yap.

- [ ] Log'da `Ctrl+Click: contentType='...', sqlLike=True, identifier='dbo.PersonelCardDetails'` satırı var.
- [ ] Yeni bir **Spike: dbo.PersonelCardDetails** sekmesi açıldı.
- [ ] Ctrl+Tık normal kelime seçimini yapmadı (tıklamayı biz yakaladık).
- [ ] `sqlLike=False` görürsen: content type adını bana gönder.

## Soru 4 — Aktif bağlantı ve DB okunabiliyor mu?

Soru 3'te açılan sekmede (veya *Spike: Show Active Connection* ile):

- [ ] `Server`, `Database` doğru (editörün üstündeki DB seçicideki DB).
- [ ] Editörde DB'yi değiştir (`USE` veya açılır liste) → tekrar Ctrl+Tık → `Database` yeni DB'yi gösteriyor.
- [ ] `Own connection: OK` ve `OBJECT_ID(...)` dolu, `BaseType: U`.
- [ ] **SQL login** (ör. `webLogin`) ile tekrarla → `Own connection: OK`.
- [ ] **Windows auth** ile tekrarla → `Own connection: OK`.
- [ ] (Opsiyonel) Entra ID bağlantısı varsa: `Own connection: SKIPPED (Unsupported: ...)` görmelisin, SSMS'te giriş penceresi **açılmamalı**.

## Geri bildirim

Şunları gönder:
- Log dosyasının tamamı (parola içermez; yalnızca anahtar adları loglanır).
- Her kutucuk için evet/hayır.

## Kaldırma

SSMS kapalıyken: `powershell -ExecutionPolicy Bypass -File tools\install-vsix.ps1 -Uninstall`
