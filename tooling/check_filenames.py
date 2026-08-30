#!/usr/bin/env python3
"""Check that every tracked path conforms to this repo's filename convention.

Two independent passes, both fatal:

1. **Portable charset** (repo-wide). Over `git ls-files`: every path segment is
   drawn from the POSIX Portable Filename Character Set (IEEE Std 1003.1) --
   `A-Z a-z 0-9 . _ -` -- with no leading `-`, no trailing `.` or space, and no
   Windows-reserved basename. Uppercase and `_` are *permitted*: POSIX allows
   them and `README.md`, `src/ScriptForge/`, `tooling/gh_meta.py` all depend on
   it. This pass encodes only the hard rule, never the kebab/snake/Pascal
   convention layered on top of it (that one is in docs/ship-a-plugin/file-naming.md).

2. **Stem <-> header name** (component sources only). For every source
   `gh_meta.all_sources()` finds, `kebab(header["name"]) == Path(stem)`, so the
   source stem, the icon stem and the `@component` name are one string. Files
   whose header does not parse are skipped, which is what correctly excludes
   `audit-fixtures/` (deliberately malformed) and `examples/native-ceiling/`
   (headerless, and not flat in `examples/` anyway).

This is deliberately NOT part of `gh_meta.check_meta`. `gh_meta.py` is a
hand-synced Python port of Script Forge's own header parser, and Script Forge
has no opinion about filenames; folding repo hygiene into it would muddy that
parity invariant. This checker also shells out to `git`, which the parser has no
business doing.

Usage:
    python3 tooling/check_filenames.py                  # check CWD
    python3 tooling/check_filenames.py --root <dir>     # check <dir> instead
    python3 tooling/check_filenames.py --help           # this message

Exit 0 when both passes are clean, 1 on any violation, 2 on a usage error.
"""

import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gh_meta  # noqa: E402

# The POSIX Portable Filename Character Set, per path segment.
PORTABLE = re.compile(r"^[A-Za-z0-9._-]+$")

# Reserved with *any* extension on Windows, so `nul.txt` is out too. Matched
# against the segment up to the first dot, case-insensitively.
WINDOWS_RESERVED = {"con", "prn", "aux", "nul"} | {
    f"{stem}{i}" for stem in ("com", "lpt") for i in range(1, 10)
}

# Pass 2 exemptions: scratch fixtures whose stem deliberately does not track
# their header `name`. Both are wire-preservation / rename test sources that no
# doc references; `examples/mystery.cs` additionally *cannot* take its kebab
# name, since `examples/mystery-surprise.cs` carries the same header `name` and
# already owns it. Matched as a path suffix, so a consuming project with an
# `examples/mystery.cs` of its own is exempted too — narrow enough to accept,
# but it is not the no-op in a consumer that a repo-relative list would be.
# Delete an entry the moment its file is renamed or retired.
STEM_EXEMPT = {
    "examples/mystery.cs",
    "examples/swap-script-test.cs",
}


def is_stem_exempt(path):
    """True when `path` is one of STEM_EXEMPT, whatever `--root` was passed.

    The entries are repo-relative, but `--root examples` makes a file's
    root-relative name `mystery.cs` — so the two can only ever be compared as a
    path *suffix*, on segment boundaries. Matching the bare tail instead would
    exempt any `mystery.cs` anywhere.
    """
    segs = os.path.abspath(path).split(os.sep)
    return any(segs[-len(e.split("/")):] == e.split("/") for e in STEM_EXEMPT)


def kebab(name):
    """'Sci-Fi Text Extrude PY' -> 'sci-fi-text-extrude-py'.

    The lowercase sibling of `gh_codegen.slug()`: same tokenization, joined with
    `-` instead of PascalCased.
    """
    return "-".join(p for p in re.split(r"[^A-Za-z0-9]+", name) if p).lower()


def tracked_paths(root):
    """Every path `git ls-files` reports for `root`, or None if it isn't a repo.

    `git ls-files` (rather than a walk) is what keeps untracked scratch out of
    pass 1 for free -- a local `examples/my probe.cs` is nobody's business.
    """
    try:
        out = subprocess.run(
            ["git", "-C", root, "ls-files", "-z"],
            check=True, capture_output=True,
        ).stdout.decode("utf-8", "surrogateescape")
    except (OSError, subprocess.CalledProcessError):
        return None
    return [p for p in out.split("\0") if p]


def check_charset(root):
    """Pass 1. Returns a list of problem strings."""
    paths = tracked_paths(root)
    if paths is None:
        print("note: not a git work tree — skipping the portable-charset pass")
        return []

    problems = []
    for path in paths:
        segs = path.split("/")
        for seg in segs:
            if not PORTABLE.match(seg):
                bad = sorted({c for c in seg if not re.match(r"[A-Za-z0-9._-]", c)})
                shown = " ".join(repr(c) for c in bad)
                problems.append(f"{path}: segment {seg!r} has non-portable character(s): {shown}")
            elif seg.startswith("-"):
                problems.append(f"{path}: segment {seg!r} starts with '-' (parsed as an option)")
            elif seg.endswith("."):
                problems.append(f"{path}: segment {seg!r} ends with '.' (Windows strips it)")
            elif seg.split(".")[0].lower() in WINDOWS_RESERVED:
                problems.append(f"{path}: segment {seg!r} is a Windows-reserved name")
    return problems


def check_stems(root):
    """Pass 2. Returns a list of problem strings.

    `gh_meta.all_sources` is flat, so it is called once per directory that
    `gh_meta.py --all --check` is documented to be run in: the repo root and
    `examples/`. Reusing it rather than walking is the point -- the filename
    check then covers exactly the files the header check does, and cannot drift.
    """
    problems = []
    for sub in ("", "examples"):
        d = os.path.join(root, sub) if sub else root
        if not os.path.isdir(d):
            continue
        for path in gh_meta.all_sources(d):
            rel = os.path.relpath(path, root)
            if is_stem_exempt(path):
                continue
            try:
                meta = gh_meta.parse_header(path)
            except gh_meta.HeaderError:
                continue  # not a component -- pass 1 already covered its name
            stem, _ = os.path.splitext(os.path.basename(path))
            want = kebab(meta["name"])
            if stem != want:
                problems.append(
                    f"{rel}: stem {stem!r} != kebab(header name {meta['name']!r}) == {want!r}"
                )
    return problems


def main(argv):
    args = argv[1:]
    if "--help" in args or "-h" in args:
        print(__doc__)
        return 0
    root = gh_meta.root_arg(args)
    if args:
        print(f"error: unrecognized argument(s): {' '.join(args)}\n", file=sys.stderr)
        print(__doc__, file=sys.stderr)
        return 2

    charset = check_charset(root)
    stems = check_stems(root)

    for p in charset:
        print(f"FAIL [charset] {p}")
    for p in stems:
        print(f"FAIL [stem]    {p}")

    if charset or stems:
        print(f"\n{len(charset) + len(stems)} filename problem(s) — see docs/ship-a-plugin/file-naming.md")
        return 1
    print("OK   filenames: portable charset + stem/header agreement")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
