# Transcription Settings Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a language-selection popup after stopping a recording, plus vault-level settings for available languages and Whisper model.

**Architecture:** New `IVaultSettings` in Core; `JsonSettingsProvider` implements it alongside `ILinkMetadataService`; `RecordingWorkflowService` gains a `WaitingForLanguage` state between Recording and Transcribing; UI adds a language picker popup and settings section.

**Tech Stack:** C# .NET MAUI Blazor, Whisper.net 1.7.3, System.Text.Json, NAudio 2.2.1

---

## Task 1: Core — `TranscriptionSettings` record

**Files:**
- Create: `src/WitteNog.Core/Models/TranscriptionSettings.cs`

### Step 1: Create the file

```csharp
namespace WitteNog.Core.Models;

public record TranscriptionSettings
{
    /// <summary>ISO 639-1 language codes in user-preferred order. First entry = default in picker.</summary>
    public List<string> Languages { get; init; } = ["nl"];

    /// <summary>
    /// String name of the Whisper GgmlType enum: "Tiny" | "Base" | "Small" | "Medium" | "Large".
    /// Defaults to "Base" (~142 MB).
    /// </summary>
    public string Model { get; init; } = "Base";
}
```

### Step 2: Build

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: 0 errors, 0 warnings.

### Step 3: Commit

```bash
git add src/WitteNog.Core/Models/TranscriptionSettings.cs
git commit -m "feat: add TranscriptionSettings record to Core"
```

---

## Task 2: Core — `IVaultSettings` interface

**Files:**
- Create: `src/WitteNog.Core/Interfaces/IVaultSettings.cs`

### Step 1: Create the file

```csharp
namespace WitteNog.Core.Interfaces;

using WitteNog.Core.Models;

public interface IVaultSettings
{
    /// <summary>
    /// Returns the transcription settings for the given vault.
    /// Returns default <see cref="TranscriptionSettings"/> when the vault has no settings file.
    /// </summary>
    TranscriptionSettings GetTranscriptionSettings(string vaultPath);

    /// <summary>Persists the transcription settings for the given vault.</summary>
    void SaveTranscriptionSettings(string vaultPath, TranscriptionSettings settings);
}
```

### Step 2: Build to verify

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: 0 errors.

### Step 3: Commit

```bash
git add src/WitteNog.Core/Interfaces/IVaultSettings.cs
git commit -m "feat: add IVaultSettings interface to Core"
```

---

## Task 3: Core — Update `ITranscriptionService`

`GgmlType` is an Infrastructure type (Whisper.net); the interface stays in Core and uses `string` instead.

**Files:**
- Modify: `src/WitteNog.Core/Interfaces/ITranscriptionService.cs`

### Step 1: Update the interface

Replace the full file content:

```csharp
namespace WitteNog.Core.Interfaces;

public interface ITranscriptionService
{
    /// <summary>Returns true when the Whisper model file is present on disk.</summary>
    bool IsModelReady { get; }

    /// <summary>
    /// The Whisper model to use: "Tiny" | "Base" | "Small" | "Medium" | "Large".
    /// Set this before calling <see cref="EnsureModelAsync"/> so the correct model is downloaded.
    /// Defaults to "Base". Changing this after the factory has loaded causes a factory reload.
    /// </summary>
    string ConfiguredModel { get; set; }

    /// <summary>
    /// Downloads/verifies the configured Whisper model if not already present.
    /// Reports download progress via <paramref name="progress"/> (0.0–1.0).
    /// </summary>
    Task EnsureModelAsync(IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Transcribes the WAV file at <paramref name="wavFilePath"/> in the given ISO 639-1
    /// <paramref name="language"/> (e.g. "nl", "en") and returns the recognised text.
    /// Pass "auto" for automatic language detection.
    /// Returns <see cref="string.Empty"/> when nothing was recognised.
    /// </summary>
    Task<string> TranscribeAsync(
        string wavFilePath, string language, CancellationToken ct = default);
}
```

### Step 2: Build — expect compile errors from `WhisperTranscriptionService` (missing property + wrong `TranscribeAsync` signature)

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: errors in `WhisperTranscriptionService.cs` and `RecordingWorkflowService.cs`. These are fixed in subsequent tasks.

### Step 3: Commit the interface

```bash
git add src/WitteNog.Core/Interfaces/ITranscriptionService.cs
git commit -m "feat: add ConfiguredModel + language param to ITranscriptionService"
```

---

## Task 4: Infrastructure — Update `WhisperTranscriptionService`

**Files:**
- Modify: `src/WitteNog.Infrastructure/Audio/WhisperTranscriptionService.cs`

### Step 1: Understand the changes needed

- Add `ConfiguredModel` property (string → `GgmlType` parsing, default `"Base"`)
- When `ConfiguredModel` changes, dispose `_factory` so it reloads with the new model
- Update `IsModelReady` to check the correct model file path
- Update `EnsureModelAsync` to download the configured model
- Update `TranscribeAsync` to accept a `language` parameter and use it

Current model path is hardcoded to `ggml-base.bin`. Make it dynamic based on `ConfiguredModel`.

### Step 2: Replace the full file

