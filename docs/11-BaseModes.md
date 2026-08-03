# Base Modes

## Purpose

Every era image is an edit of one shared base image. This document describes
how that base is produced. Two modes exist: `clean` and `synthetic`. The mode
is set in appsettings.json under Images.BaseMode.

Because every era edits the same base, camera position, framing and geometry
stay identical across the whole run. Only historical content changes from era
to era. This is what removes the visual jumps between years.

`synthetic` is the production mode. `clean` is a development mode.

## Flow

```
                        MODERN PHOTO
                             |
                             v
                      Vision -> SceneDNA
                    (what is actually visible in the photo)
                             |
                +------------+------------+
                |                         |
         BaseMode = "clean"        BaseMode = "synthetic"
                |                         |
                v                         v
      BuildCleanPrompt(dna)      BuildBaseAsync(dna)
                |                         |
      reads SceneDNA as a         reads SceneDNA as a
      REMOVAL LIST:               CONSTRUCTION SPEC:
      - all vehicles              - camera: eye-level, fov 60
      - all people                - 2-lane asphalt road
      - all trees                 - commercial building, left,
      - all fuel pumps              1 story, brick, flat roof
      - all modern signage        - 2 trees, large, left
                                  - poles, overhead lines
      Categories chosen by
      scene_type. Counts are      "Build this scene from
      never stated - always        nothing. Gas station,
      "all of X".                  here is the geometry."

      "This is a gas station.
       Remove all of the above.
       Do not alter geometry."
                |                         |
                v                         v
        EditImageAsync            GenerateImageAsync
        (edits the photo)         (text to image, no photo)
                |                         |
                v                         v
        base_clean.png            base_synthetic.png
                |                         |
                +------------+------------+
                             |
                    ONE BASE FOR EVERYTHING
                             |
        +------+------+------+------+------+
        v      v      v      v      v      v
      1975   1985   1995   2005   2015   2025
        |      |      |      |      |      |
     each era = base + EraProfile:
       - vehicles of that year
       - people and clothing
       - signage and brands
       - trees at the size for that year
       - condition / decay
        |      |      |      |      |      |
        +------+------+------+------+------+
                             |
                             v
                      Video + Caption
```

## The two readings of SceneDNA

SceneDNA is written once by Vision and read differently by each mode.

In `synthetic` it is a construction spec. The source photo never reaches the
image model. Geometry, buildings, trees and utilities all have to come from
the text, so SceneDNA accuracy directly determines base quality.

In `clean` it is a removal list. Geometry comes from the pixels of the source
photo, so restating it in the prompt is noise. What SceneDNA is needed for is
knowing which categories of object are present and must be stripped out.

## Removal categories are never counted

The clean prompt states categories, not quantities: "remove all vehicles",
not "remove 3 vehicles". Counting invites the model to leave a remainder.

Which categories apply is driven by scene_type:

| scene_type      | additional removals beyond vehicles/people/trees |
|-----------------|--------------------------------------------------|
| gas_station     | fuel pumps, pump islands, all fuel branding      |
| downtown_street | storefront displays, modern signage              |
| strip_mall      | parking lot markings, rooftop HVAC units         |

## Current state

`synthetic` is implemented end to end: Pipeline lines 88-94 call
BuildBaseAsync then SynthesizeBaseAsync, and every era edits the resulting
base_synthetic.png.

`clean` is only partly implemented. Pipeline lines 98-101 load the static
data/prompts/base-clean.txt and pass it straight through. SceneDNA is not
consulted, so no scene-type-aware removal happens. BuildCleanPrompt does not
exist yet.

## Open work on clean mode

- Add BuildCleanPrompt(SceneDna) to PromptService.
- Define per-scene-type removal categories as data, not code.
- Have Pipeline call BuildCleanPrompt instead of LoadPromptAsync("base-clean").
- Verify on a gas station photo that pumps and trees actually disappear.

This is deferred. `synthetic` is the production path and takes priority.
