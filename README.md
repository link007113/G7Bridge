# G7 Bridge

**Use your GameSir G7 Pro over its 2.4 GHz receiver as a virtual Steam Controller, with independent extra buttons and gyro.** Open G7 Bridge, turn on the controller, and let Steam Input handle your game bindings.

**[Download the Windows installer](https://github.com/link007113/G7Bridge/releases)** · [Nederlandse handleiding](docs/README.nl.md) · [Report a problem](https://github.com/link007113/G7Bridge/issues)

The current installer release is **1.1.0-rc.2 (preview)**. The underlying controller bridge has been exercised on a real G7 Pro. This new installer, upgrade and uninstall flow still needs manual acceptance on a clean Windows installation. See [validation status](docs/VALIDATION.md) for the exact boundary.

## Requirements

- **Windows 11 24H2 or newer, x64**. ARM64, Windows 10 and Linux are not supported by this build.
- **GameSir G7 Pro with its 2.4 GHz USB receiver**. The supported receiver identities are `3537:100A` and `3537:106B`. Wired, Bluetooth and other GameSir models are outside this release's supported scope.
- Steam with support for the Steam Controller 2026, and Steam Input enabled for the game you want to use.
- Administrator permission **during setup** for the service and drivers. Normal use does not need elevation, Nexus or Windows Developer Mode.

## Install and play

1. Download **`G7Bridge-1.1.0-rc.2-Setup-x64.exe`** from [Releases](https://github.com/link007113/G7Bridge/releases). The GitHub source ZIP is for developers.
2. Exit an older G7 Bridge instance, run the installer and accept the Windows elevation prompt and the bundled Microsoft GameInput terms. The installer includes the app, its .NET runtime and required controller drivers. USB devices may briefly reconnect during the first driver installation. Restart Windows if setup asks.
3. Close GameSir Nexus, connect the 2.4 GHz receiver and turn on the controller.
4. Open **G7 Bridge** from the Start menu or desktop shortcut. Once ready, the window moves to the system tray.
5. In **Steam → Settings → Controller**, look for **Steam Controller**. Enable Steam Input for your game, then bind the extra buttons and choose the gyro behavior in that game's controller layout.

The interface and installer use **English by default** and **Dutch when the Windows display language is Dutch**, including Belgian Dutch. Date formats, keyboard layout and country settings do not select the UI language. Restart the app after changing your Windows display language.

The setup executable is currently **unsigned**, so Windows may show an unknown-publisher or SmartScreen prompt. Do not disable Windows security features. Obtain it from this repository's release page; `SHA256SUMS.txt` accompanies each build. The bundled third-party driver installers retain their upstream signatures.

## What is supported?

| G7 Pro input / feature | Bridge behavior |
| --- | --- |
| Sticks, triggers, D-pad and normal buttons | Forwarded to the virtual controller; sticks and triggers use the Windows/GIP values |
| L4, R4, L5 and R5 | Four independent grip buttons, assignable in Steam Input |
| Share / screenshot | An independent Quick Access input; assign the screenshot action in Steam Input |
| Gyroscope and accelerometer | Motion data forwarded to Steam Input, with relative orientation |
| Battery | Reported to the virtual controller and shown in the app |
| Standard rumble | Left and right main motors, including stop requests |
| Trigger-motor vibration | Not implemented |
| Steam trackpad haptic patterns | Not implemented |
| Trackpads | No physical trackpads; the virtual model's touch surfaces stay untouched |
| Hardware pairing, profile and configuration controls | Remain controller functions |

Extra buttons and motion require appropriate Steam Input bindings. A game that only accepts XInput will receive the actions configured by Steam; installing the bridge does not add native gyro support to that game's own input API. Existing firmware button remaps do not duplicate the independent extra button input in the virtual controller.

## Daily use

- **Minimize:** keep playing; the service and virtual controller remain active.
- **Open the shortcut again / double-click the tray icon:** show the status window.
- **Turn off:** stop the bridge and restore normal access to the physical controller.
- **Exit:** stop the bridge and close the app.

The service starts when the app requests it. It is not configured to start at boot. Automatic receiver discovery expects one supported receiver.

## What setup installs

The app goes into `C:\Program Files\Grimm\G7Bridge`. The `GrimmG7Bridge` Windows service runs under **LocalSystem**, which permits background access to the controller's GIP channel without Developer Mode. Local Windows users receive query/start/stop permission for this one service, not permission to change its executable or configuration.

The installer includes **HidHide** and the **usbip-win2 virtual USB backend** supplied by HIDMaestro. While the bridge is active, only the selected receiver's legacy XInput/HID child interfaces are hidden from games to prevent duplicate input. The physical GIP driver remains attached. On exit, the bridge removes its own hiding rules and restores the earlier HidHide state. Other applications' hiding rules are preserved.

The bridge does not flash GameSir firmware or rewrite hardware profiles. It reads Steam's local controller compatibility version to prevent a firmware-update prompt for the emulated controller. It does not modify Steam's updater files.

## Update or uninstall

**Update:** exit G7 Bridge and run the newer installer. The installation path and existing settings are retained. No manual driver installation is required.

**Uninstall:** exit G7 Bridge, then use **Windows Settings → Apps → Installed apps → G7 Bridge → Uninstall**. The uninstaller stops and removes the bridge service, restores its own device-hiding rules, and removes its installed app files and shortcuts. Shared HidHide / USB drivers stay installed because other applications may use them. Settings and diagnostics in `%PROGRAMDATA%\G7Bridge` are retained.

## Troubleshooting

| Symptom | Next step |
| --- | --- |
| Waiting for the G7 Pro | Turn it on, take it out of the dock and keep the receiver plugged in. Close Nexus. |
| More than one receiver | Connect only the G7 Pro receiver you want to bridge. |
| Drivers not ready after setup | Restart Windows. If it persists, rerun setup and save diagnostics. |
| Steam still sees two controllers | Exit the bridge, close the game, reopen the bridge and wait until ready before launching the game. Do not clear unrelated HidHide rules. |
| Steam asks to update the virtual controller | Exit the bridge, update Steam normally and reopen the bridge. Do not run a controller firmware update against the emulated device; report the issue if it remains. |
| Gyro or back buttons do nothing in a game | Check that Steam Input is enabled and that those inputs have actions in the game's layout. |
| Setup fails | Read `%PROGRAMDATA%\G7Bridge\setup-error.txt` and the Inno Setup log in `%TEMP%`. Restart if driver setup is pending, then retry. |

Use **Save diagnostics** in the app when reporting a problem. Diagnostics contain device identifiers, USB topology, input state and recent service messages; review them before posting publicly. Raw Nexus USB captures, local development plans and machine-specific configurations are not included in this repository.

## Build from source

See **[BUILD.md](docs/BUILD.md)** for the compiler prerequisites, offline unit tests and installer build. The public app consists of a .NET protocol core, a native C++/WinRT input adapter and a small Windows service/tray UI. Dependencies and their licenses are listed in **[DEPENDENCIES.md](DEPENDENCIES.md)**. Historical research windows remain in source but are excluded from the distributed app.

G7 Bridge source is MIT licensed; bundled third-party components keep their own licenses. The [redistribution notice](NOTICE.md) applies to the included components in source archives and installer packages. This is an independent community project, not affiliated with or endorsed by GameSir, Valve, Microsoft or Nefarius.
