# Validation and known limits

## Existing 1.0 controller path

Previous measurements on one Windows x64 PC with a G7 Pro and 2.4 GHz receiver demonstrated background GIP telemetry under LocalSystem with Developer Mode off, real input reaching the virtual Steam Controller, recognition and sensors in stock SDL, battery reports, standard rumble requests reaching the Windows gamepad API, and restoration of normal physical input when stopping the service.

The last output-only fix disables HIDMaestro's idle-frame generator, which could otherwise inject empty frames between raw reports. That fix was separately exercised through Windows HID using explicitly synthetic neutral data: 501 reports, unique timestamps, no invalid quaternion and no unexpected buttons/sticks/gyro. It was not a new physical-controller run. Extra-button decoding also uses selected real controller payloads as offline fixtures.

These measurements do not establish compatibility with every G7 firmware, PC, game or Steam version. Trigger vibration, trackpad haptic scripts, hardware configuration controls, Bluetooth, wired mode, ARM64 and multiple simultaneous receivers are outside the supported scope.

The 1.1 release build passed 47 core tests, 15 native API tests and 21 native component tests. Both language layouts were rendered with sample status values without connecting to a controller. The Inno Setup script compiled successfully.

## 1.1 installer and language release

This release changes distribution, setup lifecycle, language selection and presentation. It retains the controller packet formats and forwarding algorithms. Core tests cover English fallback, Dutch display-language selection and exact service-command ownership. Native suites use in-memory providers.

The authorized in-place upgrade from 1.0 to 1.1.0-rc.2 completed on the development PC with installer exit code 0 and no reboot, and the app was reopened normally. Clean install, uninstall, driver-reboot flow and over-the-shoulder administrator credentials have **not been executed for acceptance**. No new controller test was performed for the packaging release.

## 1.2 live input display and manual rumble test

The GUI reads the owned virtual Steam Controller's HID input back from Windows and displays its buttons, sticks, triggers and three gyro rates. Device selection requires the existing bridge identity; the protocol and physical-input route are unchanged. The manual rumble button sends a bounded 400 ms pulse at one-quarter strength to the two main motors through the existing virtual-controller feedback route. Hiding/closing cancels the pulse and an explicit neutral command is attempted on completion, cancellation and failure.

Offline tests cover the report decoder, stale input, signed gyro data, identity scope, rumble formatting, and neutral commands on success/cancellation/failure. English and Dutch layouts are rendered with explicitly labelled sample input. No physical rumble test or live viewer acceptance has been performed by the development assistant for this feature. Treat `1.2.0-rc.2` and later as a preview pending the user's own controller checks.

## 1.3 sign-in start, waiting and tray status

These observations were read-only, on the development PC with the 106B receiver:
- Controller off: the receiver root has no child devices, and the paired 045E:02FF legacy instances are not present. The 1.2 service meanwhile repeated a hide / open (`0x80070490`) / unhide cycle about every six seconds.
- Controller on: the `USB\VID_045E&PID_02FF&IG_00` child and its HID grandchild are present.

1.3 therefore opens a session only when such an `&IG_` descendant exists, and keeps its own hiding rule while waiting. With the controller present, three failed sessions release the physical controller until it disconnects. A controller that Windows' input API does not report within ten seconds counts as a failed session, as does an enumeration error.

In the 100A window of 2026-09-28 18:32:53–18:33:02 (controller on, switched to 106B by the running service), Windows recorded `USB\VID_3537&PID_100A&IG_02` and its HID child. Earlier 100A `&IG_` instances were present for long periods, so they may also exist with the controller off. For that reason failures on the 100A identity never release hiding: the paired rule only lists the 106B children, so it has no effect there. The service keeps retrying, as 1.2 did.

Before 1.3, the active service delivered 62.6 telemetry and 127 virtual reports per second. Without a fine-grained timer request, its intended 8 ms pad poll and 4 ms idle wait were rounded up to the default Windows tick of about 15.6 ms. 1.3 requests 1 ms resolution for the service process only during an input session. It also waits 1 ms when the queue is empty.

Offline tests cover receiver/child classification (including 100A interfaces that exist without a controller), the wait/start/release state machine including the report grace period and 100A behaviour, battery alert thresholds, hysteresis and charging, parsing of the autostart command and StartupApproved value, and tray status text. The 100A receiver mode, sign-in start after a Windows restart, the notifications themselves and the output-rate improvement require checks on real hardware.

## Manual acceptance checklist

The rc.2 trigger correction uses a user-operated partial-LT observation: physical byte 59 was 107/255 while processed byte 12, Windows input and virtual output were already at their maximum. Offline regressions reproduce that mismatch and cover all 256 physical positions for both triggers, stick preservation and stale-data release. No firmware setting was changed and the assistant did not actuate the triggers. Micro-switch-mode behavior has not been newly validated in this correction.

On a suitable test machine, with permission to install the components:

- Run setup on a clean Windows 11 24H2+ x64 installation and restart if requested.
- Confirm English for English/German/etc. display languages and Dutch for Dutch/Belgian Dutch, independent of date/number format.
- Open as a normal user, connect one G7 Pro receiver, wait for the tray state, and check Steam Input, extra buttons, Share, gyro and ordinary rumble.
- Exit and confirm the physical controller can be used normally.
- Upgrade an existing installation, preserving settings and unrelated HidHide rules.
- Uninstall through Windows Apps; confirm the service and owned hiding rules are removed, while shared drivers and unrelated rules are retained.
- Check setup failure reporting and the driver restart prompt.
- Open the live diagram and press each physical button, move both sticks/triggers and rotate the controller; confirm the corresponding virtual input indicators respond.
- Click the rumble test yourself; confirm both main motors pulse briefly and stop, including when minimizing or closing the window.
- Sign out and in (or restart) with Start with Windows enabled: G7 Bridge appears only in the tray and connects when the controller turns on.
- With the controller off, confirm the service log stays quiet. Turn it on and off repeatedly; confirm it reconnects each time.
- Unplug and reconnect the receiver with the controller off (100A mode), then turn the controller on; confirm it connects.
- Turn Start with Windows off in the app and in Windows Settings → Apps → Startup; confirm both are respected.

Do not equate the static build, unit tests or UI layout preview with completion of these manual checks.
