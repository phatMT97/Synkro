using System.Text.Json;
using Xunit;

namespace Synkro.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void RoundTrip_SerializeDeserialize_PreservesValues()
    {
        var settings = new Synkro.Models.AppSettings
        {
            MinimizeToTray = true,
            GlobalFineTuneOffsetMs = 12,
            Devices = new Dictionary<string, Synkro.Models.DeviceSettings>
            {
                ["device-id-123"] = new()
                {
                    FriendlyName = "Realtek Audio",
                    Enabled = true,
                    Volume = 0.8f,
                    DelayOffsetMs = 15
                }
            }
        };

        var json = JsonSerializer.Serialize(settings);
        var deserialized = JsonSerializer.Deserialize<Synkro.Models.AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.MinimizeToTray);
        Assert.Equal(12, deserialized.GlobalFineTuneOffsetMs);
        Assert.True(deserialized.Devices.ContainsKey("device-id-123"));
        Assert.Equal("Realtek Audio", deserialized.Devices["device-id-123"].FriendlyName);
        Assert.Equal(0.8f, deserialized.Devices["device-id-123"].Volume);
    }

    [Fact]
    public void Defaults_AreReasonable()
    {
        var settings = new Synkro.Models.AppSettings();

        Assert.True(settings.MinimizeToTray);
        Assert.Equal(0, settings.GlobalFineTuneOffsetMs);
        Assert.NotNull(settings.Devices);
        Assert.Empty(settings.Devices);
    }
}
