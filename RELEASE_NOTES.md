# LifeOverYears — Version 1.0.0-beta.1

## Status

**Current Phase:** Pre-release Beta Testing
**Date:** July 2026
**Build:** Smoke tests: 25/25 passing

## What's Implemented ✅

### Core Pipeline
- **Step 1: SceneDNA Population** — Vision analysis via NVIDIA nemotron-3-nano-omni-30b
- **Step 2: Prompt Generation** — Programmatic assembly with scene-condition trajectory, first-era density, and physical decay blocks
- **Step 3: Image Generation** — OpenAI gpt-image-2 provider (sync + batch modes); StubImageProvider for testing
- **Step 4: Video Composition** — FFmpeg-based video assembly with xfade transitions and year overlays
- **Step 5: Caption Generation** — Google Gemma (via the NVIDIA API) caption generation

### Architecture
- Layered provider system (Transport → Domain → Services → Console)
- Scene-condition trajectory: downtown streets decline monotonically, gas stations can resolve to "new" or "squatted"
- Physical decay blocks showing asphalt, markings, and litter degradation
- Business-name token persistence across all six eras
- Vehicle deduplication within runs
- Tree-size ladder per era

### Quality Assurance
- 25 smoke test checks (C1–C25) all passing
- Prompt length validation (all under 4900 chars)
- Decay section invariants validated
- Video composition smoke tests (V1–V6, O1–O4) — requires FFmpeg on the test machine

---

## What Requires MVP-Level Testing

### Image Generation (Critical Path)
- [ ] Real OpenAI API integration test with gpt-image-2
- [ ] Batch API flow validation (50% cost reduction)
- [ ] File placement: verify `images/{year}.png` lands in correct run folder
- [ ] Error handling: API failures, rate limits, quota exhaustion

### End-to-End Run
- [ ] Full pipeline: photo → SceneDNA → prompts → images → video → caption
- [ ] Run folder structure and manifest recovery
- [ ] Manual image placement fallback (for testing without API calls)
- [ ] Collection mode (`dotnet run -- collect`) restart recovery

### Monetization Readiness
- [ ] Facebook In-Stream Ads eligibility (10K followers, 600K watch minutes)
- [ ] Sponsorship integration (Ancestry.com, AARP templates)
- [ ] Organic reach analytics (Meta Business Suite Insights or Supermetrics)
- [ ] Comment template effectiveness (A/B testing against the 55+ demographic)

---

## Known Limitations

1. **SceneDNA editing is manual** — Vision model output may need tweaks for perfect building geometry
2. **Image generation cost** — ~$0.05 per image (gpt-image-2 medium, estimated); batch API not yet exercised against the live endpoint
3. **No automatic publishing** — Step 6 (PublicationService → Telegram) is designed but not deployed
4. **Organic reach diagnostics unsolved** — Motion Creative Analytics doesn't support organic; need Supermetrics
5. **Tree-background artifacts visible** — Deliberately kept pending engagement data (artifacts drive comments)

---

## Next Steps (Post-Beta)

1. **Image Generation Testing** (this week)
   - Validate OpenAI provider on live API
   - Test batch mode with real jobs
   - Measure actual cost per run

2. **End-to-End Validation** (next week)
   - Run 5 complete videos with real images
   - Validate video output quality (transitions, overlays, timing)
   - Facebook upload and monetization threshold check

3. **Production Deployment** (month 2)
   - Enable automatic publishing via Telegram/Facebook
   - Integrate sponsorship templates
   - Launch 5 videos/day posting schedule

---

## How to Test Locally

### Prerequisites
- .NET 10 SDK
- FFmpeg
- OpenAI API key (for real image generation)
- NVIDIA API key (for vision and caption)

### Run Smoke Tests
```bash
cd src/LifeOverYears
dotnet run -- --smoke-prompts
dotnet run -- --smoke-video
```
