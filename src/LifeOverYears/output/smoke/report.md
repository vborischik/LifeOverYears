# Smoke Test Report

Generated: 2026-07-17T01:03:58.5093120+00:00

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
| 1985 | 4 | 1980-1985 Buick LeSabre — boxy full-size, chrome trim, 1982-1988 Chevrolet Celebrity — boxy front-wheel drive sedan, 1983-1987 Honda Accord — clean lines, pop-up headlights, 1980-1989 Lincoln Town Car — long boxy luxury sedan |
| 1995 | 4 | 1991-1995 Dodge Caravan — rounded second-gen minivan, 1986-1997 Ford Aerostar — boxy rear-drive minivan, 1995-1999 Chevrolet Cavalier — compact, rounded, 1992-1997 Ford Taurus — rounded jellybean shape, oval theme |
| 2005 | 4 | 2002-2006 Toyota Camry — smooth conservative mid-size, 2004-2008 Chrysler 300 — bold boxy retro chrome grille, 2005-2010 Jeep Grand Cherokee — rounded modern SUV, 2003-2007 Nissan Altima — sporty mid-size |
| 2015 | 4 | 2007-2017 Jeep Wrangler — boxy off-roader, round headlights, 2013-2019 Ford Fusion — Aston-Martin-style grille, sleek, 2011-2016 Chrysler Town & Country — chrome-trimmed minivan, 2012-2015 Honda Civic — compact, angular |
| 2025 | 4 | 2019-2025 Subaru Outback — rugged wagon crossover, 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2020-2025 Hyundai Tucson — sharp parametric grille design, 2022-2025 Nissan Pathfinder — squared-off three-row SUV |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 3 | 1971-1980 Chevrolet Vega — small subcompact hatchback/wagon, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille |
| 1985 | 3 | 1983-1985 Nissan Maxima — boxy import sedan, 1980-1985 Cadillac Seville — sharp formal lines, bustleback, 1973-1987 Chevrolet C/K — square body pickup, dual headlights |
| 1995 | 4 | 1995-1999 Dodge Neon — small rounded economy, friendly face, 1994-1997 Honda Accord — smooth rounded sedan, 1992-1997 Ford Taurus — rounded jellybean shape, oval theme, 1994-1998 Ford Mustang — rounded SN95 pony car |
| 2005 | 3 | 1999-2006 Chevrolet Silverado — squared modern look, 2001-2007 Dodge Grand Caravan — family minivan, rounded, 2005-2010 Chevrolet Cobalt — compact economy sedan |
| 2015 | 3 | 2011-2017 Honda Odyssey — lightning-bolt beltline minivan, 2013-2018 Hyundai Santa Fe — fluidic sculpture styling, 2015-2023 Ford F-150 — aluminum body, blocky modern |
| 2025 | 4 | 2017-2025 Honda CR-V — rounded best-selling crossover, 2022-2025 Honda Civic — clean mature compact, 2019-2025 Chevrolet Equinox — rounded compact crossover, 2021-2025 Kia Carnival — boxy SUV-styled minivan |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1972-1980 Dodge D100 — angular cab, simple grille, 1974-1983 Jeep Cherokee — two-door SUV, boxy, new model, 1968-1979 Chevrolet Nova — compact, simple three-box shape, 1972-1976 Lincoln Continental Mark IV — long hood personal luxury coupe |
| 1985 | 5 | 1982-1985 Toyota Celica — angular sporty coupe, pop-up lights, 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline, 1982-1992 Chevrolet Camaro — wedge-shaped sporty coupe, 1984-1990 Dodge Caravan — first minivan, boxy, 1983-1985 Nissan Maxima — boxy import sedan |
| 1995 | 5 | 1991-1996 Ford Escort — small rounded economy car, 1993-1998 Jeep Grand Cherokee — early SUV, boxy-rounded, 1993-1997 Ford Ranger — compact pickup, straight lines, 1992-1996 Ford F-150 — rounded aero body, 1992-1995 Honda Civic — small rounded coupe and sedan |
| 2005 | 4 | 2004-2008 Chrysler 300 — bold boxy retro chrome grille, 2003-2008 Honda Pilot — boxy three-row SUV, 2001-2007 Toyota Highlander — early crossover, 2004-2008 Pontiac Grand Prix — sporty sedan plastic cladding |
| 2015 | 5 | 2014-2021 Subaru Outback — rugged wagon crossover, 2012-2017 Toyota Camry — sharper creased mid-size sedan, 2015-2020 Chevrolet Colorado — mid-size pickup revived, 2012-2018 Ford Focus — sharp European compact, 2010-2016 Chevrolet Equinox — mid-size crossover |
| 2025 | 4 | 2021-2025 Ford Bronco — retro boxy off-roader, 2021-2025 Chevrolet Tahoe — huge full-size SUV, 2023-2025 Kia Sportage — futuristic boomerang lights, 2022-2025 Ford Maverick — small unibody pickup |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1971-1980 Chevrolet Vega — small subcompact hatchback/wagon, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille, 1971-1976 Jeep Wagoneer — boxy full-size SUV, woodgrain trim optional |
| 1985 | 5 | 1984-1988 Toyota Pickup — small, boxy, popular import, 1979-1985 Ford Mustang — Fox body, angular hatchback coupe, 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline, 1980-1989 Lincoln Town Car — long boxy luxury sedan, 1984-1990 Dodge Caravan — first minivan, boxy |
| 1995 | 5 | 1993-1997 Toyota Corolla — rounded compact sedan, 1993-1998 Jeep Grand Cherokee — early SUV, boxy-rounded, 1995-1999 Chevrolet Cavalier — compact, rounded, 1993-1997 Ford Ranger — compact pickup, straight lines, 1992-1996 Ford F-150 — rounded aero body |
| 2005 | 5 | 2004-2008 Pontiac Grand Prix — sporty sedan plastic cladding, 2004-2010 Toyota Sienna — large rounded minivan, 2004-2008 Chrysler 300 — bold boxy retro chrome grille, 2003-2007 Nissan Altima — sporty mid-size, 2000-2005 Ford Focus — European-styled compact |
| 2015 | 5 | 2009-2018 Ram 1500 — crosshair grille, refined, 2013-2018 Toyota RAV4 — angular compact crossover, 2011-2017 Honda Odyssey — lightning-bolt beltline minivan, 2013-2016 Mazda CX-5 — flowing KODO-design crossover, 2015-2020 Ford Edge — mid-size crossover, bold grille |
| 2025 | 4 | 2021-2025 Kia Carnival — boxy SUV-styled minivan, 2023-2025 Honda Accord — clean minimalist refresh, 2021-2025 Nissan Rogue — squared-off crossover, floating roofline, 2022-2025 Chevrolet Silverado — refreshed bold grille |

