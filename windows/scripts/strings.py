#!/usr/bin/env python3
"""Copies the es and pt-BR translations from the Mac app's string catalog into src/Outatime.Core/Strings.json.
Keys the Windows app adds itself (already in Strings.json, absent from the catalog) are kept."""
import json, pathlib
root = pathlib.Path(__file__).resolve().parent.parent
catalog = json.loads((root.parent / "Outatime/Localizable.xcstrings").read_text())["strings"]
out_path = root / "src/Outatime.Core/Strings.json"
out = json.loads(out_path.read_text()) if out_path.exists() else {}
for key, entry in catalog.items():
    locs = entry.get("localizations", {})
    row = {code: locs[code]["stringUnit"]["value"] for code in ("es", "pt-BR") if code in locs}
    if row:
        out[key] = row
out_path.write_text(json.dumps(dict(sorted(out.items())), ensure_ascii=False, indent=1) + "\n")
print(len(out), "strings")
