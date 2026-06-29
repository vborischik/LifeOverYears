# QA Scenarios — Gas Stations

## Scenario 1 — Corner lot, suburban intersection
Photo: modern Shell station on a corner lot, suburban street,
one-story convenience store, flat roof, large canopy,
concrete road, sidewalk both sides,
two mature oak trees on the left, one medium maple on the right.

Expected scene_type: gas_station
Expected eras to generate: 2015, 2005, 1985, 1975

Tree regression checks:
- Left oaks (large): 2015 medium, 2005 small, 1985 sapling, 1975 absent
- Right maple (medium): 2015 small, 2005 sapling, 1985 absent, 1975 absent

Era-specific checks:
- 1975: Flying A or Gulf branding, no convenience store, mechanical pump island, attendant booth
- 1985: Early self-service layout, no digital displays, older canopy style
- 2005: Modern canopy but older Shell logo variant, no contactless payment signage
- 2015: LED price sign, updated Shell branding, loyalty card reader visible

---

## Scenario 2 — Highway off-ramp, rural setting
Photo: modern Mobil station on a highway off-ramp, rural road,
large canopy spanning four pumps, attached fast-food kiosk,
no sidewalks, gravel shoulder, one large pine tree behind building,
flat terrain, power lines overhead.

Expected scene_type: gas_station
Expected eras to generate: 2015, 2005, 1985, 1975

Tree regression checks:
- Rear pine (large): 2015 medium, 2005 small, 1985 sapling, 1975 absent

Era-specific checks:
- 1975: Mobil Pegasus logo, single pump island, no fast food, attendant present
- 1985: Early self-service pumps, rotary price signs, no canopy or small canopy
- 2005: Full canopy, Mobil rebranding to ExxonMobil in progress
- 2015: ExxonMobil branding complete, touchscreen pumps, no attached kiosk yet

---

## Scenario 3 — Urban downtown, tight lot
Photo: modern BP station on a narrow urban lot, downtown street,
four-story mixed-use building adjacent on left, two-lane one-way road,
no trees, utility poles with power lines, concrete sidewalk on both sides,
small canopy over two pump islands.

Expected scene_type: gas_station
Expected eras to generate: 2015, 2005, 1985, 1975

Tree regression checks:
- No trees present: no regression needed; 1975 era may introduce absent-state trees
  that were removed before 2025

Era-specific checks:
- 1975: Standard Oil or Gulf branding, attendant booth, gravity-feed pump globes
- 1985: BP or ARCO branding, coin-operated air pump, leaded/unleaded split signage
- 2005: BP Helios logo, early pay-at-pump terminals
- 2015: BP green canopy fascia, chip card readers, digital price boards

---

## Scenario 4 — Suburban strip, mid-block
Photo: modern Chevron station mid-block on a suburban arterial road,
single convenience store (ExtraMile), large flat-roof canopy over six pumps,
concrete apron, no sidewalk on station side, three small ornamental trees
along the street edge, residential neighborhood visible in background.

Expected scene_type: gas_station
Expected eras to generate: 2015, 2005, 1985, 1975

Tree regression checks:
- Three ornamental street trees (small): 2015 sapling, 2005 absent, 1985 absent, 1975 absent

Era-specific checks:
- 1975: Standard/Chevron early logo, no convenience store, full-service island
- 1985: Chevron chevron stripe logo, self-service dominant, no ExtraMile
- 2005: Chevron with Techron branding, early ExtraMile concept
- 2015: Current Chevron branding, ExtraMile signage, loyalty PIN pad

---

## Scenario 5 — Abandoned/converted former gas station
Photo: a former gas station converted to a tire shop, suburban street,
canopy removed, pump islands covered with asphalt, original 1960s-style
main building preserved, two large elm trees flanking the entrance,
one medium spruce in rear lot.

Expected scene_type: gas_station
Expected eras to generate: 2015, 2005, 1985, 1975

Tree regression checks:
- Front elms (large): 2015 medium, 2005 small, 1985 sapling, 1975 absent
- Rear spruce (medium): 2015 small, 2005 sapling, 1985 absent, 1975 absent

Era-specific checks:
- 1975: Active station, original canopy intact, pump islands visible, era-appropriate branding
- 1985: Station still active, updated signage but same building footprint
- 2005: Possible closure signage or transitional state
- 2015: Tire shop conversion evident, no pump islands, canopy already removed

Regression validation note:
- The building itself should appear in active gas station use for 1975–2005 eras;
  its conversion to a tire shop is a 2015+ event and should not appear in earlier eras.
