# Build G7 Bridge

## Tools

Use Windows x64 with PowerShell **7**, the **.NET 10 SDK**, Visual Studio C++ Build Tools (MSVC x64), and Windows SDK **10.0.26100.0 or later**, including C++/WinRT. `BuildNative.ps1` locates the compiler with `vswhere.exe`. To create setup, use [Inno Setup 6.7.3](https://jrsoftware.org/isdl.php).

Pinned driver installers, notices and the replaceable libusb DLL/source are in `vendor/`. `Restore-Vendor.ps1` restores the large HIDMaestro library from its official release with archive and DLL hash checks. `G7Bridge.Native.dll` is built from source and ignored by Git. NuGet restores the managed dependencies. No Nexus installation, controller, Steam session or administrator access is needed to build.

## Compile and run offline unit tests

```powershell
./Build.ps1 -RunUnitTests
```

Output: `artifacts/package/`. This runs the protocol unit suite and two native suites using in-memory providers, then builds a self-contained Windows app. It does not access controller hardware, install drivers, change services or start the app.

Core tests alone:

```powershell
dotnet run --project tests/G7Bridge.Tests.csproj -c Release
```

## Produce the single-file installer

```powershell
./Build-Installer.ps1 -RunUnitTests
```

If the compiler is outside the usual locations:

```powershell
./Build-Installer.ps1 -CompilerPath 'C:\Tools\InnoSetup\ISCC.exe' -RunUnitTests
```

Output: `artifacts/installer/G7Bridge-<version>-Setup-x64.exe` and `SHA256SUMS.txt`. This compiles setup; it never runs it. `-SkipBuild` can reuse an already completed package. The archive is self-contained; driver installation itself occurs only when a person runs setup and accepts elevation.

The installer follows the Windows **UI language** through Inno Setup's `uilanguage` detection, with English first and Dutch second. The app uses `CultureInfo.CurrentUICulture`, normalized to English or Dutch. Technical service logs are English. The service exposes stable attention codes, so UI messages do not depend on parsing translated diagnostic text.

The live diagram uses a separate, normal-user HID reader. `VirtualControllerIdentity` derives the unchanged USB serial from the pinned HIDMaestro 1.9 identity key. Both enumeration and each opened handle check VID/PID, the vendor usage page, report size and that exact serial. No physical GameSir channel is opened by this viewer. Rumble testing writes the documented Triton 0x80 report, padded to the Windows HID output size, and follows the same existing feedback route as game rumble. There is no new privileged IPC channel. Unit tests cover decoding, freshness, identity scope and stop-on-cancel/failure behavior without HID access.

Trigger sourcing: GameSir E0 report bytes 59/60 provide the physical 8-bit LT/RT positions. Bytes 12/13 and the Windows/GIP state can already contain clipped/profile-processed values. Once vendor telemetry has arrived, `InputState` uses its fresh physical triggers alongside the higher-resolution Windows/GIP sticks. It releases triggers during telemetry gaps rather than jumping back to the processed values. An ordinary gamepad-only session keeps its existing trigger path. The captured regression payload and full 256-step sweep cover this boundary.

Waiting and pacing:
- `ReceiverScanner` reads present devices through cfgmgr32. A supported receiver root is `USB\VID_3537&PID_100A|106B`, and a connected controller is a present descendant with `&IG_`. The scan opens no device.
- `ConnectionGate` decides between waiting, starting and releasing the hiding rule after three failed sessions.
- During an input session, `TimerResolution` requests 1 ms timer resolution for the service process. Without it, short waits since Windows 10 2004 take the default ~15.6 ms tick.

The app accepts `--tray` for its per-user `Run` entry: it starts without a window and never triggers setup elevation by itself. `Autostart` only registers the installed Program Files executable. The first run of a version with this feature enables it once, and afterwards only the user's choice counts. Tray icons are drawn at runtime as in-memory PNG ICO files. `--render-ui` also writes a `-tray.png` sheet with every icon state.

## Installer lifecycle

- Inno Setup owns the fixed Program Files directory, Start menu / optional desktop shortcuts and Windows uninstall registration.
- `--prepare-setup` validates the exact existing service command, stops only that service and recovers its own hiding journal. The new helper is extracted to setup's temporary directory before old binaries are replaced.
- `--configure-service` installs required drivers, preserves settings, registers a demand-start LocalSystem service and grants local users query/start/stop access. Exit `3010` requests a Windows restart.
- `--uninstall-service` stops/removes the own service and restores its hiding rules before Inno removes the binaries. Uninstall also removes the uninstalling user's `G7 Bridge` sign-in entry. Shared drivers and ProgramData settings are retained.
- Unknown command-line options are rejected. Historical manual research windows are excluded from compilation; the shipped app always uses the service UI.

The installer refuses an unrelated service that happens to use the same service name. It does not disable Developer Mode/security, flash firmware or remove other applications' virtual controllers.

## Release workflow

The GitHub build workflow compiles and unit-tests the Windows app and setup. Installer execution and real controller behavior require separate, explicitly authorized manual acceptance. Feature changes go through a branch and draft pull request. Publish a prerelease until the installer has received manual acceptance and the required independent review; build success alone is not that acceptance.

Source code is MIT licensed. Keep all third-party notices and corresponding libusb source when distributing the app. See [DEPENDENCIES.md](../DEPENDENCIES.md).
