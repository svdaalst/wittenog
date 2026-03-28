# Design: Taalkeuzepopup & Transcriptie-instellingen

**Datum:** 2026-03-20
**Status:** Goedgekeurd

---

## Overzicht

De gebruiker wil:
1. Na het stoppen van een opname een dropdown zien met de taal van de opname, vóórdat de transcriptie begint.
2. In de instellingenpagina kunnen configureren welke talen beschikbaar zijn (en in welke volgorde).
3. In de instellingenpagina het Whisper-model kunnen kiezen.

---

## Architectuur — Aanpak B: nieuwe `IVaultSettings`-service

Instellingen worden per vault opgeslagen in `{vault}/.metadata/vault-settings.json`.
Er wordt een nieuwe `IVaultSettings`-interface toegevoegd in Core; `JsonSettingsProvider` implementeert zowel `ILinkMetadataService` (ongewijzigd) als `IVaultSettings` (nieuw).

---

## Sectie 1 — Datamodel & opslag

### `vault-settings.json` (uitgebreid)

```json
{
  "archivedLinks": ["..."],
  "transcription": {
    "languages": ["nl", "en"],
    "model": "Base"
  }
}
```

### Nieuw record in Core

**`src/WitteNog.Core/Models/TranscriptionSettings.cs`**
```csharp
public record TranscriptionSettings
{
    public List<string> Languages { get; init; } = ["nl"];
    public string Model { get; init; } = "Base";
}
```

`Languages` bevat ISO 639-1 codes in de door de gebruiker gewenste volgorde.
`Model` is de string-naam van de Whisper `GgmlType`-enum ("Tiny", "Base", "Small", "Medium", "Large").

### Nieuwe interface in Core

**`src/WitteNog.Core/Interfaces/IVaultSettings.cs`**
```csharp
public interface IVaultSettings
{
    TranscriptionSettings GetTranscriptionSettings(string vaultPath);
    void SaveTranscriptionSettings(string vaultPath, TranscriptionSettings settings);
}
```

### Uitbreiding `JsonSettingsProvider`

`JsonSettingsProvider` implementeert `IVaultSettings` naast `ILinkMetadataService`.
De interne `VaultSettings`-record krijgt een extra property:
```csharp
internal record VaultSettings
{
    public List<string> ArchivedLinks { get; init; } = [];
    public TranscriptionSettings Transcription { get; init; } = new();
}
```

---

## Sectie 2 — State machine & service-interfaces

### `ITranscriptionService` (uitgebreid)

```csharp
public interface ITranscriptionService
{
    bool IsModelReady { get; }
    GgmlType ConfiguredModel { get; set; }   // ingesteld door RecordingWorkflowService
    Task EnsureModelAsync(IProgress<double>? progress = null, CancellationToken ct = default);
    Task<string> TranscribeAsync(string wavFilePath, string language, CancellationToken ct = default);
}
```

`ConfiguredModel` wordt door `RecordingWorkflowService` ingesteld vóór `EnsureModelAsync` zodat het juiste model wordt gedownload en geladen.
`WhisperFactory` wordt opnieuw aangemaakt als `ConfiguredModel` verandert ten opzichte van het vorige gebruik.

### `RecordingWorkflowService` — nieuwe toestand

```
Idle
  ↓ StartAsync(noteFilePath)
[eventueel DownloadingModel]
  ↓
Recording
  ↓ StopAsync()   ← WAV is opgeslagen, taal-picker tonen
WaitingForLanguage
  ↓ ConfirmTranscriptionAsync(language)   of   CancelTranscriptionAsync()
Transcribing
  ↓
Idle
```

**Nieuw in `RecordingWorkflowService`:**
- Property `PendingWavPath` — het pad naar het opgenomen WAV-bestand (ingesteld bij overgang naar `WaitingForLanguage`)
- `ConfirmTranscriptionAsync(string language)` — start transcriptie met gekozen taal
- `CancelTranscriptionAsync()` — verwijdert de WAV-file, keert terug naar `Idle`

