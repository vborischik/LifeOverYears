# LifeOverYears — Claude Context

Architecture and layering rules live here. For day-to-day work — commands, the
smoke-test loop, and where the prompt tuning knobs are — see `CLAUDE.md` at the
repo root.

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

## Adding a New Era

An era is a year the run renders. The six shipped years are 1975–2025 on a ten
year step. Adding one (1965, 2035, …) touches more than the era file, because
several things are anchored to the *set* of years rather than read from it.
Traced from the code Aug 2026 — re-check before trusting it.

### 1. The era profile — `data/eras/{year}.json`

Copy the nearest year and edit. Twelve top-level keys, all required by the
deserializer: `year`, `label`, `description`, `allowed_scene_conditions`,
`people_mix`, `transportation`, `architecture`, `business`, `infrastructure`,
`society`, `environment`, `photography`, `scene_content`.

What C1 fails on, so get these right first:

* `scene_content` must carry `downtown_street`, `gas_station`, `strip_mall`,
  `auto_repair`, `corner_shop`, `motel`, `freestanding_shop`, `default`. The
  shipped files also carry `mall`, `shopping_center`, `highway_urban`,
  `highway_rural` — twelve keys in total. A missing key silently falls back to
  `default`, which is why the check exists.
* `people_mix` — at least 20 entries.
* `people_activities` — at least 20 for `downtown_street`, `gas_station`,
  `strip_mall`, `auto_repair`.
* `photography.color_mode` must be present. C7 expects the oldest era to be
  `black_and_white` and every later one colour.
* `infrastructure.utilities` — `characteristics`, plus the optional
  scene-specific pools (`downtown_characteristics`, `strip_mall_characteristics`,
  `highway_characteristics`) and the `undergrounded` flag.
* `allowed_scene_conditions` drives `PickSceneCondition`; a year that offers
  nothing at or above the rank already reached holds the arc instead.

### 2. The default year list — `Program.cs`

Three separate literals, all `{ 1975, 1985, 1995, 2005, 2015, 2025 }`: the `run`
mode default, the `assemble` fallback and the `collect` fallback for run folders
predating `run.json`. Miss one and that entry point quietly ignores the new era.

### 3. The tree anchor — `PromptService.SourceYear`

Currently 2025, and it means "the year the base image already shows". **Only
matters when the new era is newer than the current newest.** Leave it stale and
`DescribeTreeSize` computes a fraction above 1 for that year and describes it
with shrink wording — "slightly smaller … about 105% of its canopy".

### 4. Decade spacing is load-bearing

`DescribeTreeSize` counts steps with integer division by 10
(`(SourceYear - year) / 10`, and `(year - from) / 10` when chained). A year that
is not a whole decade from its neighbour — 2020, say — yields zero steps, the
size string comes back empty and the era emits **no TREES section at all**. Off
step years need that arithmetic reworked, not just a new file.

### 5. Date-gated storylines — `GenerationContext`

These encode real history and do not move with the year list; check each still
reads correctly against the new span.

| what | where | gate |
|---|---|---|
| corner shop turns liquor | `LiquorFromYear` | 2015 |
| corner shop stops being kept up | `DeclineFromYear` | 2005 |
| Blockbuster | `ResolveBlockbuster` | absent before 1990, named to 2009, ghost after |
| RadioShack | `ResolveRadioShack` | generic before 1990, named to 2014, ghost after |
| gas rebrand / motel reflag | `BuildGasPlan`, the motel equivalent | only plan a switch when the run spans ≥ 25 years |

### 6. Brand files with year ranges — `data/brands/`

`gas-brands.txt` and `motel-brands.txt` are `Name|from|to`. A new era outside
every range still resolves (`PickEligibleAcross` relaxes to eligibility at the
start year), but the result is a brand that did not exist then. Extend the
ranges deliberately.

### 7. The smoke suite — `PromptSmokeTest`

* `Years` at the top of the file — the array every check iterates.
* `DowntownCoffeePrices` — indexed by year with `[year]`, so a year without an
  entry throws `KeyNotFoundException` and the whole run dies rather than failing
  a check.
* Hardcoded year expectations, all of which move when the span moves: C6 (tree
  percentages at 1975 and 2005, and `year == 2025` standing in for the source
  year), C7 (`run[1975]` black and white), C8, C45, C60, C70.

### 8. What needs nothing

Captions (`{firstYear}`/`{lastYear}` come from the narrative), video assembly and
the year overlay are all generic over the list — `FfmpegProvider` derives its
timeline from `images.Count`, and `TotalEras` is passed as `years.Count`
everywhere in production. The `= 6` default on `GenerationContext.TotalEras` is
only reached by fixtures that do not set it.

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
