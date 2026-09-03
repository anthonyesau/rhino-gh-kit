#!/usr/bin/env python3
"""Pin gh_meta.py's `@component` parser against Script Forge's own C# one over
one fixture set, audit-fixtures/, so the two cannot silently drift apart.

Each fixture in audit-fixtures/ is a deliberately valid, malformed, or
edge-case `@component` source; its own header/comment prose already states
what it is exercising (see the files themselves). This runner turns those
into assertions instead of prose, on BOTH parsers:

  * gh_meta.py, imported directly and driven the way `--check` drives it
    (parse_header + check_meta).
  * script-forge.cs's own private ParseHeader / WarnDriftAndQuotes, reached
    through tooling/fixture-runner/ -- a dev-only console harness (not a
    shipped artifact, but permanent tracked source) that
    compiles gh_codegen.py's generated stand-in for the real source (see that
    project's own comments for why) and reflects into the two methods, since
    neither has any public surface of its own to call.

Three outcomes, not two, matter here:

  * "ok"         -- parses; check_meta / WarnDriftAndQuotes may still flag
                    problems (0 or more). The parse itself may additionally
                    collect non-fatal warnings -- an unrecognized key -- which
                    `--check` prints without failing on; see DIVERGENCES 4.
  * "error"      -- the header itself is structurally broken (bad JSON, a
                    missing required key, an invalid `access`). Both parsers
                    raise/throw for exactly the same cases -- this is
                    unconditional, load-bearing grammar, not a validation
                    opinion either implementation could reasonably differ on.
  * "headerless" -- C#-only. A source with no `@component` marker at all
                    parses fine to Script Forge (nothing to plan) but is not
                    a valid gh_meta.py target, so this outcome only ever shows
                    up on the C# side; see DIVERGENCES below.

**Only headers are under test here.** Neither side compiles or runs a fixture's
body: the C# harness reflects into ParseHeader / WarnDriftAndQuotes and reads
the resulting HeaderMeta, and gh_meta.py parses text. So a green run says a
header resolves the same way on both parsers -- it does NOT say the fixture
forges, compiles, or solves on a canvas. A body that a live script component
would reject (a missing `using`, or an `out T` assigned as if Grasshopper had
not rewritten it to `out object`) passes every assertion below. Only forging the
fixture finds that; see script-forge/docs/forge-under-test.md.

DIVERGENCES -- the parsers deliberately do not always agree, and this runner
pins each gap rather than papering over it:

  1. check fails where the Forge warns. Every "ok" fixture that check_meta
     flags (a bad default, a duplicate variableName, header/signature drift, a
     stray quote) fails gh_meta.py --check (exit 1) but is only a Log warning
     to a live forge, which still builds the component. Deliberate: the public
     build path is the stricter gate; canvas iteration is not meant to stop
     for it. Tracked at runtime by `divergent` in main() and asserted nonzero.
  2. headerless is an error to gh_meta.py, a supported state to the Forge. A
     source with no `@component` marker at all cannot be validated (there is
     nothing to validate), so gh_meta.py raises; the Forge creates a component
     from RunScript alone with stock defaults. Neither is "more correct" --
     they answer different questions ("is this a well-formed component
     source?" vs. "what should I put on the canvas?").
  3. componentGuid/upgradeFrom are gh_meta.py-only. They are compile-path
     keys with no forge-time meaning, so Script Forge's HeaderMeta has no
     field for either and WarnDriftAndQuotes cannot flag them no matter how
     malformed they are -- only gh_meta.py --check (which gates a compiled
     build) validates them. See err-upgrade-guid.cs / EXPECT's
     `csharp_problems` override.
  4. a header warning fails nothing. An unrecognized key is ignored by design
     on both sides, so gh_meta.py reports it as a `WARN` line that leaves
     `--check` at exit 0, and Script Forge logs it as `KEY WARNING`. It is
     still a finding both parsers must agree on, so this runner counts it in
     EXPECT's `warnings` and folds it into the C# comparison.

Usage:
    python3 script-forge/tooling/test_fixtures.py             # rebuilds the C# harness
    python3 script-forge/tooling/test_fixtures.py --no-build   # reuse its last build
"""

