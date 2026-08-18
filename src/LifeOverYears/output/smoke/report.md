# Smoke Test Report

Generated: 2026-08-18T04:50:29.6678880+00:00

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
| C9 | DISABLED — PRESERVE block contains all building types and immutable elements verbatim | ⛔ DISABLED | disabled while the short era PRESERVE is evaluated — restore together with the BuildPreserveBlock call in PromptService line 89 |
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
| C23 | default/unknown scenes always thriving; rank monotonic per run (the final era may resolve the arc for any condition-supporting type); 'abandoned' never appears as a SceneCondition; declining/squatted counts honored for every condition-supporting type; 'restored' only on a final era | ✅ PASS | Condition trajectory invariants hold |
| C24 | Every business-name token resolves to a member of its own pool and stays identical across all six eras of a run | ✅ PASS | All 8 business tokens resolve correctly and remain stable per run |
| C25 | DECAY present iff condition is declining/abandoned/squatted; healthy conditions keep verbatim era road markings with no DECAY; DECAY never precedes OUTPUT FORMAT (i.e. never inside PRESERVE) and never mentions geometry terms; bullets are drawn from the correct severity pool | ✅ PASS | Decay section invariants hold |
| C26 | Caption body files load and parse (>=5 bodies, known placeholders only, no hashtags, ends on a question); every scene_content type has a caption voice; anchor pools are well-formed and non-leaking; AnglesFor() composition holds; every reachable condition maps to a real phrase | ✅ PASS | Caption body files and voice coverage hold |
| C27 | base-clean.txt loads, declares the exact 9:16 portrait phrase (and no competing aspect-ratio term), keeps its people/vehicle-removal + pixel-identical/canvas-extension cleanup contract, and every generated prompt carries the same portrait phrase | ✅ PASS | base-clean/prompt aspect-ratio contract holds |
| C28 | People bullet lines (people_activities picks and the people_mix line) never repeat within a run unless their era's own pool is already exhausted | ✅ PASS | No premature people-line repeats |
| C29 | DeclineBias() ramps non-decreasing across the run and stays within 0..1 | ✅ PASS | Bias ramp OK across all eras |
| C30 | Neither chain ever appears Named in a prompt for a year before 1990 | ✅ PASS | No pre-1990 named chain tenants |
| C31 | Blockbuster never appears in a downtown_street prompt, in any form | ✅ PASS | No Blockbuster content in any downtown_street prompt |
| C32 | Neither chain ever appears Named in an abandoned or squatted era | ✅ PASS | No named chain tenants in derelict eras |
| C33 | Chain tenant presence is stable across a run: no flicker between schedule-eligible eras | ✅ PASS | No presence flicker across 20 seeds x 6 eras |
| C34 | A fully-dead derelict era emits the ghost line whenever the run's chain schedule calls for one (squatted downtown_street/strip_mall's half-dead block omits chain tenants by design) | ✅ PASS | Ghost lines present wherever the schedule calls for them |
| C35 | A derelict era never emits a Named or Generic chain tenant line | ✅ PASS | No Named/Generic chain content in any derelict block |
| C36 | Street-shaped placement language (sidewalk zones, curb-hugging, PLACEMENT wording) is gated on SceneDna geometry | ✅ PASS | Street language present only where geometry supports it |
| C37 | Synthetic base prompts name their scene type and carry scene geometry, with no source-photo wording (era PRESERVE header assertion parked) | ✅ PASS | Synthetic base prompts well-formed; era header assertion parked while the short era PRESERVE is evaluated — restore together with the BuildPreserveBlock call in PromptService line 89 |
| C38 | A tree's size is stated in exactly one place per prompt: never in the era PRESERVE block, always in the synthetic base's geometry block | ✅ PASS | No double-statement; synthetic base carries every tree the era PRESERVE block omits |
| C39 | A 'new' condition prompt never pairs pristine surfaces with an unexplained weathered ghost sign | ✅ PASS | No unreconciled ghost-sign contradiction in any 'new' condition prompt |
| C40 | image-template.txt carries the PRIORITY ORDER rule; every era prompt's SIGNAGE RESTRICTION whitelist lists exactly the quoted strings from its own scene block; the old blanket quotes-only line is gone | ✅ PASS | Priority order present; signage whitelist consistent everywhere; old line removed |
| C41 | Caption assembly produces a complete, fully substituted caption for every scene type; scene types differ; weekly rotation reaches every body; same-week scenes are separated by the id offset | ✅ PASS | Caption assembly varied and fully substituted |
| C43 | Chained eras are told to clear the previous year's people and vehicles; unchained eras keep the empty-source wording | ✅ PASS | Base note matches the chaining mode in every era |
| C44 | Every prompt sets its light from the condition: living eras get open daylight, only derelict eras go grey, and no prompt asks for fog, rain or night | ✅ PASS | Light matches condition in every prompt |
| C45 | Across 500 seeds no scene type is squatted at 2015 more than 35 % of the time, at least 70 % of runs still decline, trajectories stay varied, and 'abandoned' never appears | ✅ PASS | downtown_street: 2015 squatted 0 %, ever declines 99 %, 99 trajectories \| strip_mall: 2015 squatted 0 %, ever declines 99 %, 99 trajectories \| auto_repair: 2015 squatted 0 %, ever declines 99 %, 99 trajectories \| gas_station: 2015 squatted 0 %, ever declines 82 %, 151 trajectories |
| C42 | Packed-crowd mall scenes render crowd/lot wording, exactly 5 representative vehicles, no PLACEMENT line | ✅ PASS | Packed crowd rendering correct across 1975/1985/1995 |
| C46 | Every condition-bearing scene type prints its CONDITION line; mall (no condition arc) prints none | ✅ PASS | CONDITION line present for gas_station/downtown_street/strip_mall/auto_repair, absent for mall |
| C47 | Condition rank never skips 0 -> 2 between consecutive eras; a run that reached derelict rank pre-finale always resolves to 'squatted', never 'restored' or 'declining'; 'abandoned' never appears | ✅ PASS | Trajectory steps one rank at a time and resolves across 40 seeds x 4 scene types |
| C48 | Distinctive phrases appear verbatim in the synthetic base prompt; a set Composition produces the framing line | ✅ PASS | Distinctive and Composition both render correctly |
| C49 | A squatted downtown_street/strip_mall prompt draws from both PoorTenantBusinesses and SquattedGroundDetails, and never falls back to the fully-dead 'every storefront closed and dark' wording | ✅ PASS | Half-dead squatted retail draws from both pools across 30 seeds x 2 scene types |
| C50 | The 'restored' condition descriptor reads as reoccupation, not a renovated rebuild | ✅ PASS | restored descriptor contains 'reoccupied', never 'renovated appearance' |
| C59 | Title templates load for every scene type plus base; every line substitutes with no leftover placeholder and stays non-empty and under the 100-char YouTube limit | ✅ PASS | base: 8 titles, longest 59 chars \| gas_station: 8 titles, longest 62 chars \| downtown_street: 8 titles, longest 54 chars \| strip_mall: 8 titles, longest 59 chars \| auto_repair: 8 titles, longest 54 chars \| mall: 8 titles, longest 57 chars \| shopping_center: 8 titles, longest 60 chars |

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1973-1979 Ford F-100 — square body, chrome grille, 1971-1976 Chevrolet G10 Sportvan — boxy windowed van, chrome bumper, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1970-1976 Pontiac Firebird Trans Am — hood scoop, spoiler, bold graphics |
| 1985 | 4 | 1980-1985 Buick LeSabre — boxy full-size, chrome trim, 1982-1993 Chevrolet S-10 — compact pickup, square, 1977-1990 Chevrolet Caprice — boxy full-size sedan, formal lines, 1973-1987 Chevrolet C/K — square body pickup, dual headlights |
| 1995 | 3 | 1992-1996 Ford F-150 — rounded aero body, 1995-1999 Dodge Neon — small rounded economy, friendly face, 1994-2001 Dodge Ram — big rig style grille, bold |
| 2005 | 3 | 2003-2008 Toyota Corolla — conservative compact sedan, 2000-2005 Ford Focus — European-styled compact, 2004-2008 Chrysler 300 — bold boxy retro chrome grille |
| 2015 | 1 | 2011-2016 Kia Optima — stylish mid-size, sporty |
| 2025 | 4 | 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2019-2025 Toyota RAV4 — boxy rugged crossover, very common, 2022-2025 Chevrolet Silverado — refreshed bold grille, 2021-2025 Nissan Rogue — squared-off crossover, floating roofline |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1968-1979 Chevrolet Nova — compact, simple three-box shape, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille, 1975-1979 Chevrolet Monza — small hatchback, rare but present, 1971-1978 Dodge Tradesman — boxy full-size van |
| 1985 | 4 | 1975-1991 Ford Econoline — boxy full-size van, 1982-1993 Chevrolet S-10 — compact pickup, square, 1980-1986 Ford F-150 — square body, dual headlights, 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline |
| 1995 | 4 | 1989-1997 Geo Metro — very small economy hatchback, 1993-2002 Pontiac Firebird — sleek pointed sports coupe, 1995-2004 Toyota Tacoma — compact, rounded, 1995-1999 Dodge Neon — small rounded economy, friendly face |
| 2005 | 3 | 1998-2005 Volkswagen New Beetle — retro bubble shape, 2005-2010 Chevrolet Cobalt — compact economy sedan, 2001-2007 Toyota Highlander — early crossover |
| 2015 | 2 | 2007-2017 Jeep Wrangler — boxy off-roader, round headlights, 2013-2016 Mazda CX-5 — flowing KODO-design crossover |
| 2025 | 4 | 2021-2025 Nissan Rogue — squared-off crossover, floating roofline, 2019-2025 Toyota RAV4 — boxy rugged crossover, very common, 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2021-2025 Kia Carnival — boxy SUV-styled minivan |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 5 | 1973-1979 Ford F-100 — square body, chrome grille, 1971-1976 Chevrolet G10 Sportvan — boxy windowed van, chrome bumper, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1970-1976 Pontiac Firebird Trans Am — hood scoop, spoiler, bold graphics, 1973-1978 Datsun 610 — compact sedan/wagon, boxy |
| 1985 | 6 | 1979-1985 Ford LTD Country Squire — full-size woodgrain wagon, 1982-1988 Chevrolet Celebrity — boxy front-wheel drive sedan, 1981-1985 Ford Escort — small boxy economy hatchback, 1980-1989 Lincoln Town Car — long boxy luxury sedan, 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood, 1980-1985 Buick LeSabre — boxy full-size, chrome trim |
| 1995 | 4 | 1992-1996 Toyota Camry — rounded, understated, 1992-1995 Honda Civic — small rounded coupe and sedan, 1993-1997 Toyota Corolla — rounded compact sedan, 1991-1995 Dodge Caravan — rounded second-gen minivan |
| 2005 | 5 | 2003-2007 Nissan Altima — sporty mid-size, 2002-2008 Dodge Ram — big rig grille evolved, 2005-2010 Ford Mustang — retro muscle revival, 2004-2008 Chrysler 300 — bold boxy retro chrome grille, 1998-2004 Nissan Frontier — compact pickup |
| 2015 | 4 | 2011-2016 Chrysler Town & Country — chrome-trimmed minivan, 2013-2017 Honda Accord — clean modern lines, 2011-2016 Kia Optima — stylish mid-size, sporty, 2013-2018 Hyundai Santa Fe — fluidic sculpture styling |
| 2025 | 2 | 2022-2025 Toyota Tundra — massive grille, muscular stance, 2021-2025 Kia Telluride — boxy upscale three-row SUV |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 6 | 1968-1979 Chevrolet Nova — compact, simple three-box shape, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille, 1975-1979 Chevrolet Monza — small hatchback, rare but present, 1971-1978 Dodge Tradesman — boxy full-size van, 1975-1978 Datsun 280Z — sleek fastback sports coupe, 1968-1978 Volkswagen Beetle — rounded rear-engine economy car |
| 1985 | 4 | 1981-1988 Oldsmobile Cutlass Ciera — boxy, formal roofline, 1975-1991 Ford Econoline — boxy full-size van, 1982-1986 Nissan Sentra — small economy boxy sedan, 1984-1988 Toyota Pickup — small, boxy, popular import |
| 1995 | 5 | 1991-1996 Chevrolet Caprice — whale-shaped, rounded full-size, 1992-1995 Pontiac Grand Am — compact with ribbed plastic cladding, 1990-1994 Chevrolet Lumina — rounded mid-size sedan, 1994-1998 Ford Mustang — rounded SN95 pony car, 1991-1996 Buick Roadmaster — large rounded wagon and sedan |
| 2005 | 2 | 2003-2007 Honda Accord — clean lines, sharper than 1990s, 2002-2006 Toyota Camry — smooth conservative mid-size |
| 2015 | 2 | 2014-2019 Nissan Rogue — popular compact crossover, 2011-2016 Kia Optima — stylish mid-size, sporty |
| 2025 | 2 | 2022-2025 Honda Civic — clean mature compact, 2021-2025 Chrysler Pacifica — sleek minivan, thin lights |

