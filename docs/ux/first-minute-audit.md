# JoyVeyor first-minute UX audit (title -> level select -> level 1 -> first win)

Card t_dfe86793 (step 2 of 4: research -> UX audit -> owner playtest -> art). Author: agent-N (UX/UI).
Repo state audited: main @ be885c4 (2026-10-03 02:17). No game code was edited.
Scope: onboarding, HUD clarity, menu flow. This audit does not judge fun; the owner is the only judge of fun.

## Method and honest limits

- Screenshots: the existing headless MenuTest / HudTest captures from today (02:09-02:17), copied to `docs/ux/shots/` (menu, levelselect, hud, build, pause, complete, failed; 1600x900).
- Code: `unity-project/Assets/Scripts/{MenuFlow,GameScreen,Onboarding,GameSession}.cs`, level files and `reference/*.sol`.
- Headless shots show UGUI chrome only. In-game conveyor sprites do not render headless, so nothing below claims the pixel art or the live drag feel was seen. Items marked [code] are read from source and were not observed live.
- Only onboarding hint 1 (DrawBelt) appears in a capture. Hints 2 and 3 are [code]-only.
- No human playtested this. "Expected effect" ratings are my design judgement, to be checked in the owner playtest (step 3).

## Corrections folded in from the PO note

- RELEASE.md line 63 ("onboarding ... not in this build") is stale. `Onboarding.cs` exists and is wired at `GameScreen.cs:226-229`. The researcher brief's change #1 (build a new hint layer) is therefore dropped. Item 5 below extends the existing one instead.
- Brief items 3-5 (par cliff L10 -> L11-13, rate-type level, L5/L7 budget sawtooth) are owner questions on the playtest card. They are not in this list.

## Flow as it exists today

Boot (forced 3.0 s, `MenuFlow.cs:27`) -> Main menu (PLAY / HOW TO PLAY / QUIT) -> Level select (15 cells, linear unlock) -> Level 1 build phase (hint box + hotbar + RUN) -> RUN -> Complete overlay (stars + score rows) -> NEXT / REPLAY.
Count: the player is 3 clicks from playing (PLAY, cell 1, then draw a belt). That is fine; the problems are inside level 1.

## Ranked change list

Ranking = how likely the problem is to leave a new player stuck or unsure in the first minute, highest first.

### 1. Level 1's first instruction fails silently when followed literally; rejections give no visible reason
- Problem seen:
  - Hint 1 says "Hold left-click and drag to draw a belt from the source to the sink." (`Onboarding.cs:33`; visible in `shots/hud.png`).
  - The drag rule: a belt starts at the cell where you press and its length is max(|dx|,|dy|) cells, so it does not include the release cell (`GameScreen.cs:348,389-405`, `PlaceBelt` -> `GameSession.PlacePiece`). Level 1 has the source at (0,0) and the sink at (5,0) (`level01.jvl`). A player who presses on the source and releases on the sink asks for a belt starting on the locked source cell. `GameSession.cs:233-241` rejects it with Message "locked piece: can't build there".
  - Nothing ever shows `session.Message`. Grep finds only test files reading it. The only feedback is an invalid-sound call (`GameScreen.cs:437`). The same silence applies to "budget: N/M pieces used" (`GameSession.cs:223-227`).
  - The ghost preview does not follow the drag. `UpdateGhost` draws `beltLen` (default 3) in the current `dir` at the hover cell (`GameScreen.cs:524-534`; `dragX/dragY` is never used by it). The player cannot see where a drag will land or how long it will be before releasing.
  - The correct move is to start on the cell next to the source, (1,0), and drag to (5,0). The reference solution (`reference/level01.sol`) starts the belt at (1,0); nothing tells the player that.
- Proposed change (three parts, can be split into cards):
  - (a) Draw the live belt ghost from `dragX/dragY` to the hover cell while the button is held, red when invalid.
  - (b) Show `session.Message` as a short toast near the grid for about 2 s, with ClearMessage afterwards. Reuse the existing red banner style from the JAMMED banner (`GameScreen.cs:1094-1102`).
  - (c) Optional, owner's call: let a drag that starts on a locked piece snap to the first free cell, so "source to sink" works as written.
- Effort: (a) M, (b) S, (c) S.
- Expected effect on "confused in first minute": high. This is the single most likely stuck point. A new player's first, correct-by-the-text action produces no change and no message.

