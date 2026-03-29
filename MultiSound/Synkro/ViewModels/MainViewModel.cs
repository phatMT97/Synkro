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
    private AudioOutputChannel? _channel;
    private readonly AudioRouter _router;
    private readonly DeviceMonitorService _deviceMonitor;
    private readonly Action _onSettingsChanged;
    private string _selectedDeviceId = string.Empty;
    private int _fineTuneMs;

    public ObservableCollection<DeviceInfo> AvailableDevices { get; }

    public string SelectedDeviceId
    {
        get => _selectedDeviceId;
        set
        {
            if (_selectedDeviceId == value) return;
            var oldId = _selectedDeviceId;
            _selectedDeviceId = value;
            OnPropertyChanged();
            OnDeviceChanged(oldId);
        }
    }

    public string DeviceId => _channel?.DeviceId ?? string.Empty;
    public string Name => _channel != null
        ? (_channel.IsBluetooth ? $"{_channel.FriendlyName} [BT]" : _channel.FriendlyName)
        : string.Empty;
    public bool IsBluetooth => _channel?.IsBluetooth ?? false;
    public string ErrorMessage => _channel?.ErrorMessage ?? string.Empty;

    public float Volume
    {
        get => _channel?.Volume ?? 1.0f;
        set
        {
            if (_channel == null) return;
            _channel.Volume = value;
            OnPropertyChanged();
            _onSettingsChanged();
        }
    }

    public int FineTuneMs
    {
        get => _fineTuneMs;
        set
        {
            _fineTuneMs = value;
            if (_channel != null)
            {
                _channel.FineTuneMs = value;
                _router.ApplyAutoDelay();
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(DelayMs));
            _onSettingsChanged();
        }
    }

    public int DelayMs => _channel?.DelayMs ?? 0;

    public ChannelMode ChannelMode
    {
        get => _channel?.ChannelMode ?? ChannelMode.Stereo;
        set
        {
            if (_channel == null) return;
            _channel.ChannelMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStereo));
            OnPropertyChanged(nameof(IsLeft));
            OnPropertyChanged(nameof(IsRight));
            _onSettingsChanged();
        }
    }

    public bool IsStereo
    {
        get => ChannelMode == ChannelMode.Stereo;
        set { if (value) ChannelMode = ChannelMode.Stereo; }
    }

    public bool IsLeft
    {
        get => ChannelMode == ChannelMode.Left;
        set { if (value) ChannelMode = ChannelMode.Left; }
    }

    public bool IsRight
    {
        get => ChannelMode == ChannelMode.Right;
        set { if (value) ChannelMode = ChannelMode.Right; }
    }

    public ChannelViewModel(AudioRouter router, DeviceMonitorService deviceMonitor,
        ObservableCollection<DeviceInfo> availableDevices, Action onSettingsChanged)
    {
        _router = router;
        _deviceMonitor = deviceMonitor;
        AvailableDevices = availableDevices;
        _onSettingsChanged = onSettingsChanged;
    }

    /// <summary>
    /// Attach to an existing router channel (used during load).
    /// </summary>
    public void AttachChannel(AudioOutputChannel channel)
    {
        _channel = channel;
        _selectedDeviceId = channel.DeviceId;
        _fineTuneMs = channel.FineTuneMs;
        NotifyAllProperties();
    }

    private void OnDeviceChanged(string oldDeviceId)
    {
        if (!string.IsNullOrEmpty(oldDeviceId))
        {
            _channel?.Stop();
            _router.RemoveDevice(oldDeviceId);
            _channel = null;
        }

        if (string.IsNullOrEmpty(_selectedDeviceId)) return;

        var mmDevice = _deviceMonitor.GetDeviceById(_selectedDeviceId);
        if (mmDevice == null) return;

        var channel = _router.AddDevice(mmDevice);
        channel.Volume = Volume;
        channel.FineTuneMs = _fineTuneMs;
        channel.ChannelMode = ChannelMode;
        channel.IsEnabled = true;
        _channel = channel;

        NotifyAllProperties();
        _onSettingsChanged();
    }

    public void RefreshDelay()
    {
        OnPropertyChanged(nameof(DelayMs));
    }

    private void NotifyAllProperties()
    {
        OnPropertyChanged(nameof(DeviceId));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsBluetooth));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(DelayMs));
        OnPropertyChanged(nameof(FineTuneMs));
        OnPropertyChanged(nameof(ChannelMode));
        OnPropertyChanged(nameof(IsStereo));
        OnPropertyChanged(nameof(IsLeft));
        OnPropertyChanged(nameof(IsRight));
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
    private AppSettings _settings;
    private bool _isPlaying;
    private string _status = "Stopped";
    private CaptureDeviceItem? _selectedCaptureDevice;

    public ObservableCollection<ChannelViewModel> OutputSlots { get; } = new();
    public ObservableCollection<DeviceInfo> AvailableDevices { get; } = new();
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
        }
    }

    public ICommand StartStopCommand { get; }
    public ICommand AddDeviceCommand { get; }

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _captureEngine = new AudioCaptureEngine();
        _router = new AudioRouter(_captureEngine);
        _deviceMonitor = new DeviceMonitorService();

        StartStopCommand = new RelayCommand(TogglePlayback);
        AddDeviceCommand = new RelayCommand(AddDevice);

        _deviceMonitor.DeviceRemoved += OnDeviceRemoved;
        _deviceMonitor.DeviceAdded += OnDeviceAdded;
        _deviceMonitor.DefaultDeviceChanged += OnDefaultDeviceChanged;

        LoadCaptureDevices();
        RefreshAvailableDevices();
        LoadOutputSlots();
    }

    private void LoadCaptureDevices()
    {
        CaptureDevices.Clear();

        using var enumerator = new MMDeviceEnumerator();

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

    private void RefreshAvailableDevices()
    {
        AvailableDevices.Clear();
        var devices = _deviceMonitor.GetOutputDevices();
        foreach (var d in devices)
            AvailableDevices.Add(d);
    }

    private void LoadOutputSlots()
    {
        foreach (var slotSettings in _settings.OutputSlots)
        {
            // Verify the device still exists
            if (!AvailableDevices.Any(d => d.Id == slotSettings.DeviceId))
                continue;

            var mmDevice = _deviceMonitor.GetDeviceById(slotSettings.DeviceId);
            if (mmDevice == null) continue;

            var channel = _router.AddDevice(mmDevice);
            channel.Volume = slotSettings.Volume;
            channel.FineTuneMs = slotSettings.FineTuneMs;
            channel.ChannelMode = (ChannelMode)slotSettings.ChannelMode;
            channel.IsEnabled = true;

            var vm = new ChannelViewModel(_router, _deviceMonitor, AvailableDevices, SaveSettings);
            vm.AttachChannel(channel);
            OutputSlots.Add(vm);
        }
    }

    private string GetActiveCaptureDeviceId()
    {
        if (_selectedCaptureDevice == null || _selectedCaptureDevice.IsDefault)
        {
            using var enumerator = new MMDeviceEnumerator();
            try
            {
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return device.ID;
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

    public void AddDevice()
    {
        // Pick the first available device that's not already used
        var usedIds = OutputSlots.Select(s => s.DeviceId).ToHashSet();
        var firstAvailable = AvailableDevices.FirstOrDefault(d => !usedIds.Contains(d.Id));

        if (firstAvailable == null && AvailableDevices.Count > 0)
            firstAvailable = AvailableDevices[0]; // allow duplicates if all are used

        if (firstAvailable == null) return;

        var mmDevice = _deviceMonitor.GetDeviceById(firstAvailable.Id);
        if (mmDevice == null) return;

        var channel = _router.AddDevice(mmDevice);
        channel.IsEnabled = true;

        if (IsPlaying && _captureEngine.WaveFormat != null)
        {
            channel.Initialize(_captureEngine.WaveFormat);
            channel.Start();
            _router.ApplyAutoDelay();
        }

        var vm = new ChannelViewModel(_router, _deviceMonitor, AvailableDevices, SaveSettings);
        vm.AttachChannel(channel);
        OutputSlots.Add(vm);
        SaveSettings();
    }

    public void RemoveDevice(ChannelViewModel slot)
    {
        var deviceId = slot.DeviceId;
        OutputSlots.Remove(slot);
        if (!string.IsNullOrEmpty(deviceId))
            _router.RemoveDevice(deviceId);
        SaveSettings();
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
            StartPlayback(GetSelectedCaptureMMDevice());
        }
    }

    private void RestartWithNewCaptureDevice()
    {
        _router.StopAll();
        _captureEngine.Stop();
        IsPlaying = false;
        StartPlayback(GetSelectedCaptureMMDevice());
    }

    private void StartPlayback(MMDevice? captureDevice)
    {
        _captureEngine.Start(captureDevice);
        _router.CaptureSourceDeviceId = GetActiveCaptureDeviceId();
        _router.CaptureChannels = _captureEngine.WaveFormat?.Channels ?? 2;
        _router.InitializeAll();
        _router.StartAll();
        _router.ApplyAutoDelay();
        IsPlaying = true;
        Status = "Playing System Audio";

        // Refresh delay display on all slots
        foreach (var slot in OutputSlots)
            slot.RefreshDelay();
    }

    private void OnDeviceRemoved(object? sender, string deviceId)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            // Remove from available devices
            var dev = AvailableDevices.FirstOrDefault(d => d.Id == deviceId);
            if (dev != null) AvailableDevices.Remove(dev);

            // Remove any output slots using this device
            var slotsToRemove = OutputSlots.Where(s => s.DeviceId == deviceId).ToList();
            foreach (var slot in slotsToRemove)
            {
                OutputSlots.Remove(slot);
                _router.RemoveDevice(deviceId);
            }
        });
    }

    private void OnDeviceAdded(object? sender, DeviceInfo deviceInfo)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (!AvailableDevices.Any(d => d.Id == deviceInfo.Id))
                AvailableDevices.Add(deviceInfo);
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
                    _router.CaptureSourceDeviceId = GetActiveCaptureDeviceId();
                    _router.InitializeAll();
                    _router.StartAll();
                    _router.ApplyAutoDelay();

                    foreach (var slot in OutputSlots)
                        slot.RefreshDelay();
                }
            });
        }
    }

    private void SaveSettings()
    {
        _settings.OutputSlots.Clear();
        foreach (var slot in OutputSlots)
        {
            if (string.IsNullOrEmpty(slot.DeviceId)) continue;
            _settings.OutputSlots.Add(new OutputSlotSettings
            {
                DeviceId = slot.DeviceId,
                Volume = slot.Volume,
                FineTuneMs = slot.FineTuneMs,
                ChannelMode = (int)slot.ChannelMode
            });
        }
        _settingsService.Save(_settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        SaveSettings();
        _router.Dispose();
        _captureEngine.Dispose();
        _deviceMonitor.Dispose();
    }
}
