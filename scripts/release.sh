#!/bin/zsh
# Build, sign (Developer ID), package as DMG, then notarize + staple if credentials exist. One-time setup:
#   xcrun notarytool store-credentials outatime-notary --apple-id you@example.com --team-id L26TPPMPF3
set -euo pipefail
cd "$(dirname "$0")/.."
PROFILE=${NOTARY_PROFILE:-outatime-notary}
DMG=build/Outatime.dmg

xcodegen generate
rm -rf build
xcodebuild -project Outatime.xcodeproj -scheme Outatime -configuration Release \
  -archivePath build/Outatime.xcarchive archive | tail -3
xcodebuild -exportArchive -archivePath build/Outatime.xcarchive \
  -exportOptionsPlist scripts/ExportOptions.plist -exportPath build/export | tail -3

mkdir build/dmg
cp -R build/export/Outatime.app build/dmg/
ln -s /Applications build/dmg/Applications
hdiutil create -volname Outatime -srcfolder build/dmg -ov -format UDZO "$DMG" | tail -1
codesign --sign "Developer ID Application" --timestamp "$DMG"

if xcrun notarytool history --keychain-profile "$PROFILE" >/dev/null 2>&1; then
  xcrun notarytool submit "$DMG" --keychain-profile "$PROFILE" --wait
  xcrun stapler staple "$DMG"
  spctl -a -vv -t open --context context:primary-signature "$DMG"
  echo "Notarized: $DMG"
else
  echo "Built (not notarized): $DMG — runs locally, but downloads will be blocked by Gatekeeper."
  echo "Store credentials once, then rerun:  xcrun notarytool store-credentials $PROFILE --apple-id <apple-id> --team-id L26TPPMPF3"
fi
