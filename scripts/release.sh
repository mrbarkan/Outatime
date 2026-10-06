#!/bin/zsh
# Build, sign (Developer ID), package as DMG, then notarize + staple if credentials exist. One-time setup:
#   xcrun notarytool store-credentials outatime-notary --apple-id you@example.com --team-id L26TPPMPF3
# --beta: publish a pre-release (version like 1.3-beta.1) on the appcast's "beta" channel.
set -euo pipefail
cd "$(dirname "$0")/.."
PROFILE=${NOTARY_PROFILE:-outatime-notary}
DMG=build/Outatime.dmg
VERSION=$(sed -n 's/.*MARKETING_VERSION: "\(.*\)"/\1/p' project.yml)
REPO=mrbarkan/Outatime
SPARKLE=.spm/artifacts/sparkle/Sparkle/bin   # shipped with the Sparkle SPM package
BETA=${1:-}
if [[ $BETA == --beta && $VERSION != *-beta.* ]]; then echo "A beta's version looks like 1.3-beta.1, not $VERSION"; exit 1; fi
if [[ $BETA != --beta && $VERSION == *-* ]]; then echo "$VERSION is a beta version; run with --beta"; exit 1; fi
CHANNEL=(); [[ $BETA == --beta ]] && CHANNEL=(--channel beta)

xcodegen generate
rm -rf build
xcodebuild -project Outatime.xcodeproj -scheme Outatime -configuration Release \
  -clonedSourcePackagesDirPath .spm \
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

  # Sparkle appcast: signs the stapled DMG with the EdDSA key in the login keychain
  # (one-time: $SPARKLE/generate_keys, then put the public key in project.yml).
  # Starts from the published feed and adds this build, so a beta ahead of a stable hotfix (and the stable behind a
  # beta) stay listed. Sparkle offers each copy the newest build on the channels it accepts.
  mkdir build/appcast
  curl -fsL "https://github.com/$REPO/releases/latest/download/appcast.xml" -o build/appcast/appcast.xml
  cp "$DMG" build/appcast/
  "$SPARKLE/generate_appcast" "${CHANNEL[@]}" \
    --download-url-prefix "https://github.com/$REPO/releases/download/v$VERSION/" \
    --link "https://github.com/$REPO" build/appcast

  if [[ $BETA == --beta ]]; then
    # A pre-release is never "latest", so the feed every copy reads stays on the latest stable release.
    gh release create "v$VERSION" "$DMG" --prerelease --title "Outatime $VERSION" --generate-notes \
      || gh release upload "v$VERSION" "$DMG" --clobber
    gh release upload "$(gh release view --repo $REPO --json tagName -q .tagName)" build/appcast/appcast.xml --clobber
  else
    # SUFeedURL points at releases/latest/download/appcast.xml, which always redirects here.
    gh release create "v$VERSION" "$DMG" build/appcast/appcast.xml \
      --title "Outatime $VERSION" --generate-notes \
      || gh release upload "v$VERSION" "$DMG" build/appcast/appcast.xml --clobber
  fi
else
  echo "Built (not notarized): $DMG — runs locally, but downloads will be blocked by Gatekeeper."
  echo "Store credentials once, then rerun:  xcrun notarytool store-credentials $PROFILE --apple-id <apple-id> --team-id L26TPPMPF3"
fi
