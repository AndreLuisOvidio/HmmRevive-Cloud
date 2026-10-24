# LoL-style Lobby Draft Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user - do not proceed without it.**

---

**Design**: `.specs/features/lol-draft/design.md`
**Status**: In Progress

---

## Test Coverage Matrix

> Generated from codebase, project guidelines, and spec. Guidelines found: `CLAUDE.md` ("Verification = `dotnet test tools/Launcher.Tests` for launcher logic (write the test first for new lobby/draft/cloud behavior)"; the page has no test infrastructure). Style sampled from `tools/Launcher.Tests/HostRoutesTests.cs`, `LobbyPasswordTests.cs`, `CloudHostTests.cs` (xUnit, net48, lobby built with `BuildReady`/`StartServerHook` seams, `Lobby.Step(now)` for time).

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| ---------- | ------------------ | -------------------- | ---------------- | ----------- |
| Draft engine (`SeatDraft.cs`) | unit | All branches; 1:1 to spec ACs DRAFT-01..12; every listed edge case | `tools/Launcher.Tests/DraftTests.cs` | `dotnet test tools/Launcher.Tests --filter FullyQualifiedName~DraftTests` |
| Lobby draft wiring + `/lobby/draft` route (`Lobby.cs`) | integration | Every draft action through `Lobby.Handle`: happy + error paths; finalization to match; bot cars; settings | `tools/Launcher.Tests/LobbyDraftTests.cs` | `dotnet test tools/Launcher.Tests --filter FullyQualifiedName~LobbyDraftTests` |
| Client pass-through (`Session.cs`, `Program.cs`) | none | build gate only (field forwarding, no logic) | - | build gate |
| Page (`web/*`) | none | manual UAT (no JS test infrastructure in the repo) | - | build gate + UAT |
| Version / docs | none | build gate only | - | build gate |

## Gate Check Commands

| Gate Level | When to Use | Command |
| ---------- | ----------- | ------- |
| Quick | After tasks with unit tests only | `dotnet test tools/Launcher.Tests --filter FullyQualifiedName~DraftTests` |
| Full | After tasks with integration tests | `dotnet test tools/Launcher.Tests` |
| Build | After phase completion or no-test tasks | `dotnet build tools/Launcher -c Release && dotnet test tools/Launcher.Tests` |

---

## Execution Plan

### Phase 1: Draft engine

```
T1 → T2 → T3
```

### Phase 2: Lobby and client

```
T4 → T5
```

### Phase 3: Page and release notes

```
T6 → T7 → T8
```

---

## Task Breakdown

### T1: Seat-based draft core

**What**: New `SeatDraft` class (replaces `Draft` in T4) with seats + fixed order + stages (plan, turns, final, done) with mark, lock, bots, gone players, timeouts and integrity.
**Where**: `tools/Launcher/SeatDraft.cs`
**Depends on**: None
**Reuses**: current `Draft` (turn list, `Available`)
**Requirement**: DRAFT-01, DRAFT-02, DRAFT-03, DRAFT-04, DRAFT-05, DRAFT-06, DRAFT-07, DRAFT-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] `DraftTests` cover the order, skipped positions, planning lock refusal, mark/lock errors, turn advance, timeouts, bots, gone players, integrity, final stage end
- [x] Gate check passes: `dotnet test tools/Launcher.Tests --filter FullyQualifiedName~DraftTests`

**Tests**: unit
**Gate**: quick

---

### T2: Swap and trade requests

**What**: Add position swap and car trade requests with 10 s expiry, answers, and the bot trade without consent.
**Where**: `tools/Launcher/SeatDraft.cs`
**Depends on**: T1
**Reuses**: T1 seats
**Requirement**: DRAFT-11, DRAFT-12

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] `DraftTests` cover every AC of both P2 stories
- [ ] Gate check passes: `dotnet test tools/Launcher.Tests --filter FullyQualifiedName~DraftTests`

**Tests**: unit
**Gate**: quick

---

### T3: Draft state per viewer team

**What**: `SeatDraft.Json(viewerTeam, now)` with seats, turns, bans, picks, seconds left, marks only for the viewer's team and the team's requests.
**Where**: `tools/Launcher/SeatDraft.cs`
**Depends on**: T2
**Reuses**: current `Draft.Json`
**Requirement**: DRAFT-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] `DraftTests` show marks to the own team only and locked choices to everyone
- [ ] Gate check passes: `dotnet build tools/Launcher -c Release && dotnet test tools/Launcher.Tests`

**Tests**: unit
**Gate**: build

---

### T4: Lobby draft wiring

