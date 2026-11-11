# External Android touch input

**Retired from the application.** User gameplay testing established that these gestures interrupt GameLoop's existing firing and movement touch streams. The source and test traces are retained for diagnosis only. The Windows build no longer contains this transport or ships its helper. Use normal Windows mouse input with the same integrity level as the emulator.

`EspTouchMain` is a standalone shell process, started by `app_process`. It invokes Android's input service and creates ordinary touchscreen motion events on the discovered game display. It does not load code in the game, edit game files, write game memory, or install a driver or APK.

Build with `build.ps1` using a local JDK and official Google R8/D8 at `tools/r8.jar`. The current build used the official `com.android.tools:r8:9.6.0-alpha09` compiler from Google's Maven repository; the compiler is a development dependency and is not shipped in the runtime release. Runtime payload: `build/esp-touch.jar`. The root build script copies that payload into the Windows release's `native` folder.

Protocol: READY/version/session handshake; `M sequence dx dy`; `U sequence`; `PING`. Each motion is a complete approximately 32 ms drag around (65%, 50%) of the game display, with three interpolated moves and a final UP. The host permits only one unacknowledged correction, drops stale pending corrections, and bounds accumulated delta. Rejected gestures return SKIP so concurrent emulator input does not tear down the session. EOF/finally and the watchdog release the touch. Input does not fire or press gameplay buttons intentionally; the anchor must remain in a camera drag region if the game's HUD is customized.

The implementation uses the same Android platform input-service mechanism as the shell input command:

- [AOSP InputShellCommand](https://android.googlesource.com/platform/frameworks/base/+/81f52b053da6/services/core/java/com/android/server/input/InputShellCommand.java)
- [Official Google Maven repository](https://dl.google.com/dl/android/maven2/com/android/tools/r8/maven-metadata.xml)

GameLoop test: Android 13, game display ID 5, logical bounds 1920x1080, external host render 1758x988. The display is discovered on startup rather than hardcoded. Default display 0 on this machine belongs to the launcher, so its `wm size` is unsuitable for game touch mapping.
