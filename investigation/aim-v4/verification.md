# GameLoop input coexistence repair — 2026-11-04

## Confirmed causes

1. The tool ran at Windows integrity **0x2000 (Medium)** while GameLoopEmulator PID 21940 ran at **0x3000 (High)**. The read-only token probe and its source are retained as `integrity.txt` and `Integrity.cs`. Microsoft's SendInput contract requires equal or lower target integrity.
2. The Android touch transport created its own down/move/up sequences, disrupting GameLoop's existing multi-touch stream for firing and movement. Earlier camera-only convergence did not establish usable simultaneous gameplay. That transport has now been removed from `src` and from the distributed payload; its source is retained in the previous investigation archive.
3. Two copies of the old tool were running, including an invisible instance started with a hidden window. Both were stopped. Normal application launches now use a per-user named mutex, and the final interactive launch uses a visible window.

## Changes

- Ordinary Windows relative mouse movement only, with MOVE_NOCOALESCE. No mouse-button/keyboard writes, touch gestures, device grabs, game writes, hooks or drivers.
- Target/current integrity check reports a clear blocked reason instead of silently counting unusable events.
- A visible administrator restart button saves settings, invokes the normal Windows RunAs consent flow, and closes the previous instance after successful launch. Consent is handled by the user.
- Activation choices: Scoped, Firing, Always, ScopedAndFiring, Hold.
- Scoped/Firing use actual read-only game states, not assumptions about right/left mouse buttons. The native reader verifies BoolProperty metadata, exact offsets and byte masks in the live local pawn class hierarchy before reading bIsGunADS / bIsWeaponFiring. Unknown state blocks only modes requiring it.
- Activation flags propagate through native parsing and display interpolation. Diagnostic samples use the frame that produced the controller decision to avoid mixing a newer reader frame with the previous controller state.
- Existing strength, radius, aim region and input multiplier controls remain. Final settings preserve the user's tested Firing mode, strength 15, radius 482 and multiplier 0.94.

## Runtime verification

- `activation-state.json`: verified local scoped=false, firing=false, FOV 80 while idle.
- `windows-admin-live.png.aim.json`: elevated ordinary mouse transport, 20-second observation. Target error decreased from 20.269 px at the first recorded sample to 2.204 px and remained stable. No touch transport was active.
- `firing-admin-live.png.aim.json`: live 45-second user-operated test (elapsed 46.728 s due to the settings dialog), 367 samples, **429 delivered corrections**, 97 samples with actual weapon firing and 137 samples with ADS. Camera yaw crossed a broad range during user movement. User changed the assistance radius from 150 to 482 in the menu during the run.
- The user explicitly confirmed afterward: **"أيوه، التحكم والضرب شغالين طبيعي"**.
- The old telemetry combined the newest reader flags with the last controller state, producing one apparent cross-frame mismatch; final telemetry now records the controller's decision frame consistently. This is a logging correction, not proof of an additional movement outside the firing gate.
- **136 checks passed** on the deployed executable, including actual-state activation transitions, missing-state blocking, integrity checks, retired transport migration, interpolation, and prior ESP regression checks.
- Final visible elevated instance started successfully. A second normal launch exited with code 0; only one tool process remained (`single-instance.json`). The retired touch helper is absent from `dist/native`.
- LDPlayer was not running and was not tested live during this repair.

## Primary references

- [Microsoft SendInput integrity restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [Microsoft MOUSEINPUT movement flags](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)
- [GameLoop keymapping guidance](https://www.gameloop.com/en/blog/faqs/keymapping-issues-when-playing-mobile-games-in-gameloop-client)
- [Android input dispatcher handling of conflicting devices](https://android.googlesource.com/platform/frameworks/native/+/android-8.0.0_r36/services/inputflinger/InputDispatcher.cpp)
