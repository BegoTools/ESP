# Alternative visibility source investigation

Exact artifact: GameLoop PUBG Global 4.6.0 / 21518, `libUE4.so` SHA-256 `ad923890b4e12e389aba66253ef0259285b351b3742fbda7ae0d0716ea070484`.

## Calibrated rejection of renderer timestamps

The user-labelled fixed-camera scene contains one fully wall-occluded training target and three visible targets. In `fully-occluded.jsonl`, all 60 frames / 240 target samples have `WorldTime - Mesh.LastRenderTimeOnScreen` between 0 and 0.0087 seconds, and all `Rendered13a0` samples equal 1. Therefore neither a recent `+0x48c` timestamp nor this rendered flag distinguishes the labelled occluded target. Aggregate details: `alternative-calibration-summary.json`.

## Concrete alternatives inspected

- Pawn `AutoAimComp+0x4800` and `BP_AutoAimingComponent+0x7520` refer to the same live BP_AutoAimingComponent_C / WeaponAutoAimingComponent instance.
- Its reflected SelectEnemyInfo+0x1a0, LastSelectInfo+0x1d8 and LastAimSuccess+0x218 exist, but captured selected records are zero. A selected target alone would not classify every other actor.
- Native GetNearestAutoAimEnemy exec `0x784acf0` calls `0x5c81228`. This body builds a transient selector context, copies embedded component config at +0x350, collects candidates via `0x5c81660`, then calls selector `0x5c81a18`, which in turn calls `0x5c84aa4` and `0x5c84ce8`.
- This code copies a 40-byte-element array to component+0x5f0/count+0x5f8. Its elements release three heap pointers in their destructor; no per-actor LOS boolean mapping was established. Captured count is zero.
- The +0x608/count+0x610 array is a candidate-registration array of 16-byte weak-object pairs maintained by RegisterAutoAimCandidate, not a demonstrated visibility list. Membership must not be interpreted as visibility.
- CanEnemeyRaycastReach native exec `0x784a770` dispatches through component vtable+0x4e8 (`0x784a8e8: ldr x8,[x8,#0x4e8]`; `0x784a8ec: blr x8`). The captured implementation is `0x5c82d48`, exactly `mov w0,wzr; ret`. It has no memory reads, writes or cache. Blueprint override / alternative dispatch semantics remain unverified; its default false result is not evidence of wall occlusion.

## Disqualified names

Both plain `bCanSee` descriptors are parameters of vehicle-visibility notifications, not per-enemy fields: one belongs to UpdateCanPlayerSeeLastVehicle; the other to its delegate. SightVisionMask names night-vision equipment/environment modes. EnemyInfoList near SensedNearestEnemyVisibleRadius belongs to AI sensing/behavior data; local-player-camera ownership was not established. AutoAimInfo in a different component type is a configuration structure (AimClass, AimOffset, distance, screen distance, lock delay, socket name, dot value), not a visibility result.

## Conclusion / boundary

No verified always-available per-actor local-camera LOS/occlusion result was found in the reflected fields and concrete native paths above. Timestamp substitution is falsified by the calibrated scene. The examined native raycast default does not provide the missing state, and observed built-in aim caches are empty or insufficient to classify all targets.

This is a bounded capability gap in the current read-only reader, not proof that no implementation is possible by other means. Correctness would require a separately verified per-view occlusion result or collision query implementation, with runtime labelled full/partial exposure validation. No injection, game input or production reader wiring was performed in this alternative investigation.

Raw records: alternative-registration.json, alternative-owner-context.json, alternative-autoaim-info.json, alternative-runtime-execs.json, alternative-runtime-bodies.json, alternative-raycast-runtime.json.
