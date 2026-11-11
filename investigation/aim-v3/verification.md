# GameLoop aim input repair — 2026-11-04

The previous Windows SendInput implementation counted accepted Windows events without establishing that GameLoop applied them to the game camera. This repair adds an ordinary Android touchscreen input transport, selected automatically for GameLoop. Windows input remains selectable and remains the LDPlayer default.

## Runtime evidence

- `touch-result.json`: a standard Android shell swipe on the actual game display changed yaw from -11.2188892 to -5.8777504 degrees (5.3411388 degrees), with no pitch change. The user agreed to remain still for the measurement.
- `input-dump.txt`: the game is focused on Android display **5**, with logical viewport **1920x1080**. Default display 0 belongs to the launcher and has different dimensions. Display selection is parsed, not hardcoded.
- The initial long-held touch approach was rejected when physical GameLoop input became active. Android InputDispatcher reported: `Dropping move event because a pointer for a different device is already active in display 5`. The user confirmed moving the mouse during that test.
- Immediate down/move/up bursts were acknowledged but did not change the game camera. They were replaced by approximately 32 ms gestures with interpolated moves spanning game frames.
- `timed-live.png.aim.json`: **15% strength, chest**, error **45.8558 px to 1.5938 px** in the recorded observation, stable through the end of the 10-second run. 22 device acknowledgements; no recorded input errors. First observation is after startup had already begun correcting.
- `head-100-live.png.aim.json`: **100% strength, head**, error **7.3754 px to 1.2500 px**, stable at the end of the 8-second run. 19 device acknowledgements; no recorded input errors.
- `manual-live.png.aim.json`: 15-second regression run, 12 acknowledgements, zero skipped events, zero transport errors, final error 2.1296 px. Yaw remained effectively constant, so this trace does **not** independently establish continuous physical-mouse coexistence.
- Helpers exited after test shutdown; no EspTouchMain process remained in the process inventory.

## Implementation and verification

- Standalone shell-owned `app_process` helper; normal Android input service; no game memory writes, game file changes, game code injection, APK installation, or driver installation.
- Transfer SHA-256 verification; session/version handshake; single outstanding correction; bounded pending movement; stale corrections discarded; final UP, EOF cleanup, and device watchdog.
- Individual conflicting gestures return SKIP; the connection continues instead of terminating. This behavior still requires broader simultaneous movement/firing testing across game layouts.
- **119 checks passed** on the deployed Windows binary. Existing ESP checks plus display discovery, wrong-app rejection, invalid dimensions, backend persistence, pause/release gates, and input fault reporting.
- Options UI rendered and visually reviewed. Head/chest/legs, circle/strength/input multiplier, and input-method selection remain available.
- Desktop shortcut target remains `dist/ESPWorkbench.exe`. The build includes `dist/native/esp-touch.jar`.

## Primary references

- [Android shell input implementation](https://android.googlesource.com/platform/frameworks/base/+/81f52b053da6/services/core/java/com/android/server/input/InputShellCommand.java)
- [Microsoft SendInput return-value semantics](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [GameLoop keymapping guidance](https://www.gameloop.com/blog/faqs/keymapping-issues-when-playing-mobile-games-in-gameloop-client)
