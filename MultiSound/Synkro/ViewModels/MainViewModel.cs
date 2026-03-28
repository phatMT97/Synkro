using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Synkro.Core;
using Synkro.Models;
using Synkro.Services;
using NAudio.CoreAudioApi;

namespace Synkro.ViewModels;

public class ChannelViewModel : INotifyPropertyChanged
{
    private readonly AudioOutputChannel _channel;
    private readonly Action _onSettingsChanged;
    private readonly Action _onUserVolumeChanged;
    private bool _isEnabled;

    /// <summary>User-configured base volume. System volume scaling is applied on top of this.</summary>
    public float BaseVolume { get; set; } = 1.0f;

    public string DeviceId => _channel.DeviceId;
    public string Name => _channel.IsBluetooth
        ? $"{_channel.FriendlyName} [BT]"
        : _channel.FriendlyName;
    public bool IsCaptureSource { get; set; }
    public bool IsBluetooth => _channel.IsBluetooth;
    public string ErrorMessage => _channel.ErrorMessage;
    public string DeviceType => _channel.IsBluetooth ? "Bluetooth ~180ms" : "Wired ~10ms";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            _channel.IsEnabled = value;
            OnPropertyChanged();
            _onSettingsChanged();
        }
    }

    public float Volume
    {
        get => _channel.Volume;
        set
        {
            _channel.Volume = value;
            BaseVolume = value; // user manually set → update base
            OnPropertyChanged();
            _onSettingsChanged();
            _onUserVolumeChanged();
        }
    }

    /// <summary>Set volume from system sync without updating BaseVolume.</summary>
    public void SetSyncedVolume(float vol)
    {
        _channel.Volume = vol;
        OnPropertyChanged(nameof(Volume));
    }

    public int DelayMs => _channel.DelayMs;

    public ChannelViewModel(AudioOutputChannel channel, Action onSettingsChanged,
                            Action onUserVolumeChanged, bool enabled = true)
    {
        _channel = channel;
        _onSettingsChanged = onSettingsChanged;
        _onUserVolumeChanged = onUserVolumeChanged;
        _isEnabled = enabled;
        _channel.IsEnabled = enabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public class CaptureDeviceItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsDefault { get; init; }
    public override string ToString() => IsDefault ? $"{Name} (Default)" : Name;
}

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioCaptureEngine _captureEngine;
    private readonly AudioRouter _router;
    private readonly DeviceMonitorService _deviceMonitor;
    private readonly SettingsService _settingsService;
    private readonly VolumeSyncService _volumeSync;
    private float _pendingScale;
    private System.Threading.Timer? _syncDebounce;
    private AppSettings _settings;
    private bool _isPlaying;
    private bool _volumeSyncEnabled = false;
    private int _globalFineTuneMs;
    private string _status = "Stopped";
    private CaptureDeviceItem? _selectedCaptureDevice;

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<CaptureDeviceItem> CaptureDevices { get; } = new();

    public bool IsPlaying
    {
        get => _isPlaying;
        private set { _isPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartStopText)); }
    }

    public string StartStopText => IsPlaying ? "Stop" : "Start";

    public bool MinimizeToTray => _settings.MinimizeToTray;

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public CaptureDeviceItem? SelectedCaptureDevice
    {
        get => _selectedCaptureDevice;
        set
        {
            if (_selectedCaptureDevice?.Id == value?.Id) return;
            _selectedCaptureDevice = value;
            OnPropertyChanged();

            if (value != null)
            {
                _settings.CaptureDeviceId = value.IsDefault ? null : value.Id;
                _settingsService.Save(_settings);
            }

            if (IsPlaying)
                RestartWithNewCaptureDevice();
            else
                RefreshCaptureSource();
        }
    }

    public int GlobalFineTuneMs
    {
        get => _globalFineTuneMs;
        set
        {
            _globalFineTuneMs = value;
            _router.GlobalFineTuneMs = value;
            _router.ApplyAutoDelay();
            _settings.GlobalFineTuneOffsetMs = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    public bool VolumeSyncEnabled
    {
        get => _volumeSyncEnabled;
        set
        {
            _volumeSyncEnabled = value;
            if (value)
            {
                // Capture current state as reference point
                _volumeSync.MarkReferenceVolume();
                foreach (var ch in Channels)
                    ch.BaseVolume = ch.Volume;
            }
            OnPropertyChanged();
        }
    }

    public ICommand StartStopCommand { get; }

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _captureEngine = new AudioCaptureEngine();
        _router = new AudioRouter(_captureEngine);
        _deviceMonitor = new DeviceMonitorService();
        _volumeSync = new VolumeSyncService();
        _globalFineTuneMs = _settings.GlobalFineTuneOffsetMs;
        _router.GlobalFineTuneMs = _globalFineTuneMs;

        StartStopCommand = new RelayCommand(TogglePlayback);

        _deviceMonitor.DeviceRemoved += OnDeviceRemoved;
        _deviceMonitor.DeviceAdded += OnDeviceAdded;
        _deviceMonitor.DefaultDeviceChanged += OnDefaultDeviceChanged;
        _volumeSync.VolumeScaleChanged += OnSystemVolumeChanged;
        _volumeSync.Start();

        LoadCaptureDevices();
        LoadDevices();
    }

    private void LoadCaptureDevices()
    {
        CaptureDevices.Clear();

        using var enumerator = new MMDeviceEnumerator();
        string defaultId;
        try
        {
            var defaultDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = defaultDev.ID;
        }
        catch
        {
            defaultId = "";
        }

        // "Auto" option uses the system default
        CaptureDevices.Add(new CaptureDeviceItem
        {
            Id = "__default__",
            Name = "Auto (System Default)",
            IsDefault = true
        });

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            CaptureDevices.Add(new CaptureDeviceItem
            {
                Id = device.ID,
                Name = device.FriendlyName,
                IsDefault = false
            });
        }

        // Restore saved selection
        if (_settings.CaptureDeviceId != null)
        {
            var saved = CaptureDevices.FirstOrDefault(d => d.Id == _settings.CaptureDeviceId);
            _selectedCaptureDevice = saved ?? CaptureDevices[0];
        }
        else
        {
            _selectedCaptureDevice = CaptureDevices[0]; // Auto
        }
    }

    private string GetActiveCaptureDeviceId()
    {
        if (_selectedCaptureDevice == null || _selectedCaptureDevice.IsDefault)
        {
            using var enumerator = new MMDeviceEnumerator();
            try
            {
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            }
            catch { return ""; }
        }
        return _selectedCaptureDevice.Id;
    }

    private MMDevice? GetSelectedCaptureMMDevice()
    {
        if (_selectedCaptureDevice == null || _selectedCaptureDevice.IsDefault)
            return null; // null = use default
        return _deviceMonitor.GetDeviceById(_selectedCaptureDevice.Id);
    }

    private void OnUserVolumeChanged()
    {
        // User manually changed a channel volume → update reference point
        _volumeSync.MarkReferenceVolume();
    }

    private void OnSystemVolumeChanged(object? sender, float scale)
    {
        if (!_volumeSyncEnabled) return;

        // Debounce rapid volume changes (e.g. slider drag) to avoid dispatcher starvation
        _pendingScale = scale;
        _syncDebounce?.Dispose();
        _syncDebounce = new System.Threading.Timer(_ =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                foreach (var ch in Channels)
                {
                    if (ch.IsEnabled && !ch.IsCaptureSource && !ch.IsBluetooth)
                    {
                        float newVol = Math.Clamp(ch.BaseVolume * _pendingScale, 0f, AudioOutputChannel.MaxVolume);
                        ch.SetSyncedVolume(newVol);
                    }
                }
            });
        }, null, 75, Timeout.Infinite);
    }

    private void LoadDevices()
    {
        var captureDeviceId = GetActiveCaptureDeviceId();
        var devices = _deviceMonitor.GetOutputDevices();

        foreach (var deviceInfo in devices)
        {
            var mmDevice = _deviceMonitor.GetDeviceById(deviceInfo.Id);
            if (mmDevice == null) continue;

            var channel = _router.AddDevice(mmDevice);
            bool isCaptureSource = deviceInfo.Id == captureDeviceId;

            // Apply saved settings, but always disable capture source to prevent echo
            bool enabled;
            if (_settings.Devices.TryGetValue(deviceInfo.Id, out var saved))
            {
                channel.Volume = saved.Volume;
                channel.DelayMs = saved.DelayOffsetMs;
                // Override saved setting: capture source must be disabled
                enabled = isCaptureSource ? false : saved.Enabled;
            }
            else
            {
                enabled = !isCaptureSource;
            }

            var vm = new ChannelViewModel(channel, SaveSettings, OnUserVolumeChanged, enabled)
            {
                IsCaptureSource = isCaptureSource,
                BaseVolume = channel.Volume
            };
            Channels.Add(vm);
        }
    }

    /// <summary>Update which channel is marked as capture source without reloading.</summary>
    private void RefreshCaptureSource()
    {
        var captureId = GetActiveCaptureDeviceId();
        foreach (var ch in Channels)
        {
            ch.IsCaptureSource = ch.DeviceId == captureId;
        }
    }

    private void RestartWithNewCaptureDevice()
    {
        // Full restart: stop → reload capture source → start
        _router.StopAll();
        _captureEngine.Stop();
        IsPlaying = false;

        RefreshCaptureSource();

        _captureEngine.Start(GetSelectedCaptureMMDevice());
        _router.InitializeAll();
        _router.StartAll();
        IsPlaying = true;
        Status = "Playing System Audio";
    }

    private void TogglePlayback()
    {
        if (IsPlaying)
        {
            _router.StopAll();
            _captureEngine.Stop();
            IsPlaying = false;
            Status = "Stopped";
        }
        else
        {
            _captureEngine.Start(GetSelectedCaptureMMDevice());
            _router.InitializeAll();
            _router.StartAll();
            IsPlaying = true;
            Status = "Playing System Audio";
        }
    }

    private void OnDeviceRemoved(object? sender, string deviceId)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var vm = Channels.FirstOrDefault(c => c.DeviceId == deviceId);
            if (vm != null) Channels.Remove(vm);
            _router.RemoveDevice(deviceId);
        });
    }

    private void OnDeviceAdded(object? sender, DeviceInfo deviceInfo)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var mmDevice = _deviceMonitor.GetDeviceById(deviceInfo.Id);
            if (mmDevice == null) return;

            var channel = _router.AddDevice(mmDevice);

            if (_settings.Devices.TryGetValue(deviceInfo.Id, out var saved))
            {
                channel.Volume = saved.Volume;
                channel.DelayMs = saved.DelayOffsetMs;
            }

            if (IsPlaying && _captureEngine.WaveFormat != null)
            {
                channel.Initialize(_captureEngine.WaveFormat);
                channel.Start();
            }

            Channels.Add(new ChannelViewModel(channel, SaveSettings, OnUserVolumeChanged));
        });
    }

    private void OnDefaultDeviceChanged(object? sender, string defaultDeviceId)
    {
        if (IsPlaying)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                // Only auto-restart capture if using Auto (default) mode
                if (_selectedCaptureDevice == null || _selectedCaptureDevice.IsDefault)
                {
                    _captureEngine.Stop();
                    _captureEngine.Start();
                    _router.InitializeAll();
                    RefreshCaptureSource();
                }
            });
        }
    }

    private void SaveSettings()
    {
        _settings.Devices.Clear();
        foreach (var ch in Channels)
        {
            _settings.Devices[ch.DeviceId] = new DeviceSettings
            {
                FriendlyName = ch.Name,
                Enabled = ch.IsEnabled,
                Volume = ch.Volume,
                DelayOffsetMs = ch.DelayMs
            };
        }
        _settingsService.Save(_settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        SaveSettings();
        _syncDebounce?.Dispose();
        _volumeSync.Dispose();
        _router.Dispose();
        _captureEngine.Dispose();
        _deviceMonitor.Dispose();
    }
}
