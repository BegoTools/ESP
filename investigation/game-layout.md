# PUBG Mobile runtime investigation

Date: 2026-11-03 (UTC capture timestamps appear in JSON probes).

## Result

**Actual process memory is readable. A working actor/camera profile has not been established.** The completed bounded inspection supplies no verified GWorld, actor array, local-player, camera matrix, position, health, or team offsets. These values must not be populated with guesses or offsets copied from another build.

## Observed target and identity

- ADB client used successfully: `D:\LDPlayer\LDPlayer14\adb.exe`.
- Device: `emulator-5554`; Android 14; Global package `com.tencent.ig`.
- Parent observed installed versionName `4.6.0`, versionCode `21525`, ABI `arm64-v8a`.
- Process PID at capture: `3418` (ephemeral; rediscover after restart).
- `/proc/3418/maps` readable through existing `su` permission; full map text retained in `process-maps.txt`.
- `libUE4.so` load bias at capture: `0x400023200000` (ephemeral; derive from maps each run).
- On-disk library: `/data/app/~~EUtkF0ekeIN2nj8LaU0hhA==/com.tencent.ig-jAm_V8ykS2Q5aGMXudCHfA==/lib/arm64/libUE4.so`.
- On-disk size: `252290584` bytes.
- On-disk SHA-256: `abb9343f69735378b6aea4db97a66e9789664075b0196b1b99378514052454ce`.
- On-disk header observed by `toybox readelf -h`: ELF64, little endian, arm64, shared object; SONAME `libUE4.so`. Defined JNI export names include `Java_com_epicgames_ue4_GameActivity_*`. This independently corroborates the Unreal target; Unity IL2CPP paths from the supplied report do not describe this library.
- Module identity raw command output retained in `module-identity.txt`; ELF header/dynamic table in `elf-layout.txt`.

## Read transport: two concrete compatibility issues

1. Plain `dd` resolves to `/system/bin/dd`, but execution returns `dd: no such tool` on this image. The explicit `/system/bin/toybox dd` applet works. `toolbox dd` is also unavailable. A successful outer ADB exit code alone does not establish a successful memory read.
2. With this bundled ADB/su combination, raw `exec-out` binary output expands some LF bytes to CRLF. A nominal 4096-byte memory read produced 4097 bytes after suppressing dd diagnostics. Transporting Base64 text and decoding on the host preserves the exact 4096-byte page.

Verified command (outer Windows argv is constructed by Python subprocess as a list, with the remote command passed as one argument):

```text
adb.exe -s emulator-5554 exec-out "su -c '/system/bin/toybox dd if=/proc/3418/mem bs=4096 skip=17180013056 count=1 2>/dev/null | /system/bin/toybox base64'"
```

Host decoding: Python `base64.b64decode(stdout)`; .NET equivalent `Convert.FromBase64String(...)`.

- Transport output: 5608 bytes including whitespace.
- Decoded output: exactly 4096 bytes.
- Decoded page SHA-256: `f7c5362a59114cede73e1b5a1aeb312e9b2d678724ca6b47991222a19cffa007`.
- Decoded page prefix: `5000005800021fd640360668eb7b0000`.
- Saved decoded page: `libUE4-header-page.bin`.
- Exact commands and byte counts: `memory-probe.json`, `memory-probe-explicit.json`, `memory-probe-confirmed.json`. The confirmed file supersedes the failed/intermediate probes.

The runtime first page begins with different bytes from the on-disk ELF magic `7f454c4602010100`. This is an observed difference, not proof of a specific protection mechanism or a reason to reject an otherwise readable map. Treat disk ELF identity and runtime bytes as separate evidence.

## Export-anchor search boundary

Read-only command:

```text
su -c '/system/bin/toybox readelf -s /data/app/~~EUtkF0ekeIN2nj8LaU0hhA==/com.tencent.ig-jAm_V8ykS2Q5aGMXudCHfA==/lib/arm64/libUE4.so'
```

The reported `.dynsym` inventory contains 754 entries. Complete output (about 63 KB) is saved as `elf-symbols.txt`. A projection of the 77 defined GLOBAL/WEAK rows is saved as `defined-dynamic-symbols.txt`.

