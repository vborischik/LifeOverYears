# Architecture

## Overview

LifeOverYears is built around a four-layer architecture that separates entry point, business logic, domain-specific AI connectors, and raw transport connectors.

The architecture is designed to remain stable as AI providers evolve.

Swapping a transport provider requires changing only the relevant Domain Provider. Swapping a model requires changing only the Domain Provider that knows it. Services never touch HTTP.

---

## Layers

```
┌─────────────────────────────────────────────────────────────────┐
│                           Console                               │
│         Entry point. Reads env vars, args, wires everything.    │
└───────────┬─────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────┐
│                          Services                               │
│                                                                 │
│   Interfaces            Implementations        Utilities        │
│   ──────────────        ────────────────       ─────────        │
│   IVisionService    →   VisionService          Pipeline         │
│   IPromptService    →   PromptService          SceneDnaValidator│
│   IImageService     →   ImageService           DataService      │
│   IVideoService     →   VideoService           GenerationContext│
│   ICaptionService   →   CaptionService         PromptSmokeTest  │
│   IPublicationService→  PublicationService                      │
│   IStorageService   →   StorageService                          │
│   IDataService      →   DataService                             │
│                                                                 │
│   Never touch HTTP. Depend on interfaces only.                  │
└───────────┬─────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Domain Providers                           │
│                                                                 │
│   VisionProvider   — knows nvidia vision model, builds request, │
│                      parses SceneDna from response              │
│   ImageProvider    — targets OpenAI GPT Image 1.5 (provider     │
│                      implementation pending), parses            │
│                      HistoricalImage from response              │
│                                                                 │
│   Know endpoints, model names, request/response shapes.         │
│   Depend on transport providers via interfaces.                 │
└───────────┬─────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Transport Providers                          │
│                                                                 │
│   NvidiaProvider    raw HTTP: PostAsync, PollAsync              │
│   XaiProvider       raw HTTP: chat completions                  │
│   FfmpegProvider    local process: ffmpeg CLI                   │
│   TelegramProvider  raw HTTP: Telegram Bot API                  │
│   DropboxProvider   raw HTTP: Dropbox API v2                    │
│   FileSystemProvider  File.ReadAll / WriteAll / Directory       │
│   JsonProvider      JsonSerializer wrapper, generic             │
│                                                                 │
│   Pure connectors. No business logic. No model knowledge.       │
└───────────┬─────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────┐
│                           Models                                │
│                                                                 │
│   SceneDna   EraProfile   Prompt   HistoricalImage              │
│   Video   Caption   Publication                                 │
│                                                                 │
│   Records with init-only properties. No logic. No dependencies. │
└─────────────────────────────────────────────────────────────────┘
```

---

## Project Structure

```
src/LifeOverYears/
├── Models/
│   ├── SceneDna.cs
│   ├── EraProfile.cs
│   ├── Prompt.cs
│   ├── HistoricalImage.cs
│   ├── Video.cs
│   ├── Caption.cs
│   ├── Publication.cs
│   └── RunFolder.cs
│
├── Services/
│   ├── Interfaces/
│   │   ├── IVisionService.cs
│   │   ├── IVisionProvider.cs
│   │   ├── IImageProvider.cs
│   │   ├── IPromptService.cs
│   │   ├── IImageService.cs
│   │   ├── IVideoService.cs
│   │   ├── ICaptionService.cs
│   │   ├── IPublicationService.cs
│   │   ├── IStorageService.cs
│   │   ├── IDataService.cs
│   │   ├── IRunService.cs
│   │   ├── IYearOverlayService.cs
│   │   ├── IImageGenerationProvider.cs
│   │   ├── IPromptProvider.cs
│   │   ├── INvidiaProvider.cs
│   │   ├── IXaiProvider.cs
│   │   ├── IFfmpegProvider.cs
│   │   ├── ITelegramProvider.cs
│   │   ├── IDropboxProvider.cs
│   │   ├── IFileSystemProvider.cs
│   │   └── IJsonProvider.cs
│   ├── VisionService.cs
│   ├── PromptService.cs
│   ├── ImageService.cs
│   ├── VideoService.cs
│   ├── CaptionService.cs
│   ├── PublicationService.cs
│   ├── StorageService.cs
│   ├── DataService.cs
│   ├── SceneDnaValidator.cs
│   ├── GenerationContext.cs
│   ├── PromptSmokeTest.cs
│   ├── VideoSmokeTest.cs
│   ├── RunService.cs
│   ├── YearOverlayService.cs
│   ├── VideoAssemblyRunner.cs
│   └── Pipeline.cs
│
├── Providers/
│   ├── NvidiaProvider.cs
│   ├── XaiProvider.cs
│   ├── FfmpegProvider.cs
│   ├── TelegramProvider.cs
│   ├── DropboxProvider.cs
│   ├── FileSystemProvider.cs
│   ├── JsonProvider.cs
│   ├── VisionProvider.cs
│   ├── ImageProvider.cs
│   ├── PromptProvider.cs
│   └── StubImageProvider.cs
│
├── data/
│   ├── prompts/
│   │   └── vision.txt
│   ├── eras/
│   │   └── {year}.json
│   ├── brands/
│   │   └── gas-brands.txt
│   └── scenes/
│       └── {id}.json
│
└── Program.cs
```

