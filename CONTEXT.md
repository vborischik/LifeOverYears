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
- Config: `appsettings.json`

---

## Architecture — 3 Layers + Entry Point

```
Console (Program.cs)
    ↓
Services  — business logic + interfaces (decides which provider to use)
    ↓
Providers — raw API connectors, no business logic
    ↓
Models    — data structures only, no logic, no dependencies
```

### Dependency Rules
- Models → no dependencies
- Providers → Models
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
│   │   ├── IVisionService.cs
│   │   ├── IPromptService.cs
│   │   ├── IImageService.cs
│   │   ├── IVideoService.cs
│   │   ├── ICaptionService.cs
│   │   ├── IPublicationService.cs
│   │   └── IStorageService.cs
│   ├── VisionService.cs
│   ├── PromptService.cs
│   ├── ImageService.cs
│   ├── VideoService.cs
│   ├── CaptionService.cs
│   ├── PublicationService.cs
│   ├── StorageService.cs
│   └── Pipeline.cs
├── Providers/
│   ├── NvidiaProvider.cs
│   ├── XaiProvider.cs
│   ├── FfmpegProvider.cs
│   ├── TelegramProvider.cs
│   └── DropboxProvider.cs
├── Program.cs
└── appsettings.json
```

---

## Providers

| Provider | Responsibility | API |
|----------|---------------|-----|
| NvidiaProvider | Vision (photo → SceneDNA) + Image generation | NVIDIA NIM |
| XaiProvider | Text completion (prompts, captions) | xAI API |
| FfmpegProvider | Video composition from images | Local FFmpeg |
| TelegramProvider | Publish video to Telegram channel | Telegram Bot API |
| DropboxProvider | Upload/download files | Dropbox API |

### NVIDIA Models
- Vision: `nvidia/nemotron-3-nano-omni-30b-a3b-reasoning`
- Image generation: `black-forest-labs/flux.2-klein-4b`

### Provider Swap Table
| Service | Today | Tomorrow |
|---------|-------|----------|
| VisionService | NvidiaProvider | XaiProvider |
| PromptService | XaiProvider | ? |
| ImageService | NvidiaProvider | ? |
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

## EraProfile Storage

Stored as JSON files, read by year:

```
data/eras/1955.json
data/eras/1975.json
data/eras/1985.json
data/eras/1995.json
data/eras/2005.json
data/eras/2015.json
```

---

## Current Status

| Layer | Status |
|-------|--------|
| Models | ✅ Done |
| Provider Interfaces | ✅ Done |
| Providers | ✅ Done |
| Service Interfaces | ⚠️ Files exist but empty |
| Services | ⚠️ Files exist but empty |
| Pipeline | ⚠️ Empty |
| Program.cs | ⚠️ Empty |
| appsettings.json | ⚠️ Empty |

---

## Next Step

Fill Service Interfaces (IVisionService, IPromptService, IImageService, IVideoService, ICaptionService, IPublicationService, IStorageService)

---

## Key Decisions Made

- Folders not separate projects for MVP (can migrate in ~15 min when needed)
- SceneDNA is immutable after population
- AI populates SceneDNA from a modern photo
- EraProfiles are JSON data files, not code
- Instagram, TikTok, YouTube excluded from MVP (Telegram only)
- No database in MVP
- No validation, queues, or billing in MVP
- appsettings.json for config (API keys etc.)
