#!/usr/bin/env bash
#
# The release pipeline for a compiled Grasshopper plugin built from `@component`
# headers: validate → generate → build → package → install → push, as cumulative
# stages. Shared by every project that compiles a .gha with `gh_codegen.py`.
#
#   $KIT/tooling/publish.sh --repo <path> [build|package|install|push]
#
# Projects do not copy this file. They keep a three-line `tooling/publish.sh`
# wrapper that execs this one, plus `tooling/publish.conf` holding the five or so
# things that actually differ between projects. Before this was factored out, two
# projects carried byte-identical 57-line copies differing only in a .gha name,
# two paths and one flag — and they had already begun to drift.
#
# INSTALLING MEANS A YAK PACKAGE, OUT OF A PRIVATE FOLDER REPOSITORY. Packaging
# and publishing are separate concerns: a .yak is just a zip, and `yak install
# --source <dir>` accepts any ordinary directory as a package repository — no
# server involved (verified 2026-08-14, Rhino 8.33's bundled yak, both `search`
# and `install`). So a plugin gets real versioned, upgradeable installs under
# ~/Library/…/packages/<rhino>/<package>/<version>/ — `yak list` reports the live
# version, Rhino's Package Manager can read the same folder as a source — while
# `push`, the only stage that makes anything public, stays a separate decision
# each project makes for itself.
#
# This supersedes hand-copying the .gha into Grasshopper's `Libraries/` folder.
# Doing both at once loads every component twice and collides on ComponentGuid,
# so the install stage parks a loose copy as `.gha.disabled` when it finds one.
#
# ---------------------------------------------------------------------------
# tooling/publish.conf — sourced from the project root. Required:
#
#   CSPROJ="src/MyPlugin/MyPlugin.csproj"   # carries <Version>, relative to the repo root
#   GHA_NAME="MyPlugin.gha"                 # <AssemblyName>.gha
#
# Optional:
#
#   SLN="src/MyPlugin.sln"                  # defaults to CSPROJ — a plugin that is
#                                           #   one project needs no solution, and
#                                           #   `dotnet build` takes either
#   CODEGEN=0                               # for a plugin with NO `@component`
#                                           #   headers — a canvas/menu tool rather
#                                           #   than a component library. Skips both
#                                           #   header validation and codegen, which
#                                           #   have nothing to check or generate and
#                                           #   would otherwise fail the build over
#                                           #   unrelated sources elsewhere in the repo.
#   CODEGEN_ARGS=(--resource-prefix MyPlugin.Icons)   # extra gh_codegen.py flags.
#                                           #   --resource-prefix defaults to
#                                           #   GHA_NAME minus its .gha suffix, so
#                                           #   pin it only when <RootNamespace>
#                                           #   differs from <AssemblyName>.
#   PACKAGE_ICON_SVG="icons/my-plugin.svg"  # rasterized to the package's icon.png
#   MANIFEST="yak/manifest.yml"             # default
#   TFM="net8.0"                            # default
#   BUILT_GHA="…"                           # default: <csproj dir>/bin/Release/$TFM/$GHA_NAME
#   RHINO_VERSION="8.0"                     # default; picks the packages/<v> folder
#   YAK="…"                                 # default /Applications/Rhino 8.app/Contents/
#                                           #   Resources/bin/yak — the escape hatch for a
#                                           #   non-standard Rhino install location.
#   YAK_LOCAL_REPO="…"                      # default ~/.rhino-gh-kit/yak-local-repo
#                                           #   (a machine-level folder shared by all
#                                           #   projects; the environment wins over conf).
#                                           #   Must not contain a space — `yak install
#                                           #   --source <path>` fails on one even when
#                                           #   the shell passes it as a single argument
#                                           #   (verified 2026-08-24 against yak 8.x).
#
# There is deliberately no auto-discovery of the sln/csproj: project layouts vary
# (not every project keeps them under src/), and a config file that fails loudly
# beats a glob that picks the wrong one quietly.
# ---------------------------------------------------------------------------

set -euo pipefail

KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# The environment's YAK_LOCAL_REPO outranks the conf file's; captured before the
# conf is sourced, since sourcing would otherwise clobber it.
ENV_YAK_LOCAL_REPO="${YAK_LOCAL_REPO:-}"

