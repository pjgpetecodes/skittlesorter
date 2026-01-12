using System;
using System.Collections.Generic;
using System.Device.I2c;
using System.Threading;

namespace robot_firmware
{
    /// <summary>
    /// TCS3472x RGB Color Sensor (with mock mode support)
    /// </summary>
    public class TCS3472x : IDisposable
    {
        private readonly I2cDevice? _i2c;
        private const int I2C_ADDRESS = 0x29;
        private readonly bool _isMockMode;
        private readonly List<string> _mockColorSequence;
        private int _mockColorIndex = 0;

        public TCS3472x(int busId = 1)
        {
            _i2c = I2cDevice.Create(new I2cConnectionSettings(busId, I2C_ADDRESS));
            _isMockMode = false;
            _mockColorSequence = new List<string>();
            Initialize();
        }

        /// <summary>
        /// Constructor for mock mode
        /// </summary>
        public TCS3472x(bool mockMode, List<string> mockColorSequence)
        {
            _isMockMode = mockMode;
            _mockColorSequence = mockColorSequence ?? new List<string> { "Red", "Green", "Yellow" };
            _i2c = null!;
            
            if (_isMockMode)
            {
                Console.WriteLine("[MOCK] Color sensor initialized in mock mode");
            }
        }

        private void Initialize()
        {
            if (_isMockMode)
                return;

            WriteRegister(0x00, 0x01); // PON (Power ON)
            Thread.Sleep(3);
            WriteRegister(0x00, 0x03); // PON + AEN (Enable RGBC)
            WriteRegister(0x01, 0xC0); // Integration time 154ms
            WriteRegister(0x0F, 0x02); // Gain 16x
        }

        private void WriteRegister(byte register, byte value)
        {
            _i2c.Write(new byte[] { (byte)(0x80 | register), value });
        }

        private ushort ReadWord(byte lowRegister)
        {
            Span<byte> data = stackalloc byte[2];
            _i2c.WriteRead(new byte[] { (byte)(0x80 | lowRegister) }, data);
            return (ushort)(data[1] << 8 | data[0]);
        }

        /// <summary>
        /// Reads RGBC values from the sensor (or mock data if in mock mode)
        /// </summary>
        public (ushort Clear, ushort Red, ushort Green, ushort Blue) ReadColor()
        {
            if (_isMockMode)
            {
                return GetMockColor();
            }

            ushort clear = ReadWord(0x14);
            ushort red = ReadWord(0x16);
            ushort green = ReadWord(0x18);
            ushort blue = ReadWord(0x1A);

            return (clear, red, green, blue);
        }

        /// <summary>
        /// Returns mock color data that will classify to the expected color
        /// </summary>
        private (ushort Clear, ushort Red, ushort Green, ushort Blue) GetMockColor()
        {
            if (_mockColorSequence.Count == 0)
                return (30000, 12000, 10000, 7000); // Default neutral

            string nextColor = _mockColorSequence[_mockColorIndex % _mockColorSequence.Count];
            _mockColorIndex++;

            // Return hardcoded RGBC values that will classify to the desired color
            return nextColor switch
            {
                "Red" => (25000, 13000, 7000, 5000),      // gr=0.54, br=0.38 → Red
                "Green" => (24000, 10000, 9000, 4500),    // gr=0.90, br=0.45 → Green
                "Yellow" => (26000, 12000, 8500, 3000),   // gr=0.71, br=0.25 → Yellow
                "Purple" => (23000, 11000, 8000, 5500),   // gr=0.73, br=0.50 → Purple
                "Orange" => (22000, 14000, 7000, 4000),   // gr=0.50, br=0.29 → Orange
                "None" => (65535, 22000, 16000, 11000),   // Saturated → None
                _ => (30000, 12000, 10000, 7000)          // Default neutral
            };
        }

        /// <summary>
        /// Classifies the color based on RGBC values
        /// </summary>
        public string ClassifySkittleColor(ushort red, ushort green, ushort blue, ushort clear)
        {
            double gr = (double)green / red;
            double br = (double)blue / red;

            // True saturated nothing
            if (clear == 65535)
                return "None";

            // Very bright background nothing
            if (clear > 60000 && red > 20000 && green > 15000 && blue > 10000)
                return "None";

            // Mid-brightness background (chute colour)
            if (clear > 28000 && clear < 35000 &&
                red > 11000 && red < 14000 &&
                green > 9000 && green < 12000 &&
                blue > 6000 && blue < 8000)
            {
                return "None";
            }

            // Low-brightness background (chute colour) - empty sensor readings
            // But exclude actual skittles with strong color ratios
            if (clear > 10000 && clear < 25000 &&
                red > 5000 && red < 10000 &&
                green > 3500 && green < 7000 &&
                blue > 2500 && blue < 5500 &&
                gr < 0.80 && br < 0.50)  // Exclude purple/strong colors
            {
                return "None";
            }

            // From here on, trust colour ratios

            if (gr > 0.82)
                return "Green";

            if (gr > 0.72 && br > 0.48)
                return "Purple";

            if (gr > 0.66 && br < 0.40)
                return "Yellow";

            if (gr > 0.53 && br > 0.36)
                return "Red";

            if (gr > 0.48 && br < 0.38)
                return "Orange";

            return "Unknown";
        }

        public void Dispose()
        {
            if (!_isMockMode)
            {
                _i2c?.Dispose();
            }
        }
    }
}
