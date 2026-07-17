# Smoke Test Report

Generated: 2026-07-17T13:30:30.8592510+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| C1 | Era deserialization: scene_content has required keys and color_mode present | ✅ PASS | All 6 eras OK |
| C2 | No unresolved template placeholders remain | ✅ PASS | All placeholders resolved |
| C3 | No vehicle model reuse within each run (dedup invariant) | ✅ PASS | No duplicates in any run |
| C4 | Vehicle count in range and VEHICLES section lines match SelectedVehicles.Count | ✅ PASS | All vehicle counts correct |
| C5 | Run1 vs Run2: ≥3 years differ in vehicles; no year has identical full text | ✅ PASS | Sufficient variance between seeds |
| C6 | Tree size ladder (distinct per era for mature trees, size-relative) and tree position+species in all prompts | ✅ PASS | Tree ladder and positions correct |
| C7 | 1975=B&W (STRICTLY BLACK AND WHITE); 1985-2025=COLOR photograph | ✅ PASS | Color mode correct in all prompts |
| C8 | Gas station fuel prices always present; downtown coffee price in ≥1 run per year | ✅ PASS | All price anchors found |
| C9 | PRESERVE block contains all building types and immutable elements verbatim | ✅ PASS | All building types and immutable elements present |
| C10 | No TEXT OVERLAY section remains; year still anchors the VEHICLES block | ✅ PASS | Overlay removed and vehicle year anchors correct |
| C11 | Every prompt is under 650 words (limit raised from 550 for placement, sidewalk-rule, and tree-override lines) | ✅ PASS | All prompts under 650 words |
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

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1972-1980 Dodge D100 — angular cab, simple grille, 1974-1983 Jeep Cherokee — two-door SUV, boxy, new model, 1968-1979 Chevrolet Nova — compact, simple three-box shape |
| 1985 | 4 | 1981-1985 Dodge Aries — K-car, boxy economy sedan, 1980-1985 Buick LeSabre — boxy full-size, chrome trim, 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline, 1973-1987 Chevrolet C/K — square body pickup, dual headlights |
| 1995 | 3 | 1992-1996 Ford F-150 — rounded aero body, 1993-1997 Ford Ranger — compact pickup, straight lines, 1992-1995 Pontiac Grand Am — compact with ribbed plastic cladding |
| 2005 | 3 | 2000-2007 Ford Taurus — rounded, aging fleet-look sedan, 1999-2006 Chevrolet Silverado — squared modern look, 2001-2007 Toyota Highlander — early crossover |
| 2015 | 3 | 2011-2016 Hyundai Elantra — swoopy fluidic compact, 2005-2015 Toyota Tacoma — capable mid-size pickup, 2013-2017 Honda Accord — clean modern lines |
| 2025 | 4 | 2021-2025 Toyota Camry — sleek sedan, aggressive front fascia, 2017-2025 Honda CR-V — rounded best-selling crossover, 2020-2025 Hyundai Tucson — sharp parametric grille design, 2019-2025 Toyota Corolla — sharp-nosed compact |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1971-1980 Chevrolet Vega — small subcompact hatchback/wagon, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille |
| 1985 | 3 | 1980-1985 Cadillac Seville — sharp formal lines, bustleback, 1984-1988 Pontiac Fiero — small wedge two-seater, 1978-1986 Ford Bronco — full-size boxy SUV, round headlights |
| 1995 | 4 | 1990-1997 Mazda Miata — tiny rounded roadster, pop-up lights, 1994-1997 Honda Accord — smooth rounded sedan, 1990-1994 Chevrolet Lumina — rounded mid-size sedan, 1992-1995 Pontiac Grand Am — compact with ribbed plastic cladding |
| 2005 | 4 | 2003-2009 Hummer H2 — massive military-styled SUV, 2004-2012 Chevrolet Colorado — mid-size pickup, 1998-2004 Nissan Frontier — compact pickup, 2002-2009 Chevrolet TrailBlazer — mid-size SUV boxy |
| 2015 | 4 | 2013-2019 Ford Fusion — Aston-Martin-style grille, sleek, 2012-2015 Honda Civic — compact, angular, 2011-2016 Kia Optima — stylish mid-size, sporty, 2013-2016 Mazda CX-5 — flowing KODO-design crossover |
| 2025 | 3 | 2022-2025 Chevrolet Silverado — refreshed bold grille, 2019-2025 Toyota RAV4 — boxy rugged crossover, very common, 2019-2025 GMC Sierra — chrome-heavy full-size pickup |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1972-1980 Dodge D100 — angular cab, simple grille, 1974-1983 Jeep Cherokee — two-door SUV, boxy, new model, 1968-1979 Chevrolet Nova — compact, simple three-box shape, 1972-1976 Lincoln Continental Mark IV — long hood personal luxury coupe |
| 1985 | 4 | 1984-1987 Toyota Corolla — boxy compact, reliable look, 1984-1989 Plymouth Voyager — boxy first-generation minivan, 1982-1992 Chevrolet Camaro — wedge-shaped sporty coupe, 1973-1991 Chevrolet Suburban — long boxy wagon-SUV |
| 1995 | 4 | 1992-1996 Ford F-150 — rounded aero body, 1992-1997 Ford Taurus — rounded jellybean shape, oval theme, 1991-1996 Ford Escort — small rounded economy car, 1992-1996 Toyota Camry — rounded, understated |
| 2005 | 5 | 2004-2012 Chevrolet Colorado — mid-size pickup, 2005-2010 Chevrolet Cobalt — compact economy sedan, 1998-2005 Volkswagen New Beetle — retro bubble shape, 2003-2007 Nissan Altima — sporty mid-size, 2001-2007 Dodge Grand Caravan — family minivan, rounded |
| 2015 | 4 | 2013-2018 Toyota RAV4 — angular compact crossover, 2009-2018 Ram 1500 — crosshair grille, refined, 2013-2018 Hyundai Santa Fe — fluidic sculpture styling, 2014-2018 Chevrolet Silverado — squared modern grille |
| 2025 | 4 | 2023-2025 Kia Sportage — futuristic boomerang lights, 2021-2025 Chevrolet Tahoe — huge full-size SUV, 2024-2025 Toyota Grand Highlander — large family crossover, 2021-2025 Ford Bronco — retro boxy off-roader |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1971-1980 Chevrolet Vega — small subcompact hatchback/wagon, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional |
| 1985 | 4 | 1977-1990 Chevrolet Caprice — boxy full-size sedan, formal lines, 1982-1985 Toyota Celica — angular sporty coupe, pop-up lights, 1982-1993 Chevrolet S-10 — compact pickup, square, 1979-1985 Ford LTD Country Squire — full-size woodgrain wagon |
| 1995 | 5 | 1995-2004 Toyota Tacoma — compact, rounded, 1995-1999 Chevrolet Cavalier — compact, rounded, 1991-1995 Dodge Caravan — rounded second-gen minivan, 1986-1997 Ford Aerostar — boxy rear-drive minivan, 1992-1995 Honda Civic — small rounded coupe and sedan |
| 2005 | 4 | 2005-2010 Ford Mustang — retro muscle revival, 2000-2005 Ford Focus — European-styled compact, 2003-2008 Toyota Corolla — conservative compact sedan, 2001-2007 Dodge Grand Caravan — family minivan, rounded |
| 2015 | 5 | 2014-2018 Chevrolet Silverado — squared modern grille, 2007-2017 Jeep Wrangler — boxy off-roader, round headlights, 2014-2019 Kia Soul — boxy urban hatchback, 2015-2020 Ford Edge — mid-size crossover, bold grille, 2015-2020 Ford F-150 — aluminum body, C-clamp headlights |
| 2025 | 5 | 2021-2025 Kia Carnival — boxy SUV-styled minivan, 2021-2025 Jeep Grand Cherokee — upscale chrome-accent SUV, 2021-2025 Chrysler Pacifica — sleek minivan, thin lights, 2019-2025 Subaru Forester — practical boxy crossover, 2019-2025 Ram 1500 — crew cab pickup, large grille |

