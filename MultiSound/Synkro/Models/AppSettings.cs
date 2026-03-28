namespace Synkro.Models;

public class AppSettings
{
    public bool MinimizeToTray { get; set; } = true;
    public int GlobalFineTuneOffsetMs { get; set; } = 0;
    public string? CaptureDeviceId { get; set; }
    public Dictionary<string, DeviceSettings> Devices { get; set; } = new();
}

public class DeviceSettings
{
    public string FriendlyName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public float Volume { get; set; } = 1.0f;
    public int DelayOffsetMs { get; set; } = 0;
}
