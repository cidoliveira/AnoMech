#!/usr/bin/env bash
# Publishes the current commit as a Lazarek release for testers:
#   1. sets AnoMech.csproj's version (Dalamud only offers an update when it goes up),
#   2. builds the Release package,
#   3. creates the GitHub release with latest.zip,
#   4. regenerates repo.json on the dalamud-repo branch.
#
# Usage: tools/release-lazarek.sh <version>      e.g. tools/release-lazarek.sh 0.4.2.2
set -euo pipefail

version="${1:?usage: tools/release-lazarek.sh <version, e.g. 0.4.2.2>}"
repo="cidoliveira/AnoMech"
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

worktree="$(mktemp -d)"
trap 'git worktree remove --force "$worktree" >/dev/null 2>&1 || true' EXIT
git fetch -q origin dalamud-repo
git worktree add -q -B dalamud-repo "$worktree" origin/dalamud-repo
python tools/lazarek_repo.py "$package/AnoMech.json" "$tag" "$repo" "$worktree/repo.json"
git -C "$worktree" add repo.json
git -C "$worktree" commit -q -m "chore: publish Lazarek ${version}"
git -C "$worktree" push -q origin dalamud-repo

echo "Published ${tag}. Testers get it from:"
echo "  https://raw.githubusercontent.com/${repo}/dalamud-repo/repo.json"
