# LoL-style Lobby Draft Specification

## Problem Statement

The lobby draft today works per team: a team's turn bans or picks several cars at once in a host-written order (`A1 B2…`), the screen sits inside the lobby page and players find it odd and unresponsive. The owner wants a draft like League of Legends: each player has a drawn position, bans or picks their own car, sees the two teams side by side in a full-window screen with car pictures, can trade positions and cars with teammates, and picks a skin with a large preview. The team will give feedback on this version afterwards.

## Goals

- [ ] Each player bans or picks for their own position, in one fixed order, with the starting team and the positions drawn at random.
- [ ] Players can swap positions and trade cars with teammates, with the other player's consent.
- [ ] The draft takes the whole launcher window, shows car pictures, and shows the player's own car large with its skins once locked.

## Out of Scope

| Feature | Reason |
| ------- | ------ |
| Host-configurable ban/pick orders | The order is fixed by the owner (1 ban per team, picks 1-2-2-1-1-1); presets can come after the team's feedback |
| Simultaneous blind bans | Not in the agreed design |
| Draft on the 2017 builds | Those builds pick cars in the game's own screen (unchanged) |
| Changes to the mod, patcher or game | The draft is launcher-only |
| Deploying the cloud image or releasing 1.8 | Outward actions; the owner asks for them separately |

---

## Assumptions & Open Questions

| Assumption / decision | Chosen default | Rationale | Confirmed? |
| --------------------- | -------------- | --------- | ---------- |
| Notation b/r in "1b-2r-2b-1r-1b-1r" | b = Team 1 (the team drawn to start), r = Team 2 | Owner: "O time que começa é sorteado; b e r são só o Time 1 e o Time 2" | y |
| Who bans | Only position 1 of each team, 1 ban per team, Team 1 first | Owner: "vai ter somente 1 ban… só o 1 a escolher bane" | y |
| Position order inside a team | Human players in random order first, bots after them | Bots should not hold the early bans/picks humans care about | n |
| Planning stage | 15 s before the first turn; players may mark a car but not lock | From the approved design | y |
| Marking outside one's turn (planning included) | Stored as the player's intent, shown to their team, and used as their mark when their pick turn starts; never used for a ban | A planning mark must not turn into a ban of the car the player wants | n |
| Finalization stage | 20 s after the last pick, or until every human player is ready; then the match starts | From the approved design ("Prontos" then the match) | y |
| No turn time limit (host sets 0) | Turns wait for every acting player; finalization waits until every player is ready | Keeps the existing "no limit" option meaningful | n |
| Default turn time | 30 s (was 60 s) | Owner agreed to the proposed default when approving the design | y |
| Time out on a pick | The marked car if still free, else a random free car | From the approved design | y |
| Time out on a ban | The marked car if any, else no ban | From the approved design (as in LoL) | y |
| A bot's action | Bans nothing; picks a random free car at once | From the approved design | y |
| A player who leaves during the draft | Their position acts like a bot from then on | From the approved design | y |
| Swap request window | Allowed while neither player has a picked car and neither is acting in the current turn; expires after 10 s | From the approved design | y |
| Car trade window | Allowed once both have a picked car, until the match starts; expires after 10 s; trading with a bot of the team needs no consent | From the approved design | y |
| Team sizes below 4 | Positions beyond a team's players + bots do not exist; their actions are skipped | A 2v2 lobby must still draft | n |
| Lobby protocol | New draft JSON and actions; `VERSION` becomes 1.8 | CLAUDE.md: protocol changes need a version bump | y |

**Open questions:** none - all resolved or logged above.

---

## User Stories

### P1: Draft by position ⭐ MVP

**User Story**: As a player in a drafting lobby, I want my own drawn position with my own ban or pick so that the draft feels like LoL.

**Why P1**: It is the core of the new draft.

**Acceptance Criteria**:

