# In-game shortcut controls — 2026-11-04

## Delivered behavior

- Click-through, non-activating shortcut HUD in the top left of the existing overlay.
- F3: aim on/off. F4: activation mode cycle. F5: aim body region cycle. F6: ESP drawing on/off independently of aim and category filters. F7: non-activating quick adjustment panel.
- Quick panel: up/down choose strength, radius or input multiplier; left/right adjust. F7 closes it. No mouse or keyboard injection was added by this feature.
- Changes apply immediately; persistence is debounced by 300 ms and is also preserved by the existing close handler.
- F3-F6 and Ctrl+F7 are registered only while the selected game is foreground and Live is active. Arrow registrations exist only while the quick panel is open in that context. Focus loss/stop/close releases them. Repeated holding of a function key does not repeatedly toggle its action.
- Conflicting function shortcuts are shown as unavailable. Full settings remain at Ctrl+F7 while playing and at the existing main-window menu button. F7 outside Live gameplay retains its prior full-menu behavior.
- F8 suppression still pauses aim and normal overlay rendering. F7 can display only the quick panel during that suppression without resuming aim. F9 still stops the session.

## Verification

- **153 checks passed** on the deployed executable (`deployed-self-test.txt`). New checks cover action cycles, independent ESP/aim toggling, adjustment boundaries, legacy defaults, JSON persistence, renderer suppression and quick-panel painting; existing aim and reader checks remain passing.
- Collapsed and expanded HUD renders were reviewed (`render/collapsed.png`, `render/expanded.png`). Live screenshot review confirmed placement and readable state labels on GameLoop at the real game viewport.
- Computer Use reported a lower integrity level than GameLoop, and its two attempted F7 inputs did not establish a successful panel toggle. Those calls are not counted as keyboard verification.
- The user then performed a manual in-game shortcut test and explicitly confirmed: **"اشتغلت، واللوحة بتتغير من جوّه اللعبة"**.
- `live-ui.png.aim.json` retains the finite observation. It captured the in-game shortcut registrations without conflicts and a late F3 toggle; it does not independently capture every user-confirmed panel interaction before the finite probe closed.
- Final deployment preserved the original production settings: aim enabled, Firing mode, strength 35, radius 151, input multiplier 0.94. Temporary test toggles in the staged settings were not copied over production preferences.
- The normal production app was launched visibly with the same administrator context required by GameLoop. Desktop shortcut target remains `dist/ESPWorkbench.exe`.

The native reader and the verified Windows mouse aim path were not changed by this feature.
