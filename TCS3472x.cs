using System;
using System.Device.I2c;
using System.Threading;

namespace robot_firmware
{
    /// <summary>
    /// TCS3472x RGB Color Sensor
    /// </summary>
    public class TCS3472x : IDisposable
    {
        private readonly I2cDevice _i2c;
        private const int I2C_ADDRESS = 0x29;

        public TCS3472x(int busId = 1)
        {
            _i2c = I2cDevice.Create(new I2cConnectionSettings(busId, I2C_ADDRESS));
            Initialize();
        }

        private void Initialize()
        {
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
        /// Reads RGBC values from the sensor
        /// </summary>
        public (ushort Clear, ushort Red, ushort Green, ushort Blue) ReadColor()
        {
            ushort clear = ReadWord(0x14);
            ushort red = ReadWord(0x16);
            ushort green = ReadWord(0x18);
            ushort blue = ReadWord(0x1A);

            return (clear, red, green, blue);
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
            _i2c?.Dispose();
        }
    }
}
