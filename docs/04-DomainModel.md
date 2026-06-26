# Domain Model

## Purpose

The Domain Model defines the core business objects of the LifeOverYears platform.

These objects represent the information processed by the system.

The Domain Model is independent of:

* programming language
* database
* AI providers
* storage
* APIs
* infrastructure

Every implementation should preserve these concepts regardless of technology.

---

# Core Principles

The platform is centered around immutable historical information.

Every object represents a specific stage of information.

Objects should evolve by creating new objects rather than mutating previous ones.

The domain should remain stable even if the implementation changes.

---

# Primary Domain Objects

```
Location
    │
    ▼
Scene
    │
    ▼
Era
    │
    ▼
SceneDNA
    │
    ▼
Prompt
    │
    ▼
HistoricalImage
    │
    ▼
Video
    │
    ▼
Caption
    │
    ▼
Publication
```

---

# Location

Represents a real-world place.

Examples:

* gas station
* supermarket
* cinema
* school
* street
* restaurant

A Location exists independently of time.

---

# Scene

Represents a specific photographic composition of a Location.

A Scene defines:

* camera position
* camera height
* camera direction
* framing
* visible buildings
* visible roads
* visible landscape

A Scene is immutable.

Multiple photographs may belong to the same Scene.

---

# Era

Represents a historical period.

Examples:

* 1955
* 1968
* 1975
* 1984
* 1992

An Era contains historical knowledge that describes how the Scene should appear.

---

# SceneDNA

Represents the immutable structural description of a Scene.

It describes what never changes.

Examples:

* building geometry
* road layout
* sidewalk positions
* utility poles
* tree positions
* camera placement
* viewing direction

SceneDNA is the foundation for historical reconstruction.

---

# Prompt

Represents instructions used to generate historical imagery.

A Prompt is derived from:

* SceneDNA
* Era
* historical knowledge

A Prompt is disposable.

It may be regenerated at any time.

---

# HistoricalImage

Represents one generated historical reconstruction.

Multiple images may exist for the same Scene and Era.

---

# Video

Represents a rendered animation created from one or more Historical Images.

---

# Caption

Represents text accompanying the published content.

Examples:

* title
* description
* hashtags
* narration

---

# Publication

Represents a published piece of content.

Examples:

* Instagram Reel
* TikTok
* Facebook Reel
* YouTube Short

A Publication references a Video together with its Caption.

---

# Relationships

```
Location
    └── Scenes

Scene
    ├── SceneDNA
    ├── HistoricalImages
    └── Videos

Era
    ├── Prompts
    └── HistoricalImages

SceneDNA
    └── Prompts

Prompt
    └── HistoricalImages

HistoricalImage
    └── Videos

Video
    ├── Caption
    └── Publications
```

---

# Notes

The Domain Model intentionally avoids implementation details.

It does not define:

* databases
* classes
* APIs
* AI providers
* workflows

Those concerns belong to the implementation architecture.