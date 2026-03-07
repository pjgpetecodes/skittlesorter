using System;
using System.Collections.Generic;
using Iot.Device.ServoMotor;
using System.Device.Pwm;
using System.Device.Pwm.Drivers;

namespace skittle_sorter
{
    public class ServoController : IDisposable
    {
        private readonly Dictionary<string, int> _colorAngles;
        private readonly bool _useMockServo;
        private readonly ServoHardwareConfig _armConfig;
        private readonly ServoHardwareConfig _chuteConfig;
        private readonly MockServoMotor? _mockArm;
        private readonly MockServoMotor? _mockChute;
        private readonly PwmChannel? _armPwmChannel;
        private readonly PwmChannel? _chutePwmChannel;
        private readonly ServoMotor? _armServo;
        private readonly ServoMotor? _chuteServo;
        private bool _disposed;

        public ServoController(
            bool useMockServo = false,
            Dictionary<string, int>? chutePositions = null,
            ServoSystemHardwareConfig? hardwareConfig = null)
        {
            _useMockServo = useMockServo;
            var effectiveHardware = hardwareConfig ?? new ServoSystemHardwareConfig();
            _armConfig = effectiveHardware.Arm;
            _chuteConfig = effectiveHardware.Chute;

            // Use case-insensitive keys so config values like "Red" and runtime values like "red" both resolve.
            _colorAngles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sourceAngles = chutePositions ?? new Dictionary<string, int>
            {
                { "red", 22 },
                { "green", 44 },
                { "purple", 66 },
                { "yellow", 88 },
                { "orange", 112 }
            };
            foreach (var kv in sourceAngles)
            {
                _colorAngles[kv.Key] = kv.Value;
            }

            if (_useMockServo)
            {
                _mockArm = new MockServoMotor("ArmServo");
                _mockChute = new MockServoMotor("ChuteServo");
                _mockArm.Start();
                _mockChute.Start();
                return;
            }

            try
            {
                _armPwmChannel = CreatePwmChannel(_armConfig, "Arm");
                _chutePwmChannel = CreatePwmChannel(_chuteConfig, "Chute");

                _armServo = new ServoMotor(
                    _armPwmChannel,
                    _armConfig.AngleRange,
                    _armConfig.MinPulseWidthMicroseconds,
                    _armConfig.MaxPulseWidthMicroseconds);

                _chuteServo = new ServoMotor(
                    _chutePwmChannel,
                    _chuteConfig.AngleRange,
                    _chuteConfig.MinPulseWidthMicroseconds,
                    _chuteConfig.MaxPulseWidthMicroseconds);

                _armServo.Start();
                _chuteServo.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to initialize real servo. Check PWM pin/channel configuration and permissions. Details: {ex.Message}",
                    ex);
            }
        }

        public void MoveToColor(string color)
        {
            if (_colorAngles.TryGetValue(color, out int angle))
            {
                MoveChuteToAngle(angle);
            }
            else
            {
                Console.WriteLine($"[Servo] Unknown color: {color}");
            }
        }

        public void MoveArmToAngle(int angle)
        {
            if (angle < 0 || angle > _armConfig.AngleRange)
            {
                Console.WriteLine($"[Servo:Arm] Invalid angle: {angle}");
                return;
            }

            Console.WriteLine($"[Servo:Arm] Moving to angle: {angle}°");

            if (_useMockServo)
            {
                _mockArm?.WriteAngle(angle);
                return;
            }

            _armServo?.WriteAngle(angle);
        }

        public void MoveChuteToAngle(int angle)
        {
            if (angle < 0 || angle > _chuteConfig.AngleRange)
            {
                Console.WriteLine($"[Servo:Chute] Invalid angle: {angle}");
                return;
            }

            Console.WriteLine($"[Servo:Chute] Moving to angle: {angle}°");

            if (_useMockServo)
            {
                _mockChute?.WriteAngle(angle);
                return;
            }

            _chuteServo?.WriteAngle(angle);
        }

        // Backward-compatible API: default angle movement controls arm servo.
        public void MoveToAngle(int angle)
        {
            MoveArmToAngle(angle);
        }

        public void Home()
        {
            MoveArmToAngle(_armConfig.HomeAngle);
        }

        public void Stop()
        {
            if (_useMockServo)
            {
                _mockArm?.Stop();
                _mockChute?.Stop();
                return;
            }

            _armServo?.Stop();
            _chuteServo?.Stop();
            _armPwmChannel?.Stop();
            _chutePwmChannel?.Stop();
            Console.WriteLine("[Servo] Stopping");
        }

        public int GetAngleForColor(string color)
        {
            return _colorAngles.TryGetValue(color, out int angle) ? angle : -1;
        }

        public Dictionary<string, int> GetColorAngles()
        {
            return new Dictionary<string, int>(_colorAngles);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _armServo?.Dispose();
            _chuteServo?.Dispose();
            _armPwmChannel?.Dispose();
            _chutePwmChannel?.Dispose();
            _mockArm?.Dispose();
            _mockChute?.Dispose();
            _disposed = true;
        }

        private static PwmChannel CreatePwmChannel(ServoHardwareConfig config, string label)
        {
            if (config.UseSoftwarePwm)
            {
                Console.WriteLine($"[Servo:{label}] Using software PWM on GPIO {config.GpioPin} at {config.FrequencyHz}Hz");
                return new SoftwarePwmChannel(config.GpioPin, config.FrequencyHz, 0.0, false);
            }

            Console.WriteLine($"[Servo:{label}] Using hardware PWM chip {config.PwmChip}, channel {config.PwmChannel} at {config.FrequencyHz}Hz");
            return PwmChannel.Create(config.PwmChip, config.PwmChannel, config.FrequencyHz, 0.0);
        }
    }
}
