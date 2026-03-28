# Witte nog? — Design Document
*"Witte nog? Nou wel."*

## Concept

Atomische notitietool waarbij elke notitie = één .md bestand met één `#` koptekst als titel.
WikiLinks (`[[...]]`) verbinden notities met datums en onderwerpen.
De Virtual Canvas stitcht losse bestanden samen tot één vloeiende pagina per dag of onderwerp.

## Tech Stack

- .NET 9 + .NET MAUI + Blazor Hybrid
- MediatR (CQRS), System.IO.Abstractions (testbare file access)
- TipTap (WYSIWYG markdown editor via JSInterop)
- xUnit, System.IO.Abstractions.TestingHelpers

## Architectuur

```
WitteNog/
├── src/
│   ├── WitteNog.Core/          # AtomicNote, interfaces (geen externe dependencies)
│   ├── WitteNog.Application/   # CQRS commands/queries via MediatR
│   ├── WitteNog.Infrastructure/# File storage, WikiLink parser, FileSystemWatcher
│   └── WitteNog.App/           # .NET MAUI + Blazor Hybrid, NavigationService, UI
└── tests/
    ├── WitteNog.Core.Tests/
    ├── WitteNog.Application.Tests/
    └── WitteNog.Infrastructure.Tests/
```

**Clean Architecture (Onion):** Core heeft nul externe dependencies. Application kent alleen Core via interfaces. Infrastructure implementeert de interfaces. App weet van alles via DI.

**CQRS:**
- Commands: `CreateNoteCommand`, `UpdateNoteCommand`, `DeleteNoteCommand`
- Queries: `GetNotesForDateQuery`, `GetNotesForTopicQuery`

**Event-driven:** `FileSystemWatcher` → `NoteChangedEvent` → UI ververst automatisch

## Domeinmodel

```csharp
public record AtomicNote(
    string Id,                       // slug (bestandsnaam zonder .md)
    string FilePath,                 // absoluut pad
    string Title,                    // inhoud van # koptekst
    string Content,                  // volledige markdown
    IReadOnlyList<string> WikiLinks, // alle [[...]] waarden
    DateTimeOffset LastModified
);
```

## Linking via WikiLinks

- `[[2026-03-18]]` → datum-link (regex: `^\d{4}-\d{2}-\d{2}$`)
- `[[ProjectX]]` → onderwerp-link
- Dagpagina: alle notities die `[[2026-03-18]]` bevatten
- Onderwerpspagina: alle notities die `[[ProjectX]]` bevatten

## Bestandsnaam strategie

Elke notitie wordt opgeslagen als `{slug}.md`. De slug is de koptekst lowercase met spaties vervangen door `-`. Voorbeeld: "Standup Notitie" → `standup-notitie.md`.

## UI

- **MAUI Shell**: native navigatie, tabbladen via `NavigationService`
- **BlazorWebView**: Virtual Canvas en notitie-blokken als Razor-componenten
- **TipTap**: WYSIWYG editor actief bij klikken op een notitie-blok
- **Shift+klik** op `[[WikiLink]]` → nieuw tabblad; normaal klikken → navigeer in huidig tabblad

## Ontwerpbeslissingen

| Beslissing | Keuze | Reden |
|-----------|-------|-------|
| Platform | MAUI + Blazor Hybrid | Cross-platform incl. mobiel, HTML/CSS flexibiliteit voor canvas |
| Linking | WikiLinks (geen YAML frontmatter) | Simpeler, Obsidian-compatibel, zichtbaar in tekst |
| Editor | TipTap via JSInterop | Rijkste WYSIWYG-optie voor Blazor |
| Editing | Inline in Canvas | Vloeiendere UX dan apart editor-tabblad |
| Storage | `System.IO.Abstractions` | Testbaar zonder echte schijf |
