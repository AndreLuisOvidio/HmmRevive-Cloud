# LoL-style Lobby Draft Validation

**Date**: 2026-10-24
**Spec**: `.specs/features/lol-draft/spec.md`
**Diff range**: `c64d8d5..HEAD` on `feat/lol-draft` (HEAD = `40116c0`; re-verification after fix iteration 1 = `6fdb522`, `40116c0`)
**Verifier**: independent sub-agent (author ≠ verifier)

**Verdict**: PASS

Iteration 1 (HEAD `216fffe`) failed: the 15 s planning and 20 s finalization defaults weren't pinned (M8, M9 survived),
a locked position could still mark (M11 survived), the 1.8-refuses-1.7 refusal and the host reset had no direct test.
Fix iteration 1 added those tests and fixed two non-spec findings in `Lobby.cs` (Ready reset at finalization, bot cars
before leavers' cars). All 13 mutants are now killed.

---

## Task Completion

| Task | Status | Notes |
| ---- | ------ | ----- |
| T1 | ✅ Done | `SeatDraft.cs` engine |
| T2 | ✅ Done | swaps/trades |
| T3 | ✅ Done | `Json(viewerTeam, now)` |
| T4 | ✅ Done | `Lobby.cs` wiring, `Draft.cs` deleted |
| T5 | ✅ Done | `Session.cs:252`, `Program.cs:274` forward `target`/`request` |
| T6 | ✅ Done | page; no `draftBans`/`draftPicks` left in `web/*` (grep empty) |
| T7 | ✅ Done | 44 `ds_*` keys, all `[en, pt]` |
| T8 | ✅ Done | `VERSION` 1.8, `CHANGELOG.md`, both kit readmes |
| Fix 1 | ✅ Done | `6fdb522`: default times, locked-mark, version refusal, reset tests |
| Fix 2 | ✅ Done | `40116c0`: `Lobby.cs:536-542` Ready reset at finalization; `Lobby.cs:652` bot positions before leavers |

---

## Spec-Anchored Acceptance Criteria

Test files: `tools/Launcher.Tests/DraftTests.cs` (DT), `tools/Launcher.Tests/LobbyDraftTests.cs` (LDT). Lines are at HEAD `40116c0`.

### DRAFT-01 to DRAFT-08: Draft by position

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| AC1 draw | Team 1 random among blue/red; humans in positions before bots; humans in random order | LDT:129-132 `Assert.Equal(new[] { false, false, true, true }, blue.Select(s => (bool)s["bot"]))`; LDT:136 `Assert.Equal(new[] { "blue", "red" }, firsts...)`; LDT:137 `Assert.Equal(2, blueOrders.Count)` (40 draws) | ✅ PASS |
| AC2 order | bB1 rB1 bP1 rP12 bP23 rP3 bP4 rP4 (and mirrored) | DT:43-44 `Assert.Equal("bB1 rB1 bP1 rP12 bP23 rP3 bP4 rP4", Shape(...))`; DT:51-54 first turn = Team 1 ban, pos 1 | ✅ PASS |
| AC3 missing positions | skip the position; drop the turn when empty | DT:62 `"bB1 rB1 bP1 rP12 bP2 rP3"` (2v3); DT:63 `"bB1 rB1 bP1 rP1"` (1v1) | ✅ PASS |
| AC4 planning, first 15 s | marks accepted; lock refused "The draft starts in a moment."; 15 s | DT:73-75 `Assert.Equal("The draft starts in a moment.", d.Lock(...))`; DT:76-80 plan at +14, turns at +15; DT:87 `Assert.Equal(("plan", T0.AddSeconds(15)), (d.Stage, d.StageEnds))` with no explicit plan time; LDT:323 `Assert.Equal((30, 15, 20), (l.DraftTime, l.DraftPlanSeconds, l.DraftFinalSeconds))`; LDT:176 through the route | ✅ PASS |
| AC5 mark replaces | acting player's mark stored, last mark wins | DT:109-111 `Assert.Equal("4", Seat(d, "blue", 1).Mark)`; DT:177-178 a position that locked can't re-mark (`Assert.Null(...Mark)`, car kept) | ✅ PASS |
| AC6 not your turn | "It's not your turn." | DT:118-119; LDT:149 `Refused(Act(l, other, "lock"), "It's not your turn.")` | ✅ PASS |
| AC7 taken car | "That car is already banned or picked." | DT:127, DT:130; LDT:154 | ✅ PASS |
| AC8 no mark | "Choose a car first." | DT:136; LDT:150 | ✅ PASS |
| AC9 lock records, turn waits | ban/pick recorded; next turn only after all acting positions lock | DT:154 `"1" == Ban`, DT:157 `"3" == Car`, DT:161 `Assert.Equal(step, d.Step)`, DT:164 `step + 1` | ✅ PASS |
| AC10 time out | pick: mark if free else random free; ban: mark else none | DT:202 `Assert.Equal("8", ...Car)`, DT:204-205 random free car not in {1,2,3,8}; DT:215 `"6" == Ban`, DT:217 `Assert.Null(...Ban)`; DT:225-227 no limit = never times out | ✅ PASS |
| AC11 bots | ban nothing, pick random free car at once | DT:236-242 `Assert.Null(Seat(d, "red", 1).Ban)`, `Assert.NotNull(...Car)`; LDT:193 | ✅ PASS |
| AC12 leaver = bot | position acts as a bot | DT:249-251 `Assert.Null(Seat(d, "blue", 1).Ban)` with `gone`; LDT:188-192 after `l.Leave`, `Assert.NotEqual("", s["car"])` | ✅ PASS |
| AC13 integrity | no car twice; each position exactly one pick | DT:275-277 `8 == picks.Count`, `NotNull`, `8 == Distinct().Count()`; DT:287-288 no banned car picked; DT:189 a second lock on the same car refused | ✅ PASS |

### DRAFT-09: What each side sees

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| AC1 own-team marks only | own team sees `mark`/`intent`; other team, spectators, no team see "" | DT:432-437 `Assert.Equal("", SeatJson(d.Json(viewer, T0), "blue", 1)["mark"])` for red/spec/null | ✅ PASS |
| AC2 locked choices, order, current turn, seconds left for everyone | bans/picks of both teams in order, turns, step, seconds | DT:449-456 `Choices(j, "bans") == [("5","blue"),("6","red")]`, `("blue","turns",3,8,20)`, turn 3 = red pick [1,2]; for blue/red/spec. Requests per team: DT:465-468, LDT:254-255 | ✅ PASS |

### DRAFT-10: Finalization and the match

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| AC1 final stage, players' cars | stage "final"; each human's car = position pick | LDT:205-207 `Assert.Equal("final", d["stage"])`, `Assert.Equal(SeatOf(l, "Ana")["car"], M(l, "Ana").Car)` | ✅ PASS |
| AC2 20 s or all ready | match starts after 20 s (turn time > 0) or when every human is ready; no end when turn time 0 | DT:476 `Assert.Equal(("final", T0.AddSeconds(20)), (d.Stage, d.StageEnds))` with no explicit final time; LDT:323 default 20; DT:303-305 done at `StageEnds`; LDT:209-213 one ready = not started, both ready = `Assert.Single(_started)`; LDT:226-229 ends by time; LDT:276-278 a Ready from before the last pick doesn't skip finalization; DT:484-485 `Assert.Null(d.StageEnds)` for turn 0 | ✅ PASS |
| AC3 bot cars | each bot gets its position's pick | LDT:214-216 `Assert.Equal(BotCar("blue"), o.BluBotCars)`; LDT:293 `Assert.Equal(botCar, o.BluBotCars)` after a player left | ✅ PASS |
| AC4 car change ignored, skin kept | car unchanged, skin saved | LDT:240-242 `Assert.Equal(car, M(l, "Ana").Car)`, `Assert.Equal("3", M(l, "Ana").Skin)` | ✅ PASS |

### DRAFT-11: Swap positions

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| AC1 request, 10 s, team-visible | request stored, expires +10 s, only own team sees it | DT:317 `("swap","blue",2,3,T0.AddSeconds(10))`; DT:466-468 | ✅ PASS |
| AC2 accept exchanges | positions exchanged | DT:327-328 `"b3" == Seat(blue,2).Member`; LDT:257 `(bia, ana)` | ✅ PASS |
| AC3 car or acting | "You can't swap positions now." for request and answer | DT:353, DT:355 (request); DT:360-361 (answer while target is acting) | ✅ PASS |
| AC4 decline / expiry | dropped | DT:338-340 decline; DT:343-346 kept at +9, gone at +10 | ✅ PASS |
| AC5 not a human teammate | "Pick a teammate." | DT:368-370 (bot, self, missing position) | ✅ PASS |

### DRAFT-12: Trade cars

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| AC1 request, 10 s | stored, +10 s | DT:390-391 `("trade",1,2,T0.AddSeconds(10))` | ✅ PASS |
| AC2 accept exchanges picks and players' cars | seat cars and member cars swapped | DT:393-394; LDT:265 `Assert.Equal((b, a), (M(l,"Ana").Car, M(l,"Bia").Car))` | ✅ PASS |
| AC3 bot trade at once | no request, swapped | DT:403-405 `Assert.Empty(d.Requests)`, cars swapped | ✅ PASS |
| AC4 no car | "Both of you need a car first." | DT:413-414 | ✅ PASS |

### DRAFT-13: Full-window draft screen (page)

Covered by the author's manual UAT in a browser (planning, ban, pick, big car with skins, skin change, visible ban
slash, Red as Team 1 on the left). Matrix: no JS test infrastructure → manual UAT. The fix iteration didn't touch
`web/*`. Static review by the verifier:

