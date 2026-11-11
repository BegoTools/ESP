# Native process-memory reader

`reader.rs` builds a static x86-64 Linux ELF for the inspected LDPlayer 14 and GameLoop Android guests. Both kernels report `x86_64`; PUBG's ARM64 native-bridge address space remains readable through its process memory file.

This helper reads existing process memory with an already granted root identity. It performs no process-memory writes, injection, or game-file changes. The Windows host validates the exact package, version, build, ABI, and library hash before deployment and passes the verified build code to the reader. PUBG Global 4.7.0 build 21718 has its own layout; 4.6.0 builds 21525 and 21518 share the earlier layout. GameLoop 21518 evidence is under `investigation/gameloop-21518`.

For GameLoop's existing `txperm`, the host temporarily enables permissive mode solely to open maps/process-memory handles and exports `ESP_RESTORE_ENFORCING=1`. `Reader::new` restores and verifies Enforcing immediately after opening the memory file, before resolving names or reading game fields. Subsequent reads use that already-open file descriptor with Enforcing enabled. The host also installs a restoration trap and an independent two-second restoration guard. The original LDPlayer `su` path does not change enforcement.

Build from the workspace:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\build.ps1
```

The existing Rust toolchain must have `x86_64-unknown-linux-musl` installed. `rust-lld.exe` ships with the Windows Rust toolchain. No Cargo dependencies are needed.

Usage on the selected Android guest:

```text
/data/local/tmp/esp-workbench-reader PID MODULE_BASE_HEX [--once|--inventory] [--session=HEXGUID] [--keepalive] [--build=VERSION_CODE]
```

Default mode emits one UTF-8 JSON object per line, flushes after every snapshot, and targets 120 Hz (8,333 microseconds per snapshot). Snapshot work is bounded by the actor limit, and slow reads run without accumulating queued snapshots. The class metadata cache shares immutable entries to avoid cloning strings for every actor on every frame. Without `--keepalive`, each process exits after 60 seconds so command-line callers retain a bounded lifetime.

The Windows host can use `--keepalive` for a continuous stream without periodic restart gaps. It must send and flush a `PING` line immediately and at least once every three seconds; the host uses a 500 ms interval. A background stdin reader records monotonic heartbeat times. Missing heartbeats for three seconds, input EOF, or a closed stdout pipe ends the stream. This mode is rejected for `--once` and `--inventory`.

With this bundled older ADB, use `adb shell "su -c '... --keepalive'"` with redirected stdin and stdout for the continuous transport. `adb exec-out` does not forward heartbeat input in this environment; the helper correctly expires after three seconds on that transport. The host should ignore any non-JSON echo lines. `ReaderPid` and `Session` allow the host to verify the exact owned command line before terminating a stream. The host should verify both helper path and session token before sending a signal to the numeric helper PID.

`--once` returns one filtered snapshot. `--inventory` returns one bounded level-actor inventory including objects outside the supported categories. Live filtering accepts descendants of `STExtraPlayerCharacter`, the explicitly identified training target class `BP_CharacterModelTaget_C`, descendants of `STExtraVehicleBase`, and descendants of `PickUpWrapperActor`; companions are excluded. Actor class and name metadata are cached with a fixed size bound and class cache reset on world change.

Character life fields are nullable `IsDead` and `NearDeathBreath`. Before reading them, each class must validate reflected `STExtraCharacter.bDead` (`BoolProperty`, packed mask 0x01; metadata bytes `[1,0,1,1]`) and `STExtraBaseCharacter.NearDeathBreath` (`FloatProperty`) at the offsets for the verified build. This validation cache resets on world change. Zero health is retained only when death is explicitly false and finite positive knockdown breath remains. Authoritative death removes the row even with stale positive health or breath; zero breath removes a zero-health row. Missing or mismatched reflection produces unknown fields and preserves legacy zero-health rejection. Host filtering, interpolation, count and rendering carry the same discrete state. Deploy the host and helper together. Live training verifies field discovery and transport; opponent knock, revive, and death still need match validation.

Character rows include validated health/max health and nullable `IsBot`, using the reflected `bIsAI` flag. A false flag is a game flag, not independent proof that a human controls the actor. The known training AI blueprint with a false flag remains unknown; model targets use a distinct kind and unknown bot status. Vehicle rows include health and fuel values with separate `HasHealth`/`HasFuel` flags; maxima are read from the game and are not assumed to be 100. Loot rows include the verified type-specific item ID and count; labels and `LootGroup` use class-derived names rather than an invented item-ID catalog. No verified crate contents layout has been implemented.

The initial expanded payload included 71 pickup wrappers whose root locations were zero. A follow-up inspection established that these are hidden pooled actors (`AActor.bHidden` at `+0xE8`, mask `0x01`), with no attachment parent; the earlier attachment hypothesis was withdrawn. Live output now excludes hidden pickup wrappers, while inventory mode retains them for inspection. An active visible ground-loot sample still needs position validation; no invented attachment transform is applied.

Snapshot coordinates use Unreal world units, Z up. Actor `Position` is the raw capsule center. `Height` is twice the capsule half-height multiplied by absolute Z scale, when the component class and value bounds validate; otherwise it falls back to 180 units. The host must subtract `Height / 2` from actor Z to derive feet for drawing a full box.

The exact-build level accessor has an alternate provider path. If both the provider flag and pointer become nonzero, this helper returns an explicit error rather than claiming the direct level array is complete. Other scene transitions that invalidate the world, controller, pawn, or camera also produce an error and terminate the stream for the host to handle.

The initial 60 Hz update was benchmarked for 100 snapshots in the live training scene with 286 level actors and five filtered characters/targets: 59.886 Hz observed, mean interval 16.698 ms, 95th percentile 16.974 ms, and maximum 18.051 ms. Native snapshot work averaged 0.399 ms (95th percentile 0.485 ms); initial class-cache population reached 2.034 ms.

The 120 Hz character payload was benchmarked for 200 snapshots at 118.756 Hz observed. The expanded 96-record payload (one local character, four training targets, 20 vehicles, and 71 pickups) was then benchmarked for 200 snapshots at 119.127 Hz observed, with mean interval 8.394 ms, 95th percentile 9.413 ms, and maximum 10.611 ms. Expanded native work averaged 1.006 ms (95th percentile 1.324 ms). These measurements include a concurrent existing reader and apply to this scene, rather than establishing a guarantee for a full match.

The continuous heartbeat transport was also verified beyond the former 60-second restart boundary: 4,083 frames from one reader PID, mean interval 16.757 ms, 95th percentile 17.326 ms, and maximum 45.062 ms. Missing heartbeats ended the process about three seconds after the final ping; closing stdin ended it in 31 ms. The former restart gap was not present in this run. No non-JSON echoes or stream errors were observed with the tested command-shell transport.

When terminating a stream, continue draining its stdout while waiting for the local ADB client. The bundled ADB can otherwise block on buffered output even after the verified remote reader has exited.

Read-only verification results are in `investigation/native-reader-expanded-120hz-benchmark.json`, `native-reader-expanded-once.json`, `native-reader-120hz-benchmark.json`, `native-reader-keepalive-66s.json`, `native-reader-keepalive-eof-test.json`, `native-reader-keepalive-stdin-diagnostic.json`, `native-reader-60hz-benchmark.json`, `native-reader-stream-test.json` (the earlier 10 Hz baseline), `native-reader-once.json`, `native-inventory.json`, and `native-reader-failure-tests.json`. These captures establish operation in the current training scene; they do not establish compatibility with another build or every lobby/match transition.
