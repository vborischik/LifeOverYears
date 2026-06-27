# LifeOverYears — Claude Context

## Project Summary

AI-powered platform that transforms modern photographs into historically inspired videos.

Pipeline: Modern Photo → SceneDNA → Prompt → Historical Images → Video → Caption → Publication

---

## Stack

- .NET 10, console application
- Single solution, single project with folders (not separate projects)
- Solution file: `LifeOverYears.slnx`
- Project path: `src/LifeOverYears/`
- Config: `appsettings.json` (gitignored)

---

## Architecture — 4 Layers + Entry Point

```
Console (Program.cs)
    ↓
Services         — business logic + orchestration (never touch HTTP)
    ↓
Domain Providers — know one model/workflow, build requests, parse responses
    ↓
Transport Providers — pure HTTP/process connectors, no business logic
    ↓
Models           — data structures only, no logic, no dependencies
```

### Dependency Rules
- Models → no dependencies
- Transport Providers → Models
- Domain Providers → Models + Transport Provider interfaces
- Services → Models + Provider interfaces (never concrete classes)
- Console → everything (composition root)

---

## Folder Structure

```
src/LifeOverYears/
├── Models/
│   ├── SceneDna.cs
│   ├── EraProfile.cs
│   ├── Prompt.cs
│   ├── HistoricalImage.cs
│   ├── Video.cs
│   ├── Caption.cs
│   └── Publication.cs
├── Services/
│   ├── Interfaces/
│   │   ├── INvidiaProvider.cs
│   │   ├── IXaiProvider.cs
│   │   ├── IFfmpegProvider.cs
│   │   ├── ITelegramProvider.cs
│   │   ├── IDropboxProvider.cs
│   │   ├── IFileSystemProvider.cs
│   │   ├── IJsonProvider.cs
│   │   ├── IVisionProvider.cs
│   │   ├── IImageProvider.cs
│   │   ├── IDataService.cs
│   │   ├── IVisionService.cs
│   │   ├── IPromptService.cs
│   │   ├── IImageService.cs
│   │   ├── IVideoService.cs
│   │   ├── ICaptionService.cs
│   │   ├── IPublicationService.cs
│   │   └── IStorageService.cs
│   ├── VisionService.cs
│   ├── DataService.cs
│   ├── SceneDnaValidator.cs
│   ├── PromptService.cs       ← stub
│   ├── ImageService.cs        ← stub
│   ├── VideoService.cs        ← stub
│   ├── CaptionService.cs      ← stub
│   ├── PublicationService.cs  ← stub
│   ├── StorageService.cs      ← stub
│   └── Pipeline.cs
├── Providers/
│   ├── NvidiaProvider.cs
│   ├── XaiProvider.cs
│   ├── FfmpegProvider.cs
│   ├── TelegramProvider.cs
│   ├── DropboxProvider.cs
│   ├── FileSystemProvider.cs
│   ├── JsonProvider.cs
│   ├── VisionProvider.cs
│   └── ImageProvider.cs
├── data/
│   ├── prompts/
│   │   └── vision.txt
│   ├── eras/
│   │   └── {year}.json
│   └── scenes/
│       └── {id}.json
├── Program.cs
└── appsettings.json
```

---

## Transport Providers

Pure connectors. No model knowledge. No business logic.

| Provider | Responsibility | API |
|----------|---------------|-----|
| NvidiaProvider | PostAsync(url, body) + PollAsync(url) with Bearer auth | NVIDIA NIM |
| XaiProvider | CompleteAsync(prompt) — chat completions | xAI API |
| FfmpegProvider | ComposeAsync(images) — ffmpeg CLI process | Local FFmpeg |
| TelegramProvider | SendVideoAsync(video, caption) — multipart upload | Telegram Bot API |
| DropboxProvider | UploadAsync / DownloadAsync — file storage | Dropbox API v2 |
| FileSystemProvider | ReadAllText / WriteAllText / Exists / List / Delete | System.IO |
| JsonProvider | Serialize\<T\> / Deserialize\<T\> / TryDeserialize\<T\> | System.Text.Json |

