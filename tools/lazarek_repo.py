"""Write the Lazarek Dalamud repo.json from the packaged plugin manifest.

Usage:
    python lazarek_repo.py <packaged AnoMech.json> <release tag> <github owner/repo> <output repo.json>

Dalamud reads repo.json as a list of manifests, each with download links. The links point at
the tag's own asset rather than /releases/latest, so a cached repo.json never pairs an old
version number with a newer zip.
"""
from __future__ import annotations

import json
import sys
import time


def main(argv: list[str]) -> int:
    if len(argv) != 5:
        print(__doc__, file=sys.stderr)
        return 2
    manifest_path, tag, repo, output = argv[1:]
    with open(manifest_path, encoding="utf-8-sig") as f:
        manifest = json.load(f)
    url = f"https://github.com/{repo}/releases/download/{tag}/latest.zip"
    manifest.update({
        "DownloadLinkInstall": url,
        "DownloadLinkUpdate": url,
        "DownloadLinkTesting": url,
        "IsHide": False,
        "IsTestingExclusive": False,
        "LastUpdate": int(time.time()),
    })
    with open(output, "w", encoding="utf-8", newline="\n") as f:
        json.dump([manifest], f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"{output}: {manifest['Name']} {manifest['AssemblyVersion']} -> {url}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
