
# Pipeline

## Purpose

The Pipeline describes how information flows through the LifeOverYears platform.

It does not describe implementation details.

It does not describe AI providers.

It does not describe software components.

The Pipeline represents the logical transformation of data from the initial input to the final published content.

Every stage consumes one domain object and produces another.

---

# Pipeline Philosophy

LifeOverYears is a data transformation engine.

The platform is built around the evolution of structured information rather than the execution of AI models.

AI providers, APIs, and implementation details may change over time.

The logical Pipeline should remain stable.

---

# Logical Pipeline

```text
Modern Photo
        │
        ▼
SceneDNA
        │
        ▼
Prompt
        │
        ▼
Historical Images
        │
        ▼
Video
        │
        ▼
Caption
        │
        ▼
Publication
