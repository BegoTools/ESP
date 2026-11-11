# Camera and level accessors from bounded runtime code reads

This is ordinary Capstone disassembly of parent-acquired read-only process-memory pages, not instrumented execution or a claim that a method was called. The exact-build disk identity and module base used for address translation are recorded in other investigation files.

## Camera

The exact `GetCameraRotation` function-name string (ASCII RVA `0x403D7A6`) is relocated into a native-function registration pair at `0xE8BC828`. The adjacent native exec pointer is RVA `0xB0C388C`. Its code calls RVA `0xABCAFD0`, then writes returned S0/S1/S2 to its result. The runtime bytes at `0xABCAFD0` (from `native-runtime-abca000.bin`, which contains two pages starting RVA `0xABCA000`) decode as:

```text
0xABCAFD0 LDR S0,[X0,#0x548]
0xABCAFD4 LDR S1,[X0,#0x54C]
0xABCAFD8 LDR S2,[X0,#0x550]
0xABCAFDC RET
```

Thus the camera-manager rotation is the three floats at +0x548. Adjacent getter at `0xABCAFE0` returns S0/S1/S2 from camera-manager +0x530/+0x534/+0x538, consistent with camera location. These runtime getter instructions support using these offsets directly rather than assuming standard FMinimalViewInfo packing.

Candidate FOV accessor at `0xABCAF58`:

```text
0xABCAF58 LDR S0,[X0,#0x4CC]
0xABCAF5C FCMP S0,#0.0
0xABCAF60 B.GT 0xABCAF68
0xABCAF64 LDR S0,[X0,#0x554]
0xABCAF68 RET
```

Adjacent functions set and reset +0x4CC, consistent with a locked-FOV override. A native exec function at `0xB0C38C4` calls camera-manager vtable slot +0x7C0 and returns S0. Runtime validation of that slot equaling moduleBase+0xABCAF58, together with reflection/runtime FOV values, can confirm the interpretation. Until that check, the FOV name/semantics are inferred from these bounded instructions, while the exact reads/branch are observed bytes.

Important packing detail: location starts +0x530, rotation starts +0x548, and candidate FOV is +0x554. There is a 12-byte gap between the three location floats and rotation; reading one contiguous standard location/rotation/FOV layout at +0x530 would be incorrect.

Disassembly retained in `native-runtime-abca000.asm.txt`; disk `native-camera-rotation-disassembly.txt` is empty because on-disk method bytes did not decode normally.

## PersistentLevel and GameInstance

Native exec `GetPersistentLevelURLByWorld`, RVA `0x75EB4C4`, parses a UWorld parameter and calls RVA `0x6C70198`. The runtime page `native-runtime-6c70000.bin` decodes:

```text
0x6C70198 CBZ X0,0x6C701B0
0x6C7019C LDR X9,[X0,#0x30]
0x6C701A0 CBZ X9,0x6C701B0
0x6C701A4 ADD X0,X9,#0x30
0x6C701A8 MOV W1,WZR
0x6C701AC B 0xAE87DD0
```

This corroborates UWorld.PersistentLevel +0x30 independently of the parent's live reflection. The immediately following getter at `0x6C701B8` reads world+0x470 then uses pointed object+0x188, corroborating the parent's world.GameInstance +0x470 route.

## Generic object-slot evidence

Disk code at RVA `0x8E946B8` is a weak-object resolver: input index/serial pair; NumElements from global +0xF36089C; array pointer from global +0xF360890; slot stride 0x18; object pointer at slot+0, flags at +8, serial at +0x10. This can validate weak references encountered in an identified container; it does not by itself identify the level actor container or justify treating arbitrary qwords as actors.

## Subsequent runtime reflection and live-sample validation

The parent subsequently validated the FOV vtable slot against the getter above and observed +0x554 equal to 90. Live Unreal reflection then established the MinimalViewInfo layout directly: `Location +0`, `LocationLocalSpace +0xC`, `Rotation +0x18`, `FOV +0x24`. Rotator members are `Pitch +0`, `Yaw +4`, `Roll +8`. Thus the apparent location-to-rotation gap is a real LocationLocalSpace vector, not evidence of encrypted rotation.

The parent reported three stable fresh samples of location `(36133.32,444237.22,1626.31)`, LocationLocalSpace `(0,0,0)`, rotation `(-10.11,-95.8997,0)`, and FOV `80`. Earlier unusual angle/roll readings do not establish a decoding scheme; no rotation encryption claim is supported, and no decryption is required by the validated layout.

These runtime reflection and current code findings supersede the earlier investigation's missing-offset state. The initial bounded export-only inspection was incomplete; continued lightweight native analysis plus live reflection recovered concrete world, player, camera, and object-layout data. Original failed probes remain retained as historical observations, not the current technical conclusion.