import glob
import json
import os
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLING = os.path.dirname(os.path.abspath(__file__))
FIXTURES = os.path.join(REPO, "audit-fixtures")
RUNNER_DIR = os.path.join(TOOLING, "fixture-runner")
RUNNER_DLL = os.path.join(RUNNER_DIR, "bin", "Release", "net8.0", "FixtureRunner.dll")

# REPO is script-forge/ -- its own project root for the build (see publish.conf).
# gh_meta.py and publish.sh are kit-wide, one level up, shared with other
# projects that reach them the same way.
KIT_ROOT = os.path.dirname(REPO)
KIT_TOOLING = os.path.join(KIT_ROOT, "tooling")

sys.path.insert(0, KIT_TOOLING)
import gh_meta  # noqa: E402


# ---------------------------------------------------------------- fixtures --
# One entry per file gh_meta.py can be pointed at. `odd-line.txt` is
# deliberately absent: it is not a `.cs`/`.py` header source at all -- it
# exercises RunScript's own "does this look like a list of file paths" Source
# detection on a live canvas, a code path neither parser (nor this static
# runner) reaches. Likewise every fixture's *live-canvas* behavior (keyword
# matching, wire recycling, pinning) is out of scope here; this runner only
# pins what a header, read cold off disk, resolves to.
#
# `problems` is either an exact expected count (pinned regression protection)
# or the literal "parity", meaning: don't hardcode a number, just require the
# two parsers to find the *same* count on this fixture (and at least one).
# Both routes assert the plan's one deliberately-kept divergence: gh_meta.py
# --check turns every one of these into a hard failure (exit 1); Script
# Forge's WarnDriftAndQuotes turns the identical finding into a Log warning
# and forges anyway. That is not incidental -- see DIVERGENCES below.
#
# `warnings` (default 0) is the OTHER Python-side count: findings the parse
# itself collected, which `--check` prints and does not fail on. Script Forge
# puts them in the same Log as everything else, so the C# side is compared
# against `problems` + `warnings` unless `csharp_problems` overrides it.
#
# `access` pins each param's normalized access value, inputs then outputs --
# the string ApplyDef's GH_ParamAccess ladder reads.
EXPECT = {
    "base64icon-cs.cs":     {"outcome": "ok", "problems": 0},
    "display-name-cs.cs":   {"outcome": "ok", "problems": 0},
    "err-bad-access.cs":    {"outcome": "error", "error_substr": "bad access"},
    "err-case-clash-cs.cs": {"outcome": "error", "error_substr": "differ only in case"},
    "err-default-values.cs": {"outcome": "ok", "problems": 7},
    "err-dup-display.cs":   {"outcome": "ok", "problems": 1},
    "err-no-name.cs":       {"outcome": "error", "error_substr": "missing required"},
    "err-param-fields.cs":  {"outcome": "error", "error_substr": "missing required"},
    "err-unterminated.cs":  {"outcome": "error", "error_substr": "not valid JSON"},
    # componentGuid/upgradeFrom are compile-path-only keys that Script Forge's
    # ParseHeader never reads into HeaderMeta at all (it has no field for
    # either) -- so no amount of malformation here can produce a C# warning,
    # only a gh_meta.py --check failure. csharp_problems pins that at 0
    # explicitly rather than leaving it to fall out of "parity" by accident.
    "err-upgrade-guid.cs":  {"outcome": "ok", "problems": 2, "csharp_problems": 0},
    "err-variable-name.cs": {"outcome": "ok", "problems": "parity"},
    "hashcomment-py.py":    {"outcome": "ok", "problems": 0},
    "headerless-cs.cs":     {"outcome": "error", "error_substr": "no @component header found",
                              "csharp_outcome": "headerless"},
    "headerless-py.py":     {"outcome": "error", "error_substr": "no @component header found",
                              "csharp_outcome": "headerless"},
    # Mixed-case access values normalize; both parsers store the canonical
    # spelling, which is what decides the built param's GH_ParamAccess.
    "mixed-case-access-cs.cs": {"outcome": "ok", "problems": 0,
                              "access": ["tree", "list", "item", "tree"]},
    "multiline-cs.cs":      {"outcome": "ok", "problems": 0,
                              "description": "Line one.\nLine two.\nLine three."},
    "orphan-name-cs.cs":    {"outcome": "ok", "problems": 0},
    "pascal-keys-cs.cs":    {"outcome": "ok", "problems": 0},
    "pinned-cs.cs":         {"outcome": "ok", "problems": 0,
                              "instance_guid": "11112222-3333-4444-5555-666677778888"},
    "pointgrid-py.py":      {"outcome": "ok", "problems": 0},
    "remap-cs.cs":          {"outcome": "ok", "problems": 0},
    "remap-v2-cs.cs":       {"outcome": "ok", "problems": 0},
    "unique-create-cs.cs":  {"outcome": "ok", "problems": 0},
    # One unknown key at the component level, one inside a param. Neither is a
    # problem -- `--check` still exits 0 -- but neither is silent either.
    "unknown-key-cs.cs":    {"outcome": "ok", "problems": 0, "warnings": 2},
    "warnings-cs.cs":       {"outcome": "ok", "problems": "parity"},
}

