#!/usr/bin/env bash
# Publishes the current commit as a Lazarek release for testers:
#   1. sets AnoMech.csproj's version (Dalamud only offers an update when it goes up),
#   2. builds the Release package,
#   3. creates the GitHub release with latest.zip,
#   4. updates Lazarek's entry in cidoliveira/DalamudPlugins and its pluginmaster.json.
#
# Usage: tools/release-lazarek.sh <version>      e.g. tools/release-lazarek.sh 0.4.2.2
set -euo pipefail

version="${1:?usage: tools/release-lazarek.sh <version, e.g. 0.4.2.2>}"
repo="cidoliveira/AnoMech"
plugins_repo="cidoliveira/DalamudPlugins"
tag="lazarek-v${version}"
root="$(git rev-parse --show-toplevel)"
cd "$root"

if [ -n "$(git status --porcelain)" ]; then
    echo "Commit or stash your changes first." >&2
    exit 1
fi

sed -i -E "s|<Version>[^<]*</Version>|<Version>${version}</Version>|" AnoMech/AnoMech.csproj
if [ -n "$(git status --porcelain)" ]; then
    git commit -q -am "chore: release Lazarek ${version}"
fi

dotnet build AnoMech/AnoMech.csproj -c Release -v q
package="AnoMech/bin/Release/AnoMech"
git push -q origin HEAD

gh release create "$tag" "$package/latest.zip" \
    --repo "$repo" \
    --target "$(git rev-parse HEAD)" \
    --title "Lazarek ${version}" \
    --notes "Lazarek ${version} ($(git rev-parse --short HEAD) on $(git rev-parse --abbrev-ref HEAD))."

checkout="$(mktemp -d)"
trap 'rm -rf "$checkout"' EXIT
git clone -q "https://github.com/${plugins_repo}.git" "$checkout"
python tools/lazarek_manifest.py "$package/AnoMech.json" "$tag" "$repo" "$checkout/plugins/Lazarek.json"
python "$checkout/tools/build_pluginmaster.py"
git -C "$checkout" add plugins/Lazarek.json pluginmaster.json
git -C "$checkout" commit -q -m "chore: publish Lazarek ${version}"
git -C "$checkout" push -q origin HEAD

echo "Published ${tag}. Testers get it from:"
echo "  https://raw.githubusercontent.com/${plugins_repo}/main/pluginmaster.json"