REPO="$PWD"
STAGE="build"
while [ $# -gt 0 ]; do
  case "$1" in
    --repo) REPO="$2"; shift 2 ;;
    build|package|install|push) STAGE="$1"; shift ;;
    *) echo "usage: publish.sh [--repo <path>] [build|package|install|push]" >&2; exit 2 ;;
  esac
done
REPO="$(cd "$REPO" && pwd)"

step() { printf '\n\033[1m== %s\033[0m\n' "$1"; }
die()  { echo "$*" >&2; exit 1; }

# -- 0. Configuration -------------------------------------------------------
CONF="$REPO/tooling/publish.conf"
[ -f "$CONF" ] || die "no $CONF — see the header of $KIT/tooling/publish.sh for the keys"

CODEGEN_ARGS=()
# shellcheck source=/dev/null
. "$CONF"

[ -n "${CSPROJ:-}" ]   || die "$CONF: CSPROJ is required"
[ -n "${GHA_NAME:-}" ] || die "$CONF: GHA_NAME is required"

# A single-project plugin needs no solution file; `dotnet build` accepts a csproj
# just as happily, so SLN is only worth setting when there really is more than one
# project to build.
BUILD_TARGET="${SLN:-$CSPROJ}"

CODEGEN="${CODEGEN:-1}"
MANIFEST="${MANIFEST:-yak/manifest.yml}"
TFM="${TFM:-net8.0}"
RHINO_VERSION="${RHINO_VERSION:-8.0}"
BUILT="${BUILT_GHA:-$REPO/$(dirname "$CSPROJ")/bin/Release/$TFM/$GHA_NAME}"
YAK="${YAK:-/Applications/Rhino 8.app/Contents/Resources/bin/yak}"
YAK_LOCAL_REPO="${ENV_YAK_LOCAL_REPO:-${YAK_LOCAL_REPO:-$HOME/.rhino-gh-kit/yak-local-repo}}"

SUPPORT="$HOME/Library/Application Support/McNeel/Rhinoceros"
PACKAGES="$SUPPORT/packages/$RHINO_VERSION"
LIBRARIES="$SUPPORT/$RHINO_VERSION/Plug-ins/Grasshopper (b45a29b1-4343-4035-989e-044e8580d9cf)/Libraries"

[ -f "$REPO/$MANIFEST" ] || die "no $REPO/$MANIFEST"
PKG_NAME="$(sed -n 's/^name:[[:space:]]*//p' "$REPO/$MANIFEST" | head -1)"
[ -n "$PKG_NAME" ] || die "$MANIFEST has no name:"

# The package name is also the Package Manager entry's display name — the yak
# manifest has no separate display-name key — so it is tempting to write it with
# a space. Don't: `yak build` and `yak search` accept one, but `yak install "My
# Plugin" <version>` prints its usage and bails, no matter how the argument is
# quoted (verified 2026-08-22, yak 8.x), which takes the install stage with it.
# Fail here rather than four stages later.
case "$PKG_NAME" in
  *[[:space:]]*) die "$MANIFEST name '$PKG_NAME' contains whitespace; yak install cannot take it" ;;
esac

# Rhino's Package Manager renders Author / Description / Url from the package
# record a *source search* returns. A manifest missing them shows a row of blanks
# next to the name, and nothing downstream ever complains — so complain here.
for key in authors description url; do
  grep -q "^$key:" "$REPO/$MANIFEST" || die "$MANIFEST has no $key: (Package Manager shows it blank)"
done

cd "$REPO"

# -- 1a. Validate filenames -------------------------------------------------
# Outside the CODEGEN guard on purpose: the portable-charset pass is repo
# hygiene and applies to a consumer project with CODEGEN=0 too, while the
# stem/header pass simply finds nothing to check there.
step "Validating filenames"
python3 "$KIT/tooling/check_filenames.py" --root "$REPO" || die "filename validation failed"

