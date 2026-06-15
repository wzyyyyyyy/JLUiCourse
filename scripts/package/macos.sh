#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${CONFIGURATION:-Release}"
VERSION="${PACKAGE_VERSION:-1.0.0}"
APP_DISPLAY_NAME="JLUiCourse"
APP_BUNDLE_ID="${APP_BUNDLE_ID:-io.github.wzyyyyyyy.jluicourse}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ARTIFACT_ROOT="$REPO_ROOT/artifacts"
OUTPUT_DIR="$ARTIFACT_ROOT/macos"
DMG_ROOT="$ARTIFACT_ROOT/installer/macos/dmg-root"
DMG_PATH="$OUTPUT_DIR/iCourse-$VERSION-macos-universal-unsigned.dmg"

create_app_bundle() {
  local runtime="$1"
  local suffix="$2"
  local publish_dir="$ARTIFACT_ROOT/publish/$runtime"
  local app_dir="$DMG_ROOT/$APP_DISPLAY_NAME-$suffix.app"
  local contents_dir="$app_dir/Contents"
  local macos_dir="$contents_dir/MacOS"
  local resources_dir="$contents_dir/Resources"

  rm -rf "$publish_dir" "$app_dir"
  mkdir -p "$publish_dir" "$macos_dir" "$resources_dir"

  dotnet publish "$REPO_ROOT/iCourse/iCourse.csproj" \
    --configuration "$CONFIGURATION" \
    --runtime "$runtime" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    --output "$publish_dir"

  cp -R "$publish_dir/." "$macos_dir/"
  chmod +x "$macos_dir/iCourse"

  cat > "$contents_dir/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundleExecutable</key>
  <string>iCourse</string>
  <key>CFBundleIdentifier</key>
  <string>$APP_BUNDLE_ID.$suffix</string>
  <key>CFBundleName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundleVersion</key>
  <string>$VERSION</string>
  <key>LSMinimumSystemVersion</key>
  <string>10.15</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

  # This is intentionally unsigned for maintainers without an Apple Developer ID.
  # Gatekeeper can still warn because notarization is impossible without Apple credentials.
  # Ad-hoc signing keeps the bundle structurally signed for local testing, but it is not trusted distribution signing.
  codesign --force --deep --sign - "$app_dir"
}

rm -rf "$OUTPUT_DIR" "$DMG_ROOT"
mkdir -p "$OUTPUT_DIR" "$DMG_ROOT"

create_app_bundle "osx-arm64" "arm64"
create_app_bundle "osx-x64" "x64"
ln -s /Applications "$DMG_ROOT/Applications"

cat > "$DMG_ROOT/README.txt" <<README
JLUiCourse macOS unsigned build

This DMG is unsigned and not notarized because no Apple Developer ID certificate is configured.
macOS Gatekeeper may show an unsafe-app warning on first launch.

To open it anyway: Control-click the app, choose Open, then confirm. You can also allow it in System Settings > Privacy & Security after the first blocked launch.
README

rm -f "$DMG_PATH"
hdiutil create \
  -volname "$APP_DISPLAY_NAME $VERSION" \
  -srcfolder "$DMG_ROOT" \
  -ov \
  -format UDZO \
  "$DMG_PATH"

echo "macOS unsigned DMG created: $DMG_PATH"
