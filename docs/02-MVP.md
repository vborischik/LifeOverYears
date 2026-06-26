# MVP

## Purpose

The purpose of the MVP is to validate the core architecture of LifeOverYears.

The objective is not to build a fully automated platform.

The objective is to prove that a modern photograph can be transformed into historically inspired visual content through a reusable SceneDNA model.

---

# MVP Philosophy

The MVP focuses on validating the architecture rather than maximizing automation.

Only one part of the system is considered experimental:

**SceneDNA Population**

All remaining components are engineering and integration tasks built on existing providers and technologies.

Manual intervention is acceptable whenever it accelerates development or improves output quality.

---

# Success Criteria

Given:

- One modern photograph
- One or more target historical years

The system produces:

- SceneDNA
- Prompt
- Historical Images
- Short Video
- Caption

---

# Scope

## Input

- Modern photograph
- One or more historical years

Examples:

- 1975
- 1985
- 1995
- 2005
- 2015
- 2025

## Output

- One generated image for each requested year
- Vertical video
- Caption

---

# MVP Pipeline

Input Photo

↓

SceneDNA Population *(Experimental)*

↓

Prompt Generation

↓

Historical Image Generation

├── 1975

├── 1985

├── 1995

├── 2005

├── 2015

└── 2025

↓

Video Composition

↓

Caption Generation

---

# SceneDNA Population

SceneDNA Population is the only research-oriented component of the MVP.

Its responsibility is to populate a SceneDNA model from a modern photograph.

The first version of SceneDNA is intentionally minimal.

The goal is not to completely understand every aspect of the scene.

The goal is to extract only the information required to generate convincing historical images.

Manual editing of SceneDNA is an expected part of the MVP workflow.

---

# Included Components

- SceneDNA
- EraProfile
- Prompt Generation
- Historical Image Generation
- Video Composition
- Caption Generation

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
- Computer Vision
- Confidence Scoring

---

# Design Principles

1. Keep SceneDNA as simple as possible.
2. Build one complete pipeline before expanding the data model.
3. AI populates SceneDNA rather than defining it.
4. One SceneDNA can generate multiple historical eras.
5. Manual intervention is acceptable during the MVP.
6. SceneDNA Population will continuously evolve.

---

# Deliverable

The MVP is complete when a single modern photograph can be transformed into one or more historically inspired videos using a reusable SceneDNA model with only minimal manual intervention.
