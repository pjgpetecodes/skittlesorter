namespace skittle_sorter
{
    public class ServoHardwareConfig
    {
        // Software PWM on GPIO18 is the most common Raspberry Pi setup.
        public bool UseSoftwarePwm { get; set; } = true;
        public int GpioPin { get; set; } = 18;

        // Used only when UseSoftwarePwm is false.
        public int PwmChip { get; set; } = 0;
        public int PwmChannel { get; set; } = 0;

        public int FrequencyHz { get; set; } = 50;
        public int AngleRange { get; set; } = 180;
        public int MinPulseWidthMicroseconds { get; set; } = 500;
        public int MaxPulseWidthMicroseconds { get; set; } = 2500;
        public int HomeAngle { get; set; } = 0;
    }
}
