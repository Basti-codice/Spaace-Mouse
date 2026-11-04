# Architecture

## Technology decision

| Choice | Why |
|---|---|
| **C# / .NET 8** | Best native Windows integration (P/Invoke for Raw Input, SendInput, Win32 windows), strong tooling, self-contained single-file publish (no runtime install). |
| **WPF** (not WinUI 3) | Mature, fully custom-stylable (modern flat design via own theme dictionaries), and - decisive for this project - **buildable and unit-testable in a Linux cloud environment** (`EnableWindowsTargeting`). WinUI 3 cannot be built without Windows/MSIX tooling. |
| **Raw Input API** (no HID library) | Reads the device in parallel to the 3Dconnexion driver without opening it exclusively, no kernel driver, works in the background (`RIDEV_INPUTSINK`). A second backend (**Direct HID**, `CreateFile`+`ReadFile` with shared access) exists as fallback. |
| **JSON + schema versions** | Human-readable, robust, easy to migrate (`SchemaVersion` in every file, migration hooks in `ConfigStore`). |
| NSIS installer | Per-user, no admin; can be built on Linux and on Windows CI. |

Only one UI framework is used. WinForms is referenced solely for the tray icon (`NotifyIcon`).

## Projects

```
src/SpaceMouse.Core      pure .NET 8, no Windows/UI dependency. All logic. 100% unit-tested without hardware.
src/SpaceMouse.Windows   Win32: Raw Input / Direct HID readers, SendInput, window management, hotkey, autostart.
src/SpaceMouse.App       WPF UI (pages, controls, quick-menu overlay, calibration wizard, virtual controller).
tests/                   xUnit tests against Core (virtual device, dry-run platform).
```

## Data flow (identical for hardware and simulation)

```
 RawInputHidSource / DirectHidSource      VirtualSpaceMouse
           \  raw HID bytes (RawReport)   /  (emits genuine HID bytes)
            v                            v
        ┌────────────── InputHub ───────────────┐   Device detection / abstraction layer
        │  ReportDecoder + HardwareProfile      │   (slot mapping, invert, range, dead zone, gain)
        └──────────────┬────────────────────────┘
                       │ InputFrame (6 axes in [-1,1], 2 buttons)    <- the only thing downstream sees
                       v
              SpaceMouseEngine  (125 Hz tick via EngineRunner)
        ┌ Mode manager (Standard | Custom)  ┐
        │ Profile resolution (+ modifiers)  │
        │ AxisMath (dead zone/curve/gain)   │
        │ discrete triggers (arming, cooldown, repeat), button state machine
        │ QuickMenuController (radial menu state machine)
        └──────────────┬────────────────────┘
                       │ ActionHandler.Execute / Begin / End / Tick
                       v
                 ActionRegistry (library)  ──>  IPlatform { IInputSimulator, IWindowService, ISystemService }
                                                    │                      │
                                          WindowsPlatform (SendInput,   DryRunPlatform (records only)
                                          Win32 windows, ShellExecute)
```

* The virtual controller produces real HID byte reports, so decoder + calibration + normalisation are exercised in simulation exactly as with hardware.
* `SwitchablePlatform` lets the app route actions to Windows or to the dry-run recorder. By default the virtual controller only *simulates* so it can't take over your cursor.

## Module map (spec items 1-13)

| Spec | Implementation |
|---|---|
| Device detection | `DeviceScanner`, `KnownDevices` (Windows layer) |
| HID input reader | `RawInputHidSource`, `DirectHidSource`, `MessageLoopWindow` (own thread/loop) |
| Hardware calibration | `CalibrationSession` (Core) + `CalibrationWindow` |
| Axis mapping | `HardwareProfile.Axes[i].Slot/Invert` + `ReportDecoder` |
| Input processing | `ReportDecoder`, `AxisMath`, `SpaceMouseEngine` |
| Input simulator | `SendInputSimulator` (+ `DryRunPlatform`) |
| Mode manager | `SpaceMouseEngine.Mode/SetMode`, `AppController` |
| Profile manager | `Profile` model, `Presets`, `ConfigStore`, `AppController` profile ops, `ProfileValidator` |
| Action engine | `ActionRegistry`, `BuiltInActions` (modular: register descriptor + factory) |
| Windows integration | `WindowService` (move/snap/maximise/monitors), `ShellSystemService`, `HotkeyManager`, `AutoStart` |
| Radial menu | `QuickMenuController` (logic) + `RadialMenuControl` + `QuickMenuOverlayWindow` |
| UI | `src/SpaceMouse.App` |
| Settings & persistence | `ConfigStore` (atomic writes, corrupt-file quarantine, versioned) |

**Modes vs. profiles:** *Standard* / *Custom* is the engine mode. "Own modes" are profiles; each carries its own bindings, sensitivities and quick menus.

## Safety design

* Nothing blocks input: no hooks, no filter drivers, no exclusive device access.
* Every held key/button is tracked by `SendInputSimulator`; `ReleaseAll` runs on: mode switch, profile switch, device unplug, hotkey, tray quit, crash handlers, process exit.
* Discrete actions use hysteresis arming + cooldown; injected input is tagged (`dwExtraInfo = "SMST"`) and the app reads the device only through HID, so there is no feedback loop.
* All action failures are caught, reported (activity list + toast, rate-limited) and never stop the engine.
* Config files are written atomically; unreadable files are moved aside (`*.corrupt-<time>`) and defaults restored.

## Adding an action

```csharp
r.Register(new("my.action", "My action", "Custom", "star", "Does X.", ActionKinds.Discrete),
           (def, ctx) => new MyHandler(ctx, def));   // derive from ActionHandler: Execute / Begin / End / Tick
```
It then shows up in the picker (and is validated/imported/exported automatically).