1. WHEN the host starts a lobby with the draft on THEN the system SHALL draw Team 1 at random among blue and red and give every player of each team a position from 1 to 4, humans before bots.
2. The system SHALL run the turns in this order: ban Team 1 position 1; ban Team 2 position 1; pick Team 1 position 1; pick Team 2 positions 1 and 2; pick Team 1 positions 2 and 3; pick Team 2 position 3; pick Team 1 position 4; pick Team 2 position 4.
3. IF a turn names a position a team does not have THEN the system SHALL skip that position's action, and skip the turn when no position is left in it.
4. WHILE the draft is in its planning stage (first 15 s) the system SHALL accept a player's marked car and SHALL reject locking with "The draft starts in a moment."
5. WHEN a player whose position acts in the current turn marks a free car THEN the system SHALL store it as that position's marked car, replacing any earlier mark.
6. IF a player whose position does not act in the current turn tries to lock THEN the system SHALL reject it with "It's not your turn."
7. IF a player marks a car that is banned or picked THEN the system SHALL reject it with "That car is already banned or picked."
8. IF an acting player locks without a marked car THEN the system SHALL reject it with "Choose a car first."
9. WHEN an acting player locks a marked car THEN the system SHALL record it as that position's ban or pick, and SHALL move to the next turn once every acting position of the turn has locked.
10. WHEN a turn's time runs out THEN the system SHALL lock each unlocked pick with its marked car if free, else a random free car, and each unlocked ban with its marked car, else no ban.
11. WHEN a turn reaches a bot's position THEN the system SHALL ban nothing for it and pick a random free car for it at once.
12. IF an acting player leaves the lobby during the draft THEN the system SHALL treat that position as a bot.
13. The system SHALL never record a car twice among bans and picks, and every position SHALL end the draft with exactly one picked car.

**Independent Test**: Start a 4v4 drafting lobby in a test, lock every turn, and see each position end with its own car in the fixed order.

---

### P1: What each side sees ⭐ MVP

**User Story**: As a player, I want to see my teammates' marked cars but not the other team's so that the draft has no leaks.

**Why P1**: Without it the other team sees choices before they are locked.

**Acceptance Criteria**:

1. WHEN a member's lobby state is built THEN the system SHALL include the marked car of each position of the member's own team and SHALL leave it empty for the other team's positions.
2. The system SHALL show every member (spectators included) the locked bans and picks of both teams, the turn order, the current turn and the seconds left.

**Independent Test**: Mark a car as a blue player and read the state as a red player and as a blue player.

---

### P1: Finalization and the match ⭐ MVP

**User Story**: As a player, I want the match to start with the car I drafted after a short finalization so that I don't have to set anything up again.

**Why P1**: The draft must lead to the match.

**Acceptance Criteria**:

1. WHEN the last pick is locked THEN the system SHALL enter the finalization stage and set every human player's car to their position's pick.
2. WHEN the finalization stage has lasted 20 s (turn time above 0) or every human player is ready THEN the system SHALL start the match.
3. WHEN the match starts after a draft THEN the system SHALL give each bot the car picked for its position.
4. WHILE a draft exists the system SHALL ignore a player's request to change car through the lobby update and SHALL keep accepting skin changes.

**Independent Test**: Finish a draft in a test, mark everyone ready, and see the server started with the drafted bot cars.

---

### P2: Swap positions

**User Story**: As a player, I want to ask a teammate to swap positions so that we can choose who picks first.

**Why P2**: Valuable but the draft works without it.

**Acceptance Criteria**:

1. WHEN a player asks a teammate to swap positions THEN the system SHALL store a request visible to their team that expires after 10 s.
2. WHEN the teammate accepts THEN the system SHALL exchange the two players' positions.
3. IF either player already has a picked car or acts in the current turn THEN the system SHALL reject the request or the answer with "You can't swap positions now."
4. WHEN the teammate declines, or 10 s pass, THEN the system SHALL drop the request.
5. IF the target is not a human teammate THEN the system SHALL reject the request with "Pick a teammate."

**Independent Test**: Ask and accept a swap in planning, and see the positions exchanged.

---

### P2: Trade cars

**User Story**: As a player, I want to trade my picked car with a teammate so that we can fix the comp after picking.

**Why P2**: Valuable but the draft works without it.

**Acceptance Criteria**:

