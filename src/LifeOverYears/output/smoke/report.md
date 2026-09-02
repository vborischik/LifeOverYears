# Smoke Test Report

Generated: 2026-09-02T04:27:58.6993440+00:00

## Check Results

| Check | Description | Status | Detail |
|-------|-------------|--------|--------|
| C1 | Era deserialization: scene_content has required keys, color_mode present, people pools >= 20 | ✅ PASS | All 6 eras OK |
| C2 | No unresolved {TOKEN} of any kind remains in any prompt | ✅ PASS | All placeholders resolved |
| C3 | No vehicle model reuse within each run (dedup invariant) | ✅ PASS | No duplicates in any run |
| C4 | Vehicle count in range and VEHICLES section lines match SelectedVehicles.Count | ✅ PASS | All vehicle counts correct |
| C5 | Run1 vs Run2: ≥3 years differ in vehicles; no year has identical full text | ✅ PASS | Sufficient variance between seeds |
| C6 | Tree canopy proportion vs. the base image (distinct per era for mature trees, size-relative), and no TREES section or tree mention in the source year | ✅ PASS | Tree ladder and source-year omission correct |
| C7 | Every era is a COLOR photograph; no era carries the monochrome block | ✅ PASS | Color mode correct in all prompts |
| C8 | Gas station fuel prices always present; downtown coffee price in ≥1 run per year | ✅ PASS | All price anchors found |
| C9 | DISABLED — PRESERVE block contains all building types and immutable elements verbatim | ⛔ DISABLED | disabled while the short era PRESERVE is evaluated — restore together with the BuildPreserveBlock call in PromptService line 89 |
| C10 | No TEXT OVERLAY section remains; year still anchors the VEHICLES block and carries the ranged-model-year restriction | ✅ PASS | Overlay removed, vehicle year anchors correct, model-year restriction present |
| C11 | Every prompt is under 960 words | ✅ PASS | All prompts under 960 words |
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
| C23 | default/unknown scenes always thriving; rank monotonic per run (the final era may resolve the arc for any condition-supporting type); abandoned/declining/squatted counts honored for gas_station, downtown_street and strip_mall; 'squatted' only on a gas_station's final era; 'restored' only on a final era | ✅ PASS | Condition trajectory invariants hold |
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
| C34 | A derelict era emits the ghost line whenever the run's chain schedule calls for one | ✅ PASS | Ghost lines present wherever the schedule calls for them |
| C35 | A derelict era never emits a Named or Generic chain tenant line | ✅ PASS | No Named/Generic chain content in any derelict block |
| C36 | Street-shaped placement language (sidewalk zones, curb-hugging, PLACEMENT wording) is gated on SceneDna geometry | ✅ PASS | Street language present only where geometry supports it |
| C37 | Synthetic base prompts name their scene type and carry scene geometry, with no source-photo wording (era PRESERVE header assertion parked) | ✅ PASS | Synthetic base prompts well-formed; era header assertion parked while the short era PRESERVE is evaluated — restore together with the BuildPreserveBlock call in PromptService line 89 |
| C38 | A tree's size is stated in exactly one place per prompt: never in the era PRESERVE block, always in the synthetic base's geometry block | ✅ PASS | No double-statement; synthetic base carries every tree the era PRESERVE block omits |
| C39 | A 'new' condition prompt never pairs pristine surfaces with an unexplained weathered ghost sign | ✅ PASS | No unreconciled ghost-sign contradiction in any 'new' condition prompt |
| C40 | image-template.txt carries the PRIORITY ORDER rule; every era prompt's SIGNAGE RESTRICTION whitelist lists exactly the quoted strings from its own scene block; the old blanket quotes-only line is gone | ✅ PASS | Priority order present; signage whitelist consistent everywhere; old line removed |
| C41 | Caption assembly produces a complete, fully substituted caption for every scene type; scene types differ; weekly rotation reaches every body; same-week scenes are separated by the id offset | ✅ PASS | Caption assembly varied and fully substituted |
| C43 | Chained eras are told to clear the previous year's people and vehicles; unchained eras keep the empty-source wording | ✅ PASS | Base note matches the chaining mode in every era |
| C44 | Every prompt sets its light from the condition: living eras get open daylight, only derelict eras go grey, and no prompt asks for fog, rain or night | ✅ PASS | Light matches condition in every prompt |
| C45 | Across 500 seeds no scene type abandons 2015 more than 35% of the time, at least 70% of runs still decline, and trajectories stay varied | ✅ PASS | downtown_street: 2015 abandoned 0%, ever declines 99%, 99 trajectories \| strip_mall: 2015 abandoned 0%, ever declines 99%, 99 trajectories \| auto_repair: 2015 abandoned 0%, ever declines 99%, 99 trajectories \| gas_station: 2015 abandoned 0%, ever declines 82%, 151 trajectories |
| C42 | Packed-crowd mall scenes render crowd/lot wording, exactly 5 representative vehicles, no PLACEMENT line | ✅ PASS | Packed crowd rendering correct across 1975/1985/1995 |
| C46 | Every condition-bearing scene type prints its CONDITION line; mall (no condition arc) prints none | ✅ PASS | CONDITION line present for gas_station/downtown_street/strip_mall/auto_repair, absent for mall |
| C47 | Condition rank never skips 0 -> 2 between consecutive eras; a run that ever decayed never ends on 'abandoned' | ✅ PASS | Trajectory steps one rank at a time and resolves across 40 seeds x 4 scene types |
| C48 | Distinctive phrases appear verbatim in the synthetic base prompt; a set Composition produces the framing line | ✅ PASS | Distinctive and Composition both render correctly |
| C49 | No era of any run reaches 'abandoned' (40 seeds x 6 eras x 2 retail scene types) | ✅ PASS | 480 era conditions sampled, none abandoned |
| C50 | Squatted retail prompts carry a surviving tenant and ground-level decay, never the fully-closed wording | ✅ PASS | survivors present, ground details reachable, fully-closed line absent |
| C51 | A run that reached rank 2 before the finale ends squatted — never restored or declining | ✅ PASS | 80 rank-2 runs all held their finale at squatted |
| C52 | The 'restored' descriptor reads as reoccupation of the same shell, not a renovation | ✅ PASS | 56 restored finales, all reoccupation wording |
| C53 | Squatted retail gets trading-row people and vehicles; the squatted forecourt stays dead and its figures sum to its stated total | ✅ PASS | retail rows populated and parked, forecourts dead with single-figure passers-by |
| C54 | Chained eras size trees as growth against the uploaded previous era, never as a fraction of the base; a step too small to draw is deferred rather than stated or dropped | ✅ PASS | large states in 2005; medium states in 1985/1995/2005/2015/2025; small states in 1985/1995/2005/2015/2025 |
| C55 | The caption tail runs from run-folder state alone, so a resumed batch run is captioned too | ✅ PASS | narrative.json and scene.json round-trip; caption.txt written with years and hashtags |
| C56 | Downtown and strip-mall poles and wires are explicitly removed from 2015 on; other scene types keep theirs | ✅ PASS | wires stay through 2005, then go underground on main street and at the strip mall only |
| C57 | A weighted hashtag (#nostalgia 70%) hits its declared share of captions, spends a sampled slot, and never ships its weight suffix | ✅ PASS | #nostalgia in ~70.9% of 4000 draws; pinned set and tag count unchanged |
| C58 | Period details are conditional on the geometry: every prompt states that a detail with no plausible place is left out, and nothing is placed in the roadway | ✅ PASS | placement rule present in every era prompt, ahead of the signage whitelist |
| C60 | The corner shop and the freestanding shop each open as one grocery or pharmacy, turn over to a liquor store from 2015 with the old name ghosting above it, draw regulars rather than shoppers after that, and never recover | ✅ PASS | all four runs hold the trade arc, the decline and the prompt budgets; across 500 seeds each, corner_shop ends boarded 33% of the time; freestanding_shop ends boarded 33% of the time, never earlier |
| C63 | SceneContentKey splits highway into urban/rural content keys by terrain — urban, suburban and industrial all take the corridor flavor, only rural and unrecognized or missing values take the countryside one — and leaves every other scene type unchanged | ✅ PASS | content key resolution holds for every terrain and scene type |
| C64 | A highway prompt describes moving traffic and no storefronts: no parked/curb/stall wording, no PLACEMENT line, no shop content even when the era offers some, and the urban flavor goes packed from 2005 while the rural one never does | ✅ PASS | both flavors hold the traffic wording and the density arc |
| C65 | Route numbers, exit numbers and mile markers are genericized out of Distinctive while the sign itself and every unnumbered landmark survive | ✅ PASS | numbered route signage genericized; gantry and landmark kept |
| C66 | A highway names exactly one generic background business per era when buildings are in frame, and none at all on open road | ✅ PASS | background tenants follow the buildings, one per era, never on an empty road |
| C67 | Every prompt states what may be read in the frame — a whitelist or an explicit none — and a highway asks for no skyline, no exit numbering and no legible business name | ✅ PASS | signage is constrained in every prompt; highway invents no geography |
| C68 | A highway keeps its guide sign as a green-faced object with an illegible legend instead of the blanket no-text block; no period detail turns the sign away, and no other scene type gets the highway variant | ✅ PASS | highway signage stays visible and wordless; other scene types unchanged |
| C69 | The synthetic base emits architecture only where buildings exist, states outright that nothing is built where none do, and never does both at once | ✅ PASS | architecture follows the building list, not the scene type |
| C70 | A background tree grows on a flatter curve than a kerbside one and ends the run at no more than ~165% of its first-era canopy, in both the chained and unchained paths | ✅ PASS | background tree: first era 70% of base, 132% across the chained run (1985:— 1995:115% 2005:— 2015:115% 2025:—) |
| C73 | The Meta rewrite of every era prompt drops the alcohol and nowhere-to-be wording, holds people to a small group and vehicles to two, and keeps the main sign | ✅ PASS | 109 prompts rewritten clean across every scene type |
| C74 | Prompts and a synthetic base build from generated SceneDna for every scene type over 6 seeds, with no photo and no Vision call, inside the same budgets | ✅ PASS | 360 prompts from generated scenes; 11 tree-free and 3 building-free shapes covered |
| C62 | The corner shop's street tree reads as a living tree in every era and its state follows the shop's arc (leafy while trading, untrimmed while declining, half dead once derelict); other scene types carry no tree state | ✅ PASS | corner_shop tree moves through 2 states across the run, canopy sizing intact, no other scene type affected |
| C61 | corner_shop always draws from liquor_urban and freestanding_shop always draws from liquor_suburban; no other scene type renders a liquor name; strip_mall/shopping_center stay mapped to suburban for later, still unused today | ✅ PASS | urban 46, suburban 33, no overlap; corner_shop drew 32 distinct urban names and freestanding_shop drew 25 distinct suburban names across 60 seeds each; no liquor name in any other scene type |
| C72 | Liquor-name and origin-kind randomness for corner_shop and freestanding_shop is visible and healthy across many seeds | ✅ PASS | corner_shop: 46/46 urban names hit (100%); freestanding_shop: 33/33 suburban names hit (100%) — full tables in the log above |
| C71 | Every motel flag is a chain that existed in the era it is rendered in, a derelict motel shows a stripped pylon instead, and the flag actually changes across a run | ✅ PASS | 33 chains, 30 distinct flags across 120 seeds, 68% of runs reflag, every flag inside its own year window |
| C59 | Title templates load for base and every scene type; every line substitutes with no leftover placeholder and stays non-empty and inside YouTube's 100-char limit | ✅ PASS | gas_station: 16 titles, longest 62 \| downtown_street: 16 titles, longest 55 \| strip_mall: 16 titles, longest 59 \| auto_repair: 16 titles, longest 55 \| corner_shop: 16 titles, longest 62 \| freestanding_shop: 16 titles, longest 64 \| motel: 16 titles, longest 58 \| highway_urban: 16 titles, longest 61 \| highway_rural: 16 titles, longest 70 \| mall: 16 titles, longest 57 \| shopping_center: 16 titles, longest 60 \| base: 16 titles, longest 59 |
| C75 | Every brand-series prompt resolves all placeholders, carries every block, and is a colour photograph with no monochrome block | ✅ PASS | 6 era prompts, no unresolved tokens, all blocks present, every era colour |
| C76 | Each brand era states a logo reference exactly when the series carries one | ✅ PASS | reference in 1975, 1985, 1995, 2005; none in 2015, 2025 |
| C77 | The sign-removal era states the removal explicitly and says what is left in its place | ✅ PASS | 2015: SIGN REMOVED — lettering taken down, mounting points, faded outline, empty pylon frame |
| C78 | The redeveloped era names no original brand signage, clears the old sign's hardware, and fills the frontage with trades that were actually open that year | ✅ PASS | 2025: no "Kmart" anywhere, fascia resurfaced, units Crunch Fitness, Salvation Army Family Store, Rent-A-Center; 35 eligible, no two of a kind across 300 seeds |
| C79 | No brand-series prompt states a numeric count of people or vehicles | ✅ PASS | density is words only across all six eras |
| C80 | No vehicle class repeats across the eras of a brand run, and the classes are stated as examples rather than as the whole lot | ✅ PASS | 18 distinct classes across 6 eras, none repeated, each era's list stated as examples |
| C81 | Every brand-series prompt is under 960 words and 6000 chars | ✅ PASS | worst case 536 words / 3455 chars |
| C82 | Every brand era after the first carries one continuity block stating the real year gap; the first carries none | ✅ PASS | 1975 none; 1985, 1995, 2005, 2015, 2025 each state their own gap |
| C83 | The first brand era states the 9:16 canvas and clears nothing; every era after it clears the people and traffic of the frame it edits | ✅ PASS | 1975 drawn from text, states the crop; 1985, 1995, 2005, 2015, 2025 each clear the frame they edit |
| C84 | A brand era with an empty crowd states a place with nobody in it, not the live people block with one word changed | ✅ PASS | deserted eras: 2015; every populated era states a group, never the bare density word |
| C85 | A brand era whose logo changed states the old sign's takedown; one whose logo did not says it stays; every stated logo carries its reference image | ✅ PASS | replaced in 1995, 2005; held in 1985; every logo era carries a reference |
| C86 | The brand series has its own caption bodies and title hooks, and the caption tail resolves to them rather than falling back to base | ✅ PASS | 15 bodies, 14 titles under data/captions/brand_series*, caption and title assemble from them |

## Vehicle Selections

### gas_station / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1973-1979 Ford F-100 — square body, chrome grille, 1971-1976 Chevrolet G10 Sportvan — boxy windowed van, chrome bumper, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1970-1976 Pontiac Firebird Trans Am — hood scoop, spoiler, bold graphics |
| 1985 | 3 | 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood, 1973-1987 Chevrolet C/K — square body pickup, dual headlights, 1983-1985 Nissan Maxima — boxy import sedan |
| 1995 | 3 | 1990-1997 Mazda Miata — tiny rounded roadster, pop-up lights, 1992-1997 Ford Taurus — rounded jellybean shape, oval theme, 1989-1997 Geo Metro — very small economy hatchback |
| 2005 | 4 | 2004-2008 Pontiac Grand Prix — sporty sedan plastic cladding, 2003-2009 Hummer H2 — massive military-styled SUV, 2001-2005 Honda Civic — rounded compact, very common, 2002-2006 Toyota Camry — smooth conservative mid-size |
| 2015 | 4 | 2010-2016 Chevrolet Equinox — mid-size crossover, 2011-2016 Volkswagen Jetta — clean simple sedan, 2015-2020 Ford Edge — mid-size crossover, bold grille, 2011-2017 Jeep Grand Cherokee — refined upscale SUV |
| 2025 | 2 | 2022-2025 Ford Maverick — small unibody pickup, 2017-2025 Honda CR-V — rounded best-selling crossover |

### gas_station / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 4 | 1968-1979 Chevrolet Nova — compact, simple three-box shape, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille, 1975-1979 Chevrolet Monza — small hatchback, rare but present, 1971-1978 Dodge Tradesman — boxy full-size van |
| 1985 | 3 | 1980-1986 Ford F-150 — square body, dual headlights, 1983-1985 Nissan Maxima — boxy import sedan, 1982-1985 Toyota Celica — angular sporty coupe, pop-up lights |
| 1995 | 4 | 1991-1996 Ford Escort — small rounded economy car, 1992-1996 Toyota Camry — rounded, understated, 1991-1995 Dodge Caravan — rounded second-gen minivan, 1994-1997 Honda Accord — smooth rounded sedan |
| 2005 | 1 | 2005-2010 Jeep Grand Cherokee — rounded modern SUV |
| 2015 | 1 | 2009-2018 Ram 1500 — crosshair grille, refined |
| 2025 | 3 | 2019-2025 Subaru Outback — rugged wagon crossover, 2021-2025 Ford Bronco — retro boxy off-roader, 2024-2025 Toyota Grand Highlander — large family crossover |

### downtown_street / Run 1 (seed=42)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 5 | 1973-1979 Ford F-100 — square body, chrome grille, 1971-1976 Chevrolet G10 Sportvan — boxy windowed van, chrome bumper, 1975-1979 AMC Pacer — wide bubble-shaped compact, huge glass area, 1970-1976 Pontiac Firebird Trans Am — hood scoop, spoiler, bold graphics, 1973-1978 Datsun 610 — compact sedan/wagon, boxy |
| 1985 | 6 | 1981-1985 Ford Escort — small boxy economy hatchback, 1980-1986 Ford F-150 — square body, dual headlights, 1983-1988 Ford Thunderbird — aero coupe, rounded, 1984-1988 Toyota Pickup — small, boxy, popular import, 1978-1987 Chevrolet Monte Carlo — personal luxury coupe, long hood, 1982-1986 Nissan Sentra — small economy boxy sedan |
| 1995 | 5 | 1995-2004 Toyota Tacoma — compact, rounded, 1995-1999 Dodge Neon — small rounded economy, friendly face, 1988-1998 Chevrolet C/K 1500 — softly squared pickup, 1991-1994 Saturn SL — plastic body panels, compact, 1990-1994 Chevrolet Lumina — rounded mid-size sedan |
| 2005 | 1 | 2003-2008 Toyota Corolla — conservative compact sedan |
| 2015 | 1 | 2014-2019 Kia Soul — boxy urban hatchback |
| 2025 | 5 | 2021-2025 Ford Bronco — retro boxy off-roader, 2021-2025 Toyota Camry — sleek sedan, aggressive front fascia, 2022-2025 Nissan Pathfinder — squared-off three-row SUV, 2021-2025 Nissan Rogue — squared-off crossover, floating roofline, 2021-2025 Kia Telluride — boxy upscale three-row SUV |

### downtown_street / Run 2 (seed=1337)
| Year | Count | Vehicles |
|------|-------|----------|
| 1975 | 6 | 1968-1979 Chevrolet Nova — compact, simple three-box shape, 1975-1980 Ford Granada — compact luxury, Mercedes-inspired formal grille, 1975-1979 Chevrolet Monza — small hatchback, rare but present, 1971-1978 Dodge Tradesman — boxy full-size van, 1975-1978 Datsun 280Z — sleek fastback sports coupe, 1968-1978 Volkswagen Beetle — rounded rear-engine economy car |
| 1985 | 6 | 1984-1988 Toyota Pickup — small, boxy, popular import, 1973-1991 Chevrolet Suburban — long boxy wagon-SUV, 1978-1986 Ford Bronco — full-size boxy SUV, round headlights, 1975-1991 Ford Econoline — boxy full-size van, 1982-1988 Chevrolet Celebrity — boxy front-wheel drive sedan, 1973-1987 Chevrolet C/K — square body pickup, dual headlights |
| 1995 | 6 | 1989-1997 Geo Metro — very small economy hatchback, 1993-2002 Pontiac Firebird — sleek pointed sports coupe, 1994-2001 Dodge Ram — big rig style grille, bold, 1995-1999 Dodge Neon — small rounded economy, friendly face, 1993-1997 Ford Ranger — compact pickup, straight lines, 1992-1996 Ford F-150 — rounded aero body |
| 2005 | 2 | 2000-2005 Ford Focus — European-styled compact, 2003-2007 Honda Accord — clean lines, sharper than 1990s |
| 2015 | 1 | 2013-2019 Ford Fusion — Aston-Martin-style grille, sleek |
| 2025 | 2 | 2021-2025 Kia Telluride — boxy upscale three-row SUV, 2022-2025 Nissan Pathfinder — squared-off three-row SUV |

