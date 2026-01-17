using System;
using System.Collections.Generic;

namespace skittle_sorter
{
    public class ServoController
    {
        private readonly Dictionary<string, int> _colorAngles;
        private readonly bool _useMockServo;

        public ServoController(bool useMockServo = false, Dictionary<string, int>? chutePositions = null)
        {
            _useMockServo = useMockServo;
            _colorAngles = chutePositions ?? new Dictionary<string, int>
            {
                { "red", 22 },
                { "green", 44 },
                { "purple", 66 },
                { "yellow", 88 },
                { "orange", 112 }
            };
        }

        public void MoveToColor(string color)
        {
            if (_colorAngles.TryGetValue(color.ToLower(), out int angle))
            {
                MoveToAngle(angle);
            }
            else
            {
                Console.WriteLine($"[Servo] Unknown color: {color}");
            }
        }

        public void MoveToAngle(int angle)
        {
            if (angle < 0 || angle > 360)
            {
                Console.WriteLine($"[Servo] Invalid angle: {angle}");
                return;
            }

            Console.WriteLine($"[Servo] Moving to angle: {angle}°");
            // In real hardware, this would interface with ServoMotor/PwmChannel
            // In mock mode, this just logs
        }

        public void Home()
        {
            MoveToAngle(0);
        }

        public void Stop()
        {
            Console.WriteLine("[Servo] Stopping");
        }

        public int GetAngleForColor(string color)
        {
            return _colorAngles.TryGetValue(color.ToLower(), out int angle) ? angle : -1;
        }

        public Dictionary<string, int> GetColorAngles()
        {
            return new Dictionary<string, int>(_colorAngles);
        }
    }
}
