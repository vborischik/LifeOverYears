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
      ▼  Step 5 — CaptionService → data/captions/
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
             per-era SceneCondition, on a monotonic decay arc
        → builds PRESERVE / SCENE / PEOPLE / VEHICLES /
          ENVIRONMENT / TREES / STYLE blocks into the template
        → resolves gas brand from data/brands/gas-brands.txt
          filtered by era year (era JSON gas_brands is fallback)
3. DataService.SavePromptAsync(prompt)
```

Conditions (`thriving` / `busy` / `new` / `declining` / `abandoned` / `squatted` / `restored`) apply to `gas_station`, `downtown_street`, `strip_mall`, `auto_repair` and `corner_shop`; other scene types always use their base scene_content ranges. `abandoned` forces zero people and vehicles, `declining` clamps counts to sparse activity, and derelict eras swap the live-business PERIOD DETAILS block for a closed-down one — a boarded block must not advertise. The rank only ever worsens across a run, one step at a time, and the final era resolves the arc.

Period details (storefronts, window signs, extras) are sampled from era pools that know nothing about the photographed geometry, so the scene block closes with an explicit escape clause — place each detail where it plausibly belongs, and leave out anything with nowhere to go, never in the roadway or a driving lane — echoed by PRIORITY ORDER item 4 in the template. A list without that clause reads as mandatory and the image model plants every prop somewhere, which is how a bench ends up standing in the road.

`highway` is excluded from conditions and decay like `mall` and `shopping_center`, and is the one scene type whose content key differs from its Vision scene type: `SceneContentKey.Resolve` splits it into `highway_urban` / `highway_rural` on `Environment.Terrain`, so an interstate and a country two-lane draw different era content, captions and titles from one classification. Its vehicles are moving traffic rather than arranged parking, and the urban flavor switches to a packed, uncountable stream from 2005.

`corner_shop` is the exception to conditions being sampled at all: its arc is scripted, because the scene type exists to show one thing happening — the neighbourhood shop becoming a liquor store. It opens as a grocery or a pharmacy, is never in good repair from 2005, always still trading (never boarded) until the last era so the 2015 turnover is actually seen, and never recovers. `GenerationContext.ResolveCornerShop` decides what the sign says each era and keeps the old name ghosting above the new one after the turnover.

Tree sizes are sized in `DescribeTreeSize` from a per-decade retention rate for the size Vision recorded (large/medium/small), damped 5% by `GrowthDamping`. Under `EraChaining` each era asks for growth against the previous era's image; unchained, each era states a fraction of the shared base. The two are exact inverses of one another, and the source year (the newest era) emits no TREES section at all — the base already shows the trees at that size.

### Smoke Tests

`dotnet run -- --smoke-prompts` executes `PromptSmokeTest` with checks C1–C56 (placeholder resolution, vehicle dedup, seed variance, tree sizing, color mode, price anchors, PRESERVE fidelity, prompt length ≤ `MaxPromptChars`, condition arcs, chained-era wording, utility undergrounding) plus `FolderSmokeTest`, and writes a markdown report to `output/smoke/report.md` alongside the full generated prompts under `output/smoke/{sceneType}/run{n}/{year}.json|.txt`.

Those files are committed, so a run rewrites them: a large `output/smoke/` diff after changing `PromptService` or `data/eras` is the expected result, not stray output. Run it after every prompt-affecting change.

---

## Step 3 — Historical Images (submit + wait)

Generates photorealistic historical reconstructions for each era year. The pipeline submits one job per year, then waits — without timeout — on the run folder: a year is done the moment `images/{year}.png` exists, no matter who put it there (a provider download or a human dropping in a hand-generated file).

**Input:** `Prompt` per year + clean base image  
**Output:** `HistoricalImage` per year in `runs/{id}/images/{year}.png`

Target model: **OpenAI GPT Image 1.5** — decision confirmed by direct image testing. `OpenAiImageProvider` is not yet written; the current `StubImageProvider` delivers nothing itself, so images arrive in the run folder from outside.

```
1. IImageGenerationProvider.CleanBaseAsync(source, prompt, base_clean.png)
        → source photo emptied of people and vehicles
