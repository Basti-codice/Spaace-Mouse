# Space Mouse Studio

Turn a **3Dconnexion SpaceMouse Compact** into a universal input device for Windows 10/11.
Move the cursor, scroll, switch browser tabs and windows, move windows around, fire shortcuts, launch programs and open a
**radial quick menu** around your mouse pointer - all configurable with a few clicks, no driver-style tables.

> **Honest status:** this software was developed and tested *without* access to a physical SpaceMouse. The HID
> input layer is implemented from public documentation and verified only against simulated devices.
> Please read [First hardware test](#first-hardware-test) before relying on it. Everything else (engine, actions,
> profiles, quick menu logic, persistence) is covered by 105 automated tests that run against a virtual SpaceMouse.

---

## Install

### Option A - Installer (recommended)
Run `SpaceMouseStudio-1.0.0-setup.exe`. It installs **per user** (no administrator rights), creates Start-menu and desktop shortcuts and an uninstaller.

### Option B - Portable
Unzip `SpaceMouseStudio-1.0.0-portable-win-x64.zip` anywhere and run `SpaceMouseStudio.exe`.
A file named `portable.flag` next to the EXE keeps all settings in `.\data` (nothing is written elsewhere, delete the folder to remove everything).

No .NET installation is needed - the runtime is bundled. Windows 10 (1809+) or Windows 11, 64-bit.

> Windows SmartScreen may warn about an unsigned app on first start ("More info" -> "Run anyway"). The binaries are not code-signed.

## Quick start (about 3 minutes)

1. **Keep the official 3Dconnexion driver installed** if you have it. Space Mouse Studio reads the device *in parallel* (Windows Raw Input); it never disables or replaces the driver.
2. Plug in the SpaceMouse and start the app. The **Home** page shows `Real hardware · SpaceMouse Compact` when it was found.
3. A banner offers **Calibrate now** (one minute, see below). Do this once.
4. Pick a **profile** on Home (for example *Standard Desktop*).
5. Switch **SpaceMouse control** on (top right). The mode changes from *Standard* to *Custom*.
6. Push the cap - the cursor moves. Left/right button click.

No SpaceMouse at hand? Open **Virtual controller** (Home or Device page): a complete software SpaceMouse with pads and sliders. By default it only *simulates* actions (they appear in the activity list) so your real cursor is not hijacked; tick *Let it control Windows* to make it real.

Always-available escape hatch: the **panic hotkey** `Ctrl+Alt+S` toggles control from anywhere. Closing the app, unplugging the device, changing profile or switching to Standard releases every held key and mouse button.

## The three ways to use the device

| Mode | What happens |
|---|---|
| **Standard** | The app injects nothing. The SpaceMouse behaves exactly as it would without this software. |
| **Custom** | The active profile interprets every movement and button. |
| **Your own modes** | Create as many profiles as you like (Profiles page). Each has its own bindings, sensitivities and quick menus. Switch from the dashboard, the tray, with an action on a button/axis, or automatically per application. |

## Presets

| Preset | Idea |
|---|---|
| **Standard Desktop** | Tilt/slide moves the cursor, twist scrolls, buttons click (hold = drag), roll switches windows (Alt+Tab), tilt forward = Task view / back = Show desktop. |
| **Browser** | Twist = next/previous tab, slide = back/forward, push = scroll, lift = new tab, press down = close tab, tilt = reload, right button holds the browser quick menu. |
| **Productivity** | Cursor + scroll as usual. **Left held +** slide = switch virtual desktop, push = maximise/minimise, twist = snap right/left. Right button holds a quick menu with your apps (browser, explorer, notes, calculator, music, window actions). |
| **Window Manager** | **Hold left** to grab the window under the cursor, slide to move it; tilt to snap left/right, maximise/minimise; **right held +** press = close. |
| **Custom** | Empty. Assign every axis and button yourself. |

All presets can be edited and **reset** to their original state on the Profiles page.

