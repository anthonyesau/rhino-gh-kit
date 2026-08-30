#!/usr/bin/env python3
"""Report — and re-serve — every install entry for a Claude Code plugin.

`claude plugin update` takes a `--scope`, and plain `claude plugin list` does not
show which scope belongs to which project: it prints one identical-looking block
per install with a scope and no directory. Guessing is not merely unhelpful, it is
unsafe — passing a `--scope` that has no entry for the current project does not
fail, it silently applies the update to whichever *other* project holds the only
entry at that scope, and reports success.

`claude plugin list --json` carries the missing field, `projectPath`, on every
entry. This module derives the scope from it instead of guessing.

  kit_scopes.py                 # table of every install entry, live and stale
  kit_scopes.py --scope-for     # the scope serving CWD (or --dir), or NONE (exit 1)
  kit_scopes.py --plan          # commands to re-serve every live entry
  kit_scopes.py --exec          # run that plan, then verify each entry moved

A `user`-scope entry serves every directory on the machine; that is the one scope
that ends the per-project bookkeeping, and `--plan` says so when it finds one.
"""

import argparse
import json
import os
import subprocess
import sys

DEFAULT_PLUGIN = "rhino-gh-kit@rhino-gh-kit"


def entries(plugin):
    """Every install entry for `plugin`, newest-first as the CLI reports them."""
    out = subprocess.run(
        ["claude", "plugin", "list", "--json"],
        capture_output=True, text=True,
    )
    if out.returncode != 0:
        sys.exit(f"claude plugin list --json failed: {out.stderr.strip()}")
    try:
        rows = json.loads(out.stdout)
    except json.JSONDecodeError as e:
        sys.exit(f"could not parse `claude plugin list --json` output: {e}")
    return [r for r in rows if r.get("id") == plugin]


def real(path):
    return os.path.realpath(path) if path else ""


def serves(entry, directory):
    """Does `entry` serve `directory`? Returns match specificity, or None.

    A `user`-scope entry serves the whole machine (specificity 0). A project- or
    local-scope entry serves its own projectPath and anything under it; the
    longest such path wins, so a nested project beats its parent.
    """
    if entry.get("scope") == "user":
        return 0
    p = real(entry.get("projectPath", ""))
    if not p:
        return None
    d = real(directory)
    if d == p or d.startswith(p + os.sep):
        return len(p)
    return None


def resolve(rows, directory):
    """The single entry serving `directory`, or None."""
    scored = [(s, e) for e in rows if (s := serves(e, directory)) is not None]
    if not scored:
        return None
    scored.sort(key=lambda t: t[0], reverse=True)
    return scored[0][1]


def live(entry):
    """False when the entry points at a directory that no longer exists."""
    if entry.get("scope") == "user":
        return True
    p = entry.get("projectPath", "")
    return bool(p) and os.path.isdir(p)


def report(rows, directory):
    if not rows:
        print("no install entries found")
        return
    here = resolve(rows, directory)
    newest = max(r.get("version", "") for r in rows)
    for e in rows:
        marks = []
        if e is here:
            marks.append("← serves this directory")
        if not live(e):
            marks.append("STALE: projectPath is gone")
        if e.get("version") != newest:
            marks.append(f"behind (newest installed is {newest})")
        print(
            f"  {e.get('scope','?'):<8} {e.get('version','?'):<8} "
            f"{e.get('projectPath') or '(machine-wide)'}"
            + ("   " + "; ".join(marks) if marks else "")
        )
    if here is None:
        print(f"\n  NONE of these serve {real(directory)} — do not guess a scope.")


def plan(rows):
    """Shell commands to re-serve every live entry, one per install."""
    cmds = []
    for e in rows:
        if not live(e):
            continue
        scope = e["scope"]
        if scope == "user":
            cmds.append((None, scope))
        else:
            cmds.append((e["projectPath"], scope))
    return cmds


def render(cmds, plugin):
    lines = ["claude plugin marketplace update " + plugin.split("@")[-1]]
    for cwd, scope in cmds:
        update = f"claude plugin update {plugin} --scope {scope}"
        lines.append(update if cwd is None else f'(cd "{cwd}" && {update})')
    return lines


def run(cmds, plugin, rows):
    marketplace = plugin.split("@")[-1]
    steps = [(None, ["claude", "plugin", "marketplace", "update", marketplace])]
    for cwd, scope in cmds:
        steps.append((cwd, ["claude", "plugin", "update", plugin, "--scope", scope]))
    failed = False
    for cwd, argv in steps:
        where = f" (in {cwd})" if cwd else ""
        print(f"$ {' '.join(argv)}{where}")
        r = subprocess.run(argv, cwd=cwd, capture_output=True, text=True)
        print("  " + (r.stdout.strip() or r.stderr.strip()).replace("\n", "\n  "))
        if r.returncode != 0:
            failed = True

    print("\nafter:")
    after = entries(plugin)
    report(after, os.getcwd())
    before = {(e["scope"], e.get("projectPath")): e.get("version") for e in rows}
    for e in after:
        key = (e["scope"], e.get("projectPath"))
        if key in before and before[key] == e.get("version"):
            print(
                f"  note: {e['scope']} {e.get('projectPath') or '(machine-wide)'} "
                f"is still on {e.get('version')} — either already current, or the "
                f"update did not reach it."
            )
    return 1 if failed else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--plugin", default=DEFAULT_PLUGIN, help="plugin@marketplace id")
    ap.add_argument("--dir", default=os.getcwd(), help="directory to resolve (default: cwd)")
    ap.add_argument("--scope-for", action="store_true",
                    help="print only the scope serving --dir; NONE and exit 1 if none")
    ap.add_argument("--plan", action="store_true", help="print re-serve commands")
    ap.add_argument("--exec", dest="execute", action="store_true",
                    help="run the plan, then verify each entry moved")
    args = ap.parse_args()

    rows = entries(args.plugin)

    if args.scope_for:
        e = resolve(rows, args.dir)
        if e is None:
            print("NONE")
            return 1
        print(e["scope"])
        return 0

    if args.plan or args.execute:
        cmds = plan(rows)
        if not cmds:
            print(f"no live install entries for {args.plugin}")
            return 1
        if any(scope == "user" for _, scope in cmds):
            print("# a user-scope entry serves every project on this machine\n")
        if args.execute:
            return run(cmds, args.plugin, rows)
        print("\n".join(render(cmds, args.plugin)))
        return 0

    print(f"{args.plugin} install entries:")
    report(rows, args.dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
