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

## Software Hooks
- Sensor: see [TCS3472x.cs](../TCS3472x.cs)
- Mock devices: [MockColorSensorConfig.cs](../MockColorSensorConfig.cs), [MockServoMotor.cs](../MockServoMotor.cs)

## Related

- [Quickstart](./Quickstart.md)
- [Configuration](./Configuration.md)