# -- 1b. Validate the @component headers ------------------------------------
# Fatal on drift between a header and its RunScript signature. Grasshopper
# tolerates that drift silently on the canvas — it rewrites the signature on
# every solve, for inputs as well as outputs — so this and the C# compiler are
# the only things that ever catch it.
if [ "$CODEGEN" = "1" ]; then
  step "Validating headers"
  python3 "$KIT/tooling/gh_meta.py" --all --check || die "header validation failed"

  # -- 2. Generate ----------------------------------------------------------
  # gh_codegen.py requires --resource-prefix rather than defaulting to one: a
  # wrong prefix compiles cleanly and silently shows no icons. Derive it from
  # GHA_NAME (the assembly name) unless the conf pinned one explicitly, which
  # is only needed when <RootNamespace> differs from <AssemblyName>.
  case " ${CODEGEN_ARGS[*]-} " in
    *" --resource-prefix "*) ;;
    *) CODEGEN_ARGS+=(--resource-prefix "${GHA_NAME%.gha}.Icons") ;;
  esac

  step "Generating build/gen"
  python3 "$KIT/tooling/gh_codegen.py" ${CODEGEN_ARGS[@]+"${CODEGEN_ARGS[@]}"}
else
  # CODEGEN=0: the plugin carries no `@component` headers, so there is nothing to
  # validate and nothing to generate. Both stages are skipped rather than made
  # tolerant — a repo can hold script components that this plugin has no relation
  # to, and failing its build over their headers would be nonsense.
  step "Skipping header validation and codegen (CODEGEN=0)"
fi

# -- 3. Build ---------------------------------------------------------------
step "Building $GHA_NAME"
dotnet build "$REPO/$BUILD_TARGET" -c Release

# Package version and assembly version must agree: the manifest is what a yak
# repository indexes, the assembly is what Grasshopper's plugin list shows, and
# a mismatch between them is invisible until someone tries to work out which
# build they are running.
MANIFEST_VERSION="$(awk '/^version:/ {print $2; exit}' "$REPO/$MANIFEST")"
CSPROJ_VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO/$CSPROJ" | head -1)"
[ "$MANIFEST_VERSION" = "$CSPROJ_VERSION" ] || \
  die "version mismatch: $MANIFEST=$MANIFEST_VERSION csproj=$CSPROJ_VERSION"
echo "version $CSPROJ_VERSION (manifest and assembly agree)"

[ "$STAGE" = "build" ] && { step "Done (build only)"; exit 0; }

# -- 4. Package -------------------------------------------------------------
# yak build packages the *current directory*, so stage a clean folder holding
# exactly the manifest, the .gha and the icon — nothing else. The finished .yak
# is then copied into the local repo, which is what `install` and Rhino's
# Package Manager read.
step "Packaging .yak"
[ -x "$YAK" ] || die "yak not found at $YAK"
STAGING="$REPO/build/yak"
rm -rf "$STAGING"
mkdir -p "$STAGING"
cp "$REPO/$MANIFEST" "$STAGING/"
cp "$BUILT" "$STAGING/"

# The package icon identifies the entry in Rhino's Package Manager, so it is
# rasterized larger than the 24px canvas slot a component icon uses. A manifest
# that names an icon it cannot get is a build failure, not a silent blank.
if [ -n "${PACKAGE_ICON_SVG:-}" ]; then
  [ -f "$REPO/$PACKAGE_ICON_SVG" ] || die "PACKAGE_ICON_SVG not found: $REPO/$PACKAGE_ICON_SVG"
  sips -s format png -Z 128 "$REPO/$PACKAGE_ICON_SVG" --out "$STAGING/icon.png" >/dev/null
elif grep -q '^icon:' "$REPO/$MANIFEST"; then
  die "$MANIFEST declares an icon: but $CONF sets no PACKAGE_ICON_SVG"
fi

( cd "$STAGING" && "$YAK" build --platform any )
YAKFILE="$(find "$STAGING" -name '*.yak' -maxdepth 1 | head -1)"
[ -n "$YAKFILE" ] || die "yak build produced no .yak"
echo "built $YAKFILE"
echo "contents:"
unzip -l "$YAKFILE"

mkdir -p "$YAK_LOCAL_REPO"
cp "$YAKFILE" "$YAK_LOCAL_REPO/"
echo "published (privately) -> $YAK_LOCAL_REPO/$(basename "$YAKFILE")"

