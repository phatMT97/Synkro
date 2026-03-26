using Synkro.Models;
using Synkro.Services;
using Xunit;

namespace Synkro.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"multisound-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _service = new SettingsService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var settings = _service.Load();

        Assert.NotNull(settings);
        Assert.True(settings.MinimizeToTray);
        Assert.Empty(settings.Devices);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var settings = new AppSettings
        {
            MinimizeToTray = false,
            GlobalFineTuneOffsetMs = 25,
            Devices = new Dictionary<string, DeviceSettings>
            {
                ["id-1"] = new() { FriendlyName = "Speaker", Volume = 0.5f }
            }
        };

        _service.Save(settings);
        var loaded = _service.Load();

        Assert.False(loaded.MinimizeToTray);
        Assert.Equal(25, loaded.GlobalFineTuneOffsetMs);
        Assert.Equal(0.5f, loaded.Devices["id-1"].Volume);
    }

    [Fact]
    public void Load_CorruptedFile_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "NOT JSON{{{");

        var settings = _service.Load();

        Assert.NotNull(settings);
        Assert.True(settings.MinimizeToTray);
    }
}
