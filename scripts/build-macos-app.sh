#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_NAME="MCP Manager"
BUNDLE_ID="tools.franks.mcp-manager"
CONFIGURATION="Release"
OUTPUT_DIR="$ROOT_DIR/dist"
RID=""
ENABLE_CODESIGN="false"
APP_VERSION=""

usage() {
  cat <<EOF
Builds a distributable macOS .app bundle for MCP Manager.

Usage:
  scripts/build-macos-app.sh [options]

Options:
  --rid <rid>              Runtime identifier (default: osx-arm64 on Apple Silicon, osx-x64 on Intel)
  --configuration <conf>   Build configuration (default: Release)
  --output <dir>           Output directory for .app bundle (default: ./dist)
  --codesign               Apply ad-hoc codesign to bundle
  --version <semver>       Version to embed in published binaries (e.g. 1.2.3)
  -h, --help               Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      RID="$2"
      shift 2
      ;;
    --configuration)
      CONFIGURATION="$2"
      shift 2
      ;;
    --output)
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --codesign)
      ENABLE_CODESIGN="true"
      shift
      ;;
    --version)
      APP_VERSION="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$RID" ]]; then
  ARCH="$(uname -m)"
  if [[ "$ARCH" == "arm64" ]]; then
    RID="osx-arm64"
  else
    RID="osx-x64"
  fi
fi

PUBLISH_DIR="$ROOT_DIR/.artifacts/publish/gui/$RID"
APP_BUNDLE_DIR="$OUTPUT_DIR/$APP_NAME.app"
APP_CONTENTS_DIR="$APP_BUNDLE_DIR/Contents"
APP_MACOS_DIR="$APP_CONTENTS_DIR/MacOS"
APP_RESOURCES_DIR="$APP_CONTENTS_DIR/Resources"
ICON_PNG="$ROOT_DIR/src/McpManager/Assets/app-icon.png"
ICON_ICNS="$APP_RESOURCES_DIR/AppIcon.icns"

PUBLISH_VERSION_ARG=()
if [[ -n "$APP_VERSION" ]]; then
  PUBLISH_VERSION_ARG=("-p:Version=$APP_VERSION")
fi

echo "==> Publishing McpManager ($RID, $CONFIGURATION)"
dotnet publish "$ROOT_DIR/src/McpManager/McpManager.csproj" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  -p:PublishAot=false \
  "${PUBLISH_VERSION_ARG[@]}" \
  -o "$PUBLISH_DIR"

echo "==> Creating app bundle at $APP_BUNDLE_DIR"
rm -rf "$APP_BUNDLE_DIR"
mkdir -p "$APP_MACOS_DIR" "$APP_RESOURCES_DIR"

cp -R "$PUBLISH_DIR"/* "$APP_MACOS_DIR/"
chmod +x "$APP_MACOS_DIR/McpManager"

cat > "$APP_CONTENTS_DIR/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleVersion</key>
  <string>${APP_VERSION:-1.0.0}</string>
  <key>CFBundleShortVersionString</key>
  <string>${APP_VERSION:-1.0.0}</string>
  <key>CFBundleExecutable</key>
  <string>McpManager</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

if command -v iconutil >/dev/null 2>&1 && [[ -f "$ICON_PNG" ]]; then
  echo "==> Generating .icns from $ICON_PNG"
  ICONSET_DIR="$APP_RESOURCES_DIR/AppIcon.iconset"
  rm -rf "$ICONSET_DIR"
  mkdir -p "$ICONSET_DIR"
  sips -z 16 16 "$ICON_PNG" --out "$ICONSET_DIR/icon_16x16.png" >/dev/null
  sips -z 32 32 "$ICON_PNG" --out "$ICONSET_DIR/icon_16x16@2x.png" >/dev/null
  sips -z 32 32 "$ICON_PNG" --out "$ICONSET_DIR/icon_32x32.png" >/dev/null
  sips -z 64 64 "$ICON_PNG" --out "$ICONSET_DIR/icon_32x32@2x.png" >/dev/null
  sips -z 128 128 "$ICON_PNG" --out "$ICONSET_DIR/icon_128x128.png" >/dev/null
  sips -z 256 256 "$ICON_PNG" --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null
  sips -z 256 256 "$ICON_PNG" --out "$ICONSET_DIR/icon_256x256.png" >/dev/null
  sips -z 512 512 "$ICON_PNG" --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null
  sips -z 512 512 "$ICON_PNG" --out "$ICONSET_DIR/icon_512x512.png" >/dev/null
  sips -z 1024 1024 "$ICON_PNG" --out "$ICONSET_DIR/icon_512x512@2x.png" >/dev/null
  iconutil -c icns "$ICONSET_DIR" -o "$ICON_ICNS"
  rm -rf "$ICONSET_DIR"
fi

if [[ "$ENABLE_CODESIGN" == "true" ]]; then
  echo "==> Applying ad-hoc codesign"
  codesign --force --deep --sign - "$APP_BUNDLE_DIR"
fi

echo "==> Done"
echo "App bundle: $APP_BUNDLE_DIR"
echo "Run it with: open \"$APP_BUNDLE_DIR\""
