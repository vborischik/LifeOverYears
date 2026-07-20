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
      ▼  Step 2 — PromptService (programmatic assembly, no AI call)
Prompt
      │
      ▼  Step 3 — ImageService → ImageProvider → OpenAI GPT Image 1.5
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

## Step 2 — Prompt ✅ implemented

Builds era-specific image generation prompts by combining SceneDna geometry with EraProfile visual characteristics. Assembly is fully programmatic C# — no LLM call.

**Input:** `SceneDna` + `EraProfile` + `GenerationContext`  
**Output:** `Prompt` (one per era year), saved to `output/prompts/{sceneDnaId}/{year}.json`

```
1. DataService.LoadEraProfileAsync(year)
2. PromptService.BuildAsync(sceneDna, eraProfile, context)
        → DataService.LoadPromptAsync("image-template")
        → resolves scene_content by SceneDna.SceneType,
          falling back to the "default" key
        → samples via GenerationContext:
             seeded Random shared across the run
             cross-era vehicle dedup (UsedCarModels HashSet)
             placement pattern dedup per run
             per-run DinerName (stable across all eras)
             per-era SceneCondition — gas stations only
        → builds PRESERVE / SCENE / PEOPLE / VEHICLES /
          ENVIRONMENT / STYLE blocks into the template
        → resolves gas brand from data/brands/gas-brands.txt
          filtered by era year (era JSON gas_brands is fallback)
3. DataService.SavePromptAsync(prompt)
```

Conditions (`thriving` / `busy` / `new` / `declining` / `abandoned` / `restored`) are a gas-station-only concept: `abandoned` forces zero people and vehicles, `declining` clamps counts to sparse activity. Other scene types always use their base scene_content ranges.

### Smoke Tests

`dotnet run -- --smoke-prompts` executes `PromptSmokeTest` with checks C1–C23 (placeholder resolution, vehicle dedup, seed variance, tree ladder, color mode, price anchors, PRESERVE fidelity, prompt length ≤ 4850 chars, condition isolation) and writes a markdown report to `output/smoke/report.md`.

---

## Step 3 — Historical Images (submit/collect)

Generates photorealistic historical reconstructions for each era year via an async batch-style job model: submission and collection are decoupled so the process does not sit waiting on slow provider jobs.

**Input:** `Prompt` per year + clean base image  
**Output:** `HistoricalImage` per year in `runs/{id}/images/{year}.png`

Target model: **OpenAI GPT Image 1.5** — decision confirmed by direct image testing (`OpenAiImageProvider` not yet written; `StubImageProvider` exercises the flow).

```
Submit (part of `run`):
1. IImageGenerationProvider.CleanBaseAsync(source, prompt, base_clean.png)
        → source photo emptied of people and vehicles
2. per year: IImageGenerationProvider.SubmitEraAsync(base, prompt, year, jobsDir)
        → submits the generation job to the provider
        → persists job state to runs/{id}/jobs/{year}.json
          ({ "year", "provider", "jobId", "submittedAt" })
3. pipeline logs the collect command and exits — nothing waits in-process

Collect (separate invocation):
   dotnet run -- collect <runFolder> [--wait]
        → reads years from the run manifest run.json
        → per missing year: IImageGenerationProvider.TryCollectAsync
             true  → result downloaded to images/{year}.png
             false → job still pending
             throws → job failed with the provider error
        → all collected: overlay + video via VideoAssemblyRunner, exit 0
        → some pending without --wait: summary, exit 2
        → with --wait: polls every 60s until done; Ctrl+C safe —
          state is on disk, rerunning collect resumes
```

The run folder (`runs/{sceneId}_{timestamp}/`) contains `run.json` (manifest: sceneDnaId, sourcePath, years, createdAt), `prompts/`, `jobs/`, `images/`, `stamped/`, `video/`.

---

## Step 4 — Video ✅ implemented

Composes historical images into an animated video transition across eras.

**Input:** `IReadOnlyList<HistoricalImage>`  
**Output:** `Video` saved via `VideoAssemblyRunner`

```
1. VideoAssemblyRunner.RunAsync(...)
        → waits for exactly the requested years' images
        → YearOverlayService stamps each image with its year
        → VideoService.ComposeAsync(stampedImages, outputPath)
             → FfmpegProvider: ffmpeg filter graph with xfade
               radial-wipe transitions between eras, with a
               duration guard against overlapping mid-sequence
               transitions
```

Used by both `Pipeline` (after real generation) and the `assemble` CLI mode, which runs against a folder of images already on disk. `dotnet run -- --smoke-video` executes `VideoSmokeTest`.

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