```csharp
namespace WitteNog.Infrastructure.Audio;

using System.Text;
using Whisper.net;
using Whisper.net.Ggml;
using WitteNog.Core.Interfaces;

public sealed class WhisperTranscriptionService : ITranscriptionService, IAsyncDisposable
{
    private static readonly string ModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WitteNog", "models");

    private string _configuredModel = "Base";
    private GgmlType _ggmlType = GgmlType.Base;

    // Lazy-loaded; reused across calls. Disposed when ConfiguredModel changes.
    private WhisperFactory? _factory;
    private bool _disposed;

    public string ConfiguredModel
    {
        get => _configuredModel;
        set
        {
            if (_configuredModel == value) return;
            _configuredModel = value;
            _ggmlType = ParseGgmlType(value);
            // Invalidate factory so it reloads with the new model on next use
            _factory?.Dispose();
            _factory = null;
        }
    }

    private string ModelPath =>
        Path.Combine(ModelDir, $"ggml-{_ggmlType.ToString().ToLowerInvariant()}.bin");

    private string ModelTmpPath => ModelPath + ".tmp";

    public bool IsModelReady => File.Exists(ModelPath);

    public async Task EnsureModelAsync(
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsModelReady) return;

        Directory.CreateDirectory(ModelDir);

        if (File.Exists(ModelTmpPath))
            File.Delete(ModelTmpPath);

        using var modelStream = await WhisperGgmlDownloader
            .GetGgmlModelAsync(_ggmlType, QuantizationType.NoQuantization, ct);

        long? totalBytes = modelStream.CanSeek ? modelStream.Length : null;
        long bytesRead = 0;

        await using (var fs = File.Create(ModelTmpPath))
        {
            var buffer = new byte[81_920];
            int read;
            while ((read = await modelStream.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                bytesRead += read;
                if (progress is not null && totalBytes.HasValue)
                    progress.Report((double)bytesRead / totalBytes.Value);
            }
        }

        File.Move(ModelTmpPath, ModelPath, overwrite: false);
    }

    public async Task<string> TranscribeAsync(
        string wavFilePath, string language, CancellationToken ct = default)
    {
        if (!IsModelReady)
            throw new InvalidOperationException(
                "Whisper-model is niet beschikbaar. Roep EnsureModelAsync aan eerst.");

        _factory ??= WhisperFactory.FromPath(ModelPath);

        var builder = _factory.CreateBuilder();

        if (language == "auto")
            builder = builder.WithLanguageDetection();
        else
            builder = builder.WithLanguage(language);

        await using var processor = builder.Build();
        await using var fileStream = File.OpenRead(wavFilePath);
        var sb = new StringBuilder();

        await foreach (var segment in processor.ProcessAsync(fileStream, ct))
            sb.Append(segment.Text);

        return sb.ToString().Trim();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _factory?.Dispose();
        _factory = null;
        await ValueTask.CompletedTask;
    }

    private static GgmlType ParseGgmlType(string model) =>
        Enum.TryParse<GgmlType>(model, ignoreCase: true, out var result) ? result : GgmlType.Base;
}
```

### Step 3: Build

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: remaining errors only in `RecordingWorkflowService.cs` (wrong `TranscribeAsync` call).

### Step 4: Commit

```bash
git add src/WitteNog.Infrastructure/Audio/WhisperTranscriptionService.cs
git commit -m "feat: make WhisperTranscriptionService model and language configurable"
```

---

## Task 5: Infrastructure — Extend `JsonSettingsProvider`

**Files:**
- Modify: `src/WitteNog.Infrastructure/Settings/JsonSettingsProvider.cs`
- Modify: `tests/WitteNog.Infrastructure.Tests/Settings/JsonSettingsProviderTests.cs`

### Step 1: Write the failing tests first

Add these test methods to `JsonSettingsProviderTests.cs` (inside the existing class):

```csharp
[Fact]
public void GetTranscriptionSettings_ReturnsDefaults_WhenNoSettingsFile()
{
    var sut = new JsonSettingsProvider(_fs);
    var settings = sut.GetTranscriptionSettings(VaultPath);
    Assert.Equal(["nl"], settings.Languages);
    Assert.Equal("Base", settings.Model);
}

[Fact]
public void SaveTranscriptionSettings_Persists_AndIsReadBack()
{
    var sut = new JsonSettingsProvider(_fs);
    var saved = new TranscriptionSettings { Languages = ["en", "nl"], Model = "Small" };
    sut.SaveTranscriptionSettings(VaultPath, saved);

    var sut2 = new JsonSettingsProvider(_fs);
    var loaded = sut2.GetTranscriptionSettings(VaultPath);
    Assert.Equal(["en", "nl"], loaded.Languages);
    Assert.Equal("Small", loaded.Model);
}

[Fact]
public void SaveTranscriptionSettings_PreservesArchivedLinks()
{
    var sut = new JsonSettingsProvider(_fs);
    sut.SetArchivedStatus(VaultPath, "Projecten/Test", true);

    sut.SaveTranscriptionSettings(VaultPath,
        new TranscriptionSettings { Languages = ["de"], Model = "Tiny" });

    // Re-read using a fresh instance
    var sut2 = new JsonSettingsProvider(_fs);
    Assert.True(sut2.IsArchived(VaultPath, "Projecten/Test"));
    Assert.Equal(["de"], sut2.GetTranscriptionSettings(VaultPath).Languages);
}

[Fact]
public void SetArchivedStatus_PreservesTranscriptionSettings()
{
    var sut = new JsonSettingsProvider(_fs);
    sut.SaveTranscriptionSettings(VaultPath,
        new TranscriptionSettings { Languages = ["fr"], Model = "Medium" });

    sut.SetArchivedStatus(VaultPath, "Link", true);

    Assert.Equal("Medium", sut.GetTranscriptionSettings(VaultPath).Model);
}
```

