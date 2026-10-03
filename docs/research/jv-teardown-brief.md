# JoyVeyor teardown brief: Factorio / shapez / Mini Motorways

Card t_b05197b8 (step 1 of 4: research, then UX audit, then owner playtest, then art). For @builder.
Scope: evidence and design patterns only. Nothing here claims anything is fun; the owner is the only judge of fun. No game code was edited.
Confidence: medium. Factorio and shapez numbers come from first-party posts and wiki tables. Mini Motorways numbers are thinner (one dev interview + Wikipedia). Rules marked [inference] are my reading, not a source claim.

## JoyVeyor baseline (from repo, levels 01-15 .jvl; ticks @30 Hz)

Supply = sum of floor(ticks / spawn_period) per source. Ratio = total sink quota / supply. Lower ratio = more slack.

| L | name | S/K | quota | limit | par | par pcs | budget | quota/supply @limit | @par |
|---|---|---|---|---|---|---|---|---|---|
| 1 | First Delivery | 1/1 | 10 | 900 (30 s) | 450 | 3 | 4 | 0.17 | 0.33 |
| 2 | The Turn | 1/2 | 10 | 900 | 540 | 5 | 7 | 0.17 | 0.28 |
| 3 | Two Flows | 2/1 | 16 | 900 | 600 | 4 | 6 | 0.13 | 0.20 |
| 4 | Balance | 1/3 | 16 | 1200 | 750 | 7 | 10 | 0.20 | 0.32 |
| 5 | Long Haul | 1/1 | 12 | 1200 | 660 | 1 | 3 | 0.15 | 0.27 |
| 6 | Crossroads | 2/2 | 16 | 1200 | 840 | 7 | 9 | 0.10 | 0.14 |
| 7 | Bottleneck | 1/2 | 20 | 1200 | 900 | 4 | 6 | 0.25 | 0.33 |
| 8 | Gauntlet | 2/3 | 26 | 1500 | 1050 | 10 | 12 | 0.13 | 0.19 |
| 9 | Overload | 1/3 | 32 | 1500 | 1200 | 7 | 11 | 0.21 | 0.27 |
| 10 | Grand Central | 3/4 | 40 | 1800 | 1350 | 11 | 14 | 0.11 | 0.15 |
| 11-15 | (t_f96ef9f8) | | 32-60 | 1300-2000 | 600-1300 | 4-17 | 6-17 | 0.09-0.24 | 0.23-0.37 |

Observations (data, not verdicts):
- Spawn period is 15 ticks in 9 of 10 original levels (10 in L9). Difficulty comes from sinks, quota, budget and layout. It does not come from speed.
- Supply exceeds quota 4-10x at the time limit (ratio 0.09-0.25). A level is lost by geometry or budget, not by running out of items. Raw throughput never becomes the constraint. This is the largest divergence from shapez (rate goals) and Mini Motorways (capacity collapse); see the ramp rules.
- Time limit ramps 900 to 1800 ticks (30 s to 60 s). Quota ramps 10 to 40 (4x). Budget ramps 4 to 14 (3.5x). L5 (budget 3, par 1 piece) and L7 (budget 6) dip below their neighbours, which is a sawtooth.
- Item type is single (RELEASE.md "Known limitations"). Piece types seen in reference solutions: B (belt), T, M. L10 uses M plus 2 T plus 8 B.
- There is no in-level hint or tutorial data in the .jvl format. Onboarding is listed as Sprint 5 (t_04665aad) in RELEASE.md. I did not verify whether it has since shipped.
- Level 1's par is 3 pieces; the sink is 5 tiles from the source at (0,0)-(5,0). It is the simplest possible puzzle, and L1 plus L2 is a 30-second-limit pair.

## (1) First-5-minutes rules

