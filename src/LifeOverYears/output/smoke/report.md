# Smoke Test Report

Generated: 2026-07-20T20:05:49.0920399+00:00

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
| C11 | Every prompt is under 720 words (limit raised from 650 for condition, brand, traffic-flow, and pump-coupling lines) | ✅ PASS | All prompts under 720 words |
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
| C22 | Every prompt is at most 4850 characters | ✅ PASS | All prompts within 4850 chars |
| C23 | Conditions stay gas-station-only: downtown always thriving with no zero-out lines; gas abandoned/declining prompts honor their counts | ✅ PASS | No condition leakage; gas condition counts honored |
| C24 | Every business-name token resolves to a member of its own pool and stays identical across all six eras of a run | ✅ PASS | All 8 business tokens resolve correctly and remain stable per run |

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1973-1980 Chevrolet C10 — square body, chrome bumper, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional, 1970-1978 AMC Gremlin — short stubby hatchback rear |
| 1985 | 3 | 1982-1986 Nissan Sentra — small economy boxy sedan, 1984-1989 Plymouth Voyager — boxy first-generation minivan, 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood |
| 1995 | 4 | 1993-1997 Toyota Corolla — rounded compact sedan, 1991-1996 Ford Escort — small rounded economy car, 1988-1998 Chevrolet C/K 1500 — softly squared pickup, 1995-1999 Dodge Neon — small rounded economy, friendly face |
| 2005 | 2 | 2001-2007 Ford Escape — compact boxy SUV, 2003-2009 Hummer H2 — massive military-styled SUV |
| 2015 | 0 |  |
| 2025 | 3 | 2017-2025 Honda CR-V — rounded best-selling crossover, 2021-2025 Ford F-150 — refreshed, full-width LED bar options, 2021-2025 Kia Telluride — boxy upscale three-row SUV |

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
| 1975 | 4 | 1972-1980 Dodge D100 — angular cab, simple grille, 1974-1983 Jeep Cherokee — two-door SUV, boxy, new model, 1968-1979 Chevrolet Nova — compact, simple three-box shape, 1972-1976 Lincoln Continental Mark IV — long hood personal luxury coupe |
| 1985 | 5 | 1982-1992 Chevrolet Camaro — wedge-shaped sporty coupe, 1984-1988 Pontiac Fiero — small wedge two-seater, 1975-1991 Ford Econoline — boxy full-size van, 1977-1990 Chevrolet Caprice — boxy full-size sedan, formal lines, 1984-1987 Toyota Corolla — boxy compact, reliable look |
| 1995 | 4 | 1991-1996 Ford Escort — small rounded economy car, 1989-1997 Geo Metro — very small economy hatchback, 1991-1994 Saturn SL — plastic body panels, compact, 1988-1998 Chevrolet C/K 1500 — softly squared pickup |
| 2005 | 6 | 1998-2005 Volkswagen New Beetle — retro bubble shape, 2002-2006 Toyota Camry — smooth conservative mid-size, 2001-2007 Dodge Grand Caravan — family minivan, rounded, 2002-2008 Dodge Ram — big rig grille evolved, 2004-2008 Ford F-150 — bigger, bolder grille, 2003-2007 Honda Accord — clean lines, sharper than 1990s |
| 2015 | 6 | 2010-2016 Chevrolet Equinox — mid-size crossover, 2007-2017 Jeep Wrangler — boxy off-roader, round headlights, 2011-2016 Hyundai Elantra — swoopy fluidic compact, 2009-2018 Ram 1500 — crosshair grille, refined, 2013-2019 Ford Fusion — Aston-Martin-style grille, sleek, 2014-2018 Chevrolet Silverado — squared modern grille |
| 2025 | 6 | 2017-2025 Honda CR-V — rounded best-selling crossover, 2019-2025 Subaru Outback — rugged wagon crossover, 2021-2025 Chevrolet Tahoe — huge full-size SUV, 2019-2025 GMC Sierra — chrome-heavy full-size pickup, 2023-2025 Kia Sportage — futuristic boomerang lights, 2021-2025 Nissan Rogue — squared-off crossover, floating roofline |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1971-1980 Chevrolet Vega — small subcompact hatchback/wagon, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional |
| 1985 | 6 | 1980-1985 Cadillac Seville — sharp formal lines, bustleback, 1978-1986 Ford Bronco — full-size boxy SUV, round headlights, 1979-1985 Ford Mustang — Fox body, angular hatchback coupe, 1982-1986 Nissan Sentra — small economy boxy sedan, 1983-1987 Honda Accord — clean lines, pop-up headlights, 1984-1989 Plymouth Voyager — boxy first-generation minivan |
| 1995 | 4 | 1995-2004 Toyota Tacoma — compact, rounded, 1995-1999 Chevrolet Cavalier — compact, rounded, 1989-1997 Geo Metro — very small economy hatchback, 1991-1996 Chevrolet Caprice — whale-shaped, rounded full-size |
| 2005 | 6 | 2000-2005 Ford Focus — European-styled compact, 2005-2010 Chevrolet Cobalt — compact economy sedan, 2001-2007 Ford Escape — compact boxy SUV, 2002-2006 Toyota Camry — smooth conservative mid-size, 2003-2009 Hummer H2 — massive military-styled SUV, 2004-2010 Toyota Sienna — large rounded minivan |
| 2015 | 6 | 2015-2020 Ford Edge — mid-size crossover, bold grille, 2014-2019 Nissan Rogue — popular compact crossover, 2011-2016 Hyundai Elantra — swoopy fluidic compact, 2011-2016 Volkswagen Jetta — clean simple sedan, 2011-2016 Chrysler Town & Country — chrome-trimmed minivan, 2011-2016 Kia Optima — stylish mid-size, sporty |
| 2025 | 6 | 2020-2025 Hyundai Tucson — sharp parametric grille design, 2019-2025 GMC Sierra — chrome-heavy full-size pickup, 2022-2025 Toyota Tundra — massive grille, muscular stance, 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2019-2025 Ram 1500 — crew cab pickup, large grille, 2017-2025 Honda CR-V — rounded best-selling crossover |

