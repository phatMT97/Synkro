#!/bin/bash
set -e

PROJECT="MultiSound/Synkro/Synkro.csproj"
DEST="/mnt/c/Users/Admin/Desktop/Synkro.exe"

~/.dotnet/dotnet publish "$PROJECT" -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true

cp "MultiSound/Synkro/bin/Release/net8.0-windows/win-x64/publish/Synkro.exe" "$DEST"

echo "Done → $DEST"
