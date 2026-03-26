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
    private bool _isEnabled;

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
            OnPropertyChanged();
            _onSettingsChanged();
        }
    }

    public int DelayMs => _channel.DelayMs;

    public ChannelViewModel(AudioOutputChannel channel, Action onSettingsChanged, bool enabled = true)
    {
        _channel = channel;
        _onSettingsChanged = onSettingsChanged;
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

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioCaptureEngine _captureEngine;
    private readonly AudioRouter _router;
    private readonly DeviceMonitorService _deviceMonitor;
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private bool _isPlaying;
    private int _globalFineTuneMs;
    private string _status = "Stopped";

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

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

    public ICommand StartStopCommand { get; }

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _captureEngine = new AudioCaptureEngine();
        _router = new AudioRouter(_captureEngine);
        _deviceMonitor = new DeviceMonitorService();
        _globalFineTuneMs = _settings.GlobalFineTuneOffsetMs;
        _router.GlobalFineTuneMs = _globalFineTuneMs;

        StartStopCommand = new RelayCommand(TogglePlayback);

        _deviceMonitor.DeviceRemoved += OnDeviceRemoved;
        _deviceMonitor.DeviceAdded += OnDeviceAdded;
        _deviceMonitor.DefaultDeviceChanged += OnDefaultDeviceChanged;

        LoadDevices();
    }

    private void LoadDevices()
    {
        // Get default device to detect capture source
        using var enumerator = new MMDeviceEnumerator();
        var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var defaultDeviceId = defaultDevice.ID;

        var devices = _deviceMonitor.GetOutputDevices();
        foreach (var deviceInfo in devices)
        {
            var mmDevice = _deviceMonitor.GetDeviceById(deviceInfo.Id);
            if (mmDevice == null) continue;

            var channel = _router.AddDevice(mmDevice);
            bool isCaptureSource = deviceInfo.Id == defaultDeviceId;

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

            var vm = new ChannelViewModel(channel, SaveSettings, enabled)
            {
                IsCaptureSource = isCaptureSource
            };
            Channels.Add(vm);
        }
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
            _captureEngine.Start();
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

            Channels.Add(new ChannelViewModel(channel, SaveSettings));
        });
    }

    private void OnDefaultDeviceChanged(object? sender, string defaultDeviceId)
    {
        if (IsPlaying)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _captureEngine.Stop();
                _captureEngine.Start();
                _router.InitializeAll();
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
        _router.Dispose();
        _captureEngine.Dispose();
        _deviceMonitor.Dispose();
    }
}
