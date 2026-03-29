namespace Synkro.Models;

public class AppSettings
{
    public bool MinimizeToTray { get; set; } = true;
    public string? CaptureDeviceId { get; set; }
    public List<OutputSlotSettings> OutputSlots { get; set; } = new();
}

public class OutputSlotSettings
{
    public string DeviceId { get; set; } = string.Empty;
    public float Volume { get; set; } = 1.0f;
    public int FineTuneMs { get; set; } = 0;
    public int ChannelMode { get; set; } = 0;
}
