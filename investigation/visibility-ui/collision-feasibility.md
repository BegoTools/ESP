# Read-only external collision / LOS feasibility

Scope: existing local reflection and the exact GameLoop `libUE4.so` (build 21518, SHA-256 `ad923890b4e12e389aba66253ef0259285b351b3742fbda7ae0d0716ea070484`). Offline inspection only; no game input, injection, runtime calls, security changes, or source edits. This review does not validate any new live field.

## Result

There is no currently supported external collision query or per-camera enemy LOS cache in the retained evidence. A separate read-only geometry decoder is possible as a research direction, but it requires substantial new format/layout/coverage validation. The current reader cannot answer whether any tiny part of a target is exposed from its actor position and capsule height alone. Finite head/chest/limb ray samples would be an approximation and can miss a small visible part.

## Concrete entry points in retained reflection

From `investigation/live-reflection-layout.json`:

| Object / owner | Field | Offset | What is actually established |
|---|---|---:|---|
| Character | Mesh | `0x510` | Reflected object pointer, not target skin/bone geometry decoding |
| PrimitiveComponent | BodyInstance | `0x550` | Reflected struct; its PhysX actor/shape internals are not decoded in the current reader |
| ShapeComponent | ShapeBodySetup | `0x9C8` | Reflected object pointer on the inspected capsule; does not inventory environment obstacles |
| SceneComponent | AttachParent / AttachChildren | `0x188` / `0x198` | Attachment pointers/array; transforms must respect this hierarchy |
| SceneComponent | RelativeLocation / RelativeRotation / RelativeScale3D | `0x1E4` / `0x1F0` / `0x1FC` | Reflected local transforms, not a complete immutable collision-scene snapshot |
| World | Levels | `0x440` | Existing reader inventories all loaded level actor arrays; inventory is not collision geometry |
| BP_STExtraPlayerController_C | closedAimTarget / CurAimTargets | `0x45D8` / `0x45E0` | Object / array selection-cache candidates only; no verified per-view LOS meaning or full-target coverage |
| STExtraPlayerController | closedAimTarget | `0x2F10` | Separate same-name native-owner property; must not confuse with the blueprint-owner field |
| STExtraBaseCharacter | SightVisionMask | `0x3BFC` | UInt32 feature mask; retained enum names refer to night-vision equipment/frame/environment, not per-camera enemy occlusion |

The saved full controller property list (1,087 rows) and pawn property list (1,244 rows) contain no established all-target per-camera visible/occluded result map. This is a bounded reflection search, not proof that private caches do not exist.

## Focused exact-artifact name observations

The four distinct `LineOfSight` token names found were `AILineOfSight`, `LineOfSight`, `SimpleLineOfSight`, and `LineOfSightTo`. Their presence does not provide an externally callable API or cached result; calling game methods would violate the current no-injection/no-runtime-calls boundary.

`bEnableVisibilityCheck` at string VA `0x4041782`, descriptor reference `0xEBFF8E8`, is near `GenericMonsterSensingConfig`, `MaxRememberEnemyTime`, `SensedAngle`, `LoseSightRadius`, `SensedRadius`, `bEnableWaterCheck`, and `bEnableFactionCheck`. This supports investigating an AI sensing configuration context, not treating its bool as the local camera's target LOS result. No owner/runtime semantics were validated here.

`OverrideVisibleActors` at string VA `0x40E1818` has a candidate property parameter record at `0xE8CBFB8` carrying offset `0x4E0`. Its neighboring names are `OverrideInvisibleLevels` and `OverrideInvisibleActors`. This is an explicit visibility-policy/override candidate, not evidence of a readback cache of naturally visible enemies. No runtime owner/value semantics are established.

Physics-related names include `BodySetup`, `PhysicsAsset`, `CollisionResponses`, `PhysicsScene`, `AggGeom`, `ConvexElems`, `BoxElems`, `SphereElems`, `SphylElems`, `TriMeshesCollisionEnabledList`, and PhysX-style `HeightFieldGeometry` / `RaycastQueryResult`. An `AggGeom` property candidate at record `0xE790688` carries offset `0x28`, near `BoneName`, `PhysicsType`, and `PhysicsBlendWeight`. Names and candidate descriptor offsets do not establish decoded triangle/convex/heightfield buffers or a live broadphase.

No `PxScene`, `PxShape`, `PxTriangleMesh`, `PxConvexMesh`, raycast, LineTrace, BodySetup, or collision-query export was found in the existing small defined-dynamic-symbol inventory. This is export-inventory coverage only; internal implementations plainly can exist.

## What an external physics path would still require

1. Verified per-build traversal of all obstacle components across loaded levels, including static meshes, instanced meshes/foliage, landscapes/heightfields, dynamic doors, vehicles, and streaming changes.
2. Geometry buffers (cooked triangles, convex hulls, shape primitives), correct component/instance transforms and scale, collision enabled state, and the collision responses for the chosen query channel.
3. Safe handling of object lifetime and concurrent physics-scene mutation without assuming an external raw memory read is a coherent snapshot.
4. Exact current camera position and target surface geometry/pose. Existing approximate actor-foot/height aim points are insufficient for an arbitrary tiny exposed body part.
5. Calibration against partial exposure and fully hidden cases. Collision and visual opacity can differ for fences, glass, foliage, smoke, and materials.

Until those facts exist, a new boolean inferred from renderer activity, configuration flags, selected aim targets, or one capsule-center ray must not be labeled a verified wall-visibility result. Preserve unknown and keep the visible-only filter fail-closed.
