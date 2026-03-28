# WitteNog

A markdown-based note-taking app built with .NET MAUI and Blazor Hybrid. Notes are stored as plain `.md` files in a local vault folder, making them fully portable and editor-agnostic.

## Features

- **Markdown notes** – Create and edit notes using a rich TipTap editor; files are stored as plain Markdown
- **WikiLinks** – Link notes together with `[[link-name]]` syntax; links open in tabs
- **Tab-based navigation** – Multiple notes open simultaneously, with date-based and topic-based pages
- **Audio recording & transcription** – Record from microphone (with WASAPI loopback for system audio); transcribed locally via OpenAI Whisper (no cloud dependency)
- **Task tracking** – Parse and manage `- [ ] task` items across notes; view all tasks in a dashboard
- **Live vault watching** – Detects external file changes and updates the UI automatically
- **Link tree sidebar** – Browse all WikiLinks in the vault as a tree
- **Per-vault settings** – Model selection, link colors, and metadata stored in `<vault>/.metadata/`

## Tech Stack

| Layer | Libraries |
|---|---|
| UI | .NET MAUI Blazor Hybrid, TipTap 2 (CDN) |
| CQRS | MediatR 14 |
| Markdown | Markdig |
| Transcription | Whisper.net 1.7.3 (GGML models, runs locally) |
| Audio | NAudio 2 (Windows WASAPI) |
| File system | System.IO.Abstractions (testable) |
| Settings | System.Text.Json |

## Architecture

Clean Architecture with four layers; dependencies point inward only.

```
WitteNog.Core           Domain models, interfaces, parsers — zero external dependencies
WitteNog.Application    MediatR commands and queries (CQRS)
WitteNog.Infrastructure File storage, audio, Whisper transcription, vault watcher
WitteNog.App            MAUI + Blazor Hybrid UI, DI wiring
```

## Getting Started

**Prerequisites**

- Windows 10 (build 19041) or later
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- MAUI workload: `dotnet workload install maui`

**Run**

```bash
dotnet run --project src/WitteNog.App -f net9.0-windows10.0.19041.0
```

**Build**

```bash
dotnet build src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0
```

**Publish (self-contained Windows exe)**

```bash
dotnet publish src/WitteNog.App/WitteNog.App.csproj -f net9.0-windows10.0.19041.0 -c Release -o publish/windows
```

On first launch the app asks you to select a vault folder. Notes are stored there as `.md` files.

## Testing

```bash
# All tests
dotnet test WitteNog.sln

# Single project
dotnet test tests/WitteNog.Application.Tests

# Single test class
dotnet test tests/WitteNog.Infrastructure.Tests --filter "NoteRepositoryTests"

# With code coverage
dotnet test WitteNog.sln --collect:"XPlat Code Coverage" --results-directory ./coverage-results
```

## Project Structure

```
src/
  WitteNog.Core/            Domain models, interfaces, parsers
  WitteNog.Application/     MediatR commands, queries, navigation service
  WitteNog.Infrastructure/  Storage, WikiLink parser, audio, Whisper, vault watcher
  WitteNog.App/             MAUI Blazor app (pages, components, wwwroot)
tests/
  WitteNog.Core.Tests/
  WitteNog.Application.Tests/
  WitteNog.Infrastructure.Tests/
publish/                    Output directory for Windows builds
icons/                      App icons
docs/                       Design documents
```