### 2. Hint 1 wording and placement do not match the board (what a "belt" is, where to start)
- Problem seen:
  - The hint box is anchored bottom-centre at y=-150 (`Onboarding.cs:43`) with the arrow pointing up. In `shots/hud.png` the box sits mid-screen, overlapping the lower part of the grid, and the arrow points at empty grid. It is not anchored to the source or sink [code + shot]. The brief's own rule is "tied to a visible object".
  - The text assumes the player already knows what the source and sink look like. The hotbar icons (green dot, blue-ish glyph) are the only identification, and slot 3's glyph is blank (see item 7).
  - Hint 2 ("Ready? Press RUN to start the clock.", anchored x=250, y=-150) points at an area where RUN is at bottom-right. Whether the arrow lands on RUN at other resolutions was not captured [code only].
  - The hint box is 460x64 px with Overflow wrap, and the 70-character line is wider than its box in `shots/hud.png` (text visibly runs past the box edges).
- Proposed change:
  - Reword to the actual rule: "Drag from the cell next to the source to the sink to lay a belt." (one line, under about 55 characters if possible), and enlarge the box to fit.
  - Anchor each hint to the object: world-position the arrow at the source cell for hint 1 and at the RUN button for hint 2, instead of fixed canvas offsets.
- Effort: S (wording + box size), M (world anchoring).
- Expected effect: high. It removes the mismatch between "source to sink" and the real rule from item 1 even if item 1(c) is declined.

### 3. No way to open the menu, retry or leave during Build phase; the pause button is dead until RUN
- Problem seen:
  - `TogglePause` only acts in the Run phase (`GameScreen.cs:497-501`; `Pause()` in `GameSession.cs:107-110` requires Run). The overlay shows only when `Paused && phase == Run` (`GameScreen.cs:1575`).
  - In Build phase the pause button in the top bar does nothing and Esc does nothing. Both the controls reference ("Keys: R rotate...") and the "Skip hints" link live only inside that pause overlay (`GameScreen.cs:1159-1190`), so a new player in Build cannot reach the key list, text-size setting, quit-to-menu or Skip hints.
  - The top-right pause button is drawn as a blank grey tile in `shots/hud.png` (U+23F8 glyph missing from the built-in font), so the player has no signal that it is a button at all.
- Proposed change: make the pause overlay available in Build phase (Esc and the button), with RESUME/RESTART/QUIT/Skip hints working there. Fix the glyph (see item 7).
- Effort: M.
- Expected effect: medium-high. It gives a stuck player an exit and a controls cheat-sheet at the moment they are stuck, not only after RUN.

### 4. Splitter and merger placement is not controllable in the UI, and nothing says how
- Problem seen:
  - Tool placement hard-codes orientation: splitter outputs always E and S (`GameScreen.cs:415`), merger inputs S and W with output E (`GameScreen.cs:416`). R rotation changes only `dir`, which affects belts and the ghost; the T/M branch of `PlaceAt` ignores it [code].
  - Reference solutions: splitter `T(1,2)` (outputs E and S) matches the hard-coded one, but every merger in the references is `M(1,3,2)` (inputs E and W, output S, `level03.sol`, `level06.sol` and others), which differs from the hard-coded merger (inputs S and W, output E).
  - How to Play only says "Splitter - 1 in, 2 out / Merger - 2 in, 1 out" (`MenuFlow.cs:388-392`). It does not say which sides they use.
  - Level 3 (`level03.sol`) is where merger orientation first matters. A player who follows the hint text cannot match the reference geometry. [code: please confirm in the owner playtest; I could not drive the placement headlessly.]
- Proposed change: needs a builder to confirm the bug first. If confirmed, make R rotate T/M placement and show the port directions on the ghost (small arrows on the output/input sides).
- Effort: M-L (depends on confirming; ghost arrows are the larger part).
- Expected effect: high on levels 2-4, low on level 1. This is outside the literal first minute but is the next wall: a player who clears L1 and meets L3 may be unable to place the piece they were told to use.

### 5. Onboarding teaches only level 1; splitter (L2) and merger (L3) arrive with no introduction, and the brief's rule (new piece named when first available) is not met
- Problem seen:
  - `Onboarding.TutorialLevel = 1` and `Reset` returns early for any other level (`Onboarding.cs:28,91`). Three hints exist in total, all about belts and RUN.
  - The hotbar shows all six tools from level 1, including splitter and merger that no level-1 solution uses. `level02.sol` is the first with a T and `level03.sol` the first with an M.
  - The only introduction to T/M is the optional How to Play screen, whose cards are 26 px one-liners.
