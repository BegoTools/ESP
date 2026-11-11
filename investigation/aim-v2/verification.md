# Aim update verification — 2026-11-04

- Build: .NET Framework x64; successful with no compiler warnings.
- 112 checks passed; report: `self-test.txt`. Includes input adapter/controller gates, continuous activation, optional cursor guard migration, head/chest/legs, radius eligibility, persistence, zero strength, camera response feedback, and prior ESP checks.
- Menu rendered using the application's `--render-menu`; reviewed `build-aim-v2/menu-preview/menu-1.png`. Region selector and three sliders are visible; remaining options scroll.
- Previous 45-second trace: 430 samples, all combined key/cursor blocking state, zero accepted movement events. That trace does not distinguish the exact blocked condition.
- New 12-second training-target-only trace: 113 samples, 545 accepted movement events, strength 15%, continuous activation, radius 90, input scale 1. First tracked target's recorded error started at 45.681 px and reached 3.706 px. Camera rotation changed during the observation. Manual movement was not excluded, so this is an integration observation rather than isolated proof of convergence.
- New trace: `continuous-ui.png.aim.json`; live UI report: `continuous-ui.png.json`. Fresh data and visible overlay at 1758 x 988, read rate 118.49 Hz.
- No Computer Use actions were needed for this update; menu checks used application rendering and live checks used the existing CLI.
- Body regions are geometric estimates from feet and height. No skeleton-bone or occlusion data was added. No game-memory writes, game-file edits, input hooks, or drivers were added.
