# LESSONS - auto-maintained by scripts/lessons.py

> Machine-owned. Do NOT hand-edit. Changes are overwritten on the next `lessons.py` write.
> Canonical state lives in `.specs/lessons.json`. Edit lessons only via the script.
> promote_threshold=2 distinct features · window_days=45 · quarantine_threshold=2

## Confirmed (load these at Specify/Design)

Corroborated across multiple features. Safe to apply as guidance.

_none_

## Candidates (under observation - do NOT load as guidance yet)

Seen once or not yet corroborated. Tracked, not trusted.

### L-001 - When tests shorten a spec-defined default time through a parameter or seam, also assert the default value itself in one test
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `launcher-tests` · harmful: 0
- features: lol-draft
- evidence: validation.md M8/M9 tools/Launcher/SeatDraft.cs:19 (launcher-tests)
- last seen: 2026-10-25T00:27:37Z

### L-002 - When an AC relies on existing behavior triggered by the change (such as a version bump gating joins), add a test that exercises that behavior, not only the changed constant
- signal: `ac_gap` · recurrence: 1 feature(s) · scope: `launcher-tests` · harmful: 0
- features: lol-draft
- evidence: DRAFT-14 AC3 tools/Launcher/Lobby.cs:288 (launcher-tests)
- last seen: 2026-10-25T00:27:37Z

### L-003 - Assert that a refused or no-op action leaves the visible state unchanged, not only that it returns no error
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `launcher-tests` · harmful: 0
- features: lol-draft
- evidence: validation.md M11 tools/Launcher/SeatDraft.cs:94 (launcher-tests)
- last seen: 2026-10-25T00:27:38Z

### L-004 - For a guard with several conditions, add a negative test where each condition alone is false
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `launcher-tests` · harmful: 0
- features: lol-draft
- evidence: validation.md round 2 S1 tools/Launcher/SeatDraft.cs:97 (launcher-tests)
- last seen: 2026-10-25T02:05:08Z

## Quarantined (failed when applied - ignore)

A confirmed lesson that recurred alongside failure. Kept for the maintainer to review.

_none_
