#!/usr/bin/env bash
#
# The release pipeline for a compiled Grasshopper plugin built from `@component`
# headers: validate → generate → build → package → install, as cumulative
# stages, and then, as separate runs, the pushes. Shared by every project that
# compiles a .gha with `gh_codegen.py`.
#
#   $KIT/tooling/publish.sh --repo <path> [build|package|install|push|push-test]
#
# With no stage named it runs through `package`, so every compile also leaves a
# .yak in $YAK_LOCAL_REPO. `build` stops before packaging; `install` installs the
# .yak. `push` uploads to the public Yak server and `push-test` to McNeel's test
# server (https://test.yak.rhino3d.com, wiped every night), the way to rehearse a
# push. Neither builds: each uploads the package holding the build installed in
# Rhino, so the order is install, restart Rhino and test, then push.
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
# Doing both at once leaves the same library in two load paths with no way to
# tell which one is live, so the install stage parks a loose copy as
# `.gha.disabled` when it finds one.
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
#                                           #   projects; the environment wins over conf,
#                                           #   so exporting it from the shell profile
#                                           #   points every project on the machine at
#                                           #   one folder).
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
STAGE="package"
while [ $# -gt 0 ]; do
  case "$1" in
    --repo) REPO="$2"; shift 2 ;;
    build|package|install|push|push-test) STAGE="$1"; shift ;;
    *) echo "usage: publish.sh [--repo <path>] [build|package|install|push|push-test]" >&2; exit 2 ;;
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

# yak keeps its login token at `.mcneel/yak.yml` relative to the CURRENT FOLDER,
# not under ~ as McNeel's Yak docs say (measured 2026-11-11, yak 0.15.5 / Rhino
# 8.37, macOS): `yak login` writes the file into whatever folder it runs from, and
# `yak push` finds it only from that same folder. The push stages therefore run
# yak from $HOME, so the one place a token can live is ~/.mcneel/yak.yml. Checked
# here, before anything is built, so a missing login costs nothing.
YAK_TOKEN="$HOME/.mcneel/yak.yml"
case "$STAGE" in
  push)      YAK_SERVER="https://yak.rhino3d.com" ;;
  push-test) YAK_SERVER="https://test.yak.rhino3d.com" ;;
esac
if [ -n "${YAK_SERVER:-}" ]; then
  [ -f "$YAK_TOKEN" ] || die "no yak login token at $YAK_TOKEN — log in from your home folder first:
  cd ~ && \"$YAK\" login --source $YAK_SERVER"
  # A token written into the project by a `yak login` run from there is an
  # untracked credential in the working copy, one `git add -A` from a commit.
  for stray in "$REPO/.mcneel/yak.yml" "$PWD/.mcneel/yak.yml"; do
    if [ -f "$stray" ] && [ "$stray" != "$YAK_TOKEN" ]; then
      echo "WARNING: $stray is a yak login token inside a working folder; delete it" \
           "(the push uses $YAK_TOKEN) and git-ignore .mcneel/"
    fi
  done
fi

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

# Package version and assembly version must agree: the manifest is what a yak
# repository indexes, the assembly is what Grasshopper's plugin list shows, and
# a mismatch between them is invisible until someone tries to work out which
# build they are running. Both are read from source, so check before building.
MANIFEST_VERSION="$(awk '/^version:/ {print $2; exit}' "$REPO/$MANIFEST")"
CSPROJ_VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO/$CSPROJ" | head -1)"
[ -n "$MANIFEST_VERSION" ] || die "$MANIFEST has no version:"
[ "$MANIFEST_VERSION" = "$CSPROJ_VERSION" ] || \
  die "version mismatch: $MANIFEST=$MANIFEST_VERSION csproj=$CSPROJ_VERSION"
echo "version $CSPROJ_VERSION (manifest and assembly agree)"

cd "$REPO"