## Customize

Click an axis or button on the SpaceMouse illustration (Home or Customize):
1. Choose **when** it applies - always, or only while the left / right / both buttons are held (combinations).
2. Choose **Smooth movement** (cursor, scrolling, window moving, volume, zoom) or **One action per push** (tabs, windows, shortcuts - fires once, re-arms when you return to centre, with a pause so it can't double-fire).
3. Pick an **action** from categories (Mouse, Browser, Windows, System, Media, Keyboard, Programs, Custom, SpaceMouse). Shortcuts can be *recorded* by pressing the keys. *Action sequence* chains several actions with waits.
4. Optional **fine tuning**: sensitivity, dead zone, speed, acceleration curve, gentle-movement damping, strong-push boost, trigger point, pause, repeat, direction. A live graph shows the response and the current cap position.

Buttons can fire **on click**, **on press** or **while held**, with an optional different action on **long press**. A button used for combinations does not also fire its own click when you moved the cap while holding it.

## Quick menu

A radial menu that opens **around the mouse cursor**.
- Assign *Quick menu while held* to a button (hold, point, release to confirm) or *Open quick menu* (left button confirms, right button cancels).
- **Selection:** push the cap toward an item (Direction mode) or twist to step through items (Rotation mode). Optional: press the cap down to confirm.
- Release in the centre = cancel. Items can open **submenus**; every profile can have several menus.
- Edit items, icons, actions, order and submenus on the **Quick Menu** page.

## Hardware calibration

SpaceMouse generations differ in axis order, signs and value ranges, so the app never assumes a layout.
**Device -> Calibrate** (or the Home banner) guides you through: detect the device -> measure the resting position -> push/pull/slide/tilt/twist in all 12 directions -> review and flip directions -> check range and dead zone -> live test -> save. Several hardware profiles can be stored and switched. Everything can also be set manually (raw channel, invert, range, dead zone, gain).

*Hardware calibration* (what the device sends) is separate from *sensitivity* (how a profile reacts), which you set per axis on the Customize page.

## Troubleshooting

| Problem | What to try |
|---|---|
| Home says **No device** | Check the USB cable/port. Device -> *Input method* -> try **Direct HID**. Check Device Manager for "HID-compliant ... controller". |
| Device found, nothing happens | Device page shows *Reports received*. If it stays 0 while you move the cap, switch input method. If reports arrive but the live view doesn't move, run **Calibrate** or **Detect layout from received reports**. |
| Axes feel wrong/swapped | Run the calibration wizard; or fix channel / invert manually on the Device page. |
| Cursor drifts at rest | Increase the profile's *dead zone* (Customize -> Fine tuning) or recalibrate. |
| Shortcuts don't work in some programs | Windows blocks input to elevated (administrator) windows from normal apps. Start Space Mouse Studio as administrator for those. |
| Something is stuck | Press the panic hotkey, or quit from the tray. Everything is released on exit. |
| Antivirus/SmartScreen warning | The app is unsigned; it uses `SendInput` (like any automation tool) which some scanners flag. |

Data location: `%APPDATA%\SpaceMouseStudio` (or `.\data` in portable mode) - readable JSON, versioned.

## Build from source

```bash
dotnet test tests/SpaceMouse.Core.Tests          # 105 tests, runs on Windows, Linux, macOS
./build/release.sh                               # tests + win-x64 publish + portable zip (+ NSIS installer if makensis is available)
```
The WPF app targets `net8.0-windows` and can be cross-built on Linux (`EnableWindowsTargeting`). A GitHub Actions workflow (`.github/workflows/build.yml`) builds everything including the installer on `windows-latest`.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/HARDWARE.md](docs/HARDWARE.md) and [docs/TESTPROTOCOL.md](docs/TESTPROTOCOL.md).

## First hardware test

See [docs/HARDWARE.md](docs/HARDWARE.md#first-hardware-test-checklist) for a 10-minute checklist.
