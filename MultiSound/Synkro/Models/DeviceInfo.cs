namespace Synkro.Models;

public class DeviceInfo
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public bool IsActive { get; set; }
    public long ReportedLatencyMs { get; set; }
    public override string ToString() => FriendlyName;
}