1. Make the first level winnable by doing one obvious thing, with a concrete named goal. shapez L1 asks for 30 circles with one extractor + belt to the hub (quota 30; the in-game text is "Place an extractor on a circle shape... connect via conveyor belt to your hub" per a recorded playthrough). Factorio's first-steps design also targets "as little complexity as possible" for the core concept (FFF-241). JoyVeyor L1 already fits: 1 source, 1 sink, quota 10. Keep it.
2. Introduce exactly one new thing per level, and unlock it at the moment it solves a problem the player just hit. shapez unlocks a new building or mechanic almost every level for its first 26 levels (levels table; Cutter at L1, Balancer L3, Rotator L4, Tunnel L5, Painter L6). In JoyVeyor, the map suggests M (merger) arrives in L3 and T (splitter) in L4. [inference from names and the .sol files]. Put the piece name on the HUD and in the level card the first time it is available.
3. Do not use blocking dialogs as the main teaching tool. Factorio's own tutorial had about 22 stop-the-game message dialogs before free play in level 2. The team named two drawbacks: constant interruptions, and players mindlessly following steps without understanding (FFF-261). Their new direction: no message dialogs. They also say essential info must not live only in the speech-bubble channel (FFF-342). Rule: any hint is non-blocking, dismissible, and tied to a visible object.
4. Show failure causes in the world. Factorio added interaction-error messages after new players could not tell why they could not interact with a far-away object (FFF-261). Mini Motorways answers the same need with a visible per-building pin counter and timer ring (Wikipedia). For JoyVeyor, when a sink is not filling, show why (blocked belt, no path, over budget) on the object itself. [inference]
5. Get the player to the core loop fast, and hold the "fun" claim for the playtest. Factorio's own post-mortem: its old tutorial took 30-45 minutes to reach automation, "which is what the game is about" (FFF-241). Their first-15-minutes warning is "if the first 15 minutes feels shitty, there is big chance that the player will not play any further" (FFF-261). Target: the first belt delivers items, and the first win, inside 60 seconds of starting. L1's 30 s time limit allows that. The owner playtest (step 3) must time it.

## (2) Difficulty-ramp rules (with numbers where sources give them)

1. Early goals grow geometrically but slowly. shapez quota ramp for levels 1-11: 30, 40, 70, 125, 170, 270, 300, 480, 600, 800, 1000, about 1.3-1.8x per level (Levels wiki). JoyVeyor's quota ramp is 10, 10, 16, 16, 12, 16, 20, 26, 32, 40. That is a similar overall shape (4x over 10 levels vs shapez's 33x over 11) and much gentler. L5's dip to 12 is the only non-monotone point, and it is a deliberate "one piece" teach level. Verify with owner whether it feels like a breather or a stall.
2. Switch the constraint type periodically, not just the size. shapez L14 is the one early level that asks for a rate (8/s) instead of a total, and rate goals take over in freeplay: 4/s at L27, rising to 22/s at L100, so one belt eventually cannot carry it (Steam achievement guide, citing the hub requirements). Total-quota levels teach building; the rate level forces redesign. JoyVeyor has no rate constraint. Because supply exceeds quota 4-10x (table), only par_time loosely acts as one.
3. Use a pressure meter that decays and can be recovered, so a lost level is gradual. In Mini Motorways, pins pile on a building; at 7 pins (square buildings) or 10 (circular) a timer starts, vehicles delivering reduce it, and a full timer ends the game (Wikipedia). The designers say the end state is deliberate: "every game needs to level with you and tell you it didn't go well enough" (Pocket Tactics GDC 2023). JoyVeyor's countdown is a flat cliff at time_limit. An optional per-sink "starvation" indicator is a candidate, but this is a game-feel change, so it goes to the owner as a question rather than a build item.
4. Control pacing with a schedule plus weighted randomness, and tune by repeated balance passes. Mini Motorways designers paint each map with weighted spawn areas and combine that with a "schedule of destinations to spawn throughout the game", giving control over cadence with replay variability. They also say balance took "a lot of time and iteration" with beta testers (Gamasutra/Game Developer interview). JoyVeyor's levels are fully authored and deterministic, enforced by tests/test_levels.cpp. That fits a puzzle design; keep it. Note the divergence rather than copy the randomness.
5. Insert a long pause between feature drops, and let old work keep paying off. shapez does not consume level deliveries ("shapes delivered to complete a level can be reused for upgrades", Levels wiki) and holds ten-plus levels at 25k quota with a new mechanic each (L19-L25) before freeplay. Factorio's first-steps campaign is just three levels (FFF-342); the later Tutorial lists five (Wiki). Applied to JoyVeyor: levels 11-15 (already committed) lower par_time hard (par 600-800 ticks vs 900-1350 in L7-L10) while raising budget to 17. The jump in par pressure at L11-13 is the biggest step in the file. Check that playtesters can still 2-star L11 before shipping.

