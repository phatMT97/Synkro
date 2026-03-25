# Multi Sound — Design Spec

## Overview

A Windows desktop app that captures system audio and plays it simultaneously through multiple speakers (wired + Bluetooth), with delay compensation and independent volume control per device.

**Target user:** A user with a wired speaker and a Sony SRS-XB13 Bluetooth speaker who wants synchronized multi-room audio from any Windows application.

## Requirements

- **Capture system audio** via WASAPI Loopback (any app playing sound gets routed)
- **Output to multiple speakers** simultaneously via separate WASAPI output sessions
- **Delay compensation** to synchronize wired (~0ms) and Bluetooth (~150-200ms) speakers
  - Auto-detect latency difference using `IAudioClient.GetStreamLatency()`
  - Manual fine-tune slider (±50ms) for user adjustment by ear
  - Save offset per device name for persistence across sessions
- **Independent volume control** per speaker (0-100%)
- **Auto-fallback** when a device disconnects — continue playing on remaining devices
- **Auto-reconnect** when a device reconnects — resume with saved settings

## Non-Requirements

- No file playback (MP3, FLAC, etc.) — only system audio capture
- No cross-platform — Windows only
- No startup with Windows — manual launch only
- No advanced audio processing (EQ, effects, etc.)

## Tech Stack

| Component       | Choice               | Reason                                           |
|-----------------|----------------------|--------------------------------------------------|
| Language        | C#                   | Native Windows, strong audio library ecosystem   |
| Runtime         | .NET 8 (LTS)        | Windows 10/11 compatible                         |
| Audio library   | NAudio 2.x           | Mature WASAPI Loopback + Output support          |
| GUI framework   | WPF                  | Native Windows, MVVM, system tray support        |
| Settings format | JSON (System.Text.Json) | Simple, human-readable                        |

## Architecture

```
┌─────────────────────────────────────┐
│           System Audio              │
│      (any app playing sound)        │
└──────────────┬──────────────────────┘
               │ WASAPI Loopback Capture
               ▼
┌─────────────────────────────────────┐
│         Audio Capture Engine        │
│   (capture default render device)   │
└──────────────┬──────────────────────┘
               │ PCM audio buffer
               ▼
┌─────────────────────────────────────┐
│         Audio Router / Mixer        │
│  ┌───────────┐  ┌────────────────┐  │
│  │ Channel A │  │   Channel B    │  │
│  │ (Wired)   │  │ (Bluetooth)    │  │
│  │ Volume    │  │ Volume         │  │
│  │ Delay buf │  │ Delay buf      │  │
│  └─────┬─────┘  └──────┬────────┘  │
└────────┼────────────────┼───────────┘
         │                │
         ▼                ▼
   WASAPI Output    WASAPI Output
   (Wired Speaker)  (BT Speaker)
```

### Components

#### AudioCaptureEngine
- Uses `WasapiLoopbackCapture` to capture default render device
- Captures at device's native format (typically 48kHz, 32-bit float, stereo)
- Fires `DataAvailable` event with PCM byte[] buffer
- Runs on its own thread (managed by NAudio)

#### AudioRouter
- Receives PCM data from capture engine
- Distributes to all active `AudioOutputChannel` instances
- Manages channel lifecycle (add/remove on device connect/disconnect)

#### AudioOutputChannel
- Represents one output device (one speaker)
- **Volume control:** Multiplies PCM samples by gain factor (0.0–1.0)
- **Delay buffer:** Circular buffer sized to delay time x sample rate
  - Wired speaker gets delay added to match Bluetooth latency
  - Buffer size for 200ms at 48kHz stereo 32-bit float ≈ 35KB
- Uses `WasapiOut` in **shared mode** (so other apps can still use the device)
- Runs on its own thread (managed by NAudio)

#### DeviceMonitorService
- Uses `MMNotificationClient` (COM callback) to detect device changes
- On device disconnect: stops corresponding `AudioOutputChannel`, marks disconnected
- On device reconnect: recreates channel with saved settings

#### SettingsService
- Persists settings to JSON file in `%AppData%/MultiSound/settings.json`
- Saves per-device: volume, delay offset, enabled state (keyed by device name)
- Loads on startup, saves on change

## Delay Compensation

