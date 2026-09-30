#!/usr/bin/env python3
"""Fail when the two set-param-value payloads' shared sections differ.

tooling/set-param-value.cs and tooling/set-param-value-special.cs are each pasted
whole into mcp__rhino__run_csharp, which cannot include one file from another, so
the code both need (finding the target, setting a plain param, scheduling the
solution) is copied into each between `// --- SHARED with …` and
`// --- END SHARED ---`. This checks the two copies are identical.

    python3 tooling/check_set_param_sync.py      # exit 1 and a diff on drift
"""

import difflib
import os
import sys

TOOLING = os.path.dirname(os.path.abspath(__file__))
FILES = ["set-param-value.cs", "set-param-value-special.cs"]


def shared(name):
    lines = open(os.path.join(TOOLING, name), encoding="utf-8").read().splitlines()
    starts = [i for i, l in enumerate(lines) if l.startswith("// --- SHARED with ")]
    ends = [i for i, l in enumerate(lines) if l == "// --- END SHARED ---"]
    if len(starts) != 1 or len(ends) != 1 or ends[0] < starts[0]:
        sys.exit(f"FAIL {name}: needs exactly one SHARED marker followed by one END SHARED marker")
    return lines[starts[0] + 1:ends[0]]


a, b = (shared(f) for f in FILES)
if a != b:
    sys.stdout.writelines(l + "\n" for l in difflib.unified_diff(a, b, *FILES, lineterm=""))
    sys.exit(f"FAIL the shared sections of {FILES[0]} and {FILES[1]} differ")
print(f"OK   set-param-value payloads: shared section identical ({len(a)} lines)")
