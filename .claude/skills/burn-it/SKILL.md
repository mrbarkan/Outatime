---
name: burn-it
description: Ship a signed, notarized Outatime release — bump the version, commit, push, then build, notarize, and publish to GitHub Releases with a Sparkle appcast. Use when the user says "burn it", "ship it", "cut a release", or "publish a release".
---

# Burn it

One full release. Takes ~5 minutes, nearly all of it waiting on Apple's notary
service. Every command runs from the repo root.

Optional argument: an explicit version (`burn it 1.1`). With none, bump the patch.

## 1. Preflight — bail here, not after the push

```sh
git status --short                                             # uncommitted work
gh auth status                                                 # release upload
xcrun notarytool history --keychain-profile outatime-notary    # signing creds
```

- Uncommitted changes get swept into the release commit. Show them and ask before continuing.
- Either of the last two failing means no release can be published — stop and say which.

## 2. Bump

`MARKETING_VERSION` is what users see; `CURRENT_PROJECT_VERSION` is what Sparkle
compares. **Both must go up, every release**, or the update is invisible.

```sh
OLD=$(sed -n 's/.*MARKETING_VERSION: "\(.*\)"/\1/p' project.yml)
BUILD=$(sed -n 's/.*CURRENT_PROJECT_VERSION: "\(.*\)"/\1/p' project.yml)
NEW=${1:-$(echo $OLD | awk -F. '{$NF+=1; print $1"."$2"."$3}' OFS=.)}
sed -i '' "s/MARKETING_VERSION: \"$OLD\"/MARKETING_VERSION: \"$NEW\"/;s/CURRENT_PROJECT_VERSION: \"$BUILD\"/CURRENT_PROJECT_VERSION: \"$((BUILD+1))\"/" project.yml
```

`gh release list` — if `v$NEW` already exists, pick the next one instead. The
release script cannot overwrite a published tag.

## 3. Commit and push

```sh
git commit -am "Version $NEW" && git push origin main
```

Match the existing history: the subject is just `Version <x.y.z>` when the release
is only a bump. If it carries real work, describe the work instead.

## 4. Build, notarize, publish

```sh
scripts/release.sh
```

Archive → Developer ID export → DMG → notarize → staple → signed `appcast.xml` →
`gh release create v$NEW`. Run it in the background or with a 10-minute timeout;
notarization routinely takes several minutes and a killed run leaves a pushed
version with no release behind it.

If it fails **after** notarization, don't rerun the whole thing — the DMG is
already stapled at `build/Outatime.dmg`. Fix the failing step and finish by hand.

## 5. Verify before claiming it shipped

```sh
gh release view "v$NEW" --json assets --jq '.assets[].name'   # Outatime.dmg AND appcast.xml
curl -sL https://github.com/mrbarkan/Outatime/releases/latest/download/appcast.xml | grep -E 'shortVersionString|edSignature'
```

The feed must serve the new version with a signature. An `appcast.xml` missing
from the assets means every installed copy silently stops seeing updates — that
is the one failure worth interrupting the user about.
