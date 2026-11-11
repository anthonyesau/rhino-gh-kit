# tested-yak.sh — sourced by publish.sh (the push stages) and release.sh, never
# run. Finds the .yak whose build is the one installed in Rhino, so a push or a
# release ships the build that was installed and tested rather than a rebuild.
#
# A rebuild is not the same build. The .NET SDK stamps the commit into every
# assembly's informational version (`0.4.5-beta+<40-hex sha>`), so any commit
# between testing and shipping — a README edit included — changes the bytes, and
# a source edit changes them for real. The installed copy is the reference: it is
# what Rhino loaded when it was tested.
#
#   tested_yak <package> <version> <gha name> <local yak repo> <packages dir>
#
# On success sets TESTED_YAK (the matching .yak in the local repo) and
# TESTED_COMMIT (the commit the build was made from, or empty when the assembly
# carries none). On failure returns 1 with TESTED_ERROR saying what to do; the
# caller decides whether that is fatal.
#
# Copied byte for byte into projects that build without the kit; keep the copies
# identical.

tested_yak() {
  local pkg="$1" version="$2" gha="$3" repo="$4" packages="$5"
  local installed="$packages/$pkg/$version/$gha"
  TESTED_YAK="" TESTED_COMMIT="" TESTED_ERROR=""

  if [ ! -f "$installed" ]; then
    TESTED_ERROR="$pkg $version is not installed, so it has not been tested.
  Install it (publish.sh install), restart Rhino and test it first."
    return 1
  fi
  # manifest.txt picks which installed version Rhino loads; another one there
  # means the test ran against a different build.
  local active
  active="$(head -1 "$packages/$pkg/manifest.txt" 2>/dev/null || true)"
  if [ "$active" != "$version" ]; then
    TESTED_ERROR="Rhino loads $pkg '$active', not $version (see $packages/$pkg/manifest.txt).
  Install $version (publish.sh install), restart Rhino and test it first."
    return 1
  fi

  local want yak found="" count=0
  want="$(shasum -a 256 < "$installed" | cut -d' ' -f1)"
  # yak names the file from the manifest, lowercased.
  local lower
  lower="$(printf '%s' "$pkg" | tr '[:upper:]' '[:lower:]')"
  for yak in "$repo/$lower-$version-"*.yak; do
    [ -f "$yak" ] || continue
    if [ "$(unzip -p "$yak" "$gha" 2>/dev/null | shasum -a 256 | cut -d' ' -f1)" = "$want" ]; then
      found="$yak"
      count=$((count + 1))
    fi
  done
  if [ "$count" = "0" ]; then
    TESTED_ERROR="no $pkg $version package in $repo holds the installed build: it was
  rebuilt after it was installed. Install it again (publish.sh install), restart
  Rhino and test it."
    return 1
  elif [ "$count" != "1" ]; then
    TESTED_ERROR="$count packages in $repo hold the installed $pkg $version build;
  remove all but the one to ship: $(ls "$repo/$lower-$version-"*.yak | tr '\n' ' ')"
    return 1
  fi

  TESTED_YAK="$found"
  TESTED_COMMIT="$(LC_ALL=C grep -a -o "${version//./\\.}+[0-9a-f]\{40\}" "$installed" \
    | head -1 | sed 's/.*+//')" || true
  return 0
}
