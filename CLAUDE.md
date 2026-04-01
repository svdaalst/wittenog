# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build (Windows target only — required for MAUI)
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0

# Run the app
dotnet run --project src/WitteNog.App -f net9.0-windows10.0.19041.0

# Run all tests
dotnet test WitteNog.sln

# Run a single test class
dotnet test tests/WitteNog.Infrastructure.Tests --filter "NoteRepositoryTests"

# Run tests in a specific project
dotnet test tests/WitteNog.Application.Tests

# Run tests with code coverage
dotnet test WitteNog.sln --collect:"XPlat Code Coverage" --results-directory ./coverage-results

# Publish a runnable Windows exe
dotnet publish src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0 -c Release -o publish/windows
```

## Architecture

Clean Architecture with four layers — dependencies point inward only:

```
Core  ←  Application  ←  Infrastructure  ←  App (MAUI)
```

- **Core** (`WitteNog.Core`): Domain model and interfaces, zero external dependencies. `NoteParser` lives here (not Infrastructure) so Application commands can use it without a circular reference.
- **Application** (`WitteNog.Application`): MediatR CQRS handlers only. Three queries (`GetNotesForDateQuery`, `GetNotesForTopicQuery`, `GetAllWikiLinksQuery`), four commands (`CreateNoteCommand`, `UpdateNoteCommand`, `DeleteNoteCommand`, `AppendTranscriptionCommand`). All delegate persistence to `INoteRepository` / `IMarkdownStorage`. Also contains `NavigationService` (Application-layer tab model) and `LinkTreeBuilder` (sidebar tree logic).
- **Infrastructure** (`WitteNog.Infrastructure`): Implements Core interfaces. `NoteRepository` extends `MarkdownStorageService`. Uses `System.IO.Abstractions.IFileSystem` for testability. `VaultWatcher` wraps `FileSystemWatcher`. `AudioRecorderService` mixes mic + WASAPI loopback to WAV. `WhisperTranscriptionService` downloads and runs Whisper GGML models.
- **App** (`WitteNog.App`): .NET MAUI shell with `BlazorWebView`. Blazor Hybrid components, `NavigationService` (MAUI-specific tab state), `VaultWatcherService` (deduplicates file events), `RecordingWorkflowService` (owns the record → transcribe → append state machine).

**Note**: There are two `NavigationService` classes — `WitteNog.Application.Navigation.NavigationService` (Application layer, used in tests) and `WitteNog.App.Services.NavigationService` (implements `INavigationService`, used at runtime via DI).

## Domain Model

Every note is one `.md` file. The `AtomicNote` record is the single domain object:

```csharp
record AtomicNote(string Id, string FilePath, string Title, string Content,
                  IReadOnlyList<string> WikiLinks, DateTimeOffset LastModified);
```

- `Id` = filename without extension (slug)
- `Title` = extracted from the first `# heading` via regex `^#\s+(.+)$`
- `WikiLinks` = all `[[...]]` targets extracted and deduplicated
- WikiLinks matching `^\d{4}-\d{2}-\d{2}$` are date links → open as `DailyPage` tabs; all others → `TopicPage` tabs

## Key Patterns

**IFileSystem ambiguity**: `System.IO.Abstractions.IFileSystem` conflicts with `Microsoft.Maui.Storage.IFileSystem`. In `MauiProgram.cs` these are aliased:
```csharp
using IoFileSystem = System.IO.Abstractions.FileSystem;
using IIoFileSystem = System.IO.Abstractions.IFileSystem;
```

**DOM event delegation in Blazor**: `@onclick` and `@onchange` do NOT work in this MAUI BlazorWebView — events are silently swallowed. All interactive elements (wiki links, note actions, tab clicks, task completion) must use native DOM delegation attached via `ElementReference`. Never use `@onclick` or `@onchange` on buttons, checkboxes, or any other element; always use `data-action` + a JS delegate instead. Every delegate follows this pattern:
```csharp
// C# — attach on first render
await JS.InvokeVoidAsync("NoteBlockDelegate.attach", _container, _dotNetRef);

[JSInvokable]
public void HandleNoteAction(string action) { ... }
```
```javascript
// JS — single listener on the container, route by data-* attribute
window.NoteBlockDelegate = {
    attach(element, dotNetRef) {
        element.addEventListener('click', e => {
            const t = e.target.closest('[data-action]');
            if (t) dotNetRef.invokeMethodAsync('HandleNoteAction', t.dataset.action);
        });
    }
};
```

