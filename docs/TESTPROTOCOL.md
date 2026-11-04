# Test protocol

Status of this document: **test run on Linux (cloud build environment), no Windows machine and no SpaceMouse involved.**
A passing build or passing simulation tests do **not** prove compatibility with a real SpaceMouse Compact. See "Not physically verified".

## Automated tests (xUnit) - 109 / 109 passed

`dotnet test tests/SpaceMouse.Core.Tests` - deterministic (simulated time, virtual device, dry-run platform), ~0.4 s.

| Class | Tests | Covers |
|---|---:|---|
| `DecoderTests` | 15 | HID report decoding for all 3 layouts (3 reports, combined 6-axis, wide ±32767), button bits, unknown/truncated reports ignored without state corruption, normalisation + clamping, hardware dead zone, axis remap, invert, rest offset, gain, layout guessing, **virtual device round-trip through the real decoder for every hardware variant**, disconnect publishes neutral frame, hub never claims hardware when none attached, known device ids |
| `CalibrationTests` | 8 | Detects scrambled channel order + inversion, calibrated profile normalises correctly, rest noise -> dead zone, too-small movement rejected, diagonal movement rejected, missing data, duplicate channels flagged (profile not marked calibrated), skipped axes reported |
| `AxisMathTests` | 8 | Dead zone + rescale, sign, invert, curve exponent, sensitivity/speed, strong-push boost, weak damping, discrete level clamp |
| `EngineTests` | 51 | Cursor speed/direction/inversion/sensitivity/acceleration/boost, sub-pixel accumulation, scrolling; button modes (hold/tap/long-press), modifier suppresses tap; discrete once-per-gesture, cooldown vs jitter, re-trigger, repeat, thresholds; **combinations** (button held + axis), no accidental trigger when modifier pressed with axis deflected; window grab+move; Standard mode injects nothing; **no stuck input** on mode switch / profile switch / dispose / device unplug / enabling with a held button; profile cycling, mode toggle; auto-switch by application (with fallback, disabled state); failing/invalid/unknown actions are contained; key chords release in reverse order even on failure; macro order; **quick menu**: hold-to-select-and-confirm by direction, cursor frozen while open, cancel in centre, submenu enter/back/leaf, sticky mode (left confirms / right cancels), rotation mode, push-to-confirm, closes on mode change, 60 s auto-close, empty menu error; identical behaviour for all hardware variants |
| `PersistenceAndPresetTests` | 23 | All presets valid; stable preset ids; **every binding of every preset produces real output without errors (both directions, with modifiers, all buttons)**; **every quick-menu item of every preset executes**; settings / profiles / hardware profile round-trip; missing presets restored, edited kept; corrupt files quarantined; export/import (new id, no preset key), garbage rejected; validator; key-combo parser; modifier resolution; action library metadata |

## Build checks

| Check | Result |
|---|---|
| `dotnet build` Core / Windows layer / WPF app (net8.0-windows, cross-built on Linux) | succeeded, 0 errors |
| XAML compile (all pages, themes, styles, controls) | succeeded |
| Static check: every `StaticResource`/`DynamicResource`/`FindResource` key used exists; Light/Dark themes define identical key sets | passed |
| `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` | succeeded; PE32+ x86-64 GUI executable (~66 MB) |
| Portable ZIP (`SpaceMouseStudio.exe`, `portable.flag`, `README.txt`) | created |
| NSIS installer (`makensis`, per-user, uninstaller, shortcuts) | created (Nullsoft self-extracting archive) |
| GitHub Actions workflow (`.github/workflows/build.yml`: Linux tests + Windows tests/publish/installer) | written; runs on push |

## Functions working in simulation (verified via tests above)

Input abstraction + decoding for 3 report layouts; axis mapping/inversion/ranges/dead zones/sensitivity; discrete pulses with anti-double-trigger; button modes and combinations; mode switching; profile switching and auto-switch logic; quick menu state machine incl. submenus; the action engine (mouse, scroll, zoom, keys, macros, profile control, window grab/move calls); all 4 working presets; persistence (versioned, atomic, corruption-tolerant); error containment; guaranteed release of held input.

## Not physically verified

These compile and are implemented, but could not be executed here:

1. **Reading the real SpaceMouse Compact**: USB ids (`256F:C635`), report ids/lengths/byte order/ranges and the button bit positions come from public documentation of 3Dconnexion HID devices - not from your unit. Raw Input parsing (`WM_INPUT` / `RAWHID`) was desk-checked only.
2. **Coexistence with the 3DxWare driver** on all driver versions (Raw Input and Direct HID are both provided because of this uncertainty).
3. **The whole WPF user interface at runtime**: layout, DPI scaling, animations, light/dark rendering, dialogs, tray icon, toasts. It builds and was statically checked, but has never been started.
4. **SendInput actions on a real desktop**: cursor motion via absolute positioning on multi-monitor / mixed DPI, scrolling, shortcut chords (Alt+Tab, Win+Tab, Ctrl+Win+arrows), window move/snap/maximise/monitor hop, focus-or-launch (foreground lock), screenshot (Win+Shift+S), media keys.
5. **Quick-menu overlay** placement near screen edges / on secondary monitors with different DPI, click-through and no-focus behaviour.
6. **Global hotkey** registration, autostart (registry), single-instance handling, behaviour when Windows locks the session / elevated windows (UIPI).
7. **Installer** execution (SmartScreen, uninstall, upgrade over a running instance).
8. Real calibration with physical movements (the algorithm is tested with synthetic scrambled devices).

## Known limitations

* Not code-signed (SmartScreen warning). 64-bit Windows only.
* Input into elevated (administrator) windows requires running the app as administrator.
* "Alt+Tab" style window switching is a single shortcut press (toggles between the last two windows).
* Virtual desktops are switched via the Windows shortcuts (no private API).
* Window grab picks the top-level window under the cursor; shell windows (taskbar, desktop) are ignored.
* One physical device at a time; the first known 3Dconnexion device wins.
* UI language: English only.
