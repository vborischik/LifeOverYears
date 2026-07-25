# Smoke Test Report

Generated: 2026-07-23T05:41:25.3306207+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| C1 | Era deserialization: scene_content has required keys and color_mode present | ✅ PASS | All 6 eras OK |
| C2 | No unresolved {TOKEN} of any kind remains in any prompt | ✅ PASS | All placeholders resolved |
| C3 | No vehicle model reuse within each run (dedup invariant) | ✅ PASS | No duplicates in any run |
| C4 | Vehicle count in range and VEHICLES section lines match SelectedVehicles.Count | ✅ PASS | All vehicle counts correct |
| C5 | Run1 vs Run2: ≥3 years differ in vehicles; no year has identical full text | ✅ PASS | Sufficient variance between seeds |
| C6 | Tree size ladder (distinct per era for mature trees, size-relative) and tree position+species in all prompts | ✅ PASS | Tree ladder and positions correct |
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
| C20 | Every prompt has a two-sign 'window signs:' line, >=1 extras line, and a people_mix line | ✅ PASS | All three sampling axes present in every prompt |
| C21 | Run1 vs Run2: >=3 of 6 years differ in sampled extras or window signs | ✅ PASS | Sufficient sampling variance between seeds |
| C22 | Every prompt is at most 4900 characters | ✅ PASS | All prompts within 4900 chars |
| C23 | strip_mall/default always thriving; rank monotonic per run (gas-station finale may resolve to 'new'); abandoned/declining/squatted counts honored for gas_station and downtown_street; 'squatted' only on a gas_station's final era | ✅ PASS | Condition trajectory invariants hold |
| C24 | Every business-name token resolves to a member of its own pool and stays identical across all six eras of a run | ✅ PASS | All 8 business tokens resolve correctly and remain stable per run |
| C25 | DECAY present iff condition is declining/abandoned/squatted; healthy conditions keep verbatim era road markings with no DECAY; DECAY never precedes OUTPUT FORMAT (i.e. never inside PRESERVE) and never mentions geometry terms; bullets are drawn from the correct severity pool | ✅ PASS | Decay section invariants hold |

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1973-1980 Chevrolet C10 — square body, chrome bumper, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional, 1970-1978 AMC Gremlin — short stubby hatchback rear |
| 1985 | 3 | 1982-1986 Nissan Sentra — small economy boxy sedan, 1984-1989 Plymouth Voyager — boxy first-generation minivan, 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood |
| 1995 | 4 | 1993-1997 Toyota Corolla — rounded compact sedan, 1991-1996 Ford Escort — small rounded economy car, 1988-1998 Chevrolet C/K 1500 — softly squared pickup, 1995-1999 Dodge Neon — small rounded economy, friendly face |
| 2005 | 2 | 2001-2007 Ford Escape — compact boxy SUV, 2003-2009 Hummer H2 — massive military-styled SUV |
| 2015 | 0 |  |
| 2025 | 1 | 2019-2025 Ram 1500 — crew cab pickup, large grille |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1963-1976 Dodge Dart — compact, boxy, reliable workhorse, 1970-1976 AMC Hornet — compact, simple boxy lines, 1974-1978 Oldsmobile Cutlass Supreme — best-selling car in America, formal roofline |
| 1985 | 4 | 1983-1988 Ford Thunderbird — aero coupe, rounded, 1980-1986 Ford F-150 — square body, dual headlights, 1981-1985 Ford Escort — small boxy economy hatchback, 1984-1990 Dodge Caravan — first minivan, boxy |
| 1995 | 3 | 1988-1998 Chevrolet C/K 1500 — softly squared pickup, 1991-1994 Saturn SL — plastic body panels, compact, 1990-1997 Mazda Miata — tiny rounded roadster, pop-up lights |
| 2005 | 3 | 2001-2005 Honda Civic — rounded compact, very common, 2002-2006 Toyota Camry — smooth conservative mid-size, 1998-2005 Volkswagen New Beetle — retro bubble shape |
| 2015 | 4 | 2013-2019 Ford Fusion — Aston-Martin-style grille, sleek, 2011-2016 Honda CR-V — rounded compact crossover, 2014-2021 Subaru Outback — rugged wagon crossover, 2013-2016 Mazda CX-5 — flowing KODO-design crossover |
| 2025 | 3 | 2023-2025 Honda Accord — clean minimalist refresh, 2022-2025 Ford Maverick — small unibody pickup, 2021-2025 Chrysler Pacifica — sleek minivan, thin lights |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1973-1980 Chevrolet C10 — square body, chrome bumper, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional, 1970-1978 AMC Gremlin — short stubby hatchback rear, 1974-1978 Cadillac DeVille — full-size luxury, formal roofline, chrome heavy |
| 1985 | 5 | 1975-1991 Ford Econoline — boxy full-size van, 1983-1987 Honda Accord — clean lines, pop-up headlights, 1973-1987 Chevrolet C/K — square body pickup, dual headlights, 1980-1985 Buick LeSabre — boxy full-size, chrome trim, 1984-1988 Toyota Pickup — small, boxy, popular import |
| 1995 | 5 | 1992-1995 Honda Civic — small rounded coupe and sedan, 1992-1995 Pontiac Grand Am — compact with ribbed plastic cladding, 1995-2004 Toyota Tacoma — compact, rounded, 1991-1996 Chevrolet Caprice — whale-shaped, rounded full-size, 1986-1997 Ford Aerostar — boxy rear-drive minivan |
| 2005 | 5 | 2002-2008 Dodge Ram — big rig grille evolved, 2005-2010 Jeep Grand Cherokee — rounded modern SUV, 2003-2008 Toyota Corolla — conservative compact sedan, 1999-2006 Chevrolet Silverado — squared modern look, 2003-2009 Hummer H2 — massive military-styled SUV |
| 2015 | 2 | 2012-2017 Toyota Camry — sharper creased mid-size sedan, 2014-2021 Subaru Outback — rugged wagon crossover |
| 2025 | 0 |  |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1963-1976 Dodge Dart — compact, boxy, reliable workhorse, 1970-1976 AMC Hornet — compact, simple boxy lines, 1974-1978 Oldsmobile Cutlass Supreme — best-selling car in America, formal roofline, 1971-1976 Chevrolet G10 Sportvan — boxy windowed van, chrome bumper |
| 1985 | 6 | 1982-1986 Nissan Sentra — small economy boxy sedan, 1983-1985 Nissan Maxima — boxy import sedan, 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood, 1982-1992 Chevrolet Camaro — wedge-shaped sporty coupe, 1981-1985 Dodge Aries — K-car, boxy economy sedan, 1984-1988 Pontiac Fiero — small wedge two-seater |
| 1995 | 6 | 1991-1995 Dodge Caravan — rounded second-gen minivan, 1995-1999 Dodge Neon — small rounded economy, friendly face, 1992-1995 Pontiac Grand Am — compact with ribbed plastic cladding, 1992-1997 Ford Taurus — rounded jellybean shape, oval theme, 1989-1997 Geo Metro — very small economy hatchback, 1994-1997 Honda Accord — smooth rounded sedan |
| 2005 | 6 | 2004-2012 Chevrolet Colorado — mid-size pickup, 2005-2010 Jeep Grand Cherokee — rounded modern SUV, 2000-2005 Ford Focus — European-styled compact, 2001-2005 Honda Civic — rounded compact, very common, 1998-2005 Volkswagen New Beetle — retro bubble shape, 2002-2007 Jeep Liberty — round-headlight compact SUV |
| 2015 | 5 | 2009-2018 Ram 1500 — crosshair grille, refined, 2010-2016 Chevrolet Equinox — mid-size crossover, 2013-2016 Ford Escape — rounded compact crossover, 2011-2016 Hyundai Elantra — swoopy fluidic compact, 2011-2016 Honda CR-V — rounded compact crossover |
| 2025 | 4 | 2022-2025 Ford Maverick — small unibody pickup, 2021-2025 Kia Carnival — boxy SUV-styled minivan, 2021-2025 Chevrolet Tahoe — huge full-size SUV, 2019-2025 Subaru Forester — practical boxy crossover |

