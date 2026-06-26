# MVP

## Purpose

The purpose of the MVP is to validate the complete production pipeline from a single input photograph to a publishable short-form video.

The MVP is intended to prove that the architecture, data model, and AI workflow can consistently produce engaging historical content.

---

# Success Criteria

Given one modern photograph, the system automatically produces:

- SceneDNA
- AI Prompt
- Historical Image
- Short Video
- Caption

The result should require only minimal manual review before publishing.

---

# Scope

## Input

- One modern photograph
- Target year (for example: 1975)

## Output

- AI generated historical image
- Vertical video
- Caption
- Ready-to-publish assets

---

# MVP Pipeline

Input Image
→ Location Discovery
→ Scene Analysis
→ SceneDNA
→ Prompt Generation
→ Image Generation
→ Quality Validation
→ Video Composition
→ Caption Generation

---

# Included Components

- SceneDNA
- EraProfile
- Prompt Generator
- NVIDIA Image Generation
- FFmpeg Video Composition
- Caption Generator

---

# Excluded From MVP

- Database
- Distributed workers
- Queue system
- Multi-user support
- Plugin architecture
- Automatic publishing
- Comment analysis
- Analytics
- Billing

---

# Manual Steps

The operator is responsible for:

- Selecting the input photo
- Reviewing the generated image
- Choosing the final result
- Publishing to social platforms

---

# Design Principles

1. Simplicity over completeness.
2. One working pipeline before optimization.
3. Structured data before prompt engineering.
4. Manual validation before automation.
5. Replaceable AI providers.

---

# Deliverable

The MVP is complete when one command can transform a single photograph into a historically inspired video package ready for publication.