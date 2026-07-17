# Smoke Test Report

Generated: 2026-07-17T00:56:22.1839990+00:00

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
| C11 | Every prompt is under 500 words | ✅ PASS | All prompts under 500 words |
| C12 | B&W prompts contain no vehicle pool colors, no 'Fashion palette', no 'desaturated' | ✅ PASS | B&W prompts are color-free |
| C13 | Color eras: every vehicle has a color and no color repeats within one prompt | ✅ PASS | All vehicle colors unique per prompt |
| C14 | Gas station 2025 prompt has no EV/electric/charger/Lightning content | ✅ PASS | 2025 gas prompts are fully de-electrified |

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1972-1980 Dodge D100 — angular cab, simple grille, 1974-1983 Jeep Cherokee — two-door SUV, boxy, new model, 1968-1979 Chevrolet Nova — compact, simple three-box shape |
| 1985 | 4 | 1982-1988 Chevrolet Celebrity — boxy front-wheel drive sedan, 1983-1987 Honda Accord — clean lines, pop-up headlights, 1980-1985 Cadillac Seville — sharp formal lines, bustleback, 1980-1986 Ford F-150 — square body, dual headlights |
| 1995 | 3 | 1995-1999 Dodge Neon — small rounded economy, friendly face, 1995-2004 Toyota Tacoma — compact, rounded, 1993-2002 Pontiac Firebird — sleek pointed sports coupe |
| 2005 | 3 | 2005-2010 Jeep Grand Cherokee — rounded modern SUV, 2002-2009 Chevrolet TrailBlazer — mid-size SUV boxy, 2001-2007 Toyota Highlander — early crossover |
| 2015 | 3 | 2013-2019 Ford Fusion — Aston-Martin-style grille, sleek, 2013-2018 Hyundai Santa Fe — fluidic sculpture styling, 2015-2020 Chevrolet Colorado — mid-size pickup revived |
| 2025 | 4 | 2019-2025 Chevrolet Equinox — rounded compact crossover, 2021-2025 Nissan Rogue — squared-off crossover, floating roofline, 2019-2025 Ram 1500 — crew cab pickup, large grille, 2020-2025 Hyundai Tucson — sharp parametric grille design |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1971-1980 Chevrolet Vega — small subcompact hatchback/wagon, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille |
| 1985 | 3 | 1980-1985 Cadillac Seville — sharp formal lines, bustleback, 1982-1993 Chevrolet S-10 — compact pickup, square, 1983-1987 Honda Accord — clean lines, pop-up headlights |
| 1995 | 4 | 1992-1997 Ford Taurus — rounded jellybean shape, oval theme, 1995-1999 Chevrolet Cavalier — compact, rounded, 1991-1996 Chevrolet Caprice — whale-shaped, rounded full-size, 1995-1999 Dodge Neon — small rounded economy, friendly face |
| 2005 | 3 | 1999-2006 Chevrolet Silverado — squared modern look, 2004-2008 Pontiac Grand Prix — sporty sedan plastic cladding, 2001-2005 Honda Civic — rounded compact, very common |
| 2015 | 3 | 2011-2016 Kia Optima — stylish mid-size, sporty, 2011-2016 Honda CR-V — rounded compact crossover, 2016-2023 Toyota Tacoma — aggressive modern mid-size |
| 2025 | 3 | 2021-2025 Ford F-150 — refreshed, full-width LED bar options, 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2020-2025 Hyundai Tucson — sharp parametric grille design |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1972-1980 Dodge D100 — angular cab, simple grille, 1974-1983 Jeep Cherokee — two-door SUV, boxy, new model, 1968-1979 Chevrolet Nova — compact, simple three-box shape, 1972-1976 Lincoln Continental Mark IV — long hood personal luxury coupe |
| 1985 | 5 | 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline, 1982-1992 Chevrolet Camaro — wedge-shaped sporty coupe, 1984-1990 Dodge Caravan — first minivan, boxy, 1984-1988 Toyota Pickup — small, boxy, popular import, 1983-1988 Ford Thunderbird — aero coupe, rounded |
| 1995 | 4 | 1992-1997 Ford Taurus — rounded jellybean shape, oval theme, 1995-1999 Chevrolet Cavalier — compact, rounded, 1994-2001 Dodge Ram — big rig style grille, bold, 1994-1997 Honda Accord — smooth rounded sedan |
| 2005 | 4 | 2004-2008 Chrysler 300 — bold boxy retro chrome grille, 2002-2006 Toyota Camry — smooth conservative mid-size, 2003-2007 Nissan Altima — sporty mid-size, 2005-2010 Ford Mustang — retro muscle revival |
| 2015 | 5 | 2013-2019 Ford Fusion — Aston-Martin-style grille, sleek, 2013-2018 Toyota RAV4 — angular compact crossover, 2014-2021 Subaru Outback — rugged wagon crossover, 2014-2019 Nissan Rogue — popular compact crossover, 2012-2015 Honda Civic — compact, angular |
| 2025 | 4 | 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2019-2025 Chevrolet Equinox — rounded compact crossover, 2020-2025 Hyundai Tucson — sharp parametric grille design, 2021-2025 Ford Bronco — retro boxy off-roader |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1971-1980 Chevrolet Vega — small subcompact hatchback/wagon, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional |
| 1985 | 5 | 1984-1988 Toyota Pickup — small, boxy, popular import, 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline, 1984-1990 Dodge Caravan — first minivan, boxy, 1982-1993 Chevrolet S-10 — compact pickup, square, 1984-1987 Toyota Corolla — boxy compact, reliable look |
| 1995 | 5 | 1995-1999 Chevrolet Cavalier — compact, rounded, 1991-1996 Buick Roadmaster — large rounded wagon and sedan, 1992-1996 Ford F-150 — rounded aero body, 1994-2001 Dodge Ram — big rig style grille, bold, 1993-2002 Pontiac Firebird — sleek pointed sports coupe |
| 2005 | 4 | 2001-2007 Toyota Highlander — early crossover, 2003-2008 Honda Pilot — boxy three-row SUV, 1999-2006 Chevrolet Silverado — squared modern look, 2001-2005 Honda Civic — rounded compact, very common |
| 2015 | 6 | 2009-2018 Ram 1500 — crosshair grille, refined, 2011-2016 Honda CR-V — rounded compact crossover, 2012-2017 Toyota Camry — sharper creased mid-size sedan, 2013-2018 Hyundai Santa Fe — fluidic sculpture styling, 2016-2023 Toyota Tacoma — aggressive modern mid-size, 2015-2020 Chevrolet Colorado — mid-size pickup revived |
| 2025 | 5 | 2021-2025 Ford Bronco — retro boxy off-roader, 2022-2025 Toyota Tundra — massive grille, muscular stance, 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2023-2025 Honda Accord — clean minimalist refresh, 2019-2025 Toyota RAV4 — boxy rugged crossover, very common |

