# Verified loot layout for the inspected PUBG Global build

Target: Global `com.tencent.ig`, version 4.6.0 / code 21525, ARM64 library identity already established by the parent investigation. Current Android process PID 3418 and module base `0x400023200000` were rediscovered before probing; both are ephemeral.

## Result

The current training scene contains **71 loaded pickup objects across 34 distinct classes**. These are hidden pooled objects at the time sampled; they must not be represented as visible ground loot. Their current class inheritance, exact reflected field metadata, ItemID, quantity, flags, and local positions are retained in `loot-verified-catalog.json`. Five representative classes also have full inherited reflection inventories and raw field samples in `loot-live-reflection.json`.

All selected weapon, ammo, healing, helmet, and token actors inherit the exact base class `PickUpWrapperActor`. A bounded superclass check is a stronger category test than only matching `_Wrapper` or `_Pickup` in a class name.

## Field layout

| Field | Address relative to pickup actor | Type / interpretation |
|---|---:|---|
| RootComponent | `+0x208` | pointer to SceneComponent |
| RelativeLocation | `root + 0x1E4` | three float32 local coordinates; all sampled pooled pickup roots currently have (0,0,0); active placement still needs verification |
| bHidden | `+0xE8` | Actor BoolProperty byte mask `0x01`; skip hidden pooled actors |
| DefineID | `+0x5F8` | inline reflected `ItemDefineID` struct |
| DefineID.Type | `+0x5F8` | int32 |
| DefineID.TypeSpecificID | `+0x5FC` | int32 item identifier |
| Count | `+0x610` | int32 quantity |
| bCanBePickUp | `+0x614` | BoolProperty byte, field mask `0xFF` |
| bHasBeenPickedUp | `+0x615` | BoolProperty byte, field mask `0xFF` |
| ItemValue | `+0x6A0` | FString; empty in all five representative samples |
| ItemCategory | `+0x6B0` | FString; empty in all five representative samples |
| ItemId | `+0x758` | reflected int32 exists but is zero in all five samples; **not the observed identifier source** |

Reflection metadata for `ItemDefineID` independently reports Type at 0, TypeSpecificID at 4, bValidItem at 8, bValidInstance at 9, and InstanceID uint64 at `0x10`. The observed bValidItem/bValidInstance bytes are zero on genuine ground loot, so they must not be required by this decoder.

All 71 sampled pickup actors had bCanBePickUp=1 and bHasBeenPickedUp=0. These two flags alone therefore do not establish that an item is active/visible. The five representative actors have bHidden=1 (raw Actor byte 0x61, bit mask 1). Their root AttachParent at +0x188 is null; root local location and cached transform translations are zero; child meshes only have tiny local offsets. ObjectPoolEnabled at +0x72C and UnLimitObjectPoolEnabled at +0x72D are commonly enabled. This supports a hidden object pool explanation, not an invented attachment/world-transform offset. Skip hidden/picked actors and invalid quantity/ranges. An active visible pickup sample is required to validate placement.

## Representative observed data

| Class | Type | TypeSpecificID | Count |
|---|---:|---:|---:|
| BP_Rifle_M762_Wrapper_C | 1 | 101008 | 1 |
| BP_Rifle_M416_Wrapper_C | 1 | 101004 | 1 |
| BP_Ammo_556mm_Pickup_C | 3 | 303001 | 30 |
| BP_Ammo_12Guage_Pickup_C | 3 | 304001 | 10 |
| Drink_Pickup_C | 6 | 601001 | 1 |
| Bandage_Pickup_C | 6 | 601004 | 1 |
| PickUp_BP_Helmet_Lv1_C | 5 | 502001 | 1 |
| BP_MZJ_QX_Pickup_C | 2 | 203002 | 1 |
| GoldenTokenWrapper_C | 6 | 3000324 | 1 |

The complete observed mapping is in the catalog; no external ItemID list was imported. Category labels can be inferred from observed class words (Weapon, Ammo, Healing, Armor, Equipment, Optic, Other), but those are application presentation rules, not observed localized display strings. In particular Type 6 includes drink, bandage, smoke grenade, and golden token; it is not equivalent to healing.

## User drop correlation and remaining active-state question

After the user dropped ammo, the known Ammo762 wrapper `0x7beaa75d9a60` changed its object name to `BP_Ammo_762mm_Pickup_C_Recycled` and acquired a nonzero root position near the player. A second user drop moved the same object again to `(32985.6, 439483.2, 485.1)`, while the player was about 4.6 metres away. The root AttachParent was null. This corroborates that Root.RelativeLocation responds to drop placement in this unattached case.

However, both probes still read bHidden=1, Count=30, ItemID=302001, canPick=1, picked=0. The user described dropping one round. These observations do **not** validate quantity semantics or prove that the recycled wrapper is the currently visible physical pickup. A cached nonzero position must not be used as an active-state test. See `loot-active-visible-validation.json` and `loot-second-drop-validation.json` for exact readings.

The world has three loaded Levels, not only its PersistentLevel: `SingleTraining_Main`, `shooting01`, and `shooting_sublevel01`, totalling 977 actors at the scoped probe. Their addresses/array counts are retained in `loot-world-levels.json`. World.Levels is the reflected TArray at +0x440. The native inventory used for the original 71-object catalog covered only SingleTraining_Main, so that catalog does not establish absence of active pickup actors in the other two levels.

Pawn.PickupManagerComponent at +0x2620 points to a PickupManagerComponent. Its reflected weak-object PickupAvailableActorPool at +0x378 contained 394 entries, while ConfirmedPickupActorPool at +0x388 had count zero after the user stepped away. Full metadata and headers are in `loot-manager-reflection.json` and `loot-manager-pools.json`. These are scoped avenues for active actor coverage; no registry scan or guessed active predicate was applied.

## Naming and containers

No nonempty human-facing item name string was recovered from the sampled wrapper fields. Class-derived labels plus the real numeric item ID are a truthful fallback. Some code names are historical aliases (e.g. the observed class spells `UMP9`), so a normalized class label must not be described as the game's verified localized display name.

No crate/container actor was observed in this SingleTraining inventory. Its item-list layout and contents are therefore unresolved; the current evidence supports ordinary ground pickups, not a claim that crate contents are decoded.

## Reproducibility and coverage

`loot-layout-probe.py` reads five representative actors' exact live class/property metadata. `loot-catalog.py` obtains a fresh native actor inventory, then validates superclass chains and reads only selected pickup fields. Both are read-only. Existing REA ARM64 provider limitations from the parent investigation remain; this pass reuses its supported scoped runtime-memory fallback instead of repeating rejected provider calls. No game input, injection, process-memory write, file patch, or system change was performed.
