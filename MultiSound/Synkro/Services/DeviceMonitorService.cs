using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Synkro.Models;

namespace Synkro.Services;

public class DeviceMonitorService : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;

    public event EventHandler<DeviceInfo>? DeviceAdded;
    public event EventHandler<string>? DeviceRemoved;
    public event EventHandler<string>? DefaultDeviceChanged;

    public DeviceMonitorService()
    {
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public List<DeviceInfo> GetOutputDevices()
    {
        var devices = new List<DeviceInfo>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(
            DataFlow.Render, DeviceState.Active))
        {
            devices.Add(new DeviceInfo
            {
                Id = device.ID,
                FriendlyName = device.FriendlyName,
                IsActive = true
            });
        }
        return devices;
    }

    public MMDevice? GetDeviceById(string id)
    {
        try
        {
            return _enumerator.GetDevice(id);
        }
        catch
        {
            return null;
        }
    }

    // IMMNotificationClient
    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        if (newState == DeviceState.Active)
        {
            var device = GetDeviceById(deviceId);
            if (device?.DataFlow == DataFlow.Render)
            {
                DeviceAdded?.Invoke(this, new DeviceInfo
                {
                    Id = deviceId,
                    FriendlyName = device.FriendlyName,
                    IsActive = true
                });
            }
        }
        else
        {
            DeviceRemoved?.Invoke(this, deviceId);
        }
    }

    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId)
    {
        DeviceRemoved?.Invoke(this, deviceId);
    }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
            DefaultDeviceChanged?.Invoke(this, defaultDeviceId);
    }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        try
        {
            _enumerator?.UnregisterEndpointNotificationCallback(this);
            _enumerator?.Dispose();
        }
        catch { }
    }
}