2. per year: IImageGenerationProvider.SubmitEraAsync(base, prompt, year, jobsDir)
        → submits the generation job
        → persists job state to runs/{id}/jobs/{year}.json
          ({ "year", "provider", "jobId", "submittedAt" })
3. wait loop, no timeout, every 60s:
        → missing = years without images/{year}.png
        → per missing year: TryCollectAsync
             true  → result downloaded to images/{year}.png
             false → still pending (files dropped in by hand
                     count as done on the next iteration)
             throws → run aborts with the provider error
4. all present: YearOverlayService stamps into stamped/,
   VideoService assembles video/timeline.mp4
```

### Era chaining

`Pipeline:EraChaining` (default **true**) changes what each era is generated from. Chained, the run walks forward in time and every era edits the *previous era's finished image* rather than the shared base, so the place carries forward and stays recognisable; necessarily sequential, since year N+1 cannot be submitted until year N exists (under batch mode that means one batch and one completion window per era). Unchained, every era edits the same base in parallel.

The consequence worth remembering when reading any prompt: anything visible in the first frame propagates through the whole run unless a later era explicitly asks for its removal. A block that simply stops mentioning a feature does not delete it — that is why the undergrounded utilities carry an explicit removal line, and why tree sizes are phrased as growth against the uploaded image rather than as a fraction of the base.

The run folder (`runs/{sceneId}_{timestamp}/`) contains `run.json` (manifest: sceneDnaId, sourcePath, years, createdAt), `prompts/`, `jobs/`, `images/`, `stamped/`, `video/`.

The `collect <runFolder> [--wait]` and `assemble <folderPath>` CLI modes remain as recovery/debug tools — e.g. resuming the tail of a run whose process was interrupted — not part of the normal flow.

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

## Step 5 — Caption ✅ implemented

Assembles a social media caption from files. No LLM call — `XaiProvider` and its interface stay in the repo unwired, so the generated path can be restored, but nothing calls it.

**Input:** `SceneDna` + `SceneNarrative` (persisted as `narrative.json`, so a resumed `collect` can still caption)  
**Output:** `Caption`, written to `caption.txt` (description + hashtags) and `title.txt` (the YouTube title) in the run folder

```
1. CaptionService.GenerateAsync(sceneDna, narrative)
        → DataService.LoadCaptionBodiesAsync(sceneType),
          falling back to captions/base.txt
        → SplitBodies on lines that are exactly "---"
        → picks one body: (ISO week + stable hash of the scene id)
          % bodyCount — deterministic, and a run of bodyCount
          consecutive weeks visits every body exactly once
        → substitutes {firstYear} {lastYear} {angle} {condition}
             angle:     AnglesFor(sceneType) = scene-specific
                        anchors, then CommonAngles
             condition: MapFinalCondition(narrative.FinalCondition)
        → SelectHashtags(captions/hashtags.txt)
        → DataService.LoadTitleTemplatesAsync(sceneType),
          falling back to captions/titles/base.txt
        → picks one line, substitutes {firstYear} {lastYear}
2. CaptionRunner.WriteAsync → caption.txt (body, blank line, tags)
                            → title.txt  (the YouTube title)
```

All caption text lives under `src/LifeOverYears/data/captions/` — bodies per scene type, YouTube title hooks under `titles/`, plus the shared `hashtags.txt`. A body must carry the years and the condition, end on a question, and contain no hashtags of its own; the checks enforce all three, because a body that skips the placeholders posts a caption that never says which years the video covers.

Titles are a separate pool from bodies: `titles/{sceneType}.txt` is a plain one-per-line list with the same `base.txt` fallback, carrying only `{firstYear}`/`{lastYear}` and staying inside YouTube's 100-character limit, so `caption.txt` remains exactly the Facebook/Instagram payload while YouTube gets its own hook.

In `hashtags.txt` the first three unweighted lines are pinned and ship in file order, two more are sampled from the rest, and a `NN%` suffix (`#nostalgia 70%`) takes a tag out of the pool and gives it its own roll at that probability — a winner spends one of the sampled slots, so every post carries five tags. The file is the whole interface: repinning, reweighting and adding tags need no code change.

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