| Criterion | Static evidence | Result |
| --------- | --------------- | ------ |
| AC1 draft replaces the lobby view, Team 1/Team 2 at the sides | `web/app.js:765-766` `#draft-screen` shown while `phase === "draft"`; `app.js:801-803` Team 1 (`D.first`) in `#ds-left` | ✅ UAT + static |
| AC2 car squares, banned greyed/crossed, picked in team color | `app.js:859-873` `banned`/`picked` classes `by-<team>` | ✅ UAT + static |
| AC3 big car + skins, click saves skin | `app.js:881-888` skin row; `app.js:1052-1055` `act("settings", { skin, skinCar })` | ✅ UAT + static |
| AC4 mark shows at once | `app.js:1041-1044` `dsPending` rendered before `act("lobby/draft", { action: "mark" })`; lock awaits the mark (`app.js:1046`) | ✅ UAT + static |
| AC5 every draft text in en and pt-BR | Node check: all 44 `ds_*` keys in `web/i18n.js` are 2 non-empty strings; every `t("…")`/`data-i18n` key used in `app.js`/`index.html` exists (only pre-existing dynamic prefixes unresolved) | ✅ static |

### DRAFT-14: Host settings and version

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| AC1 only on/off + time; saved orders ignored | setup JSON has no `draftBans`/`draftPicks` | LDT:324-328 `Assert.False(setup.ContainsKey("draftBans"))` after configuring them | ✅ PASS |
| AC2 default 30 s | 30 | LDT:323 `(30, 15, 20)`; LDT:329 `Assert.Equal(30, setup["draftTime"])` | ✅ PASS |
| AC3 version 1.8, 1.8 lobby refuses 1.7 | version "1.8"; a 1.7 join refused | `UpdateSourceTests.cs:19` `Assert.Equal("1.8", Program.Version)`; LDT:312-313 `Assert.Null(l.Join("Old", "1.7", ...))`, `Assert.Equal("The host runs HMM Revive 1.8 and you have 1.7. Everyone needs the same version.", error)` | ✅ PASS |

