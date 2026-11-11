# Player AI flag evidence, exact Global 4.6.0 build

Read-only live reflection and selected-object reads, process3418. Raw metadata and samples are in `playerflags-verified.json`.

- Reflected field `bIsAI`, owner `UAECharacter`, property class `BoolProperty`.
- `Offset_Internal = 0xA40`.
- Property metadata address at capture: `0x40000A637890`.
- Bytes at UBoolProperty+0x70..0x73 are `[1,0,1,255]`, corresponding to FieldSize1, ByteOffset0, ByteMask1, FieldMask0xFF. It is a native bool byte, not one packed flag among multiple booleans. Read `byte[pawn+0xA40] != 0` for this reflected flag.
- Distinct field `bIsAIWithPet` is at +0xA42; it must not be substituted for bIsAI.

Observed samples in the current single-player training world:

| Class | bIsAI byte | Context |
|---|---:|---|
| BP_PlayerPawn_ST_C | 0 | Known local player |
| BP_PlayerPawn_ST_AI_C | 0 | AI-named training pawn class |
| BP_CharacterModelTaget_C | 0 | Four training target objects |

Thus a false bIsAI value does not establish that every entity in this training scene is a remote human. Exact known training-target classes should be labeled TRAINING TARGET, and the observed AI-named training class handled distinctly. A true bIsAI value supports an AI label; ordinary false-flag remote-human versus AI behavior still requires validation in a scene containing actual remote participants. The current samples contain no true bIsAI value, and no classification should claim such a sample was tested.

Class name decoding and the player superclass chain were independently validated earlier through `STExtraPlayerCharacter`; those checks remain applicable. Game object addresses and metadata-node addresses are capture-time values and are not hardcoded offsets for future sessions.
