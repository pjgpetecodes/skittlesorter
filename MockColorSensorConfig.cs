using System;
using System.Collections.Generic;

namespace robot_firmware
{
    /// <summary>
    /// Configuration for mock hardware mode
    /// </summary>
    public class MockColorSensorConfig
    {
        /// <summary>
        /// Enable mock mode for color sensor instead of real hardware
        /// </summary>
        public bool EnableMockColorSensor { get; set; } = false;

        /// <summary>
        /// Enable mock mode for PWM/Servos instead of real hardware
        /// </summary>
        public bool EnableMockServos { get; set; } = false;

        /// <summary>
        /// Sequence of colors to return from the mock sensor
        /// Cycles through the list repeatedly
        /// </summary>
        public List<string> MockColorSequence { get; set; } = new()
        {
            "Red",
            "Green",
            "Yellow",
            "Purple",
            "Orange",
            "Red",
            "Green"
        };
    }
}