### Problem
Bluetooth speakers (SRS-XB13 via BT 4.2) have ~150-200ms latency due to codec encoding/decoding. Wired speakers have ~5-10ms. Playing simultaneously causes audible echo.

### Solution

```
Wired:     [===DELAY BUFFER===][---audio data--->]
Bluetooth: [---audio data--->]
           ^                  ^
           sent at same time  heard at same time
```

1. **Auto-detect:** On start, query each output device's stream latency via `IAudioClient.GetStreamLatency()`. Calculate difference. Add delay buffer to the lower-latency device (wired speaker).
2. **Limitation:** `GetStreamLatency()` measures Windows audio pipeline latency only — not Bluetooth codec latency. Accuracy is ~70-80%.
3. **Manual fine-tune:** User adjusts ±50ms slider by ear. Value is saved per device name.

## GUI

### System Tray
- Icon in system tray
- Left click: open/toggle popup window
- Right click: context menu with "Open" / "Exit"

### Popup Window (~300x400px)

```
┌─ Multi Sound ──────────────────┐
│                                │
│  Status: Playing System Audio  │
│                                │
│  ── Wired (Realtek Audio) ──── │
│  Volume: [████████░░] 80%      │
│  Delay:  [██░░░░░░░░] +15ms   │
│                                │
│  ── BT (SRS-XB13) ─────────── │
│  Volume: [██████░░░░] 60%      │
│  Delay:  [░░░░░░░░░░] auto     │
│                                │
│  ── Sync ─────────────────── │ │
│  Auto offset: 165ms            │
│  Fine-tune:  [████░░░] +12ms   │
│                                │
│  [▶ Start]  [⚙ Settings]      │
└────────────────────────────────┘
```

### Behavior
- Minimize to tray when closing window (configurable)
- Device disconnect: auto-remove from list, show notification
- Device reconnect: auto-add with saved settings

## Project Structure

```
MultiSound/
├── MultiSound.sln
├── MultiSound/
│   ├── App.xaml                    # WPF entry point
│   ├── MainWindow.xaml             # Popup window UI
│   ├── Core/
│   │   ├── AudioCaptureEngine.cs   # WASAPI Loopback capture
│   │   ├── AudioOutputChannel.cs   # One output device (volume + delay)
│   │   └── AudioRouter.cs          # Manage capture → multiple outputs
│   ├── Models/
│   │   ├── DeviceInfo.cs           # Output device info
│   │   └── ChannelSettings.cs      # Volume, delay offset per device
│   ├── Services/
│   │   ├── DeviceMonitorService.cs # Monitor device connect/disconnect
│   │   └── SettingsService.cs      # Read/write JSON settings
│   └── ViewModels/
│       └── MainViewModel.cs        # MVVM binding for UI
└── MultiSound.Tests/
    └── ...
```

## Threading Model

| Thread            | Responsibility                        |
|-------------------|---------------------------------------|
| NAudio Capture    | WASAPI Loopback capture loop          |
| NAudio Output (×N)| One per speaker, WASAPI playback loop |
| UI (WPF Dispatcher)| MVVM bindings, user interaction     |
| Device Monitor    | COM callback for device changes       |

No shared mutable state between audio threads — each channel has its own buffer. Volume/delay changes are applied via atomic float writes read by the audio thread.

## Error Handling

- **Device disconnect mid-playback:** `AudioOutputChannel` catches `COMException`, stops gracefully, signals `DeviceMonitorService`
- **No output devices:** App shows "No output devices found" status, keeps capture ready
- **Capture device changes:** If default render device changes, restart capture on new device

## Settings Schema

```json
{
  "minimizeToTray": true,
  "devices": {
    "Realtek Audio": {
      "enabled": true,
      "volume": 0.8,
      "delayOffsetMs": 15
    },
    "SRS-XB13": {
      "enabled": true,
      "volume": 0.6,
      "delayOffsetMs": 0
    }
  }
}
```

## Success Criteria

1. System audio plays through both speakers simultaneously
2. Delay between speakers is imperceptible after auto-detect + fine-tune (<20ms perceived)
3. Volume can be adjusted independently per speaker
4. App handles device disconnect/reconnect gracefully
5. Settings persist across sessions
