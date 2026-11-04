# Hardware notes

## What is known (and from where)

* USB vendor id of current 3Dconnexion devices: `0x256F`. **SpaceMouse Compact: product id `0xC635`** (as listed in public USB-id tables and the open-source `spacenavd`/`libspnav` device lists). Older devices use Logitech's vendor id `0x046D` (e.g. SpaceNavigator `0xC626`). See `KnownDevices.cs`.
* The device is a HID *Multi-Axis Controller*: usage page `0x01` (Generic Desktop), usage `0x08`. The app registers for exactly that usage - it therefore also picks up other multi-axis devices.
* Commonly documented report layout for 3Dconnexion devices (spacenavd / HID dumps):
  * **Report ID 1** - translation: X, Y, Z as signed 16-bit little endian,
  * **Report ID 2** - rotation: RX, RY, RZ as signed 16-bit little endian,
  * **Report ID 3** - buttons: bit mask (bit 0 = left, bit 1 = right),
  * typical raw range about **±350** (some firmware reports up to ±32767).
  Some newer devices send all six axes in one 12-byte report - this is also supported ("combined" layout).

**None of this was verified on a physical device.** That is why nothing is hard-wired:

* layouts are data (`HardwareProfile.Reports`), selectable and editable,
* the **calibration wizard** determines which raw channel is which axis, its direction, rest value, range and dead zone from your actual movements,
* **Detect layout** guesses the layout from the report ids/lengths actually received,
* the Device page has a **report inspector** (hex dump of the latest reports) to diagnose unknown formats.

## Coexistence with the official driver

The app uses the Windows Raw Input API with `RIDEV_INPUTSINK`: it receives copies of the device's reports like any other registered application. The device is never opened exclusively and the 3Dconnexion driver (3DxWare) is neither modified nor stopped. If Raw Input delivers nothing on a system, **Device -> Input method -> Direct HID** opens the HID collection read-only with shared access (each handle has its own report queue).
Not verified: whether every 3DxWare version/configuration lets both paths receive data at the same time. If not, quit the "3Dconnexion Home/Driver" tray app or use the fallback.

## First hardware test checklist

1. Start the app **without** touching anything else. Home must say `Real hardware` + the device name and `USB 256F:C635`. If it says `No device`: see README troubleshooting (input method).
2. Device page -> *Reports received* must increase when you move the cap. Look at the report inspector: ids 1/2/3 with lengths 7/7/(≥2)?
   * If ids/lengths differ -> *Detect layout from received reports*.
3. Run **Calibrate**. Follow the prompts. If the wizard complains about channels, use Redo for that direction.
4. Device page live view: every axis must move in the direction shown on screen; fix inversions in the review step or manually.
5. Home: choose *Standard Desktop*, switch control **on**. Check: cursor moves in the pushed direction, twist scrolls, buttons click.
   Keep the panic hotkey `Ctrl+Alt+S` in mind (or the tray icon -> Quit).
6. Try the Browser preset (twist = tabs) and the quick menu (hold right button, point, release).
7. Unplug and re-plug the device while control is on: nothing may stay pressed; Home shows `No device` and then `Real hardware` again.
8. Window Manager preset: hold left over a window, slide - the window follows; check on a second monitor / different DPI if you have one.

Please report: Device page screenshot incl. report inspector, `%APPDATA%\SpaceMouseStudio\hardware.json`, and anything unexpected.

## Not verifiable without hardware / Windows

See [TESTPROTOCOL.md](TESTPROTOCOL.md#not-physically-verified).
