#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${CONFIGURATION:-Release}"
RUNTIME="${RUNTIME:-linux-x64}"
VERSION="${PACKAGE_VERSION:-1.0.0}"
PACKAGE_NAME="jluicourse"
APP_NAME="JLUiCourse"
MAINTAINER="wzyyyyyyy"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PUBLISH_DIR="$REPO_ROOT/artifacts/publish/$RUNTIME"
OUTPUT_DIR="$REPO_ROOT/artifacts/linux"
PACKAGE_ROOT="$REPO_ROOT/artifacts/installer/linux/${PACKAGE_NAME}_${VERSION}_amd64"

rm -rf "$PUBLISH_DIR" "$PACKAGE_ROOT"
mkdir -p "$PUBLISH_DIR" "$OUTPUT_DIR"

dotnet publish "$REPO_ROOT/iCourse/iCourse.csproj" \
  --configuration "$CONFIGURATION" \
  --runtime "$RUNTIME" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  --output "$PUBLISH_DIR"

mkdir -p \
  "$PACKAGE_ROOT/DEBIAN" \
  "$PACKAGE_ROOT/opt/$PACKAGE_NAME" \
  "$PACKAGE_ROOT/usr/bin" \
  "$PACKAGE_ROOT/usr/share/applications"

cp -R "$PUBLISH_DIR/." "$PACKAGE_ROOT/opt/$PACKAGE_NAME/"
chmod +x "$PACKAGE_ROOT/opt/$PACKAGE_NAME/iCourse"
ln -s "/opt/$PACKAGE_NAME/iCourse" "$PACKAGE_ROOT/usr/bin/icourse"

cat > "$PACKAGE_ROOT/usr/share/applications/$PACKAGE_NAME.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=$APP_NAME
Comment=Jilin University course selection desktop client
Exec=/opt/$PACKAGE_NAME/iCourse
Terminal=false
Categories=Education;Utility;
DESKTOP

INSTALLED_SIZE="$(du -sk "$PACKAGE_ROOT/opt/$PACKAGE_NAME" | awk '{print $1}')"
cat > "$PACKAGE_ROOT/DEBIAN/control" <<CONTROL
Package: $PACKAGE_NAME
Version: $VERSION
Section: education
Priority: optional
Architecture: amd64
Maintainer: $MAINTAINER
Installed-Size: $INSTALLED_SIZE
Description: Jilin University course selection desktop client
 JLUiCourse is an Avalonia desktop client for study and research use.
CONTROL

dpkg-deb --build "$PACKAGE_ROOT" "$OUTPUT_DIR/${PACKAGE_NAME}_${VERSION}_amd64.deb"
tar -C "$PUBLISH_DIR" -czf "$OUTPUT_DIR/iCourse-$VERSION-$RUNTIME.tar.gz" .

echo "Linux packages created:"
echo "  $OUTPUT_DIR/${PACKAGE_NAME}_${VERSION}_amd64.deb"
echo "  $OUTPUT_DIR/iCourse-$VERSION-$RUNTIME.tar.gz"
