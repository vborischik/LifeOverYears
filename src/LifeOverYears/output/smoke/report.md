# Smoke Test Report

Generated: 2026-07-17T01:09:13.2211220+00:00

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
| C11 | Every prompt is under 550 words (limit raised from 500 for extras/window-sign/people-mix sampling) | ✅ PASS | All prompts under 550 words |
| C12 | B&W prompts contain no vehicle pool colors, no 'Fashion palette', no 'desaturated' | ✅ PASS | B&W prompts are color-free |
| C13 | Color eras: every vehicle has a color and no color repeats within one prompt | ✅ PASS | All vehicle colors unique per prompt |
| C14 | Gas station 2025 prompt has no EV/electric/charger/Lightning content | ✅ PASS | 2025 gas prompts are fully de-electrified |
| C20 | Every prompt has a two-sign 'window signs:' line, >=1 extras line, and a people_mix line | ✅ PASS | All three sampling axes present in every prompt |
| C21 | Run1 vs Run2: >=3 of 6 years differ in sampled extras or window signs | ✅ PASS | Sufficient sampling variance between seeds |

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1972-1980 Dodge D100 — angular cab, simple grille, 1974-1983 Jeep Cherokee — two-door SUV, boxy, new model, 1968-1979 Chevrolet Nova — compact, simple three-box shape |
| 1985 | 3 | 1983-1987 Honda Accord — clean lines, pop-up headlights, 1984-1988 Pontiac Fiero — small wedge two-seater, 1984-1987 Toyota Corolla — boxy compact, reliable look |
| 1995 | 3 | 1986-1997 Ford Aerostar — boxy rear-drive minivan, 1992-1995 Pontiac Grand Am — compact with ribbed plastic cladding, 1992-1997 Ford Taurus — rounded jellybean shape, oval theme |
| 2005 | 4 | 2002-2009 Chevrolet TrailBlazer — mid-size SUV boxy, 2002-2006 Toyota Camry — smooth conservative mid-size, 1998-2005 Volkswagen New Beetle — retro bubble shape, 2005-2010 Ford Mustang — retro muscle revival |
| 2015 | 4 | 2013-2018 Toyota RAV4 — angular compact crossover, 2011-2016 Chrysler Town & Country — chrome-trimmed minivan, 2011-2016 Kia Optima — stylish mid-size, sporty, 2015-2023 Ford F-150 — aluminum body, blocky modern |
| 2025 | 3 | 2019-2025 Chevrolet Equinox — rounded compact crossover, 2021-2025 Jeep Grand Cherokee — upscale chrome-accent SUV, 2022-2025 Nissan Pathfinder — squared-off three-row SUV |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1971-1980 Chevrolet Vega — small subcompact hatchback/wagon, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille |
| 1985 | 3 | 1983-1988 Ford Thunderbird — aero coupe, rounded, 1979-1985 Ford LTD Country Squire — full-size woodgrain wagon, 1980-1986 Ford F-150 — square body, dual headlights |
| 1995 | 3 | 1986-1997 Ford Aerostar — boxy rear-drive minivan, 1995-1999 Dodge Neon — small rounded economy, friendly face, 1994-1998 Ford Mustang — rounded SN95 pony car |
| 2005 | 3 | 2004-2008 Chrysler 300 — bold boxy retro chrome grille, 2003-2008 Toyota Corolla — conservative compact sedan, 2005-2010 Jeep Grand Cherokee — rounded modern SUV |
| 2015 | 3 | 2012-2015 Honda Civic — compact, angular, 2007-2017 Jeep Wrangler — boxy off-roader, round headlights, 2014-2019 Kia Soul — boxy urban hatchback |
| 2025 | 4 | 2022-2025 Toyota Tundra — massive grille, muscular stance, 2022-2025 Nissan Pathfinder — squared-off three-row SUV, 2019-2025 Ram 1500 — crew cab pickup, large grille, 2021-2025 Kia Carnival — boxy SUV-styled minivan |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1972-1980 Dodge D100 — angular cab, simple grille, 1974-1983 Jeep Cherokee — two-door SUV, boxy, new model, 1968-1979 Chevrolet Nova — compact, simple three-box shape, 1972-1976 Lincoln Continental Mark IV — long hood personal luxury coupe |
| 1985 | 6 | 1986-1989 Nissan Sentra — small economy boxy sedan, 1982-1985 Toyota Celica — angular sporty coupe, pop-up lights, 1980-1985 Cadillac Seville — sharp formal lines, bustleback, 1986-1991 Ford Taurus — aerodynamic, rounded, revolutionary look, 1982-1988 Chevrolet Celebrity — boxy front-wheel drive sedan, 1982-1993 Chevrolet S-10 — compact pickup, square |
| 1995 | 4 | 1994-2001 Dodge Ram — big rig style grille, bold, 1992-1996 Toyota Camry — rounded, understated, 1994-1998 Ford Mustang — rounded SN95 pony car, 1995-1999 Dodge Neon — small rounded economy, friendly face |
| 2005 | 5 | 2004-2008 Pontiac Grand Prix — sporty sedan plastic cladding, 2003-2007 Nissan Altima — sporty mid-size, 2004-2010 Toyota Sienna — large rounded minivan, 2004-2012 Chevrolet Colorado — mid-size pickup, 2001-2007 Ford Escape — compact boxy SUV |
| 2015 | 5 | 2010-2016 Chevrolet Equinox — mid-size crossover, 2013-2017 Honda Accord — clean modern lines, 2011-2016 Hyundai Elantra — swoopy fluidic compact, 2011-2016 Kia Optima — stylish mid-size, sporty, 2011-2016 Honda CR-V — rounded compact crossover |
| 2025 | 4 | 2022-2025 Nissan Pathfinder — squared-off three-row SUV, 2019-2025 Subaru Forester — practical boxy crossover, 2021-2025 Kia Carnival — boxy SUV-styled minivan, 2019-2025 GMC Sierra — chrome-heavy full-size pickup |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1971-1980 Chevrolet Vega — small subcompact hatchback/wagon, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional |
| 1985 | 5 | 1979-1985 Ford Mustang — Fox body, angular hatchback coupe, 1978-1986 Ford Bronco — full-size boxy SUV, round headlights, 1973-1987 Chevrolet C/K — square body pickup, dual headlights, 1983-1985 Nissan Maxima — boxy import sedan, 1984-1989 Plymouth Voyager — boxy first-generation minivan |
| 1995 | 5 | 1993-1998 Jeep Grand Cherokee — early SUV, boxy-rounded, 1992-1996 Ford F-150 — rounded aero body, 1994-2001 Dodge Ram — big rig style grille, bold, 1992-1997 Ford Taurus — rounded jellybean shape, oval theme, 1993-1997 Toyota Corolla — rounded compact sedan |
| 2005 | 4 | 2000-2006 Chevrolet Tahoe — full-size SUV peak era, 1999-2006 Chevrolet Silverado — squared modern look, 2003-2009 Hummer H2 — massive military-styled SUV, 2004-2008 Chrysler 300 — bold boxy retro chrome grille |
| 2015 | 4 | 2011-2017 Honda Odyssey — lightning-bolt beltline minivan, 2013-2019 Ford Fusion — Aston-Martin-style grille, sleek, 2013-2016 Ford Escape — rounded compact crossover, 2015-2020 Chevrolet Colorado — mid-size pickup revived |
| 2025 | 4 | 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2017-2025 Honda CR-V — rounded best-selling crossover, 2021-2025 Nissan Rogue — squared-off crossover, floating roofline, 2020-2025 Hyundai Tucson — sharp parametric grille design |

