#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# release.sh — cut a GitHub Release for a compiled plugin, with its .yak
# attached. Shared kit infrastructure, like publish.sh: point it at a project
# with --repo and it reads that project's tooling/publish.conf.
#
#   tooling/release.sh --repo script-forge --dry-run    # every check + the build; no tag, no release
#   tooling/release.sh --repo script-forge              # tag, push, release
#
# The point is that you never TYPE a version. It is read from the manifest,
# the tag is derived from it, and the built .yak is checked to carry it — so the
# three cannot drift into naming different builds. Every failure below has
# happened to someone:
#
#   * a tag that names a version the commit does not contain
#   * a release whose asset is a different version from its tag
#   * `gh release create` inventing a missing tag at whatever HEAD happened to be
#   * a release built from a dirty tree, so the asset matches no commit
#
# Publishing to yak.rhino3d.com is a separate, public, irreversible act and is
# NOT part of this script — that is `publish.sh push`, and whether a project may
# take it belongs in that project's own CLAUDE.md.
#
# Extra conf keys this reads beyond publish.sh's:
#
#   TAG_PREFIX="forge-v"        # tag is TAG_PREFIX + the manifest version
#   PRODUCT_NAME="Script Forge" # release title; defaults to the manifest `name:`
#
# Every git operation targets the repository containing --repo, NOT the kit. For
# this repo they are the same directory; for a project that consumes the kit from
# elsewhere they are not, and tagging the kit would be badly wrong.
#
# ---------------------------------------------------------------------------

set -euo pipefail

KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

REPO="$PWD"
DRY_RUN=0
NOTES_FILE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --repo)       REPO="$2"; shift 2 ;;
    --dry-run)    DRY_RUN=1; shift ;;
    --notes-file) NOTES_FILE="$2"; shift 2 ;;
    *) echo "usage: release.sh [--repo <path>] [--notes-file <f>] [--dry-run]" >&2; exit 2 ;;
  esac
done
REPO="$(cd "$REPO" && pwd)"

step() { printf '\n\033[1m== %s\033[0m\n' "$1"; }
die()  { echo "release.sh: $*" >&2; exit 1; }
ok()   { echo "  ok  $*"; }

# -- 0. Configuration -------------------------------------------------------
CONF="$REPO/tooling/publish.conf"
[ -f "$CONF" ] || die "no $CONF"
CODEGEN_ARGS=()
# shellcheck source=/dev/null
. "$CONF"

MANIFEST="${MANIFEST:-yak/manifest.yml}"
[ -n "${TAG_PREFIX:-}" ] || die "$CONF: TAG_PREFIX is required (e.g. TAG_PREFIX=\"forge-v\")"
[ -n "${CSPROJ:-}" ]     || die "$CONF: CSPROJ is required"
[ -f "$REPO/$MANIFEST" ] || die "no $REPO/$MANIFEST"

# The repository being released is the one that CONTAINS --repo. Deriving it from
# $KIT would tag the kit itself whenever a project consumes this from elsewhere.
GIT_ROOT="$(git -C "$REPO" rev-parse --show-toplevel 2>/dev/null)" \
  || die "$REPO is not inside a git repository"

step "Pre-flight"

# -- 1. A release must describe a commit, so the tree must be clean ---------
# Otherwise the asset is built from something that is not in git at all, and no
# one can ever reproduce it.
[ -z "$(git -C "$GIT_ROOT" status --porcelain)" ] \
  || die "working tree is dirty — commit or stash first, so the asset matches a commit"
ok "working tree clean ($GIT_ROOT)"

# -- 2. The version is READ, never typed ------------------------------------
VERSION="$(awk '/^version:/ {print $2; exit}' "$REPO/$MANIFEST")"
[ -n "$VERSION" ] || die "no version: line in $REPO/$MANIFEST"
CSPROJ_VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO/$CSPROJ" | head -1)"
[ "$VERSION" = "$CSPROJ_VERSION" ] \
  || die "version mismatch: $MANIFEST=$VERSION csproj=$CSPROJ_VERSION"
ok "version $VERSION (manifest and csproj agree)"