---

## Dependency Rules

```
Models            →  no dependencies
Transport Providers →  Models
Domain Providers  →  Models, Transport Providers (via interfaces)
Services          →  Models, Domain + Transport Providers (via interfaces)
Console           →  all layers
```

Transport Providers never reference Services or Domain Providers.

Domain Providers never reference Services.

Services depend on interfaces, never on concrete classes.

Console is the only composition root.

---

## Models

Records with init-only properties. No logic. No dependencies.

```
SceneDna        immutable structural description of a scene
EraProfile      historical characteristics of a time period
Prompt          text instructions for image generation
HistoricalImage one generated historical reconstruction
Video           rendered animation from a sequence of images
Caption         title, description, hashtags for a post
Publication     published content record with platform URL
```

---

## Domain Providers

Know one AI model or workflow. Build request bodies, parse responses, map to domain objects.

```
VisionProvider   nvidia/nemotron-3-nano-omni-30b — analyzes photos, returns SceneDna
ImageProvider    OpenAI GPT Image 1.5 (target model; provider implementation pending) — generates images, returns HistoricalImage
```

---

## Transport Providers

Pure connectors. Know nothing about domain objects or business logic.

```
NvidiaProvider    PostAsync(url, body) / PollAsync(url) — Bearer auth HTTP
XaiProvider       CompleteAsync(prompt) — xAI chat completions
FfmpegProvider    ComposeAsync(images) — ffmpeg CLI process
TelegramProvider  SendVideoAsync(video, caption) — multipart upload
DropboxProvider   UploadAsync / DownloadAsync — Dropbox API v2
FileSystemProvider  ReadAllText / WriteAllText / List / Exists / Delete
JsonProvider      Serialize<T> / Deserialize<T> / TryDeserialize<T>
```

---

## Services

Business logic and pipeline orchestration. Never reference concrete Provider classes.

```
VisionService      analyze → validate → enrich → save SceneDna
PromptService      build era-specific generation prompts (programmatic assembly, no AI call)
ImageService       generate historical images per era
VideoService       compose images into video
CaptionService     generate title, description, hashtags
PublicationService publish to Telegram
StorageService     persist files to Dropbox
DataService        load/save domain objects from data/ directory

Pipeline           orchestrates all steps in order
SceneDnaValidator  validates SceneDna completeness, returns missing fields
GenerationContext  per-run sampling state: seeded Random, cross-era vehicle
                   dedup, placement pattern dedup, diner name, scene condition
RunService         run folder management (output/{run} layout)
YearOverlayService stamps each generated image with its era year
VideoAssemblyRunner shared pipeline tail: wait for images → stamp → compose
PromptSmokeTest    --smoke-prompts harness: checks C1–C23 over generated prompts
VideoSmokeTest     --smoke-video harness for the ffmpeg assembly path
```

---

## VisionService Flow

```
photoPath
    │
    ├─ DataService.LoadPromptAsync("vision")  → prompt text from data/prompts/vision.txt
    │
    ├─ VisionProvider.AnalyzeImageAsync(photoPath, prompt)  → SceneDna
    │
    ├─ SceneDnaValidator.Validate(sceneDna)  → missing fields list
    │
    ├─ if missing.Count > 0:
    │     └─ VisionProvider.EnrichAsync(photoPath, sceneDna, missing)  → corrected SceneDna
    │           └─ SceneDnaValidator.Validate(enriched)  → log remaining gaps
    │
    └─ DataService.SaveSceneDnaAsync(sceneDna)  → data/scenes/{id}.json
```

---

## Data Files

```
data/prompts/{name}.txt       plain text prompts loaded at runtime
data/eras/{year}.json         EraProfile for a given year
data/brands/gas-brands.txt    gas station brands, one per line: Name|fromYear|toYear
data/scenes/{id}.json         SceneDna persisted after analysis
```

Era JSONs include `scene_content` (per scene_type: people/vehicles ranges, narrative, storefronts, window_signs, extras, people_activities), `color_mode`, `allowed_scene_conditions`, and `gas_brands` (fallback only — the primary brand source is `data/brands/gas-brands.txt`, filtered by era year).

Prompts, EraProfiles, and brand lists are data, not code. They can be updated without recompiling.

---

## Swapping a Provider

To replace the vision model from NVIDIA to xAI:

1. Create `XaiVisionProvider.cs` implementing `IVisionProvider`
2. Wire it in `Program.cs` instead of `VisionProvider`
3. Nothing else changes — `VisionService`, `Pipeline`, `Models` are untouched

---

## Design Principles

- Models have no dependencies and no logic.
- Transport providers are pure connectors — no model knowledge, no parsing.
- Domain providers know exactly one model or workflow.
- Services never touch HTTP or file paths directly.
- Services depend on interfaces, never on concrete classes.
- Console is the only composition root.
- Prompts and EraProfiles are data files, not code.
- SceneDna is immutable after population.
- Layers are folders today, separate projects tomorrow.
