#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# release.sh — cut a GitHub Release for a compiled plugin, with its .yak
# attached. Shared kit infrastructure, like publish.sh: point it at a project
# with --repo and it reads that project's tooling/publish.conf.
#
#   tooling/release.sh --repo script-forge --dry-run    # every check; writes nothing
#   tooling/release.sh --repo script-forge              # tag, push, release
#
# A version carrying a SemVer prerelease suffix (the `-beta` in 0.4.5-beta) is
# released as a GitHub PRERELEASE, so it never becomes the repo's "Latest".
# `--prerelease` / `--no-prerelease` override the guess. This keeps the release
# page honest about the same thing the version already says elsewhere: yak treats
# a `-beta` package as a prerelease that Rhino's Package Manager hides unless
# "include pre-releases" is ticked, so a release page offering it as the current
# build contradicts the package a reader would go on to install.
#
# The point is that you never TYPE a version. It is read from the manifest,
# the tag is derived from it, and the attached .yak is checked to carry it — so
# the three cannot drift into naming different builds. Every failure below has
# happened to someone:
#
#   * a tag that names a version the commit does not contain
#   * a release whose asset is a different version from its tag
#   * `gh release create` inventing a missing tag at whatever HEAD happened to be
#   * a release built from a dirty tree, so the asset matches no commit
#
# It builds nothing. The asset is the package holding the build installed in
# Rhino (tested-yak.sh finds it), which must have been built from HEAD: install,
# restart Rhino and test, then release. A rebuild here would ship bytes nobody
# had tested.
#
# A dry run reports every refusal the real run would make and carries on, so one
# dry run lists everything left to fix; it exits 1 if there were any.
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

# The environment's YAK_LOCAL_REPO outranks the conf file's, as in publish.sh.
ENV_YAK_LOCAL_REPO="${YAK_LOCAL_REPO:-}"

REPO="$PWD"
DRY_RUN=0
NOTES_FILE=""
# empty = decide from the version string; 1 / 0 = forced by a flag.
PRERELEASE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --repo)           REPO="$2"; shift 2 ;;
    --dry-run)        DRY_RUN=1; shift ;;
    --notes-file)     NOTES_FILE="$2"; shift 2 ;;
    --prerelease)     PRERELEASE=1; shift ;;
    --no-prerelease)  PRERELEASE=0; shift ;;
    *) echo "usage: release.sh [--repo <path>] [--notes-file <f>] [--dry-run]" \
            "[--prerelease|--no-prerelease]" >&2; exit 2 ;;
  esac
done
REPO="$(cd "$REPO" && pwd)"

step() { printf '\n\033[1m== %s\033[0m\n' "$1"; }
die()  { echo "release.sh: $*" >&2; exit 1; }
ok()   { echo "  ok  $*"; }
REFUSALS=0
refuse() {
  if [ "$DRY_RUN" = "1" ]; then echo "  WOULD REFUSE  $*"; REFUSALS=$((REFUSALS + 1)); else die "$*"; fi
}

# -- 0. Configuration -------------------------------------------------------
CONF="$REPO/tooling/publish.conf"
[ -f "$CONF" ] || die "no $CONF"
CODEGEN_ARGS=()
# shellcheck source=/dev/null
. "$CONF"

MANIFEST="${MANIFEST:-yak/manifest.yml}"
[ -n "${TAG_PREFIX:-}" ] || die "$CONF: TAG_PREFIX is required (e.g. TAG_PREFIX=\"forge-v\")"
[ -n "${CSPROJ:-}" ]     || die "$CONF: CSPROJ is required"
[ -n "${GHA_NAME:-}" ]   || die "$CONF: GHA_NAME is required"
[ -f "$REPO/$MANIFEST" ] || die "no $REPO/$MANIFEST"
# The same resolution publish.sh uses, so both look in the same folders.
YAK_LOCAL_REPO="${ENV_YAK_LOCAL_REPO:-${YAK_LOCAL_REPO:-$HOME/.rhino-gh-kit/yak-local-repo}}"
PACKAGES="$HOME/Library/Application Support/McNeel/Rhinoceros/packages/${RHINO_VERSION:-8.0}"
PKG_NAME="$(sed -n 's/^name:[[:space:]]*//p' "$REPO/$MANIFEST" | head -1)"

# The repository being released is the one that CONTAINS --repo. Deriving it from
# $KIT would tag the kit itself whenever a project consumes this from elsewhere.
GIT_ROOT="$(git -C "$REPO" rev-parse --show-toplevel 2>/dev/null)" \
  || die "$REPO is not inside a git repository"

step "Pre-flight"

# -- 1. A release must describe a commit, so the tree must be clean ---------
# Otherwise the asset is built from something that is not in git at all, and no
# one can ever reproduce it.
if [ -z "$(git -C "$GIT_ROOT" status --porcelain)" ]; then
  ok "working tree clean ($GIT_ROOT)"
else
  refuse "working tree is dirty — commit or stash first, so the asset matches a commit"
fi

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

