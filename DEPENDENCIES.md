# Dependencies and provenance

G7 Bridge source is MIT licensed. Third-party components retain the licenses listed below; the complete notices are in `vendor/licenses/` in the repository and `licenses/` in the installed package. The [redistribution notice](NOTICE.md) applies to bundled components in both repository/source archives and installer packages. Setup presents the unmodified Microsoft GameInput terms for acceptance.

| Component | Version | Origin / license |
| --- | --- | --- |
| HIDMaestro.Core | 1.9.0 | [Official release](https://github.com/hifihedgehog/HIDMaestro/releases/tag/v1.9.0), MIT |
| HidHide driver | 1.5.230.0 | [Official signed x64 installer](https://github.com/nefarius/HidHide/releases/tag/v1.5.230.0), MIT |
| Nefarius.Drivers.HidHide | 3.4.0 | NuGet, MIT |
| Nefarius.Utilities.DeviceManagement | 5.2.0 | NuGet, MIT |
| Nefarius.Vicius.Abstractions | 1.4.1 | NuGet, MIT; no online update service is used |
| usbip-win2 | 0.9.7.5 | Bundled, unmodified upstream transport inside HIDMaestro; BSD-2-Clause, see HIDMaestro third-party notices |
| Microsoft GameInput | 3.5.278 | [Official NuGet package](https://www.nuget.org/packages/Microsoft.GameInput/3.5.278), supplied Microsoft license and notices |
| .NET / ServiceController | 10 / 10.0.0 | Microsoft/.NET, MIT; self-contained runtime included in setup |
| Microsoft.Extensions abstractions | 10.0.8 | Microsoft/.NET, MIT |
| libusb | 1.0.30 | [Official release](https://github.com/libusb/libusb/releases/tag/v1.0.30), LGPL-2.1; replaceable DLL and corresponding source archive included |
| UsbDk | 1.0.22 | [Official release](https://github.com/daynix/UsbDk/releases/tag/v1.00-22), Apache-2.0; historical transport, never automatically installed |
| Inno Setup (build tool) | 6.7.3 | [Official release](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3), Inno Setup license |

## Pinned binaries

`Restore-Vendor.ps1` downloads only HIDMaestro's official release archive when the library is absent. The large generated library is intentionally not stored in Git. Both archive and extracted DLL must match pinned SHA-256 hashes:

- HIDMaestro x64 release ZIP: `1fa4a57b6f2db9dc943bb81047808fdf097955497b96f22c1944ddd961b59605`
- HIDMaestro.Core.dll: `d613be086178d34def0c8d3869e801b55ce16d49b7a6e4516281d067aa730d9a`
- HidHide x64 installer: `f4bbbcb82e6258641b887c74bc81c4c5f66e4aa811808dfc304347687b7605f6`
- Inno Setup compiler installer: `9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`

HidHide is the upstream signed installer. HIDMaestro carries the original USB/IP transport and verifies its hash before deployment. G7 Bridge calls only the USB/IP backend installation API; it does not request HIDMaestro's separate UMDF driver or self-signed certificate installation. The local GameInputRedist.dll comes unchanged from Microsoft's official x64 redistributable. No Nexus binaries are distributed.

The GameInput header, loader source, import library and release notes are kept under `vendor/gameinput/`. Runtime/redistributable licensing is governed by the supplied Microsoft terms; it should not be described as though all GameInput binaries were MIT licensed.

## Protocol references

- Microsoft's public [Windows.Gaming.Input.Custom APIs](https://learn.microsoft.com/en-us/uwp/api/windows.gaming.input.custom) and [MS-GIPUSB specification](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gipusb/12feb7ea-3095-4dea-8288-ae66def54ecb). The native adapter implements its own C++/WinRT factory, aggregation and input sink. It retains the Windows Xbox driver.
- [g7ctl/pyg7](https://github.com/questionablesyntax/g7ctl), commit `0ea4b5223ba2191c88a3c4d1ff8d34f594bfeb1b`: documented GameSir session start and telemetry layout. The standalone protocol library is Apache-2.0; its GPL GUI is not included.
- [gamesir-linux-tools](https://github.com/broroeror/gamesir-linux-tools): public G7 Pro digital-button fields. Neither the Python implementation nor its GUI is bundled.
- Selected physical controller payloads from manually operated Nexus captures informed session framing and independent button decoding. The test fixtures contain controller input only, not raw USB captures, device serial numbers or authentication exchanges.
- Valve/SDL [Steam controller protocol definitions](https://github.com/libsdl-org/SDL/tree/main/src/joystick/hidapi/steam) and the stock Triton decoder: report layout, button semantics and sensor scaling. See the supplied SDL zlib notice. SDL itself is not a runtime dependency of the app.

The original HIDMaestro `steam-controller-2` profile is embedded as a template. At runtime, G7 Bridge changes its virtual compatibility timestamp using Steam's local target version and disables the SDK idle-frame generator so only bridge-owned raw input is emitted. Steam's files and the physical controller firmware are not modified.