**What**: Lobby starts the seat draft, routes `/lobby/draft` actions (mark, lock, swap, trade, accept, decline), follows stages in `Step`, deletes the old `Draft.cs`, sets players' and bots' cars, starts the match after finalization, ignores car changes while drafting, drops the order settings and defaults the turn time to 30 s.
**Where**: `tools/Launcher/Lobby.cs`
**Depends on**: None (runs after Phase 1)
**Reuses**: `Step(now)`, `BeginMatch`, `BotCars`, test seams from `HostRoutesTests`
**Requirement**: DRAFT-01, DRAFT-07, DRAFT-10, DRAFT-14

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] `LobbyDraftTests` drive a full draft through `Lobby.Handle` and `Step`, including errors, finalization to match and bot cars
- [ ] Gate check passes: `dotnet test tools/Launcher.Tests`

**Tests**: integration
**Gate**: full

---

### T5: Client pass-through

**What**: `Session.DraftAct` and `/api/lobby/draft` forward `target` and `request`.
**Where**: `tools/Launcher/Session.cs`
**Depends on**: T4
**Reuses**: existing `DraftAct`
**Requirement**: DRAFT-11, DRAFT-12

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Both files forward the two fields
- [ ] Gate check passes: `dotnet build tools/Launcher -c Release && dotnet test tools/Launcher.Tests`

**Tests**: none
**Gate**: build

---

### T6: Full-window draft view

**What**: Replace the in-lobby draft panel with a full-window view: team columns with position cards, car squares with pictures and names, turn chips, clock, requests, the big car with skins, ready button; host settings keep only draft on/off and turn time.
**Where**: `tools/Launcher/web/app.js`
**Depends on**: None (runs after Phase 2)
**Reuses**: `paint`, `act`, `skinsOf`, `S.images`, `/img/skins`
**Requirement**: DRAFT-13, DRAFT-14

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] `index.html`, `app.js`, `style.css` updated; no references to the order fields left
- [ ] Gate check passes: `dotnet build tools/Launcher -c Release && dotnet test tools/Launcher.Tests`

**Tests**: none
**Gate**: build

---

### T7: Draft texts in English and Portuguese

**What**: Every new draft string in `i18n.js` in English and pt-BR; old order strings removed.
**Where**: `tools/Launcher/web/i18n.js`
**Depends on**: T6
**Reuses**: existing i18n keys
**Requirement**: DRAFT-13

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Every `t("...")` key used by the draft view exists in both languages
- [ ] Gate check passes: `dotnet build tools/Launcher -c Release && dotnet test tools/Launcher.Tests`

**Tests**: none
**Gate**: build

---

### T8: Version 1.8 and release notes

**What**: `VERSION` 1.8, `CHANGELOG.md` entry, draft paragraph in `friend-kit/README.txt` and `LEIA-ME.txt`.
**Where**: `VERSION`
**Depends on**: T7
**Reuses**: changelog format
**Requirement**: DRAFT-14

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Version, changelog and both kit readmes updated
- [ ] Gate check passes: `dotnet build tools/Launcher -c Release && dotnet test tools/Launcher.Tests`

**Tests**: none
**Gate**: build

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3

Phase 1:  T1 ------→ T2 ------→ T3
Phase 2:  T4 ------→ T5
Phase 3:  T6 ------→ T7 ------→ T8
```

## Task Granularity Check

| Task | Scope | Status |
| ---- | ----- | ------ |
| T1 | Draft core (one class) | ✅ Granular (one file, one tight chain) |
| T2 | Requests (one feature of the class) | ✅ Granular |
| T3 | One method | ✅ Granular |
| T4 | Lobby draft region | ⚠️ Cohesive: one file, one flow |
| T5 | Two forwarding lines (`Session.cs`, `Program.cs`) | ⚠️ Cohesive pass-through |
| T6 | Draft view (`app.js` + its markup and styles) | ⚠️ Cohesive: one view |
| T7 | One file | ✅ Granular |
| T8 | Version + notes | ⚠️ Cohesive release notes |

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| ---- | ---------------------- | ------------- | ------ |
| T1 | None | start of Phase 1 | ✅ Match |
| T2 | T1 | T1 → T2 | ✅ Match |
| T3 | T2 | T2 → T3 | ✅ Match |
| T4 | T3 | Phase 1 → T4 | ✅ Match |
| T5 | T4 | T4 → T5 | ✅ Match |
| T6 | T5 | Phase 2 → T6 | ✅ Match |
| T7 | T6 | T6 → T7 | ✅ Match |
| T8 | T7 | T7 → T8 | ✅ Match |

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| ---- | --------------------------- | --------------- | --------- | ------ |
| T1 | Draft engine | unit | unit | ✅ OK |
| T2 | Draft engine | unit | unit | ✅ OK |
| T3 | Draft engine | unit | unit | ✅ OK |
| T4 | Lobby draft wiring | integration | integration | ✅ OK |
| T5 | Client pass-through | none | none | ✅ OK |
| T6 | Page | none | none | ✅ OK |
| T7 | Page | none | none | ✅ OK |
| T8 | Version / docs | none | none | ✅ OK |