# -- Push (push, push-test) -------------------------------------------------
# Publishing to the Yak server is PUBLIC and effectively irreversible: a version
# can be yanked but never re-uploaded with different content. `install` gives a
# real, versioned, upgradeable install without it, so reach for this only when
# the intent is genuinely to distribute to strangers — and check the project's
# own rules first, since a project may forbid it outright.
#
# `push-test` sends the same .yak to the test server instead. Anyone can still
# see it there until the nightly wipe, and until then the same version cannot be
# pushed to it again, so it prompts the same way.
#
# Neither push builds. They upload the package whose build is the one installed
# in Rhino, found by tested-yak.sh, and refuse when there is none: a rebuild is a
# different build (the commit is stamped into it), and only the installed one has
# been tested. So the order is `install`, restart Rhino and test, then push.
if [ -n "${YAK_SERVER:-}" ]; then
  [ -x "$YAK" ] || die "yak not found at $YAK"
  # shellcheck source=tested-yak.sh
  . "$KIT/tooling/tested-yak.sh"
  tested_yak "$PKG_NAME" "$CSPROJ_VERSION" "$GHA_NAME" "$YAK_LOCAL_REPO" "$PACKAGES" \
    || die "$TESTED_ERROR"
  HEAD_SHA="$(git -C "$REPO" rev-parse HEAD 2>/dev/null || true)"
  if [ -z "$TESTED_COMMIT" ]; then
    BUILT_FROM="an unknown commit"
  elif [ "$TESTED_COMMIT" = "$HEAD_SHA" ]; then
    BUILT_FROM="commit ${TESTED_COMMIT:0:9} (HEAD)"
  else
    BUILT_FROM="commit ${TESTED_COMMIT:0:9} (HEAD is ${HEAD_SHA:0:9})"
  fi

  if [ "$STAGE" = "push-test" ]; then
    step "Push to the TEST Yak server ($YAK_SERVER)"
    echo "About to push $(basename "$TESTED_YAK") as $PKG_NAME $CSPROJ_VERSION to $YAK_SERVER."
    echo "That version cannot be pushed there again until the server's nightly wipe."
  else
    step "Push to the PUBLIC Yak server"
    echo "About to publish $(basename "$TESTED_YAK") as $PKG_NAME $CSPROJ_VERSION — this is public and permanent."
  fi
  echo "It holds the installed build, made from $BUILT_FROM."
  read -r -p "Type the version number to confirm: " CONFIRM
  [ "$CONFIRM" = "$CSPROJ_VERSION" ] || die "aborted"
  # From $HOME, where yak finds the login token (see YAK_TOKEN above); the path
  # is absolute, so the folder change costs nothing.
  ( cd "$HOME" && "$YAK" push --source "$YAK_SERVER" "$TESTED_YAK" )
  if [ "$STAGE" = "push-test" ]; then
    echo "check: \"$YAK\" search --source $YAK_SERVER --all --prerelease $PKG_NAME"
  fi
  step "Done (pushed to $YAK_SERVER)"
  exit 0
fi

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

  # Icon rasterization shells to sips (macOS only). Off macOS, degrade to
  # --no-icons rather than fail the whole build over a cosmetic asset.
  if ! command -v sips >/dev/null 2>&1; then
    case " ${CODEGEN_ARGS[*]-} " in
      *" --no-icons "*) ;;
      *) CODEGEN_ARGS+=(--no-icons) ;;
    esac
  fi

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
[ -f "$BUILT" ] || die "the build produced no $BUILT"

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
# yak names the file from the manifest, lowercased (ScriptForge 0.4.5-beta ->
# scriptforge-0.4.5-beta-rh8_21-any.yak). Anything else means it packaged a
# different manifest than the one checked above.
PKG_LOWER="$(printf '%s' "$PKG_NAME" | tr '[:upper:]' '[:lower:]')"
case "$(basename "$YAKFILE")" in
  "$PKG_LOWER-$CSPROJ_VERSION-"*) ;;
  *) die "$(basename "$YAKFILE") is not named for $PKG_NAME $CSPROJ_VERSION" ;;
esac
echo "built $YAKFILE"
echo "contents:"
unzip -l "$YAKFILE"

mkdir -p "$YAK_LOCAL_REPO"
cp "$YAKFILE" "$YAK_LOCAL_REPO/"
echo "packaged -> $YAK_LOCAL_REPO/$(basename "$YAKFILE")"

[ "$STAGE" = "package" ] && { step "Done (packaged)"; exit 0; }

# -- 5. Install ------------------------------------------------------------
# A yak install, from the local folder repo. Rhino still has to be restarted to
# pick the new binary up — the file on disk changing does not swap what a
# running instance already mapped. Verify which binary is live by reflecting on
# something the new build changed, never by the file's timestamp.
step "Installing $PKG_NAME $CSPROJ_VERSION from $YAK_LOCAL_REPO"

# A loose .gha in Grasshopper's Libraries folder is how these projects installed
# before packaging. Left in place it is a second copy of the same library in a
# second load path, and nothing says which one Grasshopper kept — so park it
# reversibly: rename, never delete, since a `.gha.disabled` can be renamed back
# to bisect a build.
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

# yak stamps the plugin's GUID into the installed manifest as a `guid:` keyword.
# A package renamed without uninstalling its predecessor leaves a second package
# carrying the same GUID — the same plugin in two load paths, like the loose
# Libraries/ copy parked above. Uninstalling another package is the user's call,
# so name it rather than touch it.
GUID_KW="$(grep -o 'guid:[0-9a-fA-F-]*' "$PACKAGES/$PKG_NAME/$CSPROJ_VERSION/manifest.yml" 2>/dev/null | head -1 || true)"
if [ -n "$GUID_KW" ]; then
  for other in "$PACKAGES"/*/*/manifest.yml; do
    [ -f "$other" ] || continue
    case "$other" in "$PACKAGES/$PKG_NAME/"*) continue ;; esac
    if grep -qi "$GUID_KW" "$other"; then
      OTHER_NAME="$(basename "$(dirname "$(dirname "$other")")")"
      echo "WARNING: package '$OTHER_NAME' is also installed with $GUID_KW — the same plugin"
      echo "  would load twice. If it is this package's old name: \"$YAK\" uninstall $OTHER_NAME"
    fi
  done
fi
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

step "Done (installed)"
