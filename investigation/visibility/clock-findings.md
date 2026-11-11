# Exact-build world clock accessor evidence

Artifact: `investigation/gameloop-21518/libUE4.so`, SHA-256 `ad923890b4e12e389aba66253ef0259285b351b3742fbda7ae0d0716ea070484` (PUBG Global 4.6.0 / 21518). This is static ARM64 evidence, not an observation that an accessor executed.

## Verified float locations on the resolved UWorld

| Registered function | Native exec | Implementation | Final float load |
|---|---|---|---|
| GetTimeSeconds | 0xAFB3B40 | 0xA88377C | `0xA8837A4: ldr s0, [x0, #0xCC8]` |
| GetGameTimeInSeconds | 0xB022608 | 0xA979700 | `0xA979728: ldr s0, [x0, #0xCC8]` |
| GetRealTimeSeconds | 0xAFB3960 | 0xA8837FC | `0xA883824: ldr s0, [x0, #0xCD0]` |
| GetWorldDeltaSeconds | 0xAFB3C38 | 0xA88373C | `0xA883764: ldr s0, [x0, #0xCD8]` |

`ldr s0` and the exec's `str s0, [result]` establish a 32-bit floating-point result. The independent GetGameTimeInSeconds implementation corroborates the game-time offset. The functions return float zero when world resolution fails; zero is not proof of a valid active world.

GetTimeSeconds native registration: string RVA `0x410E81B`; registration name pointer at `0xE7E1408`, paired exec pointer at `0xE7E1410`. The exec calls `0xA88377C` at `0xAFB3BA4`. Some other same-string references are reflection metadata constructors and must not be mistaken for callable execs.

## World resolution

These implementations obtain GEngine through GOT slot `module+0xEE956C8`, whose RELATIVE relocation addend is `0xF621D90` (GEngine storage). They move the supplied world-context object into x1, pass error mode w2=1, then call `0xAE57D04`. That helper obtains the object's vtable method `+0x168` (GetWorld) and returns its world pointer. The final `ldr s0` is relative to that returned world, not relative to the context object, local pawn, camera or mesh.

For the external reader, use the already-verified active game-context world pointer and read its `+0xCC8` game clock. Do not add `+0xCC8` to a pawn or mesh.

## Limits

This establishes clock fields for this exact artifact only. It does not establish which clock the renderer uses for LastRenderTimeOnScreen, timestamp threshold semantics, or whether recent rendering proves wall visibility. Runtime calibration remains required for those claims. No injection, runtime calls or game input were performed.

Raw retained records: `clock-registration.json`, `clock-accessors.json`, `clock-world-resolution.json`; scratch reproduction scripts share those basenames.
