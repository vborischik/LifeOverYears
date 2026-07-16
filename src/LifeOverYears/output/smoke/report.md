# Smoke Test Report

Generated: 2026-07-16T04:26:06.5817330+00:00

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

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1973-1977 Chevrolet Caprice Classic — full-size, chrome bumpers, vinyl roof, 1974-1978 Dodge Monaco — large chrome grille, two-tone paint, 1970-1977 Ford Maverick — compact, simple lines |
| 1985 | 3 | 1982-1992 Chevrolet Camaro — wedge-shaped sporty coupe, 1984-1990 Dodge Caravan — first minivan, boxy, 1984-1987 Toyota Corolla — boxy compact, reliable look |
| 1995 | 4 | 1994-1997 Honda Accord — smooth rounded sedan, 1995-1999 Chevrolet Cavalier — compact, rounded, 1995-2004 Toyota Tacoma — compact, rounded, 1994-2001 Dodge Ram — big rig style grille, bold |
| 2005 | 4 | 2002-2009 Chevrolet TrailBlazer — mid-size SUV boxy, 2001-2005 Honda Civic — rounded compact, very common, 2001-2007 Toyota Highlander — early crossover, 2004-2008 Ford F-150 — bigger, bolder grille |
| 2015 | 3 | 2012-2015 Honda Civic — compact, angular, 2015-2020 Ford Edge — mid-size crossover, bold grille, 2010-2016 Chevrolet Equinox — mid-size crossover |
| 2025 | 3 | 2021-2025 Ford F-150 — full-width LED bar, bold, 2022-2025 Ford F-150 Lightning — electric pickup, light bar front, 2020-2025 Tesla Model 3 — minimalist, no grille, flush handles |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1973-1980 Chevrolet C10 — square body, chrome bumper, 1971-1976 Chevrolet Impala — full-size, chrome rear bumper, 1975-1979 Chevrolet Monza — small hatchback, rare but present |
| 1985 | 3 | 1984-1990 Dodge Caravan — first minivan, boxy, 1980-1986 Ford F-150 — square body, dual headlights, 1983-1988 Ford Thunderbird — aero coupe, rounded |
| 1995 | 4 | 1991-1996 Chevrolet Caprice — whale-shaped, rounded full-size, 1992-1997 Ford Taurus — rounded jellybean shape, oval theme, 1995-1999 Dodge Neon — small rounded economy, friendly face, 1992-1996 Ford F-150 — rounded aero body |
| 2005 | 3 | 2000-2007 Ford Taurus — rounded, aging fleet-look sedan, 2003-2008 Honda Pilot — boxy three-row SUV, 2004-2008 Ford F-150 — bigger, bolder grille |
| 2015 | 3 | 2013-2018 Toyota RAV4 — angular compact crossover, 2014-2021 Subaru Outback — rugged wagon crossover, 2015-2020 Ford Edge — mid-size crossover, bold grille |
| 2025 | 3 | 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2021-2025 Ford F-150 — refreshed, full-width LED bar options, 2022-2025 Ford F-150 Lightning — electric pickup, light bar front |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1973-1977 Chevrolet Caprice Classic — full-size, chrome bumpers, vinyl roof, 1974-1978 Dodge Monaco — large chrome grille, two-tone paint, 1970-1977 Ford Maverick — compact, simple lines, 1973-1977 Pontiac Catalina — full-size, chrome trim heavy |
| 1985 | 4 | 1983-1988 Ford Thunderbird — aero coupe, rounded, 1984-1988 Toyota Pickup — small, boxy, popular import, 1982-1988 Chevrolet Celebrity — boxy front-wheel drive sedan, 1983-1987 Honda Accord — clean lines, pop-up headlights |
| 1995 | 5 | 1995-1999 Chevrolet Cavalier — compact, rounded, 1995-2004 Toyota Tacoma — compact, rounded, 1991-1996 Chevrolet Caprice — whale-shaped, rounded full-size, 1992-1996 Ford F-150 — rounded aero body, 1992-1996 Toyota Camry — rounded, understated |
| 2005 | 6 | 2002-2006 Toyota Camry — smooth conservative mid-size, 2004-2008 Ford F-150 — bigger, bolder grille, 2003-2007 Honda Accord — clean lines, sharper than 1990s, 2005-2010 Jeep Grand Cherokee — rounded modern SUV, 2004-2008 Pontiac Grand Prix — sporty sedan plastic cladding, 1999-2006 Chevrolet Silverado — squared modern look |
| 2015 | 5 | 2016-2023 Toyota Tacoma — aggressive modern mid-size, 2013-2018 Hyundai Santa Fe — fluidic sculpture styling, 2014-2021 Subaru Outback — rugged wagon crossover, 2014-2018 Chevrolet Silverado — squared modern grille, 2015-2020 Chevrolet Colorado — mid-size pickup revived |
| 2025 | 6 | 2021-2025 Ford F-150 — full-width LED bar, bold, 2024-2025 Tesla Cybertruck — angular stainless steel, unmistakable, 2022-2025 Ford F-150 Lightning — electric pickup, light bar front, 2021-2025 Ford Bronco — retro boxy off-roader, 2023-2025 Honda Accord — clean minimalist refresh, 2022-2025 Chevrolet Silverado — refreshed bold grille |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1973-1980 Chevrolet C10 — square body, chrome bumper, 1971-1976 Chevrolet Impala — full-size, chrome rear bumper, 1975-1979 Chevrolet Monza — small hatchback, rare but present, 1971-1976 Plymouth Fury — full-size, vinyl roof common |
| 1985 | 5 | 1984-1988 Toyota Pickup — small, boxy, popular import, 1986-1991 Ford Taurus — aerodynamic, rounded, revolutionary look, 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline, 1984-1990 Dodge Caravan — first minivan, boxy, 1980-1985 Cadillac Seville — sharp formal lines, bustleback |
| 1995 | 4 | 1993-1998 Jeep Grand Cherokee — early SUV, boxy-rounded, 1993-2002 Pontiac Firebird — sleek pointed sports coupe, 1991-1996 Buick Roadmaster — large rounded wagon and sedan, 1994-1997 Honda Accord — smooth rounded sedan |
| 2005 | 4 | 2001-2007 Toyota Highlander — early crossover, 2002-2008 Dodge Ram — big rig grille evolved, 1999-2006 Chevrolet Silverado — squared modern look, 2001-2005 Honda Civic — rounded compact, very common |
| 2015 | 4 | 2013-2019 Ford Fusion — Aston-Martin-style grille, sleek, 2014-2019 Nissan Rogue — popular compact crossover, 2013-2018 Hyundai Santa Fe — fluidic sculpture styling, 2015-2020 Chevrolet Colorado — mid-size pickup revived |
| 2025 | 6 | 2017-2025 Honda CR-V — rounded best-selling crossover, 2024-2025 Tesla Cybertruck — angular stainless, polarizing, 2019-2025 Ram 1500 — large crosshair-evolved grille, 2022-2025 Ford F-150 Lightning — electric pickup, light bar front, 2022-2025 Chevrolet Silverado — refreshed bold grille, 2021-2025 Kia Telluride — boxy upscale three-row SUV |