**`StopAsync()` stopt na het opslaan van de WAV** en gaat naar `WaitingForLanguage`. De transcriptie-logica verhuist naar `ConfirmTranscriptionAsync()`.

---

## Sectie 3 — UI

### Taal-popup in `GlobalRecordingIndicator.razor`

Verschijnt wanneer `State == WaitingForLanguage`:

```
┌──────────────────────────────────────┐
│  Taal van de opname?                 │
│  [Nederlands ▼]                      │
│                                      │
│  [Transcriberen]    [Annuleren]      │
└──────────────────────────────────────┘
```

- De dropdown leest de geconfigureerde talen via `IVaultSettings`
- Toont volledige namen ("Nederlands", "Engels"), slaat ISO-code op ("nl", "en")
- Volgorde = de in instellingen geconfigureerde volgorde

### Instellingenpagina — sectie "Transcriptie"

Toegevoegd onderaan `SettingsPage.razor`:

```
── Transcriptie ──────────────────────────────

Model:
  [Base (142 MB, standaard) ▼]
  (Tiny 39 MB · Base 142 MB · Small 461 MB · Medium 1,5 GB · Large 2,9 GB)

Beschikbare talen  (volgorde = dropdown-volgorde):
  Nederlands (nl)   [↑] [↓] [✕]
  Engels (en)       [↑] [↓] [✕]
  [+ Taal toevoegen ▼]

[Opslaan]
```

**Beschikbare talen voor toevoegen** (vaste lijst, meest gebruikte Whisper-talen):

| Code | Naam          |
|------|---------------|
| nl   | Nederlands    |
| en   | Engels        |
| de   | Duits         |
| fr   | Frans         |
| es   | Spaans        |
| it   | Italiaans     |
| pt   | Portugees     |
| pl   | Pools         |
| ru   | Russisch      |
| tr   | Turks         |
| ja   | Japans        |
| zh   | Chinees       |
| ar   | Arabisch      |
| ko   | Koreaans      |
| auto | Auto-detecteer|

Talen die al in de lijst staan worden niet opnieuw aangeboden in de "toevoegen"-dropdown.

---

## Wijzigingen per laag

| Laag           | Bestand                                              | Wijziging                                              |
|----------------|------------------------------------------------------|--------------------------------------------------------|
| Core           | `Interfaces/IVaultSettings.cs`                       | Nieuw                                                  |
| Core           | `Models/TranscriptionSettings.cs`                    | Nieuw                                                  |
| Core           | `Interfaces/ITranscriptionService.cs`                | `ConfiguredModel` property + taalparameter `TranscribeAsync` |
| Infrastructure | `Settings/JsonSettingsProvider.cs`                   | Implementeert `IVaultSettings`, `VaultSettings` uitgebreid |
| Infrastructure | `Audio/WhisperTranscriptionService.cs`               | Respecteert `ConfiguredModel`, taal via parameter      |
| App            | `Services/RecordingWorkflowService.cs`               | Nieuwe toestand `WaitingForLanguage`, nieuwe methoden  |
| App            | `Components/GlobalRecordingIndicator.razor`          | Taal-popup voor `WaitingForLanguage`-staat             |
| App            | `Pages/SettingsPage.razor`                           | Nieuwe sectie "Transcriptie"                           |
| App            | `MauiProgram.cs`                                     | `IVaultSettings` registreren                           |

---

## Randgevallen

- **Model veranderd in instellingen:** `WhisperFactory` wordt opnieuw aangemaakt; het oude modelbestand wordt *niet* automatisch verwijderd (te destructief).
- **Annuleren in de taal-picker:** WAV-bestand wordt verwijderd; staat terug naar `Idle`.
- **Vault niet ingesteld:** `GetTranscriptionSettings` retourneert `new TranscriptionSettings()` met standaardwaarden.
- **Onbekende model-string in JSON:** Terugvallen op `GgmlType.Base`.
