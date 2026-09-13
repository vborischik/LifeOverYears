# 12 — Operations: running, resuming, configuring

Practical reference for operating the pipeline. Traced from the code on
11 Sep 2026 — where this file and an older doc disagree, trust this one and
the code. Commands run from `src/LifeOverYears/`.

## appsettings.json

`src/LifeOverYears/appsettings.json`, gitignored; `appsettings.example.json`
ships the shape (checked by F3). Every key the code actually reads:

| key | default | what it does |
|---|---|---|
| `Nvidia:ApiKey` | — | Vision model key. Required for the photo path; a brand series never calls Vision. |
| `OpenAi:ApiKey` | — | Image generation key. |
| `OpenAi:Enabled` | `true` | `false` swaps in `StubImageProvider`: jobs recorded, no API calls, no cost — drop images into the run folder by hand and `collect` them. |
| `OpenAi:Mode` | `sync` | `batch` uses the Batch API at discount. Batch has an OpenAI-side file-access bug since 19 Aug 2026 (see CLAUDE.md Known issues); `sync` is the working fallback at full price. |
| `OpenAi:BatchInlineImage` | `false` | Sends the base image as inline base64 instead of a `file_id` (B11). Tried against the batch bug, did not fix it, left off. |
| `Pipeline:BaseMode` | `clean` | `clean` = the source photo emptied of people/vehicles; `synthetic` = base drawn from SceneDna text, the photo is never uploaded anywhere. |
| `Pipeline:EraChaining` | `true` | Each era edits the previous era's finished image rather than the shared base. Photo path only — a brand series is always chained. |
| `Pipeline:MonochromeFirstEra` | `true` | The run's oldest era renders black-and-white regardless of its era file. Colour there measurably cost views. |
| `Pipeline:ShortPrompts` | `false` | Also writes `{runFolder}/short-prompts/` during the run. |
| `Pipeline:InputDir` | `testImage` | Where `run` with no argument looks for photos, in name order. |
| `Pipeline:ProcessedDir` | `processed` | Where a finished photo is moved. |
| `Pipeline:FailedDir` | `failed` | Where a failed photo is moved (non-zero exit). |
| `Pipeline:OutputDir` | `output/runs` | Where run folders are created. |
| `Publish:Enabled` | `false` | Gates the end-of-run enqueue and the `review` loop. `publish <run> --yes` ignores it. |
| `Publish:Targets` | `[]` | Platforms to post to, in order; empty is refused. |
| `Publish:Privacy` | `private` | YouTube's word; Facebook maps it; Telegram/Instagram post on call. |
| `Publish:Telegram:BotToken` / `ReviewChatId` | — | The review bot and YOUR chat id (see docs/13 for finding it). |
| `Publish:Dropbox:*` / `Instagram:*` / `Facebook:*` / `YouTube:*` | — | Per-platform credentials; see `appsettings.example.json`. |
| `Vision:DoubleCheck` | `false` | Second Vision pass re-examining five load-bearing fields. Off: measured against `testFolder8/expected.json` it corrected one field and doubled exposure to the empty-stream failure. Re-enabling is this flag plus a `vision-accuracy` re-run to justify it. |

## Running

```
cd src/LifeOverYears

dotnet run -- run photo.jpg 1975 1985 ...   # one photo; years optional
dotnet run -- run                           # every image in InputDir, each its own run
dotnet run -- brand kmart                   # brand series: no photo, no Vision
dotnet run -- brand circuit-city            # second series; ends in a Planet Fitness takeover
dotnet run -- brand kmart 1975 1985         # a subset; a year the file lacks is refused
```

One series per file under `data/brands/series/` — the argument is the file
name. A misspelled name fails with the list of available series.

A run folder appears under `output/runs/{id}_{yyyyMMdd-HHmm}/` holding
`run.json` (photo path) or `series.json` (brand path), every era prompt,
`images/`, and after assembly `video/timeline.mp4`, `caption.txt`, `title.txt`.

The brand path draws its first era from text (nothing is uploaded), and every
later era edits the frame before it. The series file is
`data/brands/series/{name}.json`; logo reference images live in
`data/brands/logos/{brand}/{year}.png` — a missing PNG logs a warning and the
era generates from the LOGO block's words alone. As of Sep 2026 that is the
live state of `circuit-city`: its four logo eras (1975–2005) reference
`data/brands/logos/circuit-city/{year}.png`, which are not dropped in yet, and
its wordmark is marked LOGO-UNVERIFIED in the series file — check it against
dated references before shipping a run.

## Resuming an interrupted run

Nothing paid is lost when a run dies mid-chain: the run folder keeps the
manifest, all prompts, and every era image already generated.

```
dotnet run -- collect <runFolder>           # finish: submit/fetch missing eras, then assemble
dotnet run -- collect <runFolder> --wait    # same, polling until batches complete
dotnet run -- assemble <runFolder> [years]  # video/overlay/caption only, offline
```

What `collect` does:

- **Skips every year whose image already exists** (B9) — it never re-bills a
  finished era, and never overwrites one.
- **Batch runs:** a failed batch is not billed (`usage` zeros), so re-running
  `collect` is the cheapest retry for the Aug-2026 batch bug. If batch keeps
  failing, flip `OpenAi:Mode` to `sync` and `collect` again.
- **Broken pipe mid-chain** (see CLAUDE.md Known issues): the photo lands in
  `failed/` but the run folder is intact — `collect <runFolder> --wait`
  resumes from the last paid image.
- **Brand runs:** `collect` reads `series.json` from the run folder, so a
  resumed era still gets its logo reference; a missing *first* frame is
  redrawn from text rather than failing the run.
- **Hand-made frames:** drop `images/{year}.png` into the run folder and
  `collect` stamps, assembles and captions without calling the provider at
  all. A `{year}-clean.png` beside it takes precedence (O5) — that is the
  hand-corrected variant.

`assemble` re-cuts an existing run and is safe to repeat; the frame order is
newest-first and comes from one definition (`VideoAssemblyRunner.NewestFirst`)
at every call site, so a resumed run composes the same cut as the original.

## Publishing

```
dotnet run -- publish <runFolder>          # queue it for review (copies into output/on-review/)
dotnet run -- publish <runFolder> --yes    # post it now, no review — the test mode
dotnet run -- review                       # the Telegram approval loop; Ctrl+C stops
```

Off by default (`Publish:Enabled=false`): a finishing run is not queued and
`review` refuses to start. `--yes` works regardless. Full design, first
contact with the bot, and the P-checks: `docs/13-Publishing.md`.

## Verifying without spending

All offline, no API key needed:

```
dotnet run -- --smoke-prompts    # C-checks over every generated prompt + folder suite
dotnet run -- --smoke-video      # ffmpeg timeline and year overlay
dotnet run -- --smoke-batch      # batch provider against a fake
dotnet run -- --smoke-vision     # vision answer parsing against a fake
dotnet run -- --smoke-publish    # review loop, queue and the three HTTP providers against fakes
```

`vision-variance <folder> [--repeat N]` and
`vision-accuracy <folder> [--no-double-check]` are the two live diagnostics:
variance reports how many distinct values each SceneDna field takes across a
folder; accuracy grades Vision against a hand-labelled `expected.json` beside
the photos (see `testFolder8/`). Both cost Vision calls, neither generates
images.