- What the existing onboarding does well (keep): non-blocking, one hint at a time, dismisses on the player's own action, persisted, plus a skip link, so it already satisfies the brief's "non-blocking, dismissible" rule. It is not a modal dialog.
- Proposed change: reuse the same `Onboarding` mechanism for two more one-line hints, shown once on first entry to L2 ("Splitter: 1 belt in, 2 out") and L3 ("Merger: 2 belts in, 1 out"). Optionally dim or hide hotbar slots 4 and 5 until L2 and L3. Do not add more than two.
- Effort: S for the two hints (data-driven, new bitmask bits); M if hotbar progressive reveal is included.
- Expected effect: medium. It answers the PO's question "does it teach T/M in L3/L4?": it does not, since both pieces appear untaught.

### 6. The top bar shows time and delivered count but not the two other numbers a level is judged on
- Problem seen (`shots/hud.png`, `shots/build.png`):
  - Top bar: timer "0:18" at left, "0/10" centre with a thin progress bar, restart and pause at right. There is no budget counter ("pieces used / budget") and no level name or number.
  - The budget is enforced silently (item 1: "budget: N/M" appears only in the unseen Message). Stars depend on it: 3 stars = win within par time and pieces <= par (`GameSession.cs:15,202-206`).
  - The "0:18" in build phase is the time limit (900 ticks / 50 Hz), before RUN starts. It is not labelled as a limit, so it reads as a stopwatch already running. The clock starts only on RUN (`GameSession.cs:100-104`), exactly what hint 2 says, but the box disappears when the player does the first thing in hint 1.
  - "0/10" has no noun. Level 1 hint 3 supplies "Get 10", but only after the first delivery.
- Proposed change: add "Belts 2/4" (pieces used / budget) beside the timer or under the delivered count; label the pre-run time "18s limit" (or show it greyed until RUN); add "delivered" after the count; show "Level N - name" small at top-left. Reuse existing top bar text styles.
- Effort: S.
- Expected effect: medium. It removes three small ambiguities that each cost a re-read.

### 7. Glyph and icon defects make the first screen look broken
- Problem seen (zoomed crops of `shots/hud.png`, `shots/levelselect.png`):
  - Hotbar slot 3 (sink) icon is blank (U+25F1 not in the built-in font); only the number "3" shows. The sink is the thing the player must reach in level 1.
  - Hotbar slot 1 (belt) shows a solid yellow square with no icon visible: the selection highlight (`SelHi`, `GameScreen.cs:964,1083`) is a full-slot opaque fill drawn over the icon.
  - Pause button: blank grey tile (U+23F8). Restart (U+21BB) renders correctly.
  - Level select: filled/empty "stars" render as a leaf/crown-like shape, not a 5-point star, and the padlock sits over the middle star (`shots/levelselect.png`, `MenuFlow.cs:314-340`). Locked cells still show three empty stars plus a padlock.
  - Hotbar slots have numbers but no names, so "belt / source / sink / splitter / merger / delete" is learnable only from How to Play.
- Proposed change:
  - Replace the three missing glyphs with code-drawn sprites (the project already does this for the arrow, star and padlock).
  - Make the selection highlight an outline or a tint behind the icon.
  - Add a short name label under each slot, at 14-16 px.
  - Draw a recognisable star. Hide the stars when a level is locked.
- Effort: S-M.
- Expected effect: medium. It is cheap and removes "is this broken?" doubts on the first gameplay screen. Note: the star shape may be a headless-render artefact; verify in the real build (`open dist/JoyVeyor.app`) before filing.

### 8. End-of-round screens explain a result without saying why or what to do about it
- Problem seen (`shots/complete.png`, `shots/failed.png`):
  - Failed: "TIME'S UP / 0 / 10 delivered / RETRY / MENU". No hint about the likely cause. Brief rule 4 asks for failure causes in the world. The sim already exposes a deadlock check (`jv_is_deadlocked`, the JAMMED banner at `GameScreen.cs:1102`) that the failed screen does not use, and the Message from item 1 is not carried here either.
  - Complete: three stars and "Delivered 10/10 / Time left +6430 / Efficiency +150 / Total 6580". The units and the star rule (par time, par pieces) are never stated, so a first-time 1-star or 2-star result is unexplained. "+6430" for time left reads as a bug on first sight.
  - On the first win the player has no sense of what a 3-star run would require.
- Proposed change:
  - Failed: one extra line chosen from {belt never reached the sink, jammed, ran out of time with N/10}.
  - Complete: show the star criteria under the stars ("3 stars = under 15 s with 3 belts or fewer"), and rename "Time left" to a plain unit.
