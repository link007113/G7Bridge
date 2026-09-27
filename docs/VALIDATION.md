# Validation and known limits

## Existing 1.0 controller path

Previous measurements on one Windows x64 PC with a G7 Pro and 2.4 GHz receiver demonstrated background GIP telemetry under LocalSystem with Developer Mode off, real input reaching the virtual Steam Controller, recognition and sensors in stock SDL, battery reports, standard rumble requests reaching the Windows gamepad API, and restoration of normal physical input when stopping the service.

The last output-only fix disables HIDMaestro's idle-frame generator, which could otherwise inject empty frames between raw reports. That fix was separately exercised through Windows HID using explicitly synthetic neutral data: 501 reports, unique timestamps, no invalid quaternion and no unexpected buttons/sticks/gyro. It was not a new physical-controller run. Extra-button decoding also uses selected real controller payloads as offline fixtures.

These measurements do not establish compatibility with every G7 firmware, PC, game or Steam version. Trigger vibration, trackpad haptic scripts, hardware configuration controls, Bluetooth, wired mode, ARM64 and multiple simultaneous receivers are outside the supported scope.

The 1.1 release build passed 47 core tests, 15 native API tests and 21 native component tests. Both language layouts were rendered with sample status values without connecting to a controller. The Inno Setup script compiled successfully.

## 1.1 installer and language release

This release changes distribution, setup lifecycle, language selection and presentation. It retains the controller packet formats and forwarding algorithms. Core tests cover English fallback, Dutch display-language selection and exact service-command ownership. Native suites use in-memory providers.

The new installer, clean install, upgrade, uninstall, driver-reboot flow and over-the-shoulder administrator credentials have **not been executed for acceptance**. No new controller test was performed for this packaging release. Treat `1.1.0-rc.1` as a preview until these have been assessed manually.

## Manual acceptance checklist

On a suitable test machine, with permission to install the components:

- Run setup on a clean Windows 11 24H2+ x64 installation and restart if requested.
- Confirm English for English/German/etc. display languages and Dutch for Dutch/Belgian Dutch, independent of date/number format.
- Open as a normal user, connect one G7 Pro receiver, wait for the tray state, and check Steam Input, extra buttons, Share, gyro and ordinary rumble.
- Exit and confirm the physical controller can be used normally.
- Upgrade an existing installation, preserving settings and unrelated HidHide rules.
- Uninstall through Windows Apps; confirm the service and owned hiding rules are removed, while shared drivers and unrelated rules are retained.
- Check setup failure reporting and the driver restart prompt.

Do not equate the static build, unit tests or UI layout preview with completion of these manual checks.
