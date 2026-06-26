# MVP

## Purpose

The MVP validates the complete end-to-end workflow of LifeOverYears.

The goal is not automation at scale. The goal is to prove that a single modern photograph can be transformed into multiple historically inspired scenes and finally into a publishable short-form video.

---

# Success Criteria

Given one modern photograph and one or more target years, the system produces:

- SceneDNA
- Prompt
- Historical Images
- Short Video
- Caption

---

# Scope

## Input

- Modern photograph
- Target years (1975, 1985, 1995, 2005, 2015, 2025)

## Output

- One generated image for each requested year
- Vertical video
- Caption

---

# MVP Pipeline

Input Photo
→ SceneDNA Builder
→ Prompt Generation
→ Historical Image Generation (1975 / 1985 / 1995 / 2005 / 2015 / 2025)
→ Video Composition
→ Caption Generation

SceneDNA is generated once.

The same SceneDNA is reused for every requested historical era.

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

- Quality Validation
- Database
- Distributed Workers
- Queue System
- Plugin Architecture
- Automatic Publishing
- Comment Analysis
- Analytics
- Billing

---

# Manual Steps

The operator is responsible for:

- Selecting the input photo
- Selecting target years
- Reviewing generated images
- Selecting the final images
- Publishing to social media

---

# Design Principles

1. Build one complete pipeline before optimizing it.
2. SceneDNA is the central domain model.
3. AI populates SceneDNA rather than defining it.
4. One SceneDNA can generate multiple historical eras.
5. Keep the MVP simple and highly iterative.

---

# Deliverable

The MVP is complete when one command can generate a complete historical video package from a single modern photograph using a reusable SceneDNA and one or more target historical years.