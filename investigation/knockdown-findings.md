# Knockdown display — 2026-11-06

Implemented: retain confirmed knocked characters, draw their snapline and KNOCKED label red, keep them in the nearby count, restore the configured line color on revival, and remove them on death or exhausted knockdown breath. Existing zero-health aim exclusion remains.

## Observed cause and field layout

- The old native reader rejected `health <= 0`. The host conversion and nearby counter also rejected it. This explained disappearance before drawing and required updating both executables.
- Existing recorded live reflection in `live-reflection-layout.json` identifies `STExtraCharacter.bDead` at +0xE7C with BoolProperty metadata `01 00 01 01` (packed bit mask 1), and `STExtraBaseCharacter.NearDeathBreath` at +0x1C40 as FloatProperty.
- Production now checks exact reflected name, owner, type, offset, and bool mask separately for each observed class before using the new fields. It caches bounded verification results and clears them on world changes. Failed verification leaves the fields unknown rather than guessing.
- `build-knockdown/live-snapshot.json` was captured from the current GameLoop 21518 training world using the updated helper and unchanged exact-build/hash gate. One local character and four ModelTarget actors each returned `IsDead=false`, `NearDeathBreath=100`. This confirms field discovery and transport in that scene, not an observed knockdown.

## Interpretation and transition policy

- Normal: valid positive health, with no authoritative death flag.
- Knocked: health exactly zero, `IsDead=false`, finite `0 < NearDeathBreath <= 100000`.
- Finished: `IsDead=true` takes precedence over stale positive health/breath; a zero-health actor with exhausted breath is also removed.
- Unknown: missing death/breath fields cannot preserve a zero-health actor. Legacy positive-health payloads continue working.
- Position interpolation uses the newer discrete health/death/breath values. It does not interpolate life states or resurrect rows absent from the latest frame.

## Verification and limits

- 251 C# self-test checks passed, including 23 new transport, state-transition, actual line/label/box pixel, interpolation, nearby-count, and aim regression checks (`src/KnockdownTests.cs`).
- Five Rust tests passed on the Android x86_64 guest, covering native retention, death precedence, missing/invalid values, and the production life-state read method using an ordinary temporary fixture file. Packed byte 2 decodes as not dead; byte 3 decodes as dead. No game memory was modified for tests.
- `build-knockdown/life-transition-check.json` and `alive.png`, `knocked.png`, `revived.png`, `finished.png` are explicitly synthetic render checks. Count remains 3 through knock/revival and becomes 2 on finish.
- No actual remote opponent was knocked or revived during validation. The zero-health/positive-breath interpretation is based on the reflected fields and is exercised with fixtures; an actual match transition remains to be checked. LDPlayer 21525 was not running for a new live test.
