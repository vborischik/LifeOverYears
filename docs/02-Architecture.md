# Architecture

## Overview

LifeOverYears is built around a three-layer architecture that separates data structures, business logic, and raw API connectors.

The architecture is designed to remain stable as AI providers evolve.

Swapping a provider requires changing only the relevant Service implementation, leaving everything else untouched.

---

## Layers

```
┌─────────────────────────────────────────────────────────────────┐
│                           Console                               │
│              Entry point. Wires everything together.            │
└───────────┬─────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────┐
│                          Services                               │
│                                                                 │
│   Interfaces          Implementations        Pipeline           │
│   ─────────────       ───────────────        ────────           │
│   IVisionService  →   VisionService          Step 1             │
│   IPromptService  →   PromptService          Step 2             │
│   IImageService   →   ImageService           Step 3             │
│   IVideoService   →   VideoService           Step 4             │
│   ICaptionService →   CaptionService         Step 5             │
│   IPublishService →   PublicationService     Step 6             │
│   IStorageService →   StorageService                            │
│                                                                 │
└───────────┬─────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────┐
│                          Providers                              │
│                                                                 │
│   XaiProvider        NvidiaProvider      FfmpegProvider         │
│   TelegramProvider   DropboxProvider                            │
│                                                                 │
│   Raw API connectors. No business logic.                        │
└───────────┬─────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────┐
│                           Models                                │
│                                                                 │
│   SceneDNA   EraProfile   BrandProfile   Prompt                 │
│   HistoricalImage   Video   Caption   Publication               │
│                                                                 │
│   No logic. No dependencies.                                    │
└─────────────────────────────────────────────────────────────────┘
```

---

## Project Structure

For the MVP all layers live as folders inside a single console project.

```
LifeOverYears/
├── Models/
│   ├── SceneDNA.cs
│   ├── EraProfile.cs
│   ├── BrandProfile.cs
│   ├── Prompt.cs
│   ├── HistoricalImage.cs
│   ├── Video.cs
│   ├── Caption.cs
│   └── Publication.cs
│
├── Services/
│   ├── Interfaces/
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
│
├── Providers/
│   ├── XaiProvider.cs
│   ├── NvidiaProvider.cs
│   ├── FfmpegProvider.cs
│   ├── TelegramProvider.cs
│   └── DropboxProvider.cs
│
└── Program.cs
```

When the project grows, each folder becomes a separate project in the solution. This migration takes approximately 15 minutes per layer.

---

## Dependency Rules

```
Models    →  no dependencies
Providers →  Models
Services  →  Models, Providers (via interfaces only)
Console   →  all layers
```

Providers never reference Services.

Services depend on interfaces, not on concrete Provider classes.

Console is the only place where Services and Providers are connected.

---

## Models

Defines all domain objects.

No logic. No dependencies.

```
SceneDNA        immutable structural description of a scene
EraProfile      historical characteristics of a period
BrandProfile    visual identity of a brand in a period
Prompt          instructions for image generation
HistoricalImage one generated historical reconstruction
Video           rendered animation from historical images
Caption         title, description, hashtags
Publication     published content on a platform
```

---

## Providers

Raw connectors to external systems.

No business logic. No decision making.

Depends only on Models.

```
XaiProvider       raw xAI API calls
NvidiaProvider    raw NVIDIA API calls
FfmpegProvider    local FFmpeg process execution
TelegramProvider  Telegram Bot API calls
DropboxProvider   Dropbox API calls
```

---

## Services

Business logic and pipeline orchestration.

Depends on Models and Provider interfaces.

Never references concrete Provider classes directly.

Decides which Provider to use for each task.

```
VisionService      today → NvidiaProvider,    tomorrow → XaiProvider
PromptService      today → XaiProvider,       tomorrow → ?
ImageService       today → NvidiaProvider,    tomorrow → ?
VideoService       today → FfmpegProvider,    tomorrow → RunwayProvider
CaptionService     today → XaiProvider,       tomorrow → ?
PublicationService today → TelegramProvider,  tomorrow → InstagramProvider
StorageService     today → DropboxProvider,   tomorrow → S3Provider

Pipeline           orchestrates all steps in order
```

---

## Swapping a Provider

To replace the Vision provider from NVIDIA to xAI:

1. Open `VisionService.cs`
2. Change the internal provider call
3. Nothing else changes

Console, Pipeline, Models, and all other Services remain untouched.

---

## EraProfile Storage

EraProfiles are stored as JSON files.

The pipeline reads them from disk by year.

```
data/
  eras/
    1955.json
    1975.json
    1985.json
    1995.json
    2005.json
    2015.json
```

---

## SceneDNA Format

SceneDNA is populated by VisionService from a modern photograph.

SceneDNA is immutable after population.

Stored as JSON for reuse across multiple Eras.

```json
{
  "id": "scene_001",
  "camera": {
    "height": "street_level",
    "direction": "north",
    "fov": "wide"
  },
  "geometry": {
    "roads": ["two_lane_highway"],
    "sidewalks": true,
    "buildings": [
      { "type": "one_story_commercial", "position": "left" },
      { "type": "canopy", "position": "center" }
    ]
  },
  "environment": {
    "terrain": "flat",
    "utilities": ["power_lines"]
  },
  "immutable": [
    "building_footprint",
    "road_layout",
    "utility_poles"
  ]
}
```

---

## Design Principles

- Models have no dependencies.
- Providers depend only on Models.
- Services depend on interfaces, never on concrete Providers.
- Console is the only composition root.
- Swapping a provider touches only the relevant Service.
- EraProfiles are data files, not code.
- SceneDNA is immutable after population.
- Layers are folders today, separate projects tomorrow.