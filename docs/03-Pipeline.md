# Pipeline

## Purpose

The Pipeline describes how information flows through the LifeOverYears platform.

It does not describe implementation details.

It does not describe AI providers.

It does not describe software components.

The Pipeline represents the logical transformation of data from the initial input to the final published content.

Every stage consumes one domain object and produces another.

---

## Pipeline Philosophy

LifeOverYears is a data transformation engine.

The platform is built around the evolution of structured information rather than the execution of AI models.

AI providers, APIs, and implementation details may change over time.

The logical Pipeline should remain stable.

---

## Logical Pipeline

```
Modern Photo
      │
      ▼  Step 1 — VisionService → VisionProvider → NvidiaProvider
SceneDna
      │
      ▼  Step 2 — PromptService → XaiProvider
Prompt
      │
      ▼  Step 3 — ImageService → ImageProvider → NvidiaProvider
Historical Images
      │
      ▼  Step 4 — VideoService → FfmpegProvider
Video
      │
      ▼  Step 5 — CaptionService → XaiProvider
Caption
      │
      ▼  Step 6 — PublicationService → TelegramProvider
Publication
```

---

## Step 1 — SceneDna ✅ implemented

Extracts the permanent structural characteristics of the scene from a modern photograph.

**Input:** path to a modern photo  
**Output:** `SceneDna` saved to `data/scenes/{id}.json`

```
1. DataService.LoadPromptAsync("vision")
        → reads data/prompts/vision.txt

2. VisionProvider.AnalyzeImageAsync(photoPath, prompt)
        → encodes photo as base64
        → POST to nvidia/nemotron-3-nano-omni-30b
        → parses JSON response into SceneDna

3. SceneDnaValidator.Validate(sceneDna)
        → checks camera.height, camera.direction,
           geometry.roads, geometry.buildings,
           immutable_elements for default/empty values

4. if missing fields:
        VisionProvider.EnrichAsync(photoPath, sceneDna, missing)
        → sends current SceneDna + missing field list back to model
        → re-parses corrected SceneDna
        → preserves original id and createdAt

5. DataService.SaveSceneDnaAsync(sceneDna)
        → serializes to data/scenes/{id}.json
```

---

## Step 2 — Prompt (planned)

Builds era-specific image generation prompts by combining SceneDna geometry with EraProfile visual characteristics.

**Input:** `SceneDna` + `EraProfile`  
**Output:** `Prompt` (one per era year)

```
1. DataService.LoadEraProfileAsync(year)
2. PromptService.BuildAsync(sceneDna, eraProfile)
        → XaiProvider.CompleteAsync(combinedContext)
        → returns structured Prompt
```

---

## Step 3 — Historical Images (planned)

Generates photorealistic historical reconstructions for each era year.

**Input:** `Prompt`  
**Output:** `HistoricalImage` saved to `output/images/{year}/{promptId}.png`

```
1. ImageProvider.GenerateImageAsync(prompt)
        → POST to black-forest-labs/flux-dev
        → polls until complete (202 → poll loop)
        → saves PNG, returns HistoricalImage
```

---

## Step 4 — Video (planned)

Composes historical images into an animated video transition across eras.

**Input:** `IReadOnlyList<HistoricalImage>`  
**Output:** `Video` saved to `output/videos/{id}.mp4`

```
1. FfmpegProvider.ComposeAsync(images)
        → writes concat list file
        → runs ffmpeg: scale 1920×1080, libx264, 30fps, 3s per frame
        → returns Video
```

---

## Step 5 — Caption (planned)

Generates a social media caption with title, description, and hashtags.

**Input:** `SceneDna` + `EraProfile` list  
**Output:** `Caption`

```
1. CaptionService.GenerateAsync(sceneDna, eras)
        → XaiProvider.CompleteAsync(contextPrompt)
        → parses title, description, hashtags
        → returns Caption
```

---

## Step 6 — Publication (planned)

Publishes the video and caption to Telegram.

**Input:** `Video` + `Caption`  
**Output:** `Publication` with platform URL

```
1. DropboxProvider.UploadAsync(video.FilePath)
        → stores video for archival
2. TelegramProvider.SendVideoAsync(video, caption)
        → multipart POST sendVideo
        → returns Publication with message URL
```
