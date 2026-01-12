using System;

namespace robot_firmware
{
    /// <summary>
    /// Mock servo motor for testing without hardware
    /// Mimics the ServoMotor interface but performs no actual motor operations
    /// </summary>
    public class MockServoMotor : IDisposable
    {
        private int _currentAngle = 0;
        private string _servoName;

        public MockServoMotor(string name = "Servo")
        {
            _servoName = name;
            Console.WriteLine($"[MOCK] {_servoName} initialized (mock mode)");
        }

        public void Start()
        {
            Console.WriteLine($"[MOCK] {_servoName}.Start() called");
        }

        public void Stop()
        {
            Console.WriteLine($"[MOCK] {_servoName}.Stop() called");
        }

        public void WriteAngle(int angle)
        {
            _currentAngle = angle;
            Console.WriteLine($"[MOCK] {_servoName}.WriteAngle({angle}°)");
        }

        public void Dispose()
        {
            Console.WriteLine($"[MOCK] {_servoName} disposed");
        }
    }
}
