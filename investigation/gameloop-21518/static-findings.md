# PUBG 4.6.0 build 21518: static comparison with inspected build 21525

Scope: read-only offline examination of the two local libraries; no ADB, game execution, memory mutation, or primary source/profile changes were performed in this analysis.

## Artifact identity

Both files are 252,290,584 bytes, ELF64 ARM64. ELF section and segment layouts are identical. Supported build21525 SHA-256 is `abb9343f69735378b6aea4db97a66e9789664075b0196b1b99378514052454ce`; GameLoop build21518 is `ad923890b4e12e389aba66253ef0259285b351b3742fbda7ae0d0716ea070484`.

35,966,900 bytes differ: 25,377 in `.rela.dyn`, 4,201,939 in `.rodata`, and 31,739,297 in `.text`. This establishes different artifacts, not different layouts for every class. Protected/undecodable code bytes account for some examined differences; no conclusion about their runtime semantics is drawn from unequal ciphertext.

REA portable native-export Evidence `ev_0077bbf3cfda0ac9ac28f69cf90921ece41f3d990b3cb5383315d94b8a3c33f6`, projected summary `ev_b9caa16e24a8979fd1e94f88aa7d4523087e136952ef192fff3c1fe588e296db`, reports arm64 and 77 exports with one export-table issue. Export coverage is partial and does not validate execution.

## Global anchors that remain supported

- GOT relocation at RVA `0xEE956C8` remains `R_AARCH64_RELATIVE` addend `0xF621D90`, establishing the same GEngine storage RVA. The GameWorld helper's relevant instructions at `0xCEC53EC` through its tail branch at `0xCEC5488` are identical and still load that GOT slot, then call the WorldContext accessor.
- WorldContext accessor at `0xA858440` is unchanged and reads engine context pointer `+0xC40`, count `+0xC48`, context byte type1, and world `+0x2A0`.
- FName state accessor at `0x8BE1BFC` has an identical 0x300-byte inspected region. It references state RVA `0xF354BE0` and retains the `(value-100)/3` initialized indirection formula.
- FName entry accessor at `0x8BE2ED8` has an identical 0xC0-byte inspected region and retains the 0x4000-entry chunk split.

These are static observations; current pointer values, name sentinel, object lifetimes, and live maps must still be validated by the host.

## Reflected member parameters

All **566 selected property-name-linked 96-byte static parameter records** are byte-identical at the same descriptor RVAs. The selected names are a bounded set in `reflection_static_compare.py`, not the full Unreal property universe. The 17 targeted numeric records in `static-layout-summary.json` include:

| Field association carried from the old verified layout | Offset | Static descriptor RVA |
|---|---:|---:|
| PlayerController.AcknowledgedPawn | 0x528 | 0xE8C6FB8 |
| PlayerController.PlayerCameraManager | 0x548 | 0xE8C6EF8 |
| Actor.RootComponent | 0x208 | 0xE75CC78 |
| SceneComponent.RelativeLocation | 0x1E4 | 0xE8F26C8 |
| STExtraCharacter.Health | 0xE60 | 0xDBB6180 |
| STExtraCharacter.HealthMax | 0xE64 | 0xDBB6148 |
| UAECharacter.TeamID | 0x998 | 0xDF89B88 |
| UAECharacter.PlayerName | 0x960 | 0xDF89C68 |
| PlayerCameraManager.CameraCache | 0x520 | 0xE8BEA60 |
| MinimalViewInfo.FOV | 0x24 | 0xE797CE0 |
| CapsuleComponent.CapsuleHalfHeight | 0x9F8 | 0xE79B338 |
| Vehicle common HPMax | 0x350 | 0xDD52688 |
| Pickup DefineID | 0x5F8 | 0xDA1A4C0 |
| World.Levels | 0x440 | 0xE935A30 |
| World.NetDriver | 0x38 | 0xE936100 |
| NetDriver.ServerConnection | 0x78 | 0xE88BD78 |
| Connection.PlayerController | 0x30 | 0xE8BC308 |

The old runtime evidence supplies field ownership; this differential observation corroborates preserved declarations rather than proving current live class/object identity. Same-name unrelated fields exist, so records are identified by both exact descriptor RVA and field name.

Two Boolean properties have additional executable static evidence:

- UAECharacter bIsAI descriptor `0xDF897E8` has callback relocation `+0x38 -> 0x7F63250`. Both builds contain identical `mov w8,#1; strb w8,[x0,#0xA40]; ret`, corroborating byte `+0xA40`.
- Actor bHidden descriptor `0xE75DCD0` has callback relocation `+0x38 -> 0xAF08F14`. Both builds contain identical `ldrh w8,[x0,#0xE8]; orr w8,w8,#1; strh w8,[x0,#0xE8]; ret`, corroborating `+0xE8`, mask1.

## Remaining runtime checks

No required offset change was identified in these inspected anchors and member declarations. This supports trying the existing reader under a **new exact hash/build profile** after authorized memory access exists; it does not justify removing the hash or version checks.

The Level actor getter `0xA98C454` remains undecodable from each disk artifact, with different bytes. World.PersistentLevel, direct/alternate Level actor provider behavior, root cached world translation `+0x220`, camera data, capsule scale, vehicle fuel/HP current values, and pickup count/active state still need current runtime corroboration. Existing native bounds, names sentinel, class filters, provider rejection, and camera validation must stay active.

## Reproduction

Run `python investigation/gameloop-21518/static_compare.py`, `anchor_compare.py`, `reflection_static_compare.py`, and `static_summary.py` in that directory order. Scripts use the already-present pyelftools/Capstone under `investigation/python-libs` and existing NumPy. Results are `static-comparison.json`, `anchor-comparison.json`, `static-reflection-params.json`, and `static-layout-summary.json`. All are ordinary offline Python/Capstone observations except the explicitly identified REA export Evidence above.