Case-insensitive searches found no rows containing: `GWorld`, `GNames`, `GEngine`, `GUObject`, `WorldToScreen`, `PlayerCamera`, `PersistentLevel`, or `STExtra`. This establishes absence of those names from this dynamic symbol inventory, **not absence of those objects/functions from the game**. No internal/static symbol table with usable field layouts was established.

## REA availability limitation

The REA `inspect_adb_device` operation was attempted for `emulator-5554`. Its ADB client returned `error: device offline` (`unsupported_target`, non-retryable), while the bundled LDPlayer client immediately read the selected target successfully. The scoped fallback therefore used the working bundled client. No REA binary-analysis session was opened, and no persistent debugger/instrumentation session requires cleanup.

## What remains to make live ESP truthful

- Exact, validated pointer chains for this installed module identity: actor data pointer and count; local position/team; view-projection matrix and memory order; per-player position, health, team, and a reliable player-type discriminator.
- Runtime validation in a controlled known scene: camera movement, actor movement, despawn, lobby/match transitions, visibility/projection, and matrix convention.
- Profiling of complete snapshots on the actual actor count. Per-page ADB process startup is functional but no real-time performance claim is established by this single-page test.

Export names alone do not provide the missing layout. Establishing it needs a separate focused native analysis and controlled runtime validation, or a trustworthy exact-build layout source with the same validation. No full-process scan, full-library dump, game-file change, injection, anti-cheat change, identity spoofing, game restart, or account action was performed in this investigation.

## Follow-up: focused native-analysis pass

The parent requested an additional pass beyond dynamic-symbol inspection. The **on-disk library was subsequently pulled** read-only to `investigation/libUE4.so` (252290584 bytes), and its local SHA-256 matches the remote identity recorded above. This was a disk artifact transfer, not a process-memory dump. It supersedes the earlier statement that no full-library copy was acquired.

REA `binary_session` reported Ghidra 12.1.4 installed and launchable, but the Windows provider explicitly supports native PE and selected raw 6502 targets only. An actual `open_binary` request with the pulled ARM64 ELF and `provider_id: ghidra` returned:

```text
capability_unavailable / unsupported_provider
selection_reason: target_unsupported
ghidra: target_format_unsupported
Windows Ghidra P0 accepts native PE and explicitly selected raw 6502 targets only.
target_format: elf; architecture: arm64
```

Other deep providers were unavailable: Hopper is unsupported on Windows; IDA and Kuna are not configured. A scoped read-only Rizin fallback (`execute_rizin_command`, command `ij`, 10-second budget) could not launch an installed Rizin executable (`ERR_REA_WINDOWS_NATIVE`, `capability_unavailable`, non-retryable). Exact provider diagnostics and errors are saved in `rea-native-provider-status.json`. No persistent provider session was opened.

A separate ordinary Python byte search of the exact disk artifact (not REA xref evidence) established that relevant reflection/diagnostic literals exist. Selected UTF-8 **file offsets**:

- `PersistentLevel`: `0x422c7c8`.
- `STExtraPlayerCharacter`: `0x40170df`.
- `AcknowledgedPawn`: `0x406b974`.
- `PlayerCameraManager`: multiple matches, first `0x409abbf`.
- `GEngine`: text matches at `0x3ffdd9a` and `0x409f093`, plus UTF-16LE matches.
- `GWorld` and `GUObjectArray`: no UTF-8 or UTF-16LE matches in this bounded term inventory.

The per-term probe saves up to 20 positions per encoding in `static-string-probes.json`; a 20-position list may be truncated and does not claim a total match count. These string byte positions are **not** actor fields, global storage, pointers, or usable profile offsets. Their static references and code behavior remain unobserved because no configured deep provider accepts this ELF target. This is a tool-support blocker; the evidence does not establish that the offline library is encrypted or impossible to analyze.

No native anchor or usable ESP profile value was derived in the follow-up pass. The next specific capability needed is an ARM64 ELF-capable native disassembler/xref/decompiler provider, followed by exact-build reflection and controlled live-scene validation. The existing frontend/ADB transport alone cannot supply the missing layout.