- Effort: S for the complete screen text; M for a reliable failure cause.
- Expected effect: medium. It matters most on the first fail, where the player decides to retry or quit.

### 9. Boot screen blocks for 3 s every launch, and PLAY adds a level-select hop before level 1
- Problem seen:
  - `MenuFlow.cs:27` BootSeconds = 3, no skip (`MenuFlow.cs:105-112`).
  - Level select shows 15 cells, 14 of them locked, on a first visit (`shots/levelselect.png`), so the first-ever visit is a one-choice screen.
- Proposed change: any key or click skips the boot; on a fresh save (no progress), PLAY goes straight to level 1, and the level-select screen opens from later visits or from a small "Levels" button. Owner can decline the second half.
- Effort: S (boot skip), S-M (fresh-save shortcut).
- Expected effect: low-medium for confusion (the flow is clear), but it trims the time to first belt, which the brief sets as a 60 s target.

### 10. Docs describe a different build than the one that ships, and a tick-rate mismatch
- Problem seen:
  - `RELEASE.md:62-66` still lists "No main menu / level select yet ... onboarding ... not in this build". They exist (this audit's screenshots).
  - Time base: `README.md:106,206` and `GameSession.cs:112` say 30 Hz; the project's fixed timestep is 0.02 s (50 Hz; `ProjectSettings/TimeManager.asset` m_Count 2822399 / m_Rate 141120000) and `FormatTime` divides by 50 (`GameScreen.cs:1615`). So level 1's 900-tick limit displays as "0:18" in game, while README, `level01.jvl` comments and the researcher brief call it 30 s.
- Proposed change: refresh RELEASE.md (the PO asked for this); correct the "30 Hz" comments. This is not a build item for the player, but it affects every time number in the docs.
- Effort: S.
- Expected effect on first-minute confusion: none directly. It is listed because the stale RELEASE.md caused the brief to propose onboarding that already exists.
- **Owner question, not a build item:** is the sim meant to run at 50 Hz (18 s for L1) or 30 Hz (30 s)? The par times and time limits were authored in "ticks @30 Hz". If the sim really runs at 50 Hz, every level plays 1.67x faster than the .jvl numbers suggest. I did not run the sim to settle which clock is correct; please check it in the playtest.

## Checked and fine (no change proposed)

- Title screen (`shots/menu.png`): one clear primary action, PLAY (largest button), HOW TO PLAY and QUIT secondary, version tag bottom-right. Clean.
- Hint box contrast: white on near-black is 18.7:1, well above the 4.5:1 AA bar. Locked-level numbers compute to about 4.55:1 (passes, barely).
- Onboarding behaviour: non-blocking, one hint at a time, advances on the player's own belt/RUN actions, hides itself before the complete/failed overlays, persists dismissal. "Skip hints" is the right control, but see below.
- Unlock rule: linear, win N to open N+1 (`ProgressStore.cs:85-90`). Level select makes locked cells non-clickable. Back button present on level select and how-to-play.

## "Skip hints" link: does it read clearly? (PO question)

No. In `shots/pause.png` it is the smallest, lowest-contrast item in the stack: 20 px blue text under the key list, no button chrome, so it does not look clickable. It also lives only in the pause overlay, which cannot be opened in Build phase (item 3), and the hint box it dismisses overlaps the pause overlay's own TEXT SIZE row (the hint box stays visible behind PAUSED in the same shot). Fix with item 3 (reachable in Build), and give the link button styling or move a small "x" onto the hint box itself (S).

## Suggested build-card grouping for @product-owner

- Card A (S-M): items 1(a,b) + 2 (drag preview, rejection toast, hint wording). Highest value.
- Card B (M): item 3 + the Skip hints fix.
- Card C (S-M): items 6 + 7 (HUD labels, glyph fixes).
- Card D (M): item 4, starting with a confirm-bug step.
- Card E (S): item 5 two extra hints.
- Card F (S): items 8, 9 text and boot skip.
- Card G (S, docs): item 10 RELEASE.md and "30 Hz" comments.

## Questions for the owner playtest (step 3)

1. Time how long a fresh player takes from the first launch to the first belt delivered. The brief's target is 60 s.
2. Watch whether the player presses on the source cell (item 1) and what they do when nothing happens.
3. Place a splitter and a merger on L2 and L3: does the orientation match what the player expects (item 4)?
4. Is the 50 Hz vs 30 Hz clock intended (item 10)?
5. Do the stars in the real build look like stars (item 7)?
