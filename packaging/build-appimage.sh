#!/usr/bin/env bash
# Builds the Linux AppImage of the Avalonia GUI (Mapper.Avalonia).
#   APPIMAGETOOL=/path/to/appimagetool packaging/build-appimage.sh [version]
# appimagetool: https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
# DOTNET defaults to dotnet on PATH, then ~/.dotnet/dotnet. Output: artifacts/MapeeExpanded-<version>-x86_64.AppImage
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Mapper.Avalonia/Mapper.Avalonia.csproj")}"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
APPIMAGETOOL="${APPIMAGETOOL:-$(command -v appimagetool || true)}"

if [[ -z "$APPIMAGETOOL" || ! -x "$APPIMAGETOOL" ]]; then
  echo "appimagetool not found; set APPIMAGETOOL to its path" >&2
  exit 127
fi

PUBLISH_DIR="$ROOT/artifacts/publish/linux-x64"
APP_DIR="$ROOT/artifacts/appimage/MapeeExpanded.AppDir"
OUTPUT="$ROOT/artifacts/MapeeExpanded-$VERSION-x86_64.AppImage"

rm -rf "$PUBLISH_DIR" "$APP_DIR"
mkdir -p "$APP_DIR/usr/bin" "$APP_DIR/usr/share/applications" "$APP_DIR/usr/share/icons/hicolor/512x512/apps"

DOTNET_CLI_TELEMETRY_OPTOUT=1 "$DOTNET" publish "$ROOT/Mapper.Avalonia/Mapper.Avalonia.csproj" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --output "$PUBLISH_DIR"

cp -a "$PUBLISH_DIR/." "$APP_DIR/usr/bin/"
cp "$ROOT/Mapper.Gui/Resources/Image/Logo/Logo_512px.png" "$APP_DIR/mapee.png"
cp "$ROOT/Mapper.Gui/Resources/Image/Logo/Logo_512px.png" "$APP_DIR/usr/share/icons/hicolor/512x512/apps/mapee.png"

cat > "$APP_DIR/AppRun" <<'APPRUN'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/Mapee" "$@"
APPRUN
chmod +x "$APP_DIR/AppRun"

cat > "$APP_DIR/mapee.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Mapee Expanded
Comment=Map viewer for Minecraft Java and Bedrock worlds
Exec=Mapee
Icon=mapee
Terminal=false
Categories=Game;Graphics;
StartupWMClass=mapee
DESKTOP
cp "$APP_DIR/mapee.desktop" "$APP_DIR/usr/share/applications/mapee.desktop"

ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" --no-appstream "$APP_DIR" "$OUTPUT"
echo "Created $OUTPUT"