**Status**: ✅ All ACs covered with spec-anchored assertions (DRAFT-13 by manual UAT + static review)

---

## Edge Cases

- [x] Unknown action → "Unknown draft action.": LDT:165
- [x] Non-player (spectator, host not playing) → "Only players draft.": LDT:166-167
- [x] Locking twice → "You already locked in.": DT:146
- [x] Team of only bots never waits: DT:255-264 (`Assert.Equal("final", d.Stage)` after the human's actions only); DT:269-277 (all bots)
- [x] Host reset drops the draft and returns to the lobby: LDT:303-305 `Assert.Equal("lobby", l.Phase)`, `Assert.Null(l.Snapshot(...)["draft"])`, `Refused(Act(l, "Ana", "lock"), "No draft is running.")`. Requests go with the draft object. Note: the request at LDT:301 targets the player's own position (1v1 lobby), so it's refused and no live request exists at reset time. Low risk: requests live inside `SeatDraft`, which `ResetDraft` drops (`Lobby.cs` `_draft = null`)

---

## Discrimination Sensor

Scratch: `git worktree add %TEMP%/hmm-verify-wt HEAD` (at `40116c0`); one mutation at a time, file restored after each,
tests run with `--filter FullyQualifiedName~DraftTests` (matches DraftTests and LobbyDraftTests, 50 tests);
worktree clean after the run, then removed with `git worktree remove --force` and pruned.
Real tree `git status --porcelain` before = after (identical: only the three untracked `.specs` report/lessons files
from iteration 1).

| # | File:line | Description | Killed? |
| - | --------- | ----------- | ------- |
| M1 | `tools/Launcher/SeatDraft.cs:33` | Order table: Team 2's 1-2 pick becomes position 1 only | ✅ Killed (12 tests) |
| M2 | `tools/Launcher/SeatDraft.cs:131` | Time out/bot: a ban without a mark bans a random free car | ✅ Killed (3) |
| M3 | `tools/Launcher/SeatDraft.cs:236` | `Json`: marks shown to every team | ✅ Killed (1) |
| M4 | `tools/Launcher/SeatDraft.cs:147` | `CanSwap`: dropped the target's `Car == null` check | ✅ Killed (1) |
| M5 | `tools/Launcher/Lobby.cs:543` | Finalization ends when ANY player is ready (`All` → `Any`) | ✅ Killed (1) |
| M6 | `tools/Launcher/Lobby.cs:652` | `BotCars`: bots take the human positions' picks (`Member == null` → `!= null`) | ✅ Killed (2) |
| M7 | `tools/Launcher/SeatDraft.cs:277` | Finalization lasts the turn time instead of the final time | ✅ Killed (2) |
| M8 | `tools/Launcher/SeatDraft.cs:19` | `FinalSeconds` 20 → 25 | ✅ Killed (2: `FinalizationLastsTwentySecondsByDefault`, `TheHostSetup...`); survived in iteration 1 |
| M9 | `tools/Launcher/SeatDraft.cs:19` | `PlanSeconds` 15 → 10 | ✅ Killed (2: `PlanningLastsFifteenSecondsByDefault`, `TheHostSetup...`); survived in iteration 1 |
| M10 | `tools/Launcher/SeatDraft.cs:176` | Request expiry 10 s → 15 s | ✅ Killed (4) |
| M11 | `tools/Launcher/SeatDraft.cs:94` | `Mark`: dropped the `!s.Locked` guard | ✅ Killed (1: `APositionThatLockedCannotMarkAgainInItsTurn`); survived in iteration 1 |
| M12 | `tools/Launcher/Lobby.cs:540` | Fix: dropped the Ready reset when finalization begins | ✅ Killed (1: `ReadyBeforeTheLastPickDoesNotSkipFinalization`) |
| M13 | `tools/Launcher/Lobby.cs:652` | Fix: reverted bot car order to position order (leavers before bots) | ✅ Killed (1: `BotsKeepTheirOwnPicksWhenAPlayerLeft`) |

**Sensor depth**: expanded (13 manual mutations; no .NET mutation tool in the repo)
**Result**: 13/13 killed. PASS ✅

---

## Interactive UAT Results

| # | Test | Result | Details |
| - | ---- | ------ | ------- |
| 1 | Draft screen in a browser (author's UAT) | ✅ Pass (reported by author) | planning, ban, pick, big car with skins, skin change, ban slash visible, Red as Team 1 on the left |

---

## Code Quality

| Principle | Status |
| --------- | ------ |
| Minimum code | ✅ `SeatDraft` is one class (290 lines) replacing `Draft` (170). The fix adds one flag (`_draftFinal`) and one reordered query |
| Surgical changes | ✅ Lobby edits stay in the draft region, setup JSON and the route; `UpdateSourceTests.cs` changed only the version pin |
| No scope creep | ✅ |
| Matches patterns | ✅ net48, no NuGet, `Dictionary<string, object>` JSON like the rest of `Lobby`, errors as returned strings |
| Spec-anchored outcome check | ✅ Exact error texts, order, 10/15/20/30 s values and the version refusal text are pinned |
| Per-layer coverage expectation | ✅ Engine ACs 1:1; route covers happy + error paths for mark/lock/swap/trade/accept/unknown/non-player/no draft (`decline` only at engine level, a one-line pass-through of the same `Answer` call) |
| Every test maps to a spec requirement | ✅ Every DT/LDT test is labelled with its DRAFT id or edge case; the two fix tests cover iteration-1 findings on DRAFT-10 |
| Documented guidelines followed: `CLAUDE.md` | ✅ No game code/DLLs; every new string is in `i18n.js` in both languages; `VERSION` bumped with a `CHANGELOG.md` entry for the protocol change; kit readme pair updated together; no mod/patcher changes |

Iteration-1 observations, now resolved:

- Ready before the last pick skipped finalization: fixed at `Lobby.cs:536-542`, test LDT:269-278.
- A leaver's pick displaced a bot's own pick: fixed at `Lobby.cs:652`, test LDT:282-293.

Remaining note: build warning CS0162 at `Program.cs:156` is pre-existing (not in the diff).

---

## Gate Check

- **Gate command**: `dotnet build tools/Launcher -c Release && dotnet test tools/Launcher.Tests`
- **Result**: build 0 errors (1 pre-existing warning); 220 passed, 0 failed, 0 skipped (30 s)
- **Test count before feature**: 170
- **Test count after feature**: 220 (iteration 1: 213)
- **Delta**: +50 (36 DraftTests + 14 LobbyDraftTests)
- **Skipped tests**: none
- **Failures**: none
- Test integrity: the only modified pre-existing test is the version pin (`UpdateSourceTests.cs:19`, 1.7 → 1.8), as DRAFT-14 requires. In the fix iteration, the only changed assertion (LDT:323) became stricter (30 → (30, 15, 20)); no assertion weakened

---

## Fix Plans

None open. Iteration 1's Fixes 1-4 are done (`6fdb522`, `40116c0`) and re-verified above.

---

## Requirement Traceability Update

| Requirement | Previous Status | New Status |
| ----------- | --------------- | ---------- |
| DRAFT-01 | Implementing | ✅ Verified |
| DRAFT-02 | Implementing | ✅ Verified |
| DRAFT-03 | Implementing | ✅ Verified |
| DRAFT-04 | Needs Fix | ✅ Verified |
| DRAFT-05 | Implementing | ✅ Verified |
| DRAFT-06 | Implementing | ✅ Verified |
| DRAFT-07 | Implementing | ✅ Verified |
| DRAFT-08 | Implementing | ✅ Verified |
| DRAFT-09 | Implementing | ✅ Verified |
| DRAFT-10 | Needs Fix | ✅ Verified |
| DRAFT-11 | Implementing | ✅ Verified |
| DRAFT-12 | Implementing | ✅ Verified |
| DRAFT-13 | Implementing | ✅ Verified (manual UAT + static review) |
| DRAFT-14 | Needs Fix | ✅ Verified |

---

## Summary

**Overall**: ✅ Ready

**Spec-anchored check**: 46/46 criteria and edge cases match the spec outcome (DRAFT-13 by manual UAT + static review)
**Sensor**: 13/13 mutations killed
**Gate**: 220 passed, 0 failed

**What works**: fixed order and skipped positions, the draw, planning (15 s), mark/lock with exact errors, time outs for
picks vs bans, bots and leavers, integrity, per-team visibility, finalization (20 s or all Ready, not skipped by an
early Ready) to the match with each bot on its own pick, swaps and trades with 10 s expiry, host settings and 30 s
default, version 1.8 refusing 1.7, host reset, both-language page texts.

**Issues found**: none open.

**Next steps**: the owner decides on merge/release (release and cloud deploy are outward actions per `CLAUDE.md`).
