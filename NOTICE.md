# Third-party terms and redistribution

The [MIT licence](LICENSE) covers G7 Bridge's own source code. It does not relicense third-party components. Each bundled component remains subject to its own terms, listed in [DEPENDENCIES.md](DEPENDENCIES.md).

## Microsoft GameInput Redistributable

`vendor/GameInputRedist.dll` and `vendor/drivers/GameInputRedist-3.5.278.msi` are supplied subject to the complete, unmodified [Microsoft GameInput Redistributable terms](vendor/licenses/GameInput-LICENSE.txt). In an installed package, these terms are available as `licenses/GameInput-LICENSE.txt`.

By installing, using or redistributing these Microsoft components, you agree to those Microsoft terms. These conditions apply to the bundled components obtained from this repository or its source archives as well as through the installer. If you do not agree, do not install, use or redistribute those components. The Windows installer presents the full terms for acceptance before installation.

If you redistribute a package containing Microsoft GameInput, you must comply with section 2 of those terms, including requiring distributors and external end users to agree to terms protecting the component and Microsoft at least as much as the Microsoft agreement. Preserve the Microsoft terms and notices in your distribution. G7 Bridge's MIT licence does not waive those requirements.

The GameInput header and loader source have their own notices; their availability as source does not change the redistribution terms of the GameInput runtime.

## Other bundled components

Keep the applicable notices for HIDMaestro, HidHide, usbip-win2, .NET and the other listed dependencies. When distributing the dynamically linked libusb binary, retain its licence and the corresponding source supplied in `vendor/sources/` (or `sources/` in the installed package). See [DEPENDENCIES.md](DEPENDENCIES.md) for origins, versions and full licence paths.