**Async event handlers in Blazor components**: CLR event subscriptions must NOT use `async void`. The correct pattern is:
```csharp
// ✓ correct
private void OnExternalChange(NoteChangedEvent _) => InvokeAsync(RefreshAsync);

// ✗ wrong — exceptions are swallowed
private async void OnExternalChange(NoteChangedEvent _) => await RefreshAsync();
```

**Markdown rendering**: `MarkdownRenderer.Render()` pre-processes `[[link]]` → `<span class="wiki-link" data-wikilink="...">` (HTML-encoded) before passing to Markdig. Audio file links produced by Markdig (`<a href="...wav">`) are then post-processed into `<span data-audiolink="...">` so WebView2 never navigates to them.

**Section splitting**: `NoteParser.SplitIntoSections()` splits only on `# H1` headings (not H2+). `UpdateNoteCommand` creates a new child `.md` file for each H1 section beyond the first, but only if the target slug does not already exist.

**Tab navigation**: `NavigationService` is a singleton managing a `List<TabViewModel>`. `OpenTab()` reuses an existing tab if one already has the same `Type+Query`; `OpenNewTab()` always creates a new one. Shift+click on a WikiLink calls `OpenNewTab`. `ActiveTab` returns `null` when the tab list is empty.

**VaultWatcherService** wraps `VaultWatcher` and re-initializes it when `StartWatching(path)` is called with a new path. It deduplicates rapid file-system events. `VirtualCanvas` and `WikiLinkSidebar` subscribe to `NoteChanged` / `MetadataChanged` and call `RefreshAsync()`.

**Recording workflow**: `RecordingWorkflowService` owns a state machine: `Idle → DownloadingModel → Recording → WaitingForLanguage → Transcribing → Idle`. Components observe the `StateChanged` event and render based on `Workflow.State`. The WAV is written to `<vault>/recordings/`. The transcribed text is appended via `AppendTranscriptionCommand`.

**Settings persistence**: `JsonSettingsProvider` (singleton) implements both `ILinkMetadataService` and `IVaultSettings`. Settings are cached per vault path in memory and written to `<vault>/.metadata/vault-settings.json`. Call `InvalidateCache(vaultPath)` after external changes.

## Testing

Tests use `MockFileSystem` from `System.IO.Abstractions.TestingHelpers` — never touch the real disk. Application layer tests use `FakeNoteRepository` (in `tests/WitteNog.Application.Tests/Fakes/`). Register `services.AddLogging()` before `AddMediatR()` in test DI setup (required by MediatR 14).

Classes that cannot be unit tested (require hardware or OS events): `AudioRecorderService`, `WhisperTranscriptionService`, `VaultWatcher`. These have 0% coverage intentionally.

## JavaScript / CSS

- `wwwroot/js/tiptap-bridge.js`: All JS delegates live here. TipTap editor lifecycle (`init`, `getContent`, `destroy`) keyed by a per-instance `id` attribute. `getContent` returns `editor.getText()` — raw text including markdown characters, not HTML. Falls back to a `<textarea>` if TipTap CDN is unavailable (both paths return raw markdown).
- `wwwroot/index.html`: Loads TipTap via CDN (`@tiptap/core` + `@tiptap/starter-kit` v2). Tracks `window._shiftPressed` via `keydown`/`keyup` for shift+click detection.
- `wwwroot/css/app.css`: Dark theme. Key variables: `--bg: #1a1a2e`, `--surface: #16213e`, `--accent: #e94560`. Notes render as a seamless document (no card borders) with `border-bottom` dividers between blocks.

## Vault Location

The user's notes are stored in `%USERPROFILE%\Documents\WitteNog` (resolved via `Environment.SpecialFolder.MyDocuments`). The directory and `recordings/` + `.metadata/` subdirectories are created on startup in `MainPage.razor`.
