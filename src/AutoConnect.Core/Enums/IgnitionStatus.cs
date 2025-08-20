namespace AutoConnect.Core.Enums;

public enum IgnitionStatus
{
    Off = 0,
    KL15_On = 1,  // Ignition on (accessories)
    KL30_On = 2,  // Battery power
    Running = 3   // Engine running
}