1. WHEN a player asks a teammate with a picked car to trade, and the player has a picked car, THEN the system SHALL store a request visible to their team that expires after 10 s.
2. WHEN the teammate accepts THEN the system SHALL exchange the two positions' picks and the two players' cars.
3. WHEN a player trades with a bot of their own team THEN the system SHALL exchange the cars at once without a request.
4. IF either side has no picked car THEN the system SHALL reject the trade with "Both of you need a car first."

**Independent Test**: Trade two picked cars in finalization and see each player's car swapped.

---

### P1: Full-window draft screen ⭐ MVP

**User Story**: As a player, I want the draft to fill the launcher window with car pictures and my car shown large so that picking is clear and quick.

**Why P1**: The owner's main complaint is the current screen.

**Acceptance Criteria**:

1. WHILE the lobby is drafting the launcher page SHALL show the draft screen in place of the lobby view, with Team 1 and Team 2 at the sides and the cars in the middle.
2. The draft screen SHALL show each car as a square with its picture and its name below, banned cars greyed and crossed, picked cars in the picking team's color.
3. WHEN the player's own pick is locked THEN the draft screen SHALL show that car large in the center with a row of its skins, and clicking a skin SHALL save it as the player's skin for that car.
4. WHEN a player clicks a car during their turn THEN the page SHALL mark it at once, before the lobby answers.
5. Every new draft text SHALL exist in English and in Brazilian Portuguese.

**Independent Test**: Start a local draft and look at the launcher page in both languages.

---

### P1: Host settings and version ⭐ MVP

**User Story**: As a host, I want the draft settings to match the new draft so that there is nothing confusing to set.

**Why P1**: The old order fields no longer mean anything.

**Acceptance Criteria**:

1. The host's draft settings SHALL offer only draft on/off and the turn time, and the lobby SHALL ignore saved ban/pick order values.
2. The default turn time SHALL be 30 s.
3. The launcher version SHALL be 1.8, so a 1.8 lobby SHALL refuse players of 1.7.

**Independent Test**: Read the host setup JSON and join with an old version string.

---

## Edge Cases

- IF a draft action names an unknown action THEN the system SHALL reject it with "Unknown draft action."
- IF a non-player (spectator or the host not playing) sends a draft action THEN the system SHALL reject it with "Only players draft."
- IF a player locks twice in one turn THEN the system SHALL reject the second with "You already locked in."
- WHEN a team has only bots THEN the system SHALL complete all its actions without waiting.
- WHEN the host resets the draft THEN the system SHALL drop the draft and its requests and return to the lobby.

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| -------------- | ----- | ----- | ------ |
| DRAFT-01 | P1: Draft by position (AC1 draw) | Execute | Implementing |
| DRAFT-02 | P1: Draft by position (AC2 order) | Execute | Implementing |
| DRAFT-03 | P1: Draft by position (AC3 missing positions) | Execute | Implementing |
| DRAFT-04 | P1: Draft by position (AC4 planning) | Execute | Implementing |
| DRAFT-05 | P1: Draft by position (AC5-9 mark and lock) | Execute | Implementing |
| DRAFT-06 | P1: Draft by position (AC10 time out) | Execute | Implementing |
| DRAFT-07 | P1: Draft by position (AC11-12 bots and leavers) | Execute | Implementing |
| DRAFT-08 | P1: Draft by position (AC13 integrity) | Execute | Implementing |
| DRAFT-09 | P1: What each side sees | Execute | Implementing |
| DRAFT-10 | P1: Finalization and the match | Execute | Implementing |
| DRAFT-11 | P2: Swap positions | Execute | Implementing |
| DRAFT-12 | P2: Trade cars | Execute | Implementing |
| DRAFT-13 | P1: Full-window draft screen | Execute | Implementing |
| DRAFT-14 | P1: Host settings and version | Execute | Implementing |

**ID format:** `DRAFT-NN`

**Status values:** Pending → In Design → In Tasks → Implementing → Verified

**Coverage:** 14 total, 14 mapped to tasks, 0 unmapped

---

## Success Criteria

- [ ] A 4v4 draft finishes in 8 turns with every player driving the car of their position.
- [ ] `dotnet test tools/Launcher.Tests` passes with the new draft tests and the 170 existing ones.