EXCLUDED = {"odd-line.txt"}


# ------------------------------------------------------------- Python side --

def run_python(name):
    path = os.path.join(FIXTURES, name)
    try:
        meta = gh_meta.parse_header(path)
    except gh_meta.HeaderError as e:
        return {"outcome": "error", "error": str(e)}
    problems = gh_meta.check_meta(path, meta)
    return {"outcome": "ok", "problems": problems,
            "warnings": meta["warnings"], "meta": meta}


# --------------------------------------------------------------- C# side --

def build_csharp():
    subprocess.run(
        ["dotnet", "build", RUNNER_DIR, "-c", "Release", "-v", "quiet"],
        cwd=REPO, check=True)


def run_csharp(names):
    paths = [os.path.join(FIXTURES, n) for n in names]
    proc = subprocess.run(["dotnet", RUNNER_DLL, *paths],
                           capture_output=True, text=True, cwd=REPO)
    if proc.returncode != 0:
        sys.exit("error: fixture-runner exited %d:\n%s%s"
                  % (proc.returncode, proc.stdout, proc.stderr))
    rows = json.loads(proc.stdout)
    return {row["file"]: row for row in rows}


# -------------------------------------------------------------------- main --

def main(argv):
    do_build = "--no-build" not in argv

    on_disk = {os.path.basename(p) for p in
               glob.glob(os.path.join(FIXTURES, "*.cs")) +
               glob.glob(os.path.join(FIXTURES, "*.py"))}
    missing_expectations = on_disk - EXCLUDED - set(EXPECT)
    if missing_expectations:
        sys.exit("error: audit-fixtures/ has a header source with no entry in "
                  "EXPECT (or EXCLUDED, if it's not meant to be parsed): "
                  + ", ".join(sorted(missing_expectations)))
    stale_expectations = set(EXPECT) - on_disk
    if stale_expectations:
        sys.exit("error: EXPECT names a fixture no longer on disk: "
                  + ", ".join(sorted(stale_expectations)))

    if do_build:
        print("== regenerating build/gen and validating headers")
        sys.stdout.flush()
        subprocess.run([os.path.join(KIT_TOOLING, "publish.sh")],
                        cwd=REPO, check=True)
        print("== building script-forge/tooling/fixture-runner")
        sys.stdout.flush()
        build_csharp()

    names = sorted(EXPECT)
    csharp = run_csharp(names)

    failures = []
    # Fixtures where gh_meta.py --check would exit 1 (it found problems) but
    # the Forge parses the same header as "ok" and only warns -- the plan's
    # one deliberate divergence between the two parsers, see DIVERGENCES in
    # this module's docstring. Counted rather than assumed, so a future edit
    # that quietly closes the gap (or removes every fixture that exercises it)
    # fails loudly instead of the divergence just stopping being under test.
    divergent = 0
    print()
    print(f"{'fixture':30s} {'py':6s} {'cs':6s}  verdict   (py = problems+warnings)")
    for name in names:
        exp = EXPECT[name]
        py = run_python(name)
        cs = csharp.get(name)
        if cs is None:
            failures.append(f"{name}: fixture-runner produced no row for this file")
            continue

        row_fail = []

        if py["outcome"] != exp["outcome"]:
            row_fail.append(f"python outcome={py['outcome']!r}, expected {exp['outcome']!r}"
                             + (f" ({py.get('error')})" if py["outcome"] == "error" else ""))
        elif exp["outcome"] == "error":
            sub = exp["error_substr"]
            if sub not in py["error"]:
                row_fail.append(f"python error {py['error']!r} does not contain {sub!r}")

        # A structural JSON/grammar failure (bad access, missing key,
        # unterminated JSON) is a hard failure on BOTH sides -- `csharp_outcome`
        # only appears on fixtures where the two are expected to disagree
        # (the headerless ones).
        expected_cs_outcome = exp.get("csharp_outcome", exp["outcome"])
        if cs["outcome"] != expected_cs_outcome:
            row_fail.append(f"csharp outcome={cs['outcome']!r}, expected {expected_cs_outcome!r}"
                             + (f" ({cs.get('error')})" if cs["outcome"] == "error" else ""))

        if exp["outcome"] == "ok" and py["outcome"] == "ok" and cs["outcome"] == "ok":
            py_n, py_w = len(py["problems"]), len(py["warnings"])
            cs_n = len(cs["warnings"])
            if py_n > 0:
                # DIVERGENCE 1: gh_meta.py --check would exit 1 on this file
                # (py_n problems found); the Forge parsed it as "ok" -- warns
                # at most, never throws. See the module docstring.
                divergent += 1
            want = exp["problems"]
            if want == "parity":
                # Both Python-side counts land in the Forge's one Log, so the
                # comparison is against their sum.
                if py_n + py_w != cs_n:
                    row_fail.append(f"finding counts diverge: python={py_n}+{py_w}, csharp={cs_n}")
                elif py_n + py_w == 0:
                    row_fail.append("expected parity with at least one finding, got zero on both")
            else:
                want_w = exp.get("warnings", 0)
                if py_n != want:
                    row_fail.append(f"python found {py_n} problems, expected {want}")
                if py_w != want_w:
                    row_fail.append(f"python found {py_w} warnings, expected {want_w}")
                want_cs = exp.get("csharp_problems", want + want_w)
                if cs_n != want_cs:
                    row_fail.append(f"csharp found {cs_n} warnings, expected {want_cs}")

            if "access" in exp:
                py_access = [p["access"] for p in
                             py["meta"]["inputs"] + py["meta"]["outputs"]]
                if py_access != exp["access"]:
                    row_fail.append(f"python access {py_access} != {exp['access']}")
                if cs.get("access") != exp["access"]:
                    row_fail.append(f"csharp access {cs.get('access')} != {exp['access']}")

            if "description" in exp and py["meta"]["description"] != exp["description"]:
                row_fail.append(f"python description {py['meta']['description']!r} != "
                                 f"expected {exp['description']!r}")
            if "description" in exp and cs.get("description") != exp["description"]:
                row_fail.append(f"csharp description {cs.get('description')!r} != "
                                 f"expected {exp['description']!r}")

            if "instance_guid" in exp:
                want_guid = exp["instance_guid"]
                if py["meta"]["instance_guid"] != want_guid:
                    row_fail.append(f"python instance_guid {py['meta']['instance_guid']!r} != "
                                     f"{want_guid!r}")
                if (cs.get("instanceGuid") or "").lower() != want_guid.lower():
                    row_fail.append(f"csharp instanceGuid {cs.get('instanceGuid')!r} != "
                                     f"{want_guid!r}")

        py_n = (f"{len(py['problems'])}+{len(py['warnings'])}"
                if py["outcome"] == "ok" else "-")
        cs_n = len(cs.get("warnings", [])) if cs["outcome"] == "ok" else "-"
        verdict = "ok" if not row_fail else "FAIL"
        print(f"{name:30s} {str(py_n):6s} {str(cs_n):6s}  {verdict}")
        for msg in row_fail:
            print(f"    {msg}")
            failures.append(f"{name}: {msg}")

    if divergent == 0:
        failures.append("no fixture exercised the check-fails/forge-warns divergence -- "
                         "the plan's one deliberate difference between the two parsers "
                         "would no longer be under test (see DIVERGENCES 1 in this "
                         "module's docstring)")

    print()
    print(f"check-fails/forge-warns divergence: exercised by {divergent} fixture(s)")
    if failures:
        print(f"FAILED: {len(failures)} problem(s) across {len(names)} fixtures")
        return 1

    print(f"passed: {len(names)} fixtures, both parsers agree "
          f"({len(EXCLUDED)} excluded: {', '.join(sorted(EXCLUDED))})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
