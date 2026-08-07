# Smoke Test Report

Generated: 2026-08-07T06:22:25.0879194+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| C1 | Era deserialization: scene_content has required keys, color_mode present, people pools >= 20 | ✅ PASS | All 6 eras OK |
| C2 | No unresolved {TOKEN} of any kind remains in any prompt | ✅ PASS | All placeholders resolved |
| C3 | No vehicle model reuse within each run (dedup invariant) | ✅ PASS | No duplicates in any run |
| C4 | Vehicle count in range and VEHICLES section lines match SelectedVehicles.Count | ✅ PASS | All vehicle counts correct |
| C5 | Run1 vs Run2: ≥3 years differ in vehicles; no year has identical full text | ✅ PASS | Sufficient variance between seeds |
| C6 | Tree canopy proportion vs. the base image (distinct per era for mature trees, size-relative), and no TREES section or tree mention in the source year | ✅ PASS | Tree ladder and source-year omission correct |
| C7 | 1975=B&W (STRICTLY BLACK AND WHITE); 1985-2025=COLOR photograph | ✅ PASS | Color mode correct in all prompts |
| C8 | Gas station fuel prices always present; downtown coffee price in ≥1 run per year | ✅ PASS | All price anchors found |
| C9 | PRESERVE block contains all building types and immutable elements verbatim | ✅ PASS | All building types and immutable elements present |
| C10 | No TEXT OVERLAY section remains; year still anchors the VEHICLES block and carries the ranged-model-year restriction | ✅ PASS | Overlay removed, vehicle year anchors correct, model-year restriction present |
| C11 | Every prompt is under 920 words | ✅ PASS | All prompts under 920 words |
| C12 | B&W prompts contain no vehicle pool colors, no 'Fashion palette', no 'desaturated' | ✅ PASS | B&W prompts are color-free |
| C13 | Color eras: every vehicle has a color and no color repeats within one prompt | ✅ PASS | All vehicle colors unique per prompt |
| C14 | Gas station 2025 prompt has no EV/electric/charger/Lightning content | ✅ PASS | 2025 gas prompts are fully de-electrified |
| C15 | Every prompt contains the populate-empty-base header and the sidewalk rule | ✅ PASS | Populate header and sidewalk rule present everywhere |
| C16 | Every prompt with a TREES section contains the tree-size override line | ✅ PASS | Tree-size override present in all TREES sections |
| C17 | Every specific_models entry (cars+trucks) starts on or before its era year | ✅ PASS | All model year ranges are era-valid |
| C18 | Every prompt has a PLACEMENT line; no repeated pattern per run unless the pool is exhausted | ✅ PASS | Placement present and de-duplicated per pool |
| C19 | No descriptive-as-signage leaks; {DINER_NAME} resolved and identical across a run | ✅ PASS | Business names clean and diner name stable |
| C20 | Every live prompt has a two-sign 'window signs:' line, >=1 extras line, and a people_mix line; derelict eras carry none of them | ✅ PASS | All three sampling axes present in every prompt |
| C21 | Run1 vs Run2: >=3 of 6 years differ in sampled extras or window signs | ✅ PASS | Sufficient sampling variance between seeds |
| C22 | Every prompt is at most 6000 characters | ✅ PASS | All prompts within 6000 chars |
| C23 | default/unknown scenes always thriving; rank monotonic per run (gas-station finale may resolve to 'new' or 'restored'); abandoned/declining/squatted counts honored for gas_station, downtown_street and strip_mall; 'squatted' only on a gas_station's final era; 'restored' only on a gas_station's final era | ✅ PASS | Condition trajectory invariants hold |
| C24 | Every business-name token resolves to a member of its own pool and stays identical across all six eras of a run | ✅ PASS | All 8 business tokens resolve correctly and remain stable per run |
| C25 | DECAY present iff condition is declining/abandoned/squatted; healthy conditions keep verbatim era road markings with no DECAY; DECAY never precedes OUTPUT FORMAT (i.e. never inside PRESERVE) and never mentions geometry terms; bullets are drawn from the correct severity pool | ✅ PASS | Decay section invariants hold |
| C26 | Caption prompt files load; every scene_content type has a caption voice; anchor pools are well-formed and non-leaking; AnglesFor() composition holds; every reachable condition maps to a real phrase | ✅ PASS | Caption voice coverage holds |
| C27 | base-clean.txt loads, declares the exact 9:16 portrait phrase (and no competing aspect-ratio term), keeps its people/vehicle-removal + pixel-identical/canvas-extension cleanup contract, and every generated prompt carries the same portrait phrase | ✅ PASS | base-clean/prompt aspect-ratio contract holds |
| C28 | People bullet lines (people_activities picks and the people_mix line) never repeat within a run unless their era's own pool is already exhausted | ✅ PASS | No premature people-line repeats |
| C29 | DeclineBias() ramps non-decreasing across the run and stays within 0..1 | ✅ PASS | Bias ramp OK across all eras |
| C30 | Neither chain ever appears Named in a prompt for a year before 1990 | ✅ PASS | No pre-1990 named chain tenants |
| C31 | Blockbuster never appears in a downtown_street prompt, in any form | ✅ PASS | No Blockbuster content in any downtown_street prompt |
| C32 | Neither chain ever appears Named in an abandoned or squatted era | ✅ PASS | No named chain tenants in derelict eras |
| C33 | Chain tenant presence is stable across a run: no flicker between schedule-eligible eras | ✅ PASS | No presence flicker across 20 seeds x 6 eras |
| C34 | A derelict era emits the ghost line whenever the run's chain schedule calls for one | ✅ PASS | Ghost lines present wherever the schedule calls for them |
| C35 | A derelict era never emits a Named or Generic chain tenant line | ✅ PASS | No Named/Generic chain content in any derelict block |
| C36 | Street-shaped placement language (sidewalk zones, curb-hugging, PLACEMENT wording) is gated on SceneDna geometry | ✅ PASS | Street language present only where geometry supports it |
| C37 | Synthetic base prompts carry scene geometry with no source-photo wording; era PRESERVE header unchanged | ✅ PASS | Synthetic base prompts well-formed and era prompts unchanged |
| C38 | A tree's size is stated in exactly one place per prompt: never in the era PRESERVE block, always in the synthetic base's geometry block | ✅ PASS | No double-statement; synthetic base carries every tree the era PRESERVE block omits |
| C39 | A 'new' condition prompt never pairs pristine surfaces with an unexplained weathered ghost sign | ✅ PASS | No unreconciled ghost-sign contradiction in any 'new' condition prompt |
| C40 | image-template.txt carries the PRIORITY ORDER rule; every era prompt's SIGNAGE RESTRICTION whitelist lists exactly the quoted strings from its own scene block; the old blanket quotes-only line is gone | ✅ PASS | Priority order present; signage whitelist consistent everywhere; old line removed |

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1973-1980 Chevrolet C10 — square body, chrome bumper, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional, 1970-1978 AMC Gremlin — short stubby hatchback rear |
| 1985 | 3 | 1982-1988 Chevrolet Celebrity — boxy front-wheel drive sedan, 1980-1985 Cadillac Seville — sharp formal lines, bustleback, 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood |
| 1995 | 3 | 1992-1996 Ford F-150 — rounded aero body, 1988-1998 Chevrolet C/K 1500 — softly squared pickup, 1993-1998 Jeep Grand Cherokee — early SUV, boxy-rounded |
| 2005 | 3 | 2002-2009 Chevrolet TrailBlazer — mid-size SUV boxy, 2001-2007 Ford Escape — compact boxy SUV, 2000-2005 Ford Focus — European-styled compact |
| 2015 | 3 | 2011-2016 Kia Optima — stylish mid-size, sporty, 2013-2018 Hyundai Santa Fe — fluidic sculpture styling, 2007-2017 Jeep Wrangler — boxy off-roader, round headlights |
| 2025 | 4 | 2022-2025 Honda Civic — clean mature compact, 2023-2025 Honda Accord — clean minimalist refresh, 2019-2025 Chevrolet Equinox — rounded compact crossover, 2017-2025 Honda CR-V — rounded best-selling crossover |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1963-1976 Dodge Dart — compact, boxy, reliable workhorse, 1970-1976 AMC Hornet — compact, simple boxy lines, 1974-1978 Oldsmobile Cutlass Supreme — best-selling car in America, formal roofline |
| 1985 | 4 | 1984-1990 Dodge Caravan — first minivan, boxy, 1981-1985 Ford Escort — small boxy economy hatchback, 1977-1990 Chevrolet Caprice — boxy full-size sedan, formal lines, 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood |
| 1995 | 3 | 1993-1998 Jeep Grand Cherokee — early SUV, boxy-rounded, 1994-1997 Honda Accord — smooth rounded sedan, 1994-1998 Ford Mustang — rounded SN95 pony car |
| 2005 | 4 | 2002-2006 Toyota Camry — smooth conservative mid-size, 2000-2006 Chevrolet Tahoe — full-size SUV peak era, 2004-2008 Pontiac Grand Prix — sporty sedan plastic cladding, 2003-2009 Hummer H2 — massive military-styled SUV |
| 2015 | 1 | 2013-2016 Mazda CX-5 — flowing KODO-design crossover |
| 2025 | 0 |  |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1973-1980 Chevrolet C10 — square body, chrome bumper, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional, 1970-1978 AMC Gremlin — short stubby hatchback rear, 1974-1978 Cadillac DeVille — full-size luxury, formal roofline, chrome heavy |
| 1985 | 5 | 1984-1987 Toyota Corolla — boxy compact, reliable look, 1983-1987 Honda Accord — clean lines, pop-up headlights, 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood, 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline, 1980-1985 Buick LeSabre — boxy full-size, chrome trim |
| 1995 | 5 | 1991-1994 Saturn SL — plastic body panels, compact, 1990-1997 Mazda Miata — tiny rounded roadster, pop-up lights, 1989-1997 Geo Metro — very small economy hatchback, 1994-2001 Dodge Ram — big rig style grille, bold, 1992-1996 Toyota Camry — rounded, understated |
| 2005 | 1 | 2001-2007 Dodge Grand Caravan — family minivan, rounded |
| 2015 | 0 |  |
| 2025 | 0 |  |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1963-1976 Dodge Dart — compact, boxy, reliable workhorse, 1970-1976 AMC Hornet — compact, simple boxy lines, 1974-1978 Oldsmobile Cutlass Supreme — best-selling car in America, formal roofline, 1971-1976 Chevrolet G10 Sportvan — boxy windowed van, chrome bumper |
| 1985 | 6 | 1979-1985 Ford Mustang — Fox body, angular hatchback coupe, 1984-1989 Plymouth Voyager — boxy first-generation minivan, 1981-1985 Ford Escort — small boxy economy hatchback, 1980-1985 Buick LeSabre — boxy full-size, chrome trim, 1978-1986 Ford Bronco — full-size boxy SUV, round headlights, 1983-1987 Honda Accord — clean lines, pop-up headlights |
| 1995 | 6 | 1990-1997 Mazda Miata — tiny rounded roadster, pop-up lights, 1993-1998 Jeep Grand Cherokee — early SUV, boxy-rounded, 1995-2004 Toyota Tacoma — compact, rounded, 1991-1995 Dodge Caravan — rounded second-gen minivan, 1993-1997 Ford Ranger — compact pickup, straight lines, 1990-1994 Chevrolet Lumina — rounded mid-size sedan |
| 2005 | 1 | 2001-2007 Toyota Highlander — early crossover |
| 2015 | 0 |  |
| 2025 | 0 |  |

