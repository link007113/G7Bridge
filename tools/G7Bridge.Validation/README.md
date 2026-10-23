# Explicit Windows HID checks

This developer tool is compiled by `Build.ps1 -RunUnitTests`. Hardware checks
are **never** run automatically by the build, installer, service or CI.

```powershell
dotnet publish tools/G7Bridge.Validation -c Release -o artifacts/validation
# Use a fresh scratch directory; existing profile files are never overwritten.
# Run elevated, with the production bridge stopped; restore its previous state afterwards.
./artifacts/validation/G7Bridge.Validation.exe --sdk "$PWD/artifacts/sdk-test-profile"
```

`--sdk` creates one temporary USB/IP controller with test VID/PID `1209:F7B2`
and a separate identity key. It uses the shipped profile and preparation code,
changing only its test identity. Steam does not bind its synthetic input to the
user's layouts. It opens only this test controller's vendor HID collection.
The test covers all 20 buttons and releases, sticks, partial/full triggers,
signed gyro/accel and quaternion bytes, repeated raw frames without neutral
injection, battery plus controller reports, command-channel serials and firmware
attributes, and Windows rumble writes reaching the SDK callback with both motor
values intact. It does not open the physical GameSir or install any driver.

For a separately authorized physical check, with the updated normal bridge active:

```powershell
./artifacts/validation/G7Bridge.Validation.exe --observe 90 "$PWD/artifacts/physical-observation.json"
# The user presses all buttons, gradually moves both analog triggers and rotates all three axes.
# This command actuates the real motors once, at 25% strength for 400 ms, then sends zero:
./artifacts/validation/G7Bridge.Validation.exe --rumble
# Optional diagnostic: left then right, one second each at 75%, with explicit stops.
./artifacts/validation/G7Bridge.Validation.exe --rumble-motors
# Wait up to 30 minutes for one real button press/release, reopening after sleep:
./artifacts/validation/G7Bridge.Validation.exe --watch-button L4 "$PWD/artifacts/left-rear.json"
```

Observation opens only the exact production USB serial/VID/PID/vendor collection.
The duration can be up to 600 seconds. Creating `<json-path>.stop` ends the
observation after its current read, so a user-operated sequence can finish without
racing a short fixed window. Use a new output path for each sequence.
It records missing buttons, partial trigger values, gyro ranges, quaternion
validity and Steam's board/unit identity. A failed or incomplete physical sequence
is a failed check, not hardware acceptance. `--rumble` confirms command delivery;
the user must confirm feeling the physical pulse. Check service feedback counters
and Steam's `controller.txt` separately. Keep personal Steam profile backups and
raw observations outside the public repository.
