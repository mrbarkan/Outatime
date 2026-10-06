---
name: burn-it
description: Ship a signed, notarized Outatime release — bump the version, commit, push, then build, notarize, and publish to GitHub Releases with a Sparkle appcast. Use when the user says "burn it", "ship it", "cut a release", or "publish a release"; "burn it beta" ships a beta to opted-in users.
---

# Burn it

One full release. Takes ~5 minutes, nearly all of it waiting on Apple's notary
service. Every command runs from the repo root.

Optional argument: an explicit version (`burn it 1.1`). With none, bump the patch.
`burn it beta` follows the same steps with the changes in **Beta** at the end.

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

## 2b. What's New — the notes users see after updating

```sh
grep -q "version: \"$NEW\"" Outatime/WhatsNew.swift && echo "notes ok" || echo "no notes for $NEW"
```

Every release needs an entry in `WhatsNew.releases` (newest first): **New** and/or
**Fixed** items, each an SF Symbol, a color, a short title and one line of detail,
written for users rather than as commit subjects. If there's none for `$NEW` — or it
was written ahead under another version number — draft one from
`git log v$OLD..HEAD`, add the es and pt-BR translations to
`Localizable.xcstrings` (`catalogIsComplete` fails without them), show the draft and
wait for approval before committing. A version without an entry just doesn't open the
window, so for a release with nothing user-facing, ask whether to skip the notes.

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

## Beta

Users who turn on Settings → General → "Get beta updates" accept appcast items on
Sparkle's `beta` channel. Everyone else never sees them.

- **Version:** `X.Y-beta.N`. From a stable `1.2.x` the next beta is `1.3-beta.1`; from
  `1.3-beta.1` it's `1.3-beta.2`. An explicit argument wins (`burn it beta 2.0-beta.1`).
  `CURRENT_PROJECT_VERSION` still goes up by one, exactly as in step 2.
- **What's New:** skip 2b. Betas neither open the window nor record a last-seen version,
  so testers get the full notes when the stable release ships. Draft that entry under
  the stable number (`1.3`) whenever convenient.
- **Commit:** `Version 1.3-beta.1` (or describe the work), push as in step 3.
- **Publish:** `scripts/release.sh --beta`. It refuses a version without `-beta.`, and a
  stable run refuses one with it. It creates a GitHub **pre-release** with the DMG, then
  uploads the merged `appcast.xml` to the latest *stable* release — `releases/latest`
  never points at a pre-release, and that's the feed every copy reads.
- **Verify:**

  ```sh
  gh release view "v$NEW" --json isPrerelease,assets --jq '.isPrerelease, .assets[].name'   # true, Outatime.dmg
  curl -sL https://github.com/mrbarkan/Outatime/releases/latest/download/appcast.xml | grep -E 'channel|shortVersionString'
  ```

  The feed must list the beta (with `<sparkle:channel>beta</sparkle:channel>`) *and* the
  current stable.

Every release, stable or beta, starts from the published feed and adds itself, so items
accumulate. Sparkle offers each copy the newest build on its channels. Build numbers only
go up, so a stable hotfix shipped while a beta is out outranks that beta: cut a fresh
beta right after the hotfix, or testers drift back to stable.
