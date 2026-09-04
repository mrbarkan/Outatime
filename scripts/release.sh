#!/bin/zsh
# Build, sign (Developer ID), notarize, staple. One-time setup:
#   xcrun notarytool store-credentials outatime-notary --apple-id you@example.com --team-id L26TPPMPF3
set -euo pipefail
cd "$(dirname "$0")/.."
PROFILE=${NOTARY_PROFILE:-outatime-notary}

xcodegen generate
rm -rf build
xcodebuild -project Outatime.xcodeproj -scheme Outatime -configuration Release \
  -archivePath build/Outatime.xcarchive archive | tail -3
xcodebuild -exportArchive -archivePath build/Outatime.xcarchive \
  -exportOptionsPlist scripts/ExportOptions.plist -exportPath build/export | tail -3

ditto -c -k --keepParent build/export/Outatime.app build/Outatime.zip
xcrun notarytool submit build/Outatime.zip --keychain-profile "$PROFILE" --wait
xcrun stapler staple build/export/Outatime.app
ditto -c -k --keepParent build/export/Outatime.app build/Outatime.zip
spctl -a -vv build/export/Outatime.app
echo "Ready: build/Outatime.zip"