# A SemVer prerelease suffix (the `-beta` in 0.4.5-beta) marks the GitHub release
# as a prerelease, so it is not the repo's "Latest" and does not reach anyone who
# only ever looks there. This mirrors what the version already means everywhere
# else: yak treats a `-beta` package as a prerelease that Rhino's Package Manager
# hides unless "include pre-releases" is ticked, so a release page advertising it
# as the current build contradicts the package a reader would then install.
# `--prerelease` / `--no-prerelease` force it either way.
if [ -z "$PRERELEASE" ]; then
  case "$VERSION" in
    *-*) PRERELEASE=1 ;;
    *)   PRERELEASE=0 ;;
  esac
fi
[ "$PRERELEASE" = "1" ] && RELEASE_KIND="prerelease" || RELEASE_KIND="full release"
HEAD_SHA="$(git -C "$GIT_ROOT" rev-parse HEAD)"

# -- 3. The tag must not already name a different commit --------------------
if git -C "$GIT_ROOT" rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
  TAGGED="$(git -C "$GIT_ROOT" rev-list -n1 "$TAG")"
  if [ "$TAGGED" = "$HEAD_SHA" ]; then
    ok "tag $TAG exists and already points at HEAD"
  else
    refuse "tag $TAG already exists and points at ${TAGGED:0:9}, not HEAD ${HEAD_SHA:0:9}.
  Either release from that commit, or bump the version — never move a published tag."
  fi
  TAG_EXISTS=1
else
  ok "tag $TAG is free"
  TAG_EXISTS=0
fi

# -- 4. HEAD must be on the remote ------------------------------------------
# A release pointing at a commit nobody can fetch is worse than no release.
if ! git -C "$GIT_ROOT" remote get-url origin >/dev/null 2>&1; then
  refuse "no origin remote — add it and push before releasing"
else
  git -C "$GIT_ROOT" fetch -q origin 2>/dev/null || true
  if git -C "$GIT_ROOT" merge-base --is-ancestor "$HEAD_SHA" origin/HEAD 2>/dev/null \
    || git -C "$GIT_ROOT" branch -r --contains "$HEAD_SHA" 2>/dev/null | grep -q .; then
    ok "HEAD is on origin"
  else
    refuse "HEAD ${HEAD_SHA:0:9} is not on origin — push before releasing"
  fi
fi

# -- 5. A release for this tag must not already exist -----------------------
if command -v gh >/dev/null && (cd "$GIT_ROOT" && gh release view "$TAG" >/dev/null 2>&1); then
  refuse "a GitHub Release already exists for $TAG.
  Bump the version and release that, or delete the existing release deliberately —
  re-uploading over a published release silently changes what people already have."
else
  ok "no existing release for $TAG"
fi

# -- 6. The asset is the installed, tested build, made from HEAD ------------
# shellcheck source=tested-yak.sh
. "$KIT/tooling/tested-yak.sh"
YAKFILE=""
if tested_yak "$PKG_NAME" "$VERSION" "$GHA_NAME" "$YAK_LOCAL_REPO" "$PACKAGES"; then
  YAKFILE="$TESTED_YAK"
  ok "asset $(basename "$YAKFILE") holds the installed build"
  # The commit stamped into the build must be the one being tagged, or the
  # release would pair a commit with a build of some other commit.
  if [ -z "$TESTED_COMMIT" ]; then
    refuse "the installed build carries no commit (no +<sha> in its informational
  version), so nothing shows it was built from HEAD"
  elif [ "$TESTED_COMMIT" = "$HEAD_SHA" ]; then
    ok "it was built from HEAD ${HEAD_SHA:0:9}"
  else
    refuse "the installed build was made from ${TESTED_COMMIT:0:9}, not HEAD ${HEAD_SHA:0:9}.
  Install from HEAD (publish.sh install), restart Rhino and test it, then release."
  fi
else
  refuse "$TESTED_ERROR"
fi

# -- 8. Do it ---------------------------------------------------------------
if [ "$DRY_RUN" = "1" ]; then
  step "Dry run — nothing written"
  echo "  would tag     $TAG at ${HEAD_SHA:0:9}"
  echo "  would push    $TAG to origin"
  echo "  would release $TAG titled \"$TITLE\" with ${YAKFILE:+$(basename "$YAKFILE")}${YAKFILE:-no tested package}"
  echo "  would mark    $TAG a $RELEASE_KIND"
  [ "$REFUSALS" = "0" ] || { echo "  the real run would refuse $REFUSALS time(s), above"; exit 1; }
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

KIND_ARGS=()
[ "$PRERELEASE" = "1" ] && KIND_ARGS=(--prerelease)

(cd "$GIT_ROOT" && gh release create "$TAG" --verify-tag \
  --title "$TITLE" \
  "${NOTES_ARGS[@]}" \
  "${KIND_ARGS[@]+"${KIND_ARGS[@]}"}" \
  "$YAKFILE")
echo "  marked $TAG a $RELEASE_KIND"

step "Done"
echo "  $(cd "$GIT_ROOT" && gh release view "$TAG" --json url --jq .url)"