TAG="${TAG_PREFIX}${VERSION}"
# The release title is a product name, not a folder name. The manifest `name:` is
# the installable spelling (no spaces — `yak install` rejects them), so a project
# whose display name differs sets PRODUCT_NAME in its conf.
TITLE="${PRODUCT_NAME:-$(awk '/^name:/ {print $2; exit}' "$REPO/$MANIFEST")} $VERSION"
HEAD_SHA="$(git -C "$GIT_ROOT" rev-parse HEAD)"

# -- 3. The tag must not already name a different commit --------------------
if git -C "$GIT_ROOT" rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
  TAGGED="$(git -C "$GIT_ROOT" rev-list -n1 "$TAG")"
  [ "$TAGGED" = "$HEAD_SHA" ] || die \
    "tag $TAG already exists and points at ${TAGGED:0:9}, not HEAD ${HEAD_SHA:0:9}.
  Either release from that commit, or bump the version — never move a published tag."
  ok "tag $TAG exists and already points at HEAD"
  TAG_EXISTS=1
else
  ok "tag $TAG is free"
  TAG_EXISTS=0
fi

# -- 4. HEAD must be on the remote ------------------------------------------
# A release pointing at a commit nobody can fetch is worse than no release.
git -C "$GIT_ROOT" fetch -q origin 2>/dev/null || true
if ! git -C "$GIT_ROOT" merge-base --is-ancestor "$HEAD_SHA" origin/HEAD 2>/dev/null \
  && ! git -C "$GIT_ROOT" branch -r --contains "$HEAD_SHA" 2>/dev/null | grep -q .; then
  die "HEAD ${HEAD_SHA:0:9} is not on origin — push before releasing"
fi
ok "HEAD is on origin"

# -- 5. A release for this tag must not already exist -----------------------
if command -v gh >/dev/null && (cd "$GIT_ROOT" && gh release view "$TAG" >/dev/null 2>&1); then
  die "a GitHub Release already exists for $TAG.
  Bump the version and release that, or delete the existing release deliberately —
  re-uploading over a published release silently changes what people already have."
fi
ok "no existing release for $TAG"

# -- 6. Build the package ---------------------------------------------------
step "Building $VERSION"
"$KIT/tooling/publish.sh" --repo "$REPO" package

YAKFILE="$(find "$REPO/build/yak" -maxdepth 1 -name '*.yak' -print | head -1)"
[ -n "$YAKFILE" ] || die "publish.sh produced no .yak in $REPO/build/yak"

# -- 7. The asset must carry the version the tag claims ---------------------
# The filename is generated by `yak build` from the manifest, so a mismatch here
# means the build did not see the version this script read — a stale build dir,
# most likely.
case "$(basename "$YAKFILE")" in
  *"$VERSION"*) ok "asset $(basename "$YAKFILE") carries $VERSION" ;;
  *) die "built asset $(basename "$YAKFILE") does not contain version $VERSION —
  stale build output? remove $REPO/build/yak and retry." ;;
esac

# -- 8. Do it ---------------------------------------------------------------
if [ "$DRY_RUN" = "1" ]; then
  step "Dry run — nothing written"
  echo "  would tag    $TAG at ${HEAD_SHA:0:9}"
  echo "  would push   $TAG to origin"
  echo "  would release $TAG titled \"$TITLE\" with $(basename "$YAKFILE")"
  exit 0
fi

step "Tagging and releasing $TAG"
if [ "$TAG_EXISTS" = "0" ]; then
  git -C "$GIT_ROOT" tag -a "$TAG" -m "$TITLE"
  echo "  tagged $TAG"
fi
git -C "$GIT_ROOT" push -q origin "$TAG"
echo "  pushed $TAG"

NOTES_ARGS=(--generate-notes)
[ -n "$NOTES_FILE" ] && NOTES_ARGS=(--notes-file "$NOTES_FILE")

(cd "$GIT_ROOT" && gh release create "$TAG" \
  --title "$TITLE" \
  "${NOTES_ARGS[@]}" \
  "$YAKFILE")

step "Done"
echo "  $(cd "$GIT_ROOT" && gh release view "$TAG" --json url --jq .url)"