---

## Domain Providers

Know one AI model or workflow. Build request bodies, parse responses, return domain objects.

| Provider | Model | Output |
|----------|-------|--------|
| VisionProvider | nvidia/nemotron-3-nano-omni-30b-a3b-reasoning | SceneDna |
| ImageProvider | black-forest-labs/flux-dev | HistoricalImage |

VisionProvider also implements `EnrichAsync(photoPath, current, missingFields)` — re-analyzes when SceneDna is incomplete.

---

## Provider Swap Table

| Service | Today | Tomorrow |
|---------|-------|----------|
| VisionService | VisionProvider → NvidiaProvider | XaiVisionProvider |
| PromptService | XaiProvider | ? |
| ImageService | ImageProvider → NvidiaProvider | ? |
| VideoService | FfmpegProvider | RunwayProvider |
| CaptionService | XaiProvider | ? |
| PublicationService | TelegramProvider | InstagramProvider |
| StorageService | DropboxProvider | S3Provider |

---

## Models (all records, init-only, no logic)

```csharp
SceneDna(Id, CreatedAt, Camera, Geometry, Environment, ImmutableElements)
  Camera(Height, Direction, Fov)
  Geometry(Roads, Sidewalks, Buildings)
  Building(Type, Position)
  Environment(Terrain, Utilities)

EraProfile(Year, Vehicles, ArchitectureStyles, Brands, SignageStyles, Fashion, Technology)

Prompt(Id, SceneDnaId, Year, Text, CreatedAt)

HistoricalImage(Id, PromptId, Year, FilePath, Provider, CreatedAt)

Video(Id, ImageIds, FilePath, CreatedAt)

Caption(Id, Title, Description, Hashtags)

Publication(Id, VideoId, CaptionId, Platform, Url, PublishedAt)
```

---

## VisionService Flow

```
photoPath
    │
    ├─ DataService.LoadPromptAsync("vision")       → data/prompts/vision.txt
    ├─ VisionProvider.AnalyzeImageAsync(...)        → SceneDna
    ├─ SceneDnaValidator.Validate(sceneDna)         → missing fields list
    ├─ if missing: VisionProvider.EnrichAsync(...)  → corrected SceneDna
    └─ DataService.SaveSceneDnaAsync(sceneDna)      → data/scenes/{id}.json
```

---

## Data Files

```
data/prompts/{name}.txt    plain text prompts, loaded at runtime
data/eras/{year}.json      EraProfile for a given year
data/scenes/{id}.json      SceneDna persisted after analysis
```

---

## Current Status

| Component | Status |
|-----------|--------|
| Models | ✅ Done |
| Transport Providers | ✅ Done |
| Domain Providers | ✅ Done (VisionProvider + ImageProvider) |
| Service Interfaces | ✅ Done |
| VisionService | ✅ Done (analyze → validate → enrich → save) |
| DataService | ✅ Done |
| SceneDnaValidator | ✅ Done |
| Pipeline | ✅ Step 1 done |
| Program.cs | ⚠️ Not wired up |
| Other Services | ⚠️ Empty stubs |

---

## Next Step

Wire up Program.cs, then implement PromptService (Step 2)

---

## Key Decisions Made

- 4-layer architecture: transport providers are pure connectors, domain providers know one model
- Folders not separate projects for MVP (can migrate in ~15 min when needed)
- SceneDNA is immutable after population (id + createdAt preserved on enrich)
- AI populates SceneDNA from a modern photo; validator catches default fallback values
- EraProfiles and prompts are JSON/text data files, not code
- Instagram, TikTok, YouTube excluded from MVP (Telegram only)
- No database in MVP
- No DI framework — manual wiring in Program.cs
- appsettings.json gitignored; secrets via environment variables (NVIDIA_API_KEY)
