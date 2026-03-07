namespace skittle_sorter
{
    public class ServoSystemHardwareConfig
    {
        public ServoHardwareConfig Arm { get; set; } = new ServoHardwareConfig
        {
            UseSoftwarePwm = false,
            PwmChip = 0,
            PwmChannel = 0,
            FrequencyHz = 50,
            AngleRange = 180,
            MinPulseWidthMicroseconds = 700,
            MaxPulseWidthMicroseconds = 2400,
            HomeAngle = 0
        };

        public ServoHardwareConfig Chute { get; set; } = new ServoHardwareConfig
        {
            UseSoftwarePwm = false,
            PwmChip = 0,
            PwmChannel = 1,
            FrequencyHz = 50,
            AngleRange = 180,
            MinPulseWidthMicroseconds = 700,
            MaxPulseWidthMicroseconds = 2400,
            HomeAngle = 22
        };
    }
}