[ "$STAGE" = "package" ] && { step "Done (packaged)"; exit 0; }

# -- 5. Install -------------------------------------------------------------
# A yak install, from the local folder repo. Rhino still has to be restarted to
# pick the new binary up — the file on disk changing does not swap what a
# running instance already mapped. Verify which binary is live by reflecting on
# something the new build changed, never by the file's timestamp.
step "Installing $PKG_NAME $CSPROJ_VERSION from $YAK_LOCAL_REPO"

# A loose .gha in Grasshopper's Libraries folder is how these projects installed
# before packaging. Left in place it loads a SECOND copy of every component and
# the two collide on ComponentGuid, so park it reversibly — rename, never delete,
# since a `.gha.disabled` can be renamed back to bisect a build.
if [ -f "$LIBRARIES/$GHA_NAME" ]; then
  mv "$LIBRARIES/$GHA_NAME" "$LIBRARIES/$GHA_NAME.disabled"
  echo "parked the hand-copied build: $LIBRARIES/$GHA_NAME.disabled"
fi

# Re-installing the SAME version is the normal development case — the version
# only moves when a release is cut, so without this every rebuild would install
# under a directory yak already considers present. Removing the version folder
# first makes the install idempotent; unlink is safe with Rhino running (it
# keeps its own mapping of the old inode until it exits, unlike a `cp` over the
# file in place, which has crashed Rhino).
if [ -d "$PACKAGES/$PKG_NAME/$CSPROJ_VERSION" ]; then
  rm -rf "$PACKAGES/$PKG_NAME/$CSPROJ_VERSION"
  echo "removed the previously installed $CSPROJ_VERSION"
fi

"$YAK" install --source "$YAK_LOCAL_REPO" "$PKG_NAME" "$CSPROJ_VERSION"

INSTALLED="$PACKAGES/$PKG_NAME/$CSPROJ_VERSION/$GHA_NAME"
[ -f "$INSTALLED" ] || die "install did not produce $INSTALLED"
# manifest.txt is how Rhino picks which installed version is active; a stale one
# leaves the new .gha on disk and unloaded, which looks exactly like a build
# that did not take.
ACTIVE="$(head -1 "$PACKAGES/$PKG_NAME/manifest.txt" 2>/dev/null || true)"
[ "$ACTIVE" = "$CSPROJ_VERSION" ] || die "manifest.txt says '$ACTIVE', expected '$CSPROJ_VERSION'"
echo "installed -> $INSTALLED"
echo "RESTART RHINO to load it."

# One-time, per machine, and not settable from here: the Package Manager fills
# Author / Url / Description / the version dropdown from the record a *source
# search* returns, never from the installed manifest.yml sitting right next to
# the .gha. Until $YAK_LOCAL_REPO is one of Rhino's package sources, a privately
# installed package renders as its name, its installed version, and four blanks.
# The setting lives in Rhino's app settings, which a running Rhino rewrites when
# it exits, so a script cannot safely add it — say so instead.
echo "if the Package Manager entry shows blank Author/Url/Description, add"
echo "  $YAK_LOCAL_REPO"
echo "as a package source in Rhino (Package Manager > settings > package sources)."

[ "$STAGE" = "install" ] && { step "Done (installed)"; exit 0; }

# -- 6. Push ----------------------------------------------------------------
# Publishing to the Yak server is PUBLIC and effectively irreversible: a version
# can be yanked but never re-uploaded with different content. The stages above
# already gave a real, versioned, upgradeable install without it, so reach for
# this only when the intent is genuinely to distribute to strangers — and check
# the project's own rules first, since a project may forbid it outright.
step "Push to the PUBLIC Yak server"
echo "About to publish $(basename "$YAKFILE") as $PKG_NAME $CSPROJ_VERSION — this is public and permanent."
read -r -p "Type the version number to confirm: " CONFIRM
[ "$CONFIRM" = "$CSPROJ_VERSION" ] || die "aborted"
( cd "$STAGING" && "$YAK" push "$(basename "$YAKFILE")" )
step "Done (pushed)"
