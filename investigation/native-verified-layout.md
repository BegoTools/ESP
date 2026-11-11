# Current exact-build layout evidence

This file supersedes the initial export-only investigation's unresolved-layout conclusion. Continuing with lightweight offline ARM64 inspection and read-only live reflection recovered the following build-specific data. It does not claim that arbitrary future builds or every game mode share this layout.

Target: PUBG Mobile Global `com.tencent.ig`, version `4.6.0`, versionCode `21525`, ARM64. Disk `libUE4.so` SHA-256: `abb9343f69735378b6aea4db97a66e9789664075b0196b1b99378514052454ce`.

## Verified traversal

| Field | Offset / rule | Evidence |
|---|---|---|
| GEngine storage | moduleBase+0xF621D90 | Named diagnostic code + GOT relocation; `live-engine-validation.json` |
| Engine contexts | pointer +0xC40, count +0xC48 | GameWorld helper code + live context type 1 |
| Context world | context+0x2A0 | Helper code + live World class/name |
| World.PersistentLevel | +0x30 | Named accessor runtime instructions + live reflection |
| World.GameInstance | +0x470 | Runtime accessor + live pointer equals engine+0xE08 |
| World.NetDriver | +0x38 | Parent's live route to local connection/controller |
| NetDriver.ServerConnection | +0x78 | Parent's live route |
| Connection.PlayerController | +0x30 | Parent's live route |
| Controller.AcknowledgedPawn | +0x528 | Live reflection owner PlayerController |
| Controller.PlayerCameraManager | +0x548 | Live reflection owner PlayerController |
| Actor.RootComponent | +0x208 | Live reflection owner Actor |
| SceneComponent.RelativeLocation | +0x1E4 | Live reflection + plausible local position |
| Pawn.Health / HealthMax | +0xE60 / +0xE64 | FloatProperty owner STExtraCharacter |
| Pawn.TeamID | +0x998 | IntProperty owner UAECharacter |
| Pawn.PlayerName | +0x960 | StrProperty owner UAECharacter |
| Controller.TeamID | +0x940 | IntProperty owner UAEPlayerController; distinct legitimate field |
| Camera.CameraCache | +0x520 | StructProperty owner PlayerCameraManager |
| CameraCache.POV | +0x10 | StructProperty owner CameraCacheEntry |
| POV.Location | +0 | MinimalViewInfo live reflection |
| POV.LocationLocalSpace | +0xC | MinimalViewInfo live reflection |
| POV.Rotation | +0x18 | Reflection + GetCameraRotation runtime code |
| POV.FOV | +0x24 | Reflection + runtime getter; effective positive locked override at camera+0x4CC |
| Rotation components | Pitch0, Yaw4, Roll8 | Parent's live Rotator reflection; fresh roll zero samples |

The parent saved the complete field inventory in `live-reflection-layout.json`. Property ownership is material: pawn.TeamID and controller.TeamID are different fields on different objects; unrelated same-name Health descriptors must not be interchanged.

## Level actor array, independently corroborated

Exact named function string `GetLevelActors` at RVA `0x418E9D0` has a native-registration pair at `0xDBCE540` pointing to exec `0x762925C`. The exec calls underlying `0x6B069CC`, which obtains a TArray pointer by calling getter `0xA98C454` and copies its pointer/count.

Runtime getter bytes, retained in `native-runtime-a98c000.bin` and `.asm.txt`, establish:

- Default return is `level+0xA0`.
- An optional provider path is selected only when level+0x440 is nonzero, level+0x438 is non-null, and provider virtual method +0x38 returns true. That path obtains the array from provider virtual method +0x68.
- Fresh current-state validation found provider flag `0`, provider pointer `0`, so the default +0xA0 route is active. A reader should fail explicitly or support the provider path if this state changes, rather than silently assume the default across all modes.
- Fresh bounded pointer-array validation found 285 actors, capacity316, with known local pawn `0x7beb6d60b000` at index52. Earlier named samples were WorldSettings, InstancedFoliageActor, ExponentialHeightFog, WaterSwimObject_C, and PhysicsVolume. Thus the array includes both environmental actors and the actual dynamic local pawn.
- The separate +0xB0 array also contained the local pawn (index9/count283), but stale entries were observed and the named getter uses +0xA0 in the current state.

Sources: `live-level-array-validation.json`, `native-level-local-pawn-membership.json`, `native-class-filter-validation.json`, and runtime disassembly.

## Names and player-family filter

Name state is moduleBase+0xF354BE0. The initialized pointer-chain formula is documented in `native-global-anchors.md`. Parent's `live-names-validation.json` observed state value115, five indirections, and name0=`None`; actual object/class names decoded correctly. A fresh independent validation again checked `None`.

UObject.ClassPrivate is +0x10; UObject.NameIndex +0x18; UClass.SuperStruct +0x30. Fresh bounded superclass traversal of the known local pawn decoded:

```text
BP_PlayerPawn_ST_C
BP_PlayerPawn_C
STExtraPlayerCharacter
STExtraBaseCharacter
STExtraCharacter
UAECharacter
Character
Pawn
Actor
Object
```

Source: `native-class-filter-validation.json`. This supports a bounded exact superclass-name filter for `STExtraPlayerCharacter` and derived classes, rather than treating every object with a plausible health float as a player. It does not establish the class hierarchy of every training target or non-player actor; such classes require their own evidence.

The evidence records contain capture-time addresses that are ephemeral. Production code must rediscover the process, module bias, world, object pointers, and initialized name state after transitions/restarts and enforce bounded counts, readable maps, valid name IDs, and cancellation.