Don't forget to add the `using` at the top of the test file:
```csharp
using WitteNog.Core.Models;
```

### Step 2: Run tests to confirm they fail

```
dotnet test tests/WitteNog.Infrastructure.Tests --filter "JsonSettingsProviderTests"
```

Expected: 4 new tests fail (compilation error or runtime — `GetTranscriptionSettings` doesn't exist yet).

### Step 3: Implement the changes in `JsonSettingsProvider.cs`

Key changes:
- Class implements `IVaultSettings` in addition to `ILinkMetadataService`
- Internal `VaultSettings` record gets a `Transcription` property
- `_archived` (HashSet) becomes derived from `_settings` (full `VaultSettings`)
- `EnsureLoaded` returns full `VaultSettings` and populates `_archivedSet`
- `Persist` writes the full `VaultSettings` (not just `ArchivedLinks`)

Replace the full file:

```csharp
using System.IO.Abstractions;
using System.Text.Json;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Infrastructure.Settings;

public class JsonSettingsProvider : ILinkMetadataService, IVaultSettings
{
    private readonly IFileSystem _fs;
    private string? _loadedVault;
    private VaultSettings _settings = new();
    // Derived from _settings.ArchivedLinks for O(1) case-insensitive lookup.
    private HashSet<string> _archivedSet = new(StringComparer.OrdinalIgnoreCase);

    public event Action? MetadataChanged;

    public JsonSettingsProvider(IFileSystem fs) => _fs = fs;

    private static string MetadataDir(string vaultPath) =>
        Path.Combine(vaultPath, ".metadata");

    private static string SettingsPath(string vaultPath) =>
        Path.Combine(MetadataDir(vaultPath), "vault-settings.json");

    private void MigrateIfNeeded(string vaultPath)
    {
        var oldPath = Path.Combine(vaultPath, "vault-settings.json");
        var newPath = SettingsPath(vaultPath);
        if (_fs.File.Exists(oldPath) && !_fs.File.Exists(newPath))
        {
            _fs.Directory.CreateDirectory(MetadataDir(vaultPath));
            _fs.File.Move(oldPath, newPath);
        }
    }

    private VaultSettings EnsureLoaded(string vaultPath)
    {
        if (_loadedVault == vaultPath) return _settings;
        MigrateIfNeeded(vaultPath);
        var path = SettingsPath(vaultPath);
        _settings = _fs.File.Exists(path)
            ? JsonSerializer.Deserialize<VaultSettings>(_fs.File.ReadAllText(path)) ?? new VaultSettings()
            : new VaultSettings();
        _archivedSet = new HashSet<string>(_settings.ArchivedLinks, StringComparer.OrdinalIgnoreCase);
        _loadedVault = vaultPath;
        return _settings;
    }

    private void Persist(string vaultPath)
    {
        // Sync the mutable _archivedSet back into the immutable record before writing.
        _settings = _settings with { ArchivedLinks = _archivedSet.OrderBy(l => l).ToList() };
        _fs.Directory.CreateDirectory(MetadataDir(vaultPath));
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        _fs.File.WriteAllText(SettingsPath(vaultPath), json);
    }

    // ── ILinkMetadataService ────────────────────────────────────────────────────

    public bool IsArchived(string vaultPath, string link)
    {
        EnsureLoaded(vaultPath);
        return _archivedSet.Contains(link);
    }

    public void SetArchivedStatus(string vaultPath, string link, bool archived)
    {
        EnsureLoaded(vaultPath);
        if (archived) _archivedSet.Add(link); else _archivedSet.Remove(link);
        Persist(vaultPath);
        MetadataChanged?.Invoke();
    }

    public IReadOnlySet<string> GetArchivedLinks(string vaultPath)
    {
        EnsureLoaded(vaultPath);
        return _archivedSet;
    }

    public void InvalidateCache(string vaultPath)
    {
        if (_loadedVault == vaultPath) _loadedVault = null;
    }

    // ── IVaultSettings ──────────────────────────────────────────────────────────

    public TranscriptionSettings GetTranscriptionSettings(string vaultPath) =>
        EnsureLoaded(vaultPath).Transcription;

    public void SaveTranscriptionSettings(string vaultPath, TranscriptionSettings settings)
    {
        EnsureLoaded(vaultPath);
        _settings = _settings with { Transcription = settings };
        Persist(vaultPath);
    }
}

internal record VaultSettings
{
    public List<string> ArchivedLinks { get; init; } = [];
    public TranscriptionSettings Transcription { get; init; } = new();
}
```

### Step 4: Run all tests

```
dotnet test tests/WitteNog.Infrastructure.Tests --filter "JsonSettingsProviderTests"
```

Expected: all 10 tests (6 existing + 4 new) pass.

```
dotnet test WitteNog.sln
```

Expected: full test suite green.

### Step 5: Commit

```bash
git add src/WitteNog.Infrastructure/Settings/JsonSettingsProvider.cs
git add tests/WitteNog.Infrastructure.Tests/Settings/JsonSettingsProviderTests.cs
git commit -m "feat: implement IVaultSettings in JsonSettingsProvider; persist full vault settings"
```

---

## Task 6: App — Update `MauiProgram.cs` DI registration

**Files:**
- Modify: `src/WitteNog.App/MauiProgram.cs`

### Step 1: Register `JsonSettingsProvider` as a concrete singleton, then register both interfaces pointing to it

The current registration creates a throw-away factory lambda that constructs a new `JsonSettingsProvider` per-request for `ILinkMetadataService`. `IVaultSettings` needs the **same instance**.

Replace the `ILinkMetadataService` registration and add `IVaultSettings`:

```csharp
// OLD (remove this):
builder.Services.AddSingleton<ILinkMetadataService>(sp =>
    new JsonSettingsProvider(sp.GetRequiredService<IIoFileSystem>()));

// NEW (add these two):
builder.Services.AddSingleton<JsonSettingsProvider>();
builder.Services.AddSingleton<ILinkMetadataService>(sp =>
    sp.GetRequiredService<JsonSettingsProvider>());
builder.Services.AddSingleton<IVaultSettings>(sp =>
    sp.GetRequiredService<JsonSettingsProvider>());
```

Also add the `IVaultSettings` using:
```csharp
using WitteNog.Core.Interfaces;
```
(Already present — `ITranscriptionService` etc. are in Core.Interfaces.)

### Step 2: Full updated registration block (for reference)

After the change, the relevant lines in `MauiProgram.cs` read:

```csharp
builder.Services.AddSingleton<IIoFileSystem, IoFileSystem>();
builder.Services.AddSingleton<NoteParser>();
builder.Services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
builder.Services.AddSingleton<INoteRepository>(sp =>
    new NoteRepository(
        sp.GetRequiredService<IIoFileSystem>(),
        sp.GetRequiredService<IWikiLinkParser>(),
        sp.GetRequiredService<NoteParser>()));
builder.Services.AddSingleton<IMarkdownStorage>(sp =>
    (IMarkdownStorage)sp.GetRequiredService<INoteRepository>());
builder.Services.AddSingleton<JsonSettingsProvider>();
builder.Services.AddSingleton<ILinkMetadataService>(sp =>
    sp.GetRequiredService<JsonSettingsProvider>());
builder.Services.AddSingleton<IVaultSettings>(sp =>
    sp.GetRequiredService<JsonSettingsProvider>());
builder.Services.AddSingleton<IAudioRecorder, AudioRecorderService>();
builder.Services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
builder.Services.AddSingleton<RecordingWorkflowService>();
```

### Step 3: Build

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: the only remaining error is in `RecordingWorkflowService.cs`.

### Step 4: Commit

```bash
git add src/WitteNog.App/MauiProgram.cs
git commit -m "feat: register IVaultSettings in DI; share singleton JsonSettingsProvider"
```

---

## Task 7: App — `KnownLanguages` helper

**Files:**
- Create: `src/WitteNog.App/Services/KnownLanguages.cs`

### Step 1: Create the file

This is shared by both the picker popup and the settings page.

```csharp
namespace WitteNog.App.Services;

/// <summary>
/// Whisper-ondersteunde talen beschikbaar voor selectie in de app.
/// Volgorde = weergavevolgorde in de "taal toevoegen" dropdown.
/// </summary>
public static class KnownLanguages
{
    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("nl", "Nederlands"),
        ("en", "Engels"),
        ("de", "Duits"),
        ("fr", "Frans"),
        ("es", "Spaans"),
        ("it", "Italiaans"),
        ("pt", "Portugees"),
        ("pl", "Pools"),
        ("ru", "Russisch"),
        ("tr", "Turks"),
        ("ja", "Japans"),
        ("zh", "Chinees"),
        ("ar", "Arabisch"),
        ("ko", "Koreaans"),
        ("auto", "Auto-detecteer"),
    ];

    /// <summary>Returns the display name for the given ISO code, or the code itself as fallback.</summary>
    public static string GetName(string code) =>
        All.FirstOrDefault(l => l.Code == code).Name ?? code;
}
```

### Step 2: Build

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

### Step 3: Commit

```bash
git add src/WitteNog.App/Services/KnownLanguages.cs
git commit -m "feat: add KnownLanguages helper with 15 supported Whisper languages"
```

---

## Task 8: App — Refactor `RecordingWorkflowService`

**Files:**
- Modify: `src/WitteNog.App/Services/RecordingWorkflowService.cs`

### Step 1: Understand all required changes

- Add `IVaultSettings` constructor parameter
- Add `WaitingForLanguage` to `WorkflowState` enum
- Add `PendingWavPath` property
- `StartAsync`: set `_transcription.ConfiguredModel` from vault settings before `EnsureModelAsync`
- `StopAsync`: stop recording, save WAV path, transition to `WaitingForLanguage` — stop here
- Add `ConfirmTranscriptionAsync(string language)`: run transcription, append to note, go to Idle
- Add `CancelTranscriptionAsync()`: delete pending WAV, go to Idle
- `TranscribeAsync` now takes a `language` string

### Step 2: Replace the full file

```csharp
namespace WitteNog.App.Services;

using MediatR;
using WitteNog.Application.Commands;
using WitteNog.Core.Interfaces;

/// <summary>
/// Singleton that owns the full record → transcribe → append workflow.
/// Components subscribe to <see cref="StateChanged"/> to stay in sync after re-renders.
/// </summary>
public class RecordingWorkflowService : IAsyncDisposable
{
    public enum WorkflowState { Idle, DownloadingModel, Recording, WaitingForLanguage, Transcribing }

    private readonly IAudioRecorder _recorder;
    private readonly ITranscriptionService _transcription;
    private readonly IMediator _mediator;
    private readonly VaultContextService _vault;
    private readonly IVaultSettings _vaultSettings;

    public WorkflowState State { get; private set; } = WorkflowState.Idle;
    public double DownloadProgress { get; private set; }
    public string? RecordingForFilePath { get; private set; }
    public string? PendingWavPath { get; private set; }
    public string? LastError { get; private set; }

    public event Action? StateChanged;

    public RecordingWorkflowService(
        IAudioRecorder recorder,
        ITranscriptionService transcription,
        IMediator mediator,
        VaultContextService vault,
        IVaultSettings vaultSettings)
    {
        _recorder = recorder;
        _transcription = transcription;
        _mediator = mediator;
        _vault = vault;
        _vaultSettings = vaultSettings;
    }

    public async Task StartAsync(string noteFilePath, CancellationToken ct = default)
    {
        if (State != WorkflowState.Idle)
            throw new InvalidOperationException(
                "Er loopt al een opname. Stop de huidige opname eerst.");

        LastError = null;

        // Apply model from vault settings before ensuring the model is ready.
        if (_vault.VaultPath is not null)
        {
            var ts = _vaultSettings.GetTranscriptionSettings(_vault.VaultPath);
            _transcription.ConfiguredModel = ts.Model;
        }

        if (!_transcription.IsModelReady)
        {
            State = WorkflowState.DownloadingModel;
            DownloadProgress = 0;
            StateChanged?.Invoke();

            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StateChanged?.Invoke();
            });
            await _transcription.EnsureModelAsync(progress, ct);
        }

        var recordingsDir = Path.Combine(
            _vault.VaultPath ?? Path.GetTempPath(), "recordings");
        await _recorder.StartAsync(recordingsDir, ct);
        RecordingForFilePath = noteFilePath;
        State = WorkflowState.Recording;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Stops the recording and transitions to <see cref="WorkflowState.WaitingForLanguage"/>.
    /// The WAV file is saved but transcription has not started yet.
    /// Call <see cref="ConfirmTranscriptionAsync"/> or <see cref="CancelTranscriptionAsync"/> next.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (State != WorkflowState.Recording)
            throw new InvalidOperationException("Er is geen actieve opname.");

        LastError = null;
        try
        {
            PendingWavPath = await _recorder.StopAsync(ct);
            State = WorkflowState.WaitingForLanguage;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            RecordingForFilePath = null;
            State = WorkflowState.Idle;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Transcribes the pending WAV with the chosen language and appends the result to the note.</summary>
    public async Task ConfirmTranscriptionAsync(string language, CancellationToken ct = default)
    {
        if (State != WorkflowState.WaitingForLanguage || PendingWavPath is null)
            throw new InvalidOperationException("Geen opname klaar voor transcriptie.");

        State = WorkflowState.Transcribing;
        StateChanged?.Invoke();

        var filePath = RecordingForFilePath;
        var wavPath = PendingWavPath;
        PendingWavPath = null;

        try
        {
            var text = await _transcription.TranscribeAsync(wavPath, language, ct);

            if (string.IsNullOrWhiteSpace(text))
                LastError = "Geen spraak herkend. Probeer opnieuw.";
            else if (filePath is not null)
                await _mediator.Send(
                    new AppendTranscriptionCommand(filePath, text, DateTimeOffset.Now, wavPath), ct);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            RecordingForFilePath = null;
            State = WorkflowState.Idle;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Discards the pending WAV and returns to Idle without transcribing.</summary>
    public Task CancelTranscriptionAsync()
    {
        if (State != WorkflowState.WaitingForLanguage)
            return Task.CompletedTask;

        if (PendingWavPath is not null)
            try { File.Delete(PendingWavPath); } catch { /* non-fatal */ }

        PendingWavPath = null;
        RecordingForFilePath = null;
        State = WorkflowState.Idle;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _recorder.DisposeAsync();
}
```

### Step 3: Build

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: 0 errors. `GlobalRecordingIndicator.razor` may show a warning if it references the missing `WaitingForLanguage` state (not yet) but there should be no compiler errors.

### Step 4: Run tests

```
dotnet test WitteNog.sln
```

Expected: all green (no tests cover RecordingWorkflowService directly).

### Step 5: Commit

```bash
git add src/WitteNog.App/Services/RecordingWorkflowService.cs
git commit -m "feat: add WaitingForLanguage state + ConfirmTranscriptionAsync/CancelTranscriptionAsync"
```

---

## Task 9: App — Language picker in `GlobalRecordingIndicator.razor`

**Files:**
- Modify: `src/WitteNog.App/Components/GlobalRecordingIndicator.razor`

### Step 1: Replace the full file

The component needs to:
- Inject `IVaultSettings` and `VaultContextService` to get the configured language list
- Show a language picker popup in `WaitingForLanguage` state
- Default selection = first configured language
- Call `ConfirmTranscriptionAsync` or `CancelTranscriptionAsync`

```razor
@inject WitteNog.App.Services.RecordingWorkflowService Workflow
@inject WitteNog.App.Services.VaultContextService VaultContext
@inject WitteNog.Core.Interfaces.IVaultSettings VaultSettings
@implements IDisposable
@using WitteNog.App.Services
@using WitteNog.App.Services

@if (Workflow.State != RecordingWorkflowService.WorkflowState.Idle || _error is not null)
{
    <div class="global-recording-indicator">
        @if (Workflow.State == RecordingWorkflowService.WorkflowState.DownloadingModel)
        {
            <span class="pulse-dot"></span>
            <span>Model downloaden... @(Workflow.DownloadProgress > 0 ? $"{Workflow.DownloadProgress:P0}" : "")</span>
        }
        else if (Workflow.State == RecordingWorkflowService.WorkflowState.Recording)
        {
            <span class="pulse-dot"></span>
            <span>Opname bezig</span>
            <button class="record-stop-global" @onclick="StopAsync">Stoppen</button>
        }
        else if (Workflow.State == RecordingWorkflowService.WorkflowState.WaitingForLanguage)
        {
            <div class="language-picker-popup">
                <span class="language-picker-label">Taal van de opname?</span>
                <select class="language-picker-select" @bind="_selectedLanguage">
                    @foreach (var (code, name) in _availableLanguages)
                    {
                        <option value="@code">@name</option>
                    }
                </select>
                <div class="language-picker-actions">
                    <button class="btn-primary" @onclick="ConfirmAsync">Transcriberen</button>
                    <button @onclick="CancelAsync">Annuleren</button>
                </div>
            </div>
        }
        else if (Workflow.State == RecordingWorkflowService.WorkflowState.Transcribing)
        {
            <span class="spinner"></span>
            <span>Transcriberen...</span>
        }

        @if (_error is not null)
        {
            <span class="record-error">@_error</span>
            <button class="record-error-dismiss" @onclick="() => _error = null" title="Sluiten">✕</button>
        }
    </div>
}

@code {
    private string? _error;
    private string _selectedLanguage = "nl";
    private IReadOnlyList<(string Code, string Name)> _availableLanguages = [];

    protected override void OnInitialized()
    {
        Workflow.StateChanged += OnStateChanged;
        RefreshLanguages();
    }

    private void OnStateChanged()
    {
        _error = Workflow.LastError;
        // Refresh available languages each time the picker appears (settings may have changed).
        if (Workflow.State == RecordingWorkflowService.WorkflowState.WaitingForLanguage)
            RefreshLanguages();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshLanguages()
    {
        if (VaultContext.VaultPath is null)
        {
            _availableLanguages = KnownLanguages.All;
            _selectedLanguage = "nl";
            return;
        }

        var ts = VaultSettings.GetTranscriptionSettings(VaultContext.VaultPath);
        _availableLanguages = ts.Languages
            .Select(code => (code, KnownLanguages.GetName(code)))
            .ToList();

        // Default = first configured language, or "nl" as fallback.
        _selectedLanguage = _availableLanguages.Count > 0
            ? _availableLanguages[0].Code
            : "nl";
    }

    private async Task StopAsync()
    {
        _error = null;
        try { await Workflow.StopAsync(); }
        catch (Exception ex)
        {
            _error = ex.Message;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ConfirmAsync()
    {
        _error = null;
        try { await Workflow.ConfirmTranscriptionAsync(_selectedLanguage); }
        catch (Exception ex)
        {
            _error = ex.Message;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task CancelAsync()
    {
        _error = null;
        await Workflow.CancelTranscriptionAsync();
    }

    public void Dispose() => Workflow.StateChanged -= OnStateChanged;
}
```

### Step 2: Build

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: 0 errors.

### Step 3: Commit

```bash
git add src/WitteNog.App/Components/GlobalRecordingIndicator.razor
git commit -m "feat: add language picker popup to GlobalRecordingIndicator for WaitingForLanguage state"
```

---

## Task 10: App — Transcription settings section in `SettingsPage.razor`

**Files:**
- Modify: `src/WitteNog.App/Pages/SettingsPage.razor`

### Step 1: Understand what to add

The settings page uses JS event delegation for the existing `data-action="save"` / `data-action="close"` buttons. The transcription section uses **standard Blazor** `@onclick`/`@bind` (no JS event delegation needed — these buttons are inside the same modal but don't need to go via JS).

New UI needed:
- Model selector dropdown (5 options with size info)
- Language list (ordered), each with ↑ ↓ ✕ buttons
- "Taal toevoegen" dropdown showing languages NOT yet in the list
- "Opslaan" button for the transcription section (separate from the vault path save)

Inject `IVaultSettings`. Load settings in `OnInitialized`. Save on click.

### Step 2: Replace the full file

```razor
@using WitteNog.App.Services
@using WitteNog.Core.Interfaces
@using WitteNog.Core.Models
@using Microsoft.JSInterop
@inject VaultContextService VaultContext
@inject IVaultSettings VaultSettings
@inject IJSRuntime JS
@implements IDisposable

<div class="modal-overlay">
    <div class="modal settings-modal" @ref="_modal">
        <h3>Instellingen</h3>

        @* ── Vault pad ───────────────────────────────────────────────── *@
        <div class="settings-row">
            <span class="settings-label">Huidige map:</span>
            <code class="settings-path">@VaultContext.VaultPath</code>
        </div>
        <div class="settings-row">
            <span class="settings-label">Nieuwe map:</span>
            <input id="settings-path-input"
                   class="onboarding-input"
                   type="text"
                   value="@_newPath" />
        </div>
        <div class="settings-actions">
            <button class="btn-primary" data-action="save">Opslaan</button>
            <button data-action="close">Sluiten</button>
        </div>
        @if (_error != null)
        {
            <p class="settings-error">@_error</p>
        }

        @* ── Transcriptie ─────────────────────────────────────────────── *@
        <hr class="settings-divider" />
        <h4 class="settings-section-title">Transcriptie</h4>

        <div class="settings-row">
            <span class="settings-label">Model:</span>
            <select class="settings-select" @bind="_transcriptionModel">
                <option value="Tiny">Tiny (39 MB, snelst)</option>
                <option value="Base">Base (142 MB, standaard)</option>
                <option value="Small">Small (461 MB)</option>
                <option value="Medium">Medium (1,5 GB)</option>
                <option value="Large">Large (2,9 GB, nauwkeurigst)</option>
            </select>
        </div>

        <div class="settings-row settings-row--column">
            <span class="settings-label">Beschikbare talen <small>(volgorde = dropdown-volgorde bij opname)</small>:</span>
            <ul class="language-list">
                @for (int i = 0; i < _languages.Count; i++)
                {
                    var idx = i; // capture for closures
                    var code = _languages[idx];
                    <li class="language-list-item">
                        <span class="language-name">@KnownLanguages.GetName(code) <code>(@code)</code></span>
                        <button class="lang-btn" title="Omhoog"
                                disabled="@(idx == 0)"
                                @onclick="() => MoveLanguage(idx, -1)">↑</button>
                        <button class="lang-btn" title="Omlaag"
                                disabled="@(idx == _languages.Count - 1)"
                                @onclick="() => MoveLanguage(idx, 1)">↓</button>
                        <button class="lang-btn lang-btn--remove" title="Verwijderen"
                                @onclick="() => RemoveLanguage(idx)">✕</button>
                    </li>
                }
            </ul>

            @if (_addableLanguages.Count > 0)
            {
                <div class="language-add-row">
                    <select class="settings-select settings-select--small" @bind="_languageToAdd">
                        @foreach (var (code, name) in _addableLanguages)
                        {
                            <option value="@code">@name</option>
                        }
                    </select>
                    <button class="btn-secondary" @onclick="AddLanguage">+ Toevoegen</button>
                </div>
            }
        </div>

        <div class="settings-actions">
            <button class="btn-primary" @onclick="SaveTranscriptionSettings">Transcriptie opslaan</button>
        </div>
        @if (_transcriptionSaved)
        {
            <p class="settings-success">Instellingen opgeslagen.</p>
        }
    </div>
</div>

@code {
    [Parameter] public EventCallback OnClose { get; set; }

    private ElementReference _modal;
    private DotNetObjectReference<SettingsPage>? _dotNetRef;
    private string _newPath = string.Empty;
    private string? _error;

    // Transcription settings state
    private string _transcriptionModel = "Base";
    private List<string> _languages = ["nl"];
    private string _languageToAdd = string.Empty;
    private bool _transcriptionSaved;

    private IReadOnlyList<(string Code, string Name)> _addableLanguages =>
        KnownLanguages.All
            .Where(l => !_languages.Contains(l.Code))
            .ToList();

    protected override void OnInitialized()
    {
        _newPath = VaultContext.VaultPath ?? string.Empty;
        LoadTranscriptionSettings();
    }

    private void LoadTranscriptionSettings()
    {
        if (VaultContext.VaultPath is null) return;
        var ts = VaultSettings.GetTranscriptionSettings(VaultContext.VaultPath);
        _transcriptionModel = ts.Model;
        _languages = [..ts.Languages];
        // Default add-picker to first addable language
        _languageToAdd = _addableLanguages.FirstOrDefault().Code ?? string.Empty;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("OnboardingDelegate.attach", _modal, _dotNetRef);
        }
    }

    [JSInvokable]
    public async Task HandleAction(string action)
    {
        if (action == "save")
            await SavePath();
        else if (action == "close")
            await OnClose.InvokeAsync();
    }

    private async Task SavePath()
    {
        _error = null;
        var typed = await JS.InvokeAsync<string>("getInputValue", "settings-path-input");
        var trimmed = typed.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        try
        {
            Directory.CreateDirectory(trimmed);
            Directory.CreateDirectory(Path.Combine(trimmed, ".metadata"));
            VaultContext.SetVaultPath(trimmed);
            await OnClose.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = $"Ongeldig pad: {ex.Message}";
            await InvokeAsync(StateHasChanged);
        }
    }

    private void MoveLanguage(int index, int direction)
    {
        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= _languages.Count) return;
        (_languages[index], _languages[newIndex]) = (_languages[newIndex], _languages[index]);
        _transcriptionSaved = false;
    }

    private void RemoveLanguage(int index)
    {
        _languages.RemoveAt(index);
        // Reset the add-picker in case a new language became addable
        _languageToAdd = _addableLanguages.FirstOrDefault().Code ?? string.Empty;
        _transcriptionSaved = false;
    }

    private void AddLanguage()
    {
        if (string.IsNullOrEmpty(_languageToAdd)) return;
        if (_languages.Contains(_languageToAdd)) return;
        _languages.Add(_languageToAdd);
        _languageToAdd = _addableLanguages.FirstOrDefault().Code ?? string.Empty;
        _transcriptionSaved = false;
    }

    private void SaveTranscriptionSettings()
    {
        if (VaultContext.VaultPath is null) return;
        VaultSettings.SaveTranscriptionSettings(VaultContext.VaultPath,
            new TranscriptionSettings
            {
                Languages = [.._languages],
                Model = _transcriptionModel,
            });
        _transcriptionSaved = true;
        InvokeAsync(StateHasChanged);
    }

    public void Dispose() => _dotNetRef?.Dispose();
}
```

### Step 3: Build

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: 0 errors.

### Step 4: Commit

```bash
git add src/WitteNog.App/Pages/SettingsPage.razor
git commit -m "feat: add transcription settings section (model + languages) to SettingsPage"
```

---

## Task 11: CSS — Styles for new UI elements

**Files:**
- Modify: `src/WitteNog.App/wwwroot/css/app.css`

### Step 1: Append these rules to the end of `app.css`

```css
/* ── Language picker popup (GlobalRecordingIndicator) ───────────────────── */
.language-picker-popup {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 10px 14px;
    background: var(--surface);
    border: 1px solid var(--accent);
    border-radius: 6px;
    min-width: 240px;
}

.language-picker-label {
    font-size: 0.85rem;
    color: var(--text-muted, #aaa);
}

.language-picker-select {
    background: var(--bg);
    color: var(--text, #eee);
    border: 1px solid var(--accent);
    border-radius: 4px;
    padding: 4px 8px;
}

.language-picker-actions {
    display: flex;
    gap: 8px;
}

/* ── Settings page transcription section ────────────────────────────────── */
.settings-divider {
    border: none;
    border-top: 1px solid rgba(255, 255, 255, 0.1);
    margin: 16px 0;
}

.settings-section-title {
    margin: 0 0 12px;
    font-size: 0.95rem;
    color: var(--accent);
}

.settings-row--column {
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
}

.settings-select {
    background: var(--bg);
    color: var(--text, #eee);
    border: 1px solid rgba(255, 255, 255, 0.2);
    border-radius: 4px;
    padding: 4px 8px;
    min-width: 200px;
}

.settings-select--small {
    min-width: 160px;
}

.language-list {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
    width: 100%;
}

.language-list-item {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 8px;
    background: rgba(255, 255, 255, 0.05);
    border-radius: 4px;
}

.language-name {
    flex: 1;
    font-size: 0.9rem;
}

.lang-btn {
    background: none;
    border: 1px solid rgba(255, 255, 255, 0.2);
    color: var(--text, #eee);
    border-radius: 3px;
    padding: 2px 6px;
    cursor: pointer;
    font-size: 0.8rem;
    line-height: 1.4;
}

.lang-btn:disabled {
    opacity: 0.3;
    cursor: default;
}

.lang-btn--remove {
    border-color: rgba(233, 69, 96, 0.4);
    color: var(--accent);
}

.lang-btn--remove:hover {
    background: rgba(233, 69, 96, 0.15);
}

.language-add-row {
    display: flex;
    align-items: center;
    gap: 8px;
}

.settings-success {
    color: #4caf50;
    font-size: 0.85rem;
    margin: 4px 0 0;
}
```

### Step 2: Build

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: 0 errors, 0 warnings.

### Step 3: Run full test suite

```
dotnet test WitteNog.sln
```

Expected: all tests green.

### Step 4: Commit

```bash
git add src/WitteNog.App/wwwroot/css/app.css
git commit -m "feat: add CSS for language picker popup and transcription settings section"
```

---

## Task 12: Final verification

### Step 1: Full build

```
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

Expected: **0 Warning(s), 0 Error(s)**.

### Step 2: Full test run

```
dotnet test WitteNog.sln
```

Expected: all tests pass.

### Step 3: Manual smoke test checklist

- [ ] App starts without crash
- [ ] Settings → "Transcriptie" section visible with model dropdown and language list
- [ ] Can add/remove/reorder languages and save
- [ ] Saved settings persist on next app start (check `{vault}/.metadata/vault-settings.json`)
- [ ] Start recording → pulserende stip
- [ ] Stop recording → language picker popup appears with configured languages in order
- [ ] Default selection = first language in the configured list
- [ ] Click "Transcriberen" → spinner "Transcriberen..." → transcription appears in note
- [ ] Click "Annuleren" → state goes back to Idle, no transcription, no WAV file left in temp
- [ ] Errors show in red with ✕ dismiss button; error persists after workflow returns to Idle

---

## Edge Cases (no code change needed — covered by design)

| Scenario | Behaviour |
|---|---|
| Vault path not set | `GetTranscriptionSettings` returns defaults; language picker shows ["nl"] |
| Unknown model string in JSON | `ParseGgmlType` falls back to `GgmlType.Base` |
| Model changed in settings | `WhisperFactory` disposed + recreated; old model file stays on disk |
| All languages removed in settings | Picker shows empty list; "Taal toevoegen" shows all 15 options |
| Cancel in language picker | WAV deleted from temp; state → Idle |
