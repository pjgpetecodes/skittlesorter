# Skittle Sorter

This project drives a 3D printed Skittle Sorter with Azure IoT Hub integration for telemetry and monitoring.

The hardware design is based on the [PTC Education Candy Sorter](https://github.com/PTC-Education/Candy-Sorter/) project.

## Features

- **Color Detection**: Uses TCS3472x color sensor to identify Skittle colors (Red, Green, Yellow, Purple, Orange)
- **Automated Sorting**: Servo motors position and sort Skittles into separate chutes
- **Mock Mode**: Test without physical hardware using mock sensors and servos
- **IoT Hub Integration**: Sends detected Skittle colors with timestamps to Azure IoT Hub for real-time monitoring

## Prerequisites

- .NET 10.0 SDK
- Azure IoT Hub (optional, for telemetry)
- Hardware:
  - Raspberry Pi or compatible device
  - TCS3472x color sensor
  - 2x Servo motors
  - 3D printed sorter components

## Hardware Wiring

### Circuit Diagram

![Circuit Diagram](circuit/circuit.png)

### Wiring Table

| Pi Pin | Item          | Pin |
|--------|---------------|-----|
| 1      | TCS34725      | LED |
| 2      | TCS34725      | VIN |
| 3      | TCS34725      | SDA |
| 5      | TCS34725      | SCL |
| 14     | Servo 1+2     | GND |
| 32     | Servo 1       | Pulse |
| 33     | Servo 2       | Pulse |

## Azure IoT Hub Setup

To enable telemetry to Azure IoT Hub:

1. **Create an IoT Hub** in the Azure Portal
2. **Register a device** in your IoT Hub:
   - Navigate to your IoT Hub → Devices → Add Device
   - Enter a device ID (e.g., `skittlesorter`)
   - Save the device
3. **Get the connection string**:
   - Click on your device
   - Copy the **Primary Connection String**

## Configuration

Create an `appsettings.json` file in the project root with the following structure:

```json
{
  "MockMode": {
    "EnableMockColorSensor": true,
    "EnableMockServos": true,
    "MockColorSequence": [
      "Red",
      "Green",
      "Yellow",
      "Purple",
      "Orange"
    ]
  },
  "IoTHub": {
    "DeviceConnectionString": "HostName=your-hub.azure-devices.net;DeviceId=your-device;SharedAccessKey=***",
    "DeviceId": "your-device-id",
    "SendTelemetry": true
  }
}
```

### Configuration Options

#### MockMode

**When to use Mock Mode:**
- **On a regular PC/laptop**: Set both `EnableMockColorSensor` and `EnableMockServos` to `true`. Standard computers don't have GPIO pins or I2C interfaces required for physical sensors and servos.
- **On a Raspberry Pi with hardware**: Set both to `false` to use the actual TCS3472x color sensor and servo motors connected via GPIO.

Options:
- `EnableMockColorSensor`: Set to `true` to use simulated color readings
- `EnableMockServos`: Set to `true` to simulate servo movements
- `MockColorSequence`: Array of colors to cycle through in mock mode

#### IoTHub
- `DeviceConnectionString`: Your Azure IoT Hub device connection string (from setup step above)
- `DeviceId`: Your device identifier registered in IoT Hub
- `SendTelemetry`: Set to `true` to enable sending telemetry data

**Note**: Replace the placeholder values with your actual Azure IoT Hub credentials. Keep your connection strings secure and never commit them to source control.

## Running the Application

```bash
dotnet restore
dotnet build
dotnet run
```

The application will:
1. Initialize the color sensor and servos
2. Connect to Azure IoT Hub (if enabled)
3. Begin the sorting loop:
   - Pick up a Skittle
   - Read its color
   - Send telemetry to IoT Hub (detected colors only)
   - Position the chute
   - Drop the Skittle
   - Repeat

## Telemetry Format

Each detected Skittle sends a message to IoT Hub with the following structure:

```json
{
  "messageId": 1,
  "deviceId": "skittlesorter",
  "color": "Red",
  "timestamp": "2026-01-12T10:30:45.123Z",
  "detectionTime": "2026-01-12 10:30:45.123"
}
```

Message properties include:
- `colorAlert`: Set to "detected" for all valid Skittle colors

## Development

### Testing Without Hardware (Mock Mode)

Perfect for development and testing on a regular PC where physical hardware isn't available:

1. Set both `EnableMockColorSensor` and `EnableMockServos` to `true` in `appsettings.json`
2. Customize the `MockColorSequence` to test different color patterns
3. The application will simulate the full sorting process, cycling through the specified colors
4. All IoT Hub telemetry will still be sent (if enabled), allowing you to test the cloud integration

### Running on Raspberry Pi with Hardware

When running on a Raspberry Pi with the physical sorter assembled:

1. Set both `EnableMockColorSensor` and `EnableMockServos` to `false`
2. Ensure your TCS3472x sensor is connected via I2C
3. Ensure your servo motors are connected to the appropriate GPIO pins
4. The application will use real hardware for color detection and sorting

## License

See the original [Candy Sorter repository](https://github.com/PTC-Education/Candy-Sorter/) for hardware licensing information.
