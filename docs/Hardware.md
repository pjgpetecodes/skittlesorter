# Hardware Guide

Wiring, components, and calibration for the skittle sorter.

## Circuit & Components
- Circuit file: [circuit/Circuit.fzz](../circuit/Circuit.fzz)
- Color sensor: `TCS3472x`
- Servo motor: chute and feeder control

## Wiring Overview
- Refer to pin table and diagram in `circuit/`.
- Ensure stable 5V supply and proper grounds.

## Calibration
- Sensor placement and ambient light control
- Servo angle tuning for chute/feeder

### Guided colour calibration

Stop the sorter service first so it cannot move the arm or access the sensor.
Have one each of red, green, yellow, purple and orange Skittles ready.
Use the device's actual configuration, with
`Hardware.MockMode.EnableMockColorSensor` set to `false`.

Calibration is a mode of the .NET sorter itself, not a separate script.
On the Pi, deploy a sorter build containing `--calibrate` support first, then run:

```bash
sudo systemctl stop skittlesorter.service
sudo /opt/skittlesorter/current/skittlesorter --calibrate --config /etc/skittlesorter/appsettings.json
# After completing calibration:
sudo systemctl start skittlesorter.service
```

These paths match the installed Pi service. No Python or .NET SDK is needed
for calibration with the published sorter. Use the actual executable location
and `--config` path if your installation differs. For development from source
with the .NET 10 SDK:

```powershell
dotnet run --project .\skittlesorter.csproj -- --calibrate --config .\appsettings.json
```

The routine does not move servos or connect to Azure. Manually put the carrier
in its normal detection position. Follow the prompts: first leave it empty
(`None`), then place each requested colour under the sensor, keeping the normal
lighting, distance and orientation. It averages ten readings per sample.
If a colour saturates, adjust the lighting or sensor placement and retry.
Indistinguishable samples are rejected rather than saved.

Type `yes` at the final prompt to update only `Hardware.ColorCalibration`
in the selected existing configuration. All other settings are preserved.
The section contains a `Samples` object with averaged `Clear`, `Red`, `Green`
and `Blue` readings for `None` and the five colours. Until saving,
cancellation or an error leaves the configuration untouched.
Keep this file private: it may also contain device credentials.

Restart the sorter after saving. Calibrated classification uses the nearest
reference based on RGB/clear ratios and clear-channel brightness, including
the empty reading. Check several Skittles of each colour before unattended use;
one reference per colour cannot capture every variation in the sweets.
Recalibrate after lighting or sensor-position changes. Configurations without
`ColorCalibration` retain the original thresholds; remove that section to
revert.

## Software Hooks
- Sensor: see [TCS3472x.cs](../src/drivers/TCS3472x.cs)
- Mock devices: [MockColorSensorConfig.cs](../src/drivers/MockColorSensorConfig.cs), [MockServoMotor.cs](../src/drivers/MockServoMotor.cs)

## Related

- [Quickstart](./Quickstart.md)
- [Configuration](./Configuration.md)