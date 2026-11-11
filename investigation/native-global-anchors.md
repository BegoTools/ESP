# Exact-build native anchor findings

Artifact: libUE4.so, SHA-256 `abb9343f69735378b6aea4db97a66e9789664075b0196b1b99378514052454ce`, Global 4.6.0 (21525), ARM64. All offsets below are ELF virtual addresses relative to module load bias, unless explicitly marked file offsets.

The earlier REA provider limitation was worked around with ordinary offline Python/Capstone 5.0.9 + pyelftools 0.33 (installed only under `investigation/python-libs`) and NumPy. These findings are ordinary static-analysis results, not REA xref Evidence. Reproducible bounded xref search: `native_xrefs.py`, with JSON and disassembly captures in `native-xrefs.json` / `native-xrefs-disassembly.txt`.

## GEngine storage and World context traversal

1. ASCII diagnostic at `0x409F06A`: `PixUI-UE  Warn        PixUtil::GameWorld GEngine is nullptr`.
2. ADRP/ADD xref in function beginning `0xCEC53EC`; exact disassembly retained in `native-engine-anchor-disassembly.txt`.
3. At `0xCEC53F8`, `ADRP X8, 0xEE95000`; at `0xCEC53FC`, `LDR X8,[X8,#0x6C8]`; at `0xCEC5400`, `LDR X19,[X8]`. The null branch references the diagnostic above.
4. The `.rela.dyn` relocation at GOT slot `0xEE956C8` is `R_AARCH64_RELATIVE` (1027) with addend `0xF621D90`. Therefore this is static evidence for GEngine global storage at `moduleBase + 0xF621D90`.
5. On the non-null/class-match path, function tail-calls `0xA858440` with the engine object.
6. `0xA858440` reads count `int32[engine+0xC48]` and array pointer `qword[engine+0xC40]`, iterates array qword pointers, tests `byte[context]==1`, then returns `qword[context+0x2A0]` if non-null. This establishes the traversal used by this GameWorld helper, rather than guessing a GWorld storage location.
7. A second diagnostic at `0x3FFDD6E`, `PixUtil::GameInstance GEngine is nullptr`, is referenced by function `0xCEC5FE8`. It uses the same GEngine global and returns `qword[engine+0xE08]` after class validation. This corroborates the anchor and provides a GameInstance candidate.

The parent has separately validated the runtime GOT/global/engine/context/world chain; see `live-engine-validation.json`. Static code does not itself establish current runtime pointer values, process lifetime, or behavior after level transitions.

## Named PersistentLevel accessor under investigation

Exact ASCII function name `GetPersistentLevelURLByWorld` at `0x422C7C5` is referenced by dynamic relocation at `0xDB8F968`. The adjacent native-function pointer relocation at `0xDB8F970` points to RVA `0x75EB4C4`.

The on-disk bytes at `0x75EB4C4` do not decode as ordinary ARM64 instructions in Capstone, unlike nearby generated registration functions `0x75F8C4C` and `0x75F5D1C`. No claimed field offset is derived from these undecoded bytes. A bounded runtime page read of `moduleBase+0x75EB000` can distinguish whether the currently loaded accessor bytes are decoded and allow focused inspection without modifying the process.

## FName storage candidate (optional, not yet runtime validated)

Function `0x8BE2ED8` resolves an FName entry by calling `0x8BE1BFC`, selecting chunk `nameIndex / 0x4000`, then selecting qword entry `nameIndex % 0x4000`. Other FName conversion code reads flags at entry+8 (bit 0 selects wide string) and text at entry+0xC.

The array accessor `0x8BE1BFC` uses a state structure at RVA `0xF354BE0`. On its initialized return path, it computes `count = (uint32[state]-100)/3`, starts with `pointer=qword[state+8]`, walks qword indirections `count` times in total, and returns the final pointer as the names chunk array. The signedness and bounded acceptable count must be checked before following this chain. Initialization code is not to be called or reproduced against the game; inspect the existing initialized state only.

This candidate is not yet part of a usable profile and must not be represented as a fixed direct GNames pointer until validated against known runtime names.