## (3) Proposed changes, ranked (each rests on evidence above; none touch game code in this card)

1. Add a non-blocking, object-anchored hint layer for L1-L5 (new-piece name on first availability, one line max, dismissible). Evidence: Factorio's 22-dialog drawback and the "nothing necessary via speech bubbles" rule (FFF-261, FFF-342); RELEASE.md says onboarding is not in the build. Owner playtest checks whether it helped.
2. Show sink-fill progress and a "why is this not filling" cue on the sink itself. Evidence: FFF-261 interaction errors; Mini Motorways per-building pin/timer UI (Wikipedia). JoyVeyor's win test is "every sink's storageCount >= storageCapacity", so it is already countable per sink.
3. Smooth the par_time cliff at L11-L13. Evidence: par drops from 1350 ticks (L10) to 800 (L11), 700 (L12), 600 (L13) while quota/supply @par goes to 0.30, 0.29, 0.23 (table). Suggest a minimal check rather than a rewrite: run the owner through L10, L11 and L12 back to back and see where 2-star becomes unreachable.
4. Add one rate-type level somewhere in L6-L10 as an experiment (for example, require sustained items per second at a sink, or a tighter source:sink ratio). Evidence: shapez L14 is its first and only early rate goal, and rate goals then drive its whole endgame; JoyVeyor currently has 0.09-0.25 quota/supply slack everywhere (table). Mark it explicitly as an owner question: it changes the core loop's constraint, so the owner decides whether it is wanted.
5. Re-examine L5 (Long Haul: budget 3, par_pieces 1) and L7 as a sawtooth. Evidence: budget series 4, 7, 6, 10, 3, 9, 6, 12, 11, 14. This is observation only, since a breather level may be intended. Ask during the owner playtest whether L5 reads as a teaching beat or a dead spot.

## (4) Sources (all fetched 2026-10-03)

- Factorio FFF-241 New player experience (2018): https://factorio.com/blog/post/fff-241
- Factorio FFF-261 Performance + New player interaction (2018): https://factorio.com/blog/post/fff-261
- Factorio FFF-342 The new old tutorial: https://factorio.com/blog/post/fff-342
- Factorio wiki, Tutorial (5 levels): https://wiki.factorio.com/Tutorial
- shapez wiki, Levels (goal amounts, unlocks): https://shapezio.fandom.com/wiki/Levels
- Steam community, shapez 100% achievement guide (level shape table; 4/s to 22/s hub rates): https://steamcommunity.com/sharedfiles/filedetails?id=2423657811
- shapez first-play transcript (tutorial prompts), Northernlion: https://youtu.be/F4jqFXYc3R0
- Mini Motorways interview, Game Developer (Gamasutra): https://www.gamedeveloper.com/audio/-i-mini-motorways-i-and-the-delicate-art-of-marrying-complexity-and-minimalism
- Thumbsticks write-up of Dinosaur Polo Club GDC 2023 talk (Wuselfaktor, agency): https://www.thumbsticks.com/hustle-and-bustle-how-mini-motorways-built-its-wuselfaktor/
- Pocket Tactics, GDC 2023 interview ("predictable chaos"): https://www.pockettactics.com/dinosaur-polo-club/interview
- Mini Motorways, Wikipedia (pin thresholds 7/10, upgrades): https://en.wikipedia.org/wiki/Mini_Motorways
- JoyVeyor repo: unity-project/Assets/Levels/level01-15.jvl, reference/*.sol, README.md section 8, RELEASE.md

Limits: Mini Motorways' fandom wiki fetch failed (http_error), so pin/spawn numbers rest on Wikipedia only. No source gave Mini Motorways spawn-interval numbers. The shapez quota ratios are my arithmetic from the wiki table. The Factorio dialog count (about 22) is from the 2018 tutorial, which has since been revised.
