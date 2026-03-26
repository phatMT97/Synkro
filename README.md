# Synkro

Windows app that captures system audio and plays it through multiple speakers simultaneously with delay compensation and independent volume control.

## What it does

- Captures **all system audio** (music, videos, games — anything playing on your PC)
- Routes it to **multiple speakers** at the same time
- **Syncs audio** between wired and Bluetooth speakers by compensating for Bluetooth latency
- **Independent volume control** per speaker (up to 300%)
- **Auto-detects Bluetooth devices** and estimates latency
- Runs in **system tray** when minimized

## Use case

You have a wired speaker (or monitor speaker) and a Bluetooth speaker. You want both to play your PC audio at the same time, in sync, without one being louder or delayed compared to the other.

## Requirements

- **Windows 10/11** (64-bit)
- **.NET 8 Desktop Runtime** — [Download here](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (select ".NET Desktop Runtime" for Windows x64)
- At least **2 audio output devices** (e.g., wired speaker + Bluetooth speaker)

## How to use

1. **Set your Bluetooth speaker as the default Windows output device** (right-click speaker icon in taskbar → Sound settings)
2. Run `Synkro.exe`
3. Your Bluetooth speaker will show as **"Capture source"** (unchecked) — this is normal, it already plays audio natively through Windows
4. **Check the wired speaker** you want to add
5. Click **Start**
6. Adjust **volume** per speaker with the slider or +/- buttons
7. Adjust **Sync Fine-tune** if the speakers sound out of sync:
   - If wired plays **before** Bluetooth → increase fine-tune (+)
   - If wired plays **after** Bluetooth → decrease fine-tune (-)
   - Typical value: **+100 to +200ms** for most Bluetooth speakers
8. Settings are saved automatically

## Tips

- **SRS-XB13** (Bluetooth 4.2, SBC codec): fine-tune around **+120ms** works well
- Volume above 100% amplifies the signal — useful when the secondary speaker is quieter
- Close button asks: **Yes** = minimize to tray, **No** = exit, **Cancel** = stay
- Right-click tray icon → **Exit** to fully close

## Build from source

```bash
# Clone
git clone https://github.com/phatMT97/Synkro.git
cd Synkro

# Build
dotnet build MultiSound/Synkro.sln

# Run tests
dotnet test MultiSound/Synkro.Tests/

# Publish (framework-dependent, ~2MB)
dotnet publish MultiSound/Synkro/Synkro.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true

# Publish (self-contained, ~157MB, no .NET install needed)
dotnet publish MultiSound/Synkro/Synkro.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

## Tech stack

- C# / .NET 8 / WPF
- [NAudio](https://github.com/naudio/NAudio) — WASAPI Loopback capture & output
- WASAPI shared mode for multi-device output

## License

MIT
