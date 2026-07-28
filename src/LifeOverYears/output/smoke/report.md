# Smoke Test Report

Generated: 2026-07-28T16:11:27.1913215+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| C1 | Era deserialization: scene_content has required keys and color_mode present | ✅ PASS | All 6 eras OK |
| C2 | No unresolved {TOKEN} of any kind remains in any prompt | ✅ PASS | All placeholders resolved |
| C3 | No vehicle model reuse within each run (dedup invariant) | ✅ PASS | No duplicates in any run |
| C4 | Vehicle count in range and VEHICLES section lines match SelectedVehicles.Count | ✅ PASS | All vehicle counts correct |
| C5 | Run1 vs Run2: ≥3 years differ in vehicles; no year has identical full text | ✅ PASS | Sufficient variance between seeds |
| C6 | Tree canopy proportion vs. source photo (distinct per era for mature trees, size-relative) and tree position+species in all prompts | ✅ PASS | Tree ladder and positions correct |
| C7 | 1975=B&W (STRICTLY BLACK AND WHITE); 1985-2025=COLOR photograph | ✅ PASS | Color mode correct in all prompts |
| C8 | Gas station fuel prices always present; downtown coffee price in ≥1 run per year | ✅ PASS | All price anchors found |
| C9 | PRESERVE block contains all building types and immutable elements verbatim | ✅ PASS | All building types and immutable elements present |
| C10 | No TEXT OVERLAY section remains; year still anchors the VEHICLES block | ✅ PASS | Overlay removed and vehicle year anchors correct |
| C11 | Every prompt is under 760 words (limit raised from 720 for the clear-driving-lane line) | ✅ PASS | All prompts under 760 words |
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
| C22 | Every prompt is at most 4900 characters | ✅ PASS | All prompts within 4900 chars |
| C23 | default/unknown scenes always thriving; rank monotonic per run (gas-station finale may resolve to 'new' or 'restored'); abandoned/declining/squatted counts honored for gas_station, downtown_street and strip_mall; 'squatted' only on a gas_station's final era; 'restored' only on a gas_station's final era | ✅ PASS | Condition trajectory invariants hold |
| C24 | Every business-name token resolves to a member of its own pool and stays identical across all six eras of a run | ✅ PASS | All 8 business tokens resolve correctly and remain stable per run |
| C25 | DECAY present iff condition is declining/abandoned/squatted; healthy conditions keep verbatim era road markings with no DECAY; DECAY never precedes OUTPUT FORMAT (i.e. never inside PRESERVE) and never mentions geometry terms; bullets are drawn from the correct severity pool | ✅ PASS | Decay section invariants hold |
| C26 | Caption prompt files load; every scene_content type has a caption voice; anchor pools are well-formed and non-leaking; AnglesFor() composition holds; every reachable condition maps to a real phrase | ✅ PASS | Caption voice coverage holds |
| C27 | base-clean.txt loads, declares the exact 9:16 portrait phrase (and no competing aspect-ratio term), keeps its people/vehicle-removal + pixel-identical/canvas-extension cleanup contract, and every generated prompt carries the same portrait phrase | ✅ PASS | base-clean/prompt aspect-ratio contract holds |
| C28 | People bullet lines (people_activities picks and the people_mix line) never repeat within a run unless their era's own pool is already exhausted | ✅ PASS | No premature people-line repeats |

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1973-1980 Chevrolet C10 — square body, chrome bumper, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional, 1970-1978 AMC Gremlin — short stubby hatchback rear |
| 1985 | 3 | 1982-1993 Chevrolet S-10 — compact pickup, square, 1982-1988 Chevrolet Celebrity — boxy front-wheel drive sedan, 1980-1986 Ford F-150 — square body, dual headlights |
| 1995 | 4 | 1992-1997 Ford Taurus — rounded jellybean shape, oval theme, 1991-1996 Chevrolet Caprice — whale-shaped, rounded full-size, 1990-1994 Chevrolet Lumina — rounded mid-size sedan, 1994-1998 Ford Mustang — rounded SN95 pony car |
| 2005 | 4 | 1999-2006 Chevrolet Silverado — squared modern look, 2004-2012 Chevrolet Colorado — mid-size pickup, 2000-2006 Chevrolet Tahoe — full-size SUV peak era, 2003-2009 Hummer H2 — massive military-styled SUV |
| 2015 | 0 |  |
| 2025 | 3 | 2019-2025 Subaru Outback — rugged wagon crossover, 2019-2025 Chevrolet Equinox — rounded compact crossover, 2019-2025 Toyota Corolla — sharp-nosed compact |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1963-1976 Dodge Dart — compact, boxy, reliable workhorse, 1970-1976 AMC Hornet — compact, simple boxy lines, 1974-1978 Oldsmobile Cutlass Supreme — best-selling car in America, formal roofline |
| 1985 | 4 | 1983-1987 Honda Accord — clean lines, pop-up headlights, 1984-1989 Plymouth Voyager — boxy first-generation minivan, 1984-1990 Dodge Caravan — first minivan, boxy, 1981-1985 Dodge Aries — K-car, boxy economy sedan |
| 1995 | 4 | 1992-1995 Honda Civic — small rounded coupe and sedan, 1993-2002 Pontiac Firebird — sleek pointed sports coupe, 1991-1996 Buick Roadmaster — large rounded wagon and sedan, 1991-1996 Chevrolet Caprice — whale-shaped, rounded full-size |
| 2005 | 3 | 2000-2007 Ford Taurus — rounded, aging fleet-look sedan, 2005-2010 Chevrolet Cobalt — compact economy sedan, 2004-2008 Chrysler 300 — bold boxy retro chrome grille |
| 2015 | 3 | 2010-2016 Chevrolet Equinox — mid-size crossover, 2011-2016 Hyundai Elantra — swoopy fluidic compact, 2014-2021 Subaru Outback — rugged wagon crossover |
| 2025 | 0 |  |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1973-1980 Chevrolet C10 — square body, chrome bumper, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional, 1970-1978 AMC Gremlin — short stubby hatchback rear, 1974-1978 Cadillac DeVille — full-size luxury, formal roofline, chrome heavy |
| 1985 | 6 | 1978-1986 Ford Bronco — full-size boxy SUV, round headlights, 1983-1985 Nissan Maxima — boxy import sedan, 1983-1988 Ford Thunderbird — aero coupe, rounded, 1982-1985 Toyota Celica — angular sporty coupe, pop-up lights, 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline, 1973-1991 Chevrolet Suburban — long boxy wagon-SUV |
| 1995 | 4 | 1993-1998 Jeep Grand Cherokee — early SUV, boxy-rounded, 1993-1997 Toyota Corolla — rounded compact sedan, 1995-2004 Toyota Tacoma — compact, rounded, 1988-1998 Chevrolet C/K 1500 — softly squared pickup |
| 2005 | 1 | 2000-2005 Ford Focus — European-styled compact |
| 2015 | 0 |  |
| 2025 | 0 |  |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1963-1976 Dodge Dart — compact, boxy, reliable workhorse, 1970-1976 AMC Hornet — compact, simple boxy lines, 1974-1978 Oldsmobile Cutlass Supreme — best-selling car in America, formal roofline, 1971-1976 Chevrolet G10 Sportvan — boxy windowed van, chrome bumper |
| 1985 | 4 | 1980-1985 Buick LeSabre — boxy full-size, chrome trim, 1982-1992 Chevrolet Camaro — wedge-shaped sporty coupe, 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood, 1983-1985 Nissan Maxima — boxy import sedan |
| 1995 | 5 | 1993-1998 Jeep Grand Cherokee — early SUV, boxy-rounded, 1991-1996 Chevrolet Caprice — whale-shaped, rounded full-size, 1995-1999 Chevrolet Cavalier — compact, rounded, 1994-1997 Honda Accord — smooth rounded sedan, 1995-2004 Toyota Tacoma — compact, rounded |
| 2005 | 1 | 2002-2006 Toyota Camry — smooth conservative mid-size |
| 2015 | 0 |  |
| 2025 | 0 |  |

