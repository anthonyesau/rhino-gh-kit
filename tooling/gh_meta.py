#!/usr/bin/env python3
"""Parse the `@component` metadata header from a Grasshopper script source.

Handles C# (`.cs`, `/* @component ... */`) and Python (`.py`,
`\"\"\"@component ... \"\"\"` docstring or `# @component` comment lines).
The header format is specified once, canonically, in
`docs/write-scripts/header-reference.md` — this module is a Python port of that
grammar, and the push tooling and codegen call it so the parser lives in exactly
one place *in this kit*. That doc's appendix covers this module's own semantics
(`gh-meta: ignore`, what `--check` enforces). When this module and the forge's
own parser (`script-forge.cs`) disagree, fix this one to match the forge, not
the reverse.

**The body is one JSON object.** It opens with `{` on or after the `@component`
marker and its own matching brace terminates it, so there is no separate
terminator keyword.

Usage:
    python3 tooling/gh_meta.py my-component.cs          # one file -> JSON
    python3 tooling/gh_meta.py --all                    # every *.cs and *.py in CWD
    python3 tooling/gh_meta.py --all --check            # validate, exit 1 on error
    python3 tooling/gh_meta.py --all --root <dir>       # scan <dir> instead of CWD
    python3 tooling/gh_meta.py --help                   # this message

`--check` also validates, per file: no double quotes in the component/param
descriptions or labels (they break the ScriptEditor plugin builder), that each
`variableName` is a non-keyword identifier and unique across both sides, that no
two params on one side share a label, that a declared `default` is input-side
and agrees with its `type`, and for C# sources that the header's param names
match the RunScript signature (drift check).

Keys and values are matched **case-insensitively** — the spelling in the
reference doc is canonical, not required. Two keys of one object that differ
only in case is an error. An unrecognized key is ignored, as it always has
been, but reported as a non-fatal `WARN` line; it never fails `--check`.

A file that will not parse is reported either way: `--check` prints
`FAIL <path>: <reason>` on stdout alongside its per-file report, and the bare
form prints the same line on **stderr**, leaving stdout as the JSON a caller may
be piping. Both exit 1. On the bare form the JSON is still emitted, so a total
failure reads as `{}` on stdout with the reason on stderr.

`--all` scans the current working directory — run it from the project root whose
root-level component sources you want to validate (a consuming project, or this
kit). Point it elsewhere with `--root <dir>`. A source that is not a component
can opt out of the sweep with a `gh-meta: ignore` comment near the top of the
file; naming a file explicitly on the command line parses it regardless.
"""

import json
import os
import re
import sys


def _strip_comment(line):
    """Remove a leading comment marker so the grammar is style-agnostic:
    C# block comment, C# line comment, Python docstring quotes, or # lines."""
    t = line.strip()
    if t.startswith("/*"):
        t = t[2:].strip()
    if t.startswith('"""'):
        t = t[3:].strip()
    if t.startswith("//"):
        t = t[2:].strip()
    elif t.startswith("#"):
        t = t.lstrip("#").strip()
    return t


class HeaderError(Exception):
    pass


def parse_header(path):
    """Return a dict describing the component, or raise HeaderError.

    Shape:
        {
          "name": str, "nickname": str, "category": str, "subcategory": str,
          "icon": str, "description": str, "exposure": str|None,
          "language": str|None, "markers": [str, ...], "upgrades": [str, ...],
          "instance_guid": str|None,    # which component on the canvas to update
          "component_guid": str|None,   # the permanent published ComponentGuid
          "inputs":  [PARAM, ...], "outputs": [PARAM, ...],
          "warnings": [str, ...],       # non-fatal, e.g. an unrecognized key
        }

    where PARAM is
        {
          "variableName": str,   # the C# identifier / a script param's NickName
          "name": str,           # PrettyName -- the tooltip title
          "nickname": str,       # the NickName a COMPILED build draws
          "hint": str, "access": str, "description": str,
          "optional": bool,      # IGH_Param.Optional; defaults True
          "default": obj|None,   # declared default value, None when undeclared
        }

    `variableName` and `nickname` each default from `name` independently (a fan,
    not a chain), so a param carrying only `name` collapses all three.

    `instanceGuid` and `componentGuid` are separate keys on purpose -- Script
    Forge reads the first (which canvas instance to update), gh_codegen the second
    (the published identity). A header that pins an instance does not thereby join
    the ship list.
    """
    with open(path, "r", encoding="utf-8-sig") as fh:  # -sig: strip a BOM if present
        text = fh.read()

    lines = text.splitlines()
    start = None
    for i, line in enumerate(lines):
        if _strip_comment(line).startswith("@component"):
            start = i
            break
    if start is None:
        raise HeaderError(f"{path}: no @component header found")

    # The body must open with `{`. Saying so out loud is worth the four lines:
    # any other body reaches raw_decode and comes back as "Expecting value:
    # line 1 column 1", which tells a reader holding one nothing useful.
    first = _strip_comment(lines[start])[len("@component"):].lstrip()
    i = start
    while not first and i + 1 < len(lines):
        i += 1
        first = _strip_comment(lines[i])
    if not first.startswith("{"):
        raise HeaderError(
            f"{path}: @component body must be a JSON object opening with '{{' -- "
            f"the `key: value` / @in / @out line grammar is retired")
    return _parse_json_header(lines, start, path)


# --------------------------------------------------------------- JSON body --
# One JSON object, opened by `{` and closed by its own matching brace (so there
# is no terminator keyword). Every field is a named key rather than a positional
# one, which is what makes `optional` and `default` expressible at all.
# Mirrored in Script Forge's ParseHeader (System.Text.Json).

ACCESS_MODES = ("item", "list", "tree")

# Component keys that must be strings when present; `name` and `description` are
# additionally required.
_JSON_STR_KEYS = ("name", "nickname", "description", "category", "subcategory",
                  "icon", "language", "exposure", "instanceGuid", "componentGuid")
_JSON_LIST_KEYS = ("markers", "upgradeFrom")

# Every key the grammar knows, at the component level and inside a param object.
# A key outside these sets is still ignored -- that is the forward-compatibility
# promise and it does not change -- but being ignored SILENTLY is how a typo'd
# `"nickanme"` costs an hour, so _fold_keys reports one as a non-fatal warning.
# `guid` counts as known here only so it is not reported twice: it has its own
# hard rejection below, with a message that says what to write instead.
# SYNC: Script Forge's ComponentKeys / ParamKeys.
_JSON_COMPONENT_KEYS = frozenset(
    k.lower() for k in
    _JSON_STR_KEYS + _JSON_LIST_KEYS + ("inputs", "outputs", "guid"))
_JSON_PARAM_KEYS = frozenset(
    k.lower() for k in ("name", "variableName", "nickname", "type", "access",
                        "description", "optional", "default"))


class _Folded(dict):
    """A JSON object whose keys are matched case-insensitively.

    Every key is stored lowered and every lookup lowers first, so a header may
    spell `variableName`, `VariableName` or `variablename` and reach the same
    slot. The documented spelling stays canonical -- this only decides what
    *matches* it.
    """

    def __contains__(self, key):
        return dict.__contains__(self, key.lower())

    def __getitem__(self, key):
        return dict.__getitem__(self, key.lower())

    def get(self, key, default=None):
        return dict.get(self, key.lower(), default)


def _fold_keys(obj, where, known, warnings):
    """`obj` as a `_Folded`, warning about keys outside `known`.

    Two keys of one object that differ only in case are a mistake JSON permits
    -- `"name"` and `"Name"` together says nothing about which was meant -- so
    they raise rather than one of them silently winning.
    SYNC: Script Forge's JsonKeys.
    """
    folded, spelling = _Folded(), {}
    for key, value in obj.items():
        low = key.lower()
        if low in spelling:
            raise HeaderError(
                f"{where}: keys {spelling[low]!r} and {key!r} differ only in "
                f"case -- one object cannot carry both")
        spelling[low] = key
        dict.__setitem__(folded, low, value)
        if low not in known:
            warnings.append(f"{where}: unknown key {key!r} -- ignored")
    return folded


def _parse_json_header(lines, start, path):
    """Parse a JSON header body from `lines[start]` (the `@component` line) on.

    The comment prefixes come off line by line, which is safe because a JSON
    string cannot span a line break -- so no stripped prefix is ever part of a
    value. `raw_decode` is then what makes the closing brace the terminator: the
    stdlib finds the matching brace itself and reports where the value ended, so
    the trailing `*/` (or `\"\"\"`, or the whole rest of the file) needs no
    handling here and no brace counting has to be re-implemented -- a hand-rolled
    scan would have to know not to count braces inside strings.
    """
    body = "\n".join([_strip_comment(lines[start])[len("@component"):]]
                     + [_strip_comment(line) for line in lines[start + 1:]])
    try:
        obj, _ = json.JSONDecoder().raw_decode(body, body.index("{"))
    except ValueError as e:
        raise HeaderError(f"{path}: @component header is not valid JSON: {e}")
    if not isinstance(obj, dict):
        raise HeaderError(f"{path}: @component header must be a JSON object")

    warnings = []
    obj = _fold_keys(obj, path, _JSON_COMPONENT_KEYS, warnings)

    for key in _JSON_STR_KEYS:
        if key in obj and not isinstance(obj[key], str):
            raise HeaderError(f"{path}: header {key!r} must be a string")
    for key in _JSON_LIST_KEYS:
        if key in obj and not (isinstance(obj[key], list)
                               and all(isinstance(v, str) for v in obj[key])):
            raise HeaderError(f"{path}: header {key!r} must be an array of strings")
    for key in ("name", "description"):
        if key not in obj:
            raise HeaderError(f"{path}: header missing required {key!r}")

    # `guid` was the line grammar's one key for two unrelated properties: the
    # canvas InstanceGuid to Script Forge, the published ComponentGuid to
    # gh_codegen. Unknown keys are otherwise ignored, and being ignored is
    # exactly what must not happen here -- a header still saying `guid` would
    # silently stop pinning its target, or silently leave the ship list.
    # SYNC: Script Forge's ParseJsonHeader raises the same refusal.
    if "guid" in obj:
        raise HeaderError(
            f"{path}: header 'guid' is retired -- say 'instanceGuid' (which "
            f"component on the canvas to update) or 'componentGuid' (the "
            f"permanent published identity), or both")

    return {
        "name": obj["name"],
        "nickname": obj.get("nickname", obj["name"]),
        "category": obj.get("category"),
        "subcategory": obj.get("subcategory"),
        "icon": obj.get("icon"),
        "description": obj["description"],
        "instance_guid": obj.get("instanceGuid"),
        "component_guid": obj.get("componentGuid"),
        "exposure": obj.get("exposure"),
        "language": obj.get("language"),
        "markers": list(obj.get("markers", [])),
        "upgrades": list(obj.get("upgradeFrom", [])),
        "inputs": _json_params(obj, "inputs", path, warnings),
        "outputs": _json_params(obj, "outputs", path, warnings),
        # Non-fatal: an unknown key is still ignored, and `--check` still
        # passes. main() prints these; check_meta leaves them alone.
        "warnings": warnings,
    }


def _json_params(obj, key, path, warnings):
    raw = obj.get(key, [])
    if not isinstance(raw, list):
        raise HeaderError(f"{path}: header {key!r} must be an array of param objects")

    params = []
    for i, p in enumerate(raw):
        where = f"{path}: {key}[{i}]"
        if not isinstance(p, dict):
            raise HeaderError(f"{where} is not an object")
        p = _fold_keys(p, where, _JSON_PARAM_KEYS, warnings)

        for k in ("name", "variableName", "nickname", "type", "description"):
            if k in p and not isinstance(p[k], str):
                raise HeaderError(f"{where}: {k!r} must be a string")
        for k in ("name", "type", "access"):
            if k not in p:
                raise HeaderError(f"{where} missing required {k!r}")
        name = p["name"]
        # `access` is matched case-insensitively and stored canonical, so every
        # consumer of this dict sees "item"/"list"/"tree" and nothing else.
        access = p["access"]
        if isinstance(access, str):
            access = access.lower()
        if access not in ACCESS_MODES:
            raise HeaderError(
                f"{path}: bad access {p['access']!r} for param {name!r} "
                f"-- expected one of {'/'.join(ACCESS_MODES)}")
        if "optional" in p and not isinstance(p["optional"], bool):
            raise HeaderError(f"{where}: 'optional' must be true or false")

        # The fan: each of the two identifiers defaults from `name` on its own.
        # Chaining them would let a short compiled NickName become the C#
        # identifier, which is the one thing a param must not have decided for it.
        params.append({
            "variableName": p.get("variableName", name),
            "name": name,
            "nickname": p.get("nickname", name),
            "hint": p["type"],
            "access": access,
            "description": p.get("description", ""),
            "optional": p.get("optional", True),
            # None means "no declared default". A literal `"default": null` is
            # not a usable default for any supported type, so the two collapsing
            # costs nothing.
            "default": p.get("default"),
        })
    return params


# A root-level source can opt out of being treated as a component by carrying
# this token in a comment. `--all` is a blunt "every .cs and .py in the project
# root", and a project root legitimately holds sources that are not components —
# a Rhino command script run through _RunPythonScript, a build helper, a
# scratch file. Without an opt-out those show up as "no @component header
# found", which is indistinguishable from a real component whose header is
# missing, so the check is either noisy or has to be ignored wholesale.
#
# The marker lives in the file rather than in a project-side ignore list so it
# travels with the file and states its own intent:
#
#     # gh-meta: ignore — Rhino command script, not a Grasshopper component
IGNORE_TOKEN = "gh-meta: ignore"


def is_ignored(path):
    """True if the source opts out of component-header treatment.

    Only the head of the file is examined, so the token has to be near the top
    (where a reader will see it) and a passing mention further down — in a
    docstring discussing this convention, say — does not silently exclude a
    real component.
    """
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            return IGNORE_TOKEN in fh.read(2048)
    except OSError:
        return False


def all_sources(root):
    """Every component source directly in `root` -- what `--all` walks.

    Flat (no recursion), `.cs`/`.py` only, minus anything carrying the
    `gh-meta: ignore` token. Public because `check_filenames.py` needs the same
    scope `--check` uses; if the two ever drift the filename check stops
    covering exactly the files the header check does.
    """
    return sorted(
        os.path.join(root, f)
        for f in os.listdir(root)
        if (f.endswith(".cs") or f.endswith(".py"))
        and not is_ignored(os.path.join(root, f))
    )


def root_arg(args):
    """Pull `--root <dir>` out of `args` (mutated in place), defaulting to CWD.

    Shared by the publish tooling so `--root` means the same thing everywhere.
    Exits on a missing or non-directory argument.
    """
    root = os.getcwd()
    if "--root" in args:
        i = args.index("--root")
        if i + 1 >= len(args):
            sys.exit("error: --root needs a directory argument")
        root = args[i + 1]
        del args[i:i + 2]
    if not os.path.isdir(root):
        sys.exit(f"error: not a directory: {root}")
    return root


# ------------------------------------------------------------ check helpers --

_GUID_RE = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")

_IDENT_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")

# C#'s reserved keywords. Contextual keywords (`var`, `value`, `async`, `record`,
# …) are legal identifiers and are deliberately absent.
_CS_KEYWORDS = frozenset("""
abstract as base bool break byte case catch char checked class const continue
decimal default delegate do double else enum event explicit extern false finally
fixed float for foreach goto if implicit in int interface internal is lock long
namespace new null object operator out override params private protected public
readonly ref return sbyte sealed short sizeof stackalloc static string struct
switch this throw true try typeof uint ulong unchecked unsafe ushort using
virtual void volatile while
""".split())

# The type hints a `default:` may be declared on, mapped to how the required
# JSON value reads in an error message. The four are not an arbitrary subset --
# they are the intersection of three independent lists:
#
#   * the JSON scalar kinds, which are true/false, number and string. There is
#     no scalar that names a Point3d or a Plane.
#   * the GH_InputParamManager overloads that accept a default value. Checked
#     against the live 8.33 SDK by reflection, not assumed: Point/Vector/Plane/
#     Interval/Colour/Box/Time/Angle/Arc/Circle/Line/Rectangle DO have one, and
#     Curve/Brep/Mesh/Surface/Geometry/Transform/Generic do NOT.
#   * the goo types Script Forge can build to seed a live param's
#     PersistentData -- GH_Boolean / GH_Integer / GH_Number / GH_String.
#
# So the ceiling is JSON's, not Grasshopper's. Widening it means designing a
# composite spelling (`"default": [0, 0, 1]` for a vector, say) and teaching
# both parsers to read it -- deliberately out of scope.
# SYNC: Script Forge's Defaultable / DefaultProblem.
DEFAULTABLE = {
    "bool":   "true or false",
    "int":    "a whole number",
    "double": "a number",
    "string": "a string",
}

# A header `type` names a Grasshopper type-hint CONVERTER, and Grasshopper names
# two of them differently per language: the C# `double` converter is `float` on
# Python, and `string` is `str`. Script Forge's HintCandidates resolves either
# spelling onto the live param, so a Python header saying `float` works -- but
# the canonical spelling, the one DEFAULTABLE and gh_codegen's HINTS key on, is
# the C# name. Rather than widen every table with aliases (which would put the
# same converter under two names in three places), name the canonical spelling
# in the one message where the difference is felt.
# SYNC: Script Forge's Canonical.
_CANONICAL = {"float": "double", "str": "string"}


def default_problem(param, side):
    """Why this param's declared `default` is unusable, or None when it is fine.

    `side` is "input" or "output". Returns the tail of a problem message so the
    caller can prefix it with the file and param name.

    SYNC: Script Forge's DefaultProblem returns the same verdicts in the same
    order -- --check fails where Forge warns.
    """
    value = param["default"]
    if value is None:
        return None

    # Grasshopper only ever collects INTO an input. An output's persistent data
    # is overwritten by the first solve, and RegisterOutputParams has no
    # value-taking overload to emit, so this would mean two different nothings
    # on the two surfaces.
    if side == "output":
        return "an output has no default -- nothing is ever collected into it"

    hint = param["hint"]
    if hint not in DEFAULTABLE:
        if hint in _CANONICAL:
            return ("default is only supported on %s -- %r is the same converter "
                    "under its Python name, so spell the type %r"
                    % ("/".join(DEFAULTABLE), hint, _CANONICAL[hint]))
        return ("default is only supported on %s, not %r"
                % ("/".join(DEFAULTABLE), hint))

    # bool before int: in Python `isinstance(True, int)` is True, so an
    # unguarded int test would accept `"default": true` on an int param.
    if isinstance(value, bool):
        ok = hint == "bool"
    elif isinstance(value, int):
        ok = hint in ("int", "double")
    elif isinstance(value, float):
        # 2.0 on an int param is a typo worth reporting, not a silent truncation.
        ok = hint == "double"
    else:
        ok = hint == "string" and isinstance(value, str)
    if not ok:
        return "default %r is not %s (%s param)" % (value, DEFAULTABLE[hint], hint)
    return None


def parse_runscript_params(text):
    """Extract the full RunScript parameter list from C# source, in declaration
    order, or None if no RunScript is found.

    Returns a list of dicts: {"name", "type", "dir"} where `dir` is "in" for a
    by-value parameter and "out" for an `out`/`ref` one, and `type` is the C#
    type as written (e.g. "List<object>", "DataTree<string>").

    This is the one place the RunScript signature is parsed. gh_codegen.py
    derives the generated `Invoke` glue from it — locals must be declared with
    the *exact* declared type, and the call emitted in *this* order, which is
    not necessarily the header's order.
    """
    m = re.search(r"void\s+RunScript\s*\(", text)
    if m is None:
        return None
    i, depth, start = m.end(), 1, m.end()
    while i < len(text) and depth:
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
        i += 1
    if depth:
        return None
    body = text[start:i - 1]

    parts, d, last = [], 0, 0
    for k, c in enumerate(body):
        if c in "<([":
            d += 1
        elif c in ">)]":
            d -= 1
        elif c == "," and d == 0:
            parts.append(body[last:k])
            last = k + 1
    parts.append(body[last:])

    params = []
    for raw in parts:
        t = " ".join(raw.split())
        if not t:
            continue
        is_out = t.startswith(("out ", "ref "))
        if is_out:
            t = t[4:].strip()
        mm = re.search(r"([A-Za-z_][A-Za-z0-9_]*)\s*$", t)
        if mm is None or mm.start() == 0:  # need both a type and a name
            continue
        params.append({
            "name": mm.group(1),
            "type": t[:mm.start()].strip(),
            "dir": "out" if is_out else "in",
        })
    return params


def parse_runscript_signature(text):
    """Extract ([input names], [output names]) from a C# RunScript declaration,
    or None if no RunScript is found. out/ref params are outputs."""
    params = parse_runscript_params(text)
    if params is None:
        return None
    return ([p["name"] for p in params if p["dir"] == "in"],
            [p["name"] for p in params if p["dir"] == "out"])


def check_meta(path, meta):
    """Extra --check validations. Returns a list of problem strings."""
    problems = []
    base = os.path.basename(path)

    # Double quotes in descriptions break the ScriptEditor plugin builder
    # (they land in an unescaped C# string literal at publish time).
    if '"' in meta["description"]:
        problems.append(f"{base}: component description contains a double quote")
    for p in meta["inputs"] + meta["outputs"]:
        if '"' in p["description"]:
            problems.append(
                f"{base}: param {p['variableName']!r} description contains a double quote")
        # Both label slots reach a generated C# literal the same way a
        # description does -- gh_codegen's cs_string() relies on this ban --
        # `name` as the compiled component's Name, `nickname` as its NickName.
        # SYNC: Script Forge's WarnDriftAndQuotes mirrors this pair of checks.
        for field in ("name", "nickname"):
            if '"' in p[field]:
                problems.append(
                    f"{base}: param {p['variableName']!r} {field} contains a double quote")

    # Two params on the same side sharing a LABEL compile to a component with two
    # identically-labelled params, and Name is additionally what DA.GetData(name)
    # resolves against. Across sides it is the whole point (an input and an
    # output both shown as `Keys`), so check each side alone. The two slots are
    # reported together when they hold the same string -- which is the common
    # case, a param carrying only `name`.
    for side in ("inputs", "outputs"):
        for i, a in enumerate(meta[side]):
            for b in meta[side][i + 1:]:
                if a["variableName"] == b["variableName"]:
                    continue
                shared = []
                if a["name"] == b["name"]:
                    shared.append(f"name {b['name']!r}")
                if a["nickname"] == b["nickname"]:
                    shared.append(f"nickname {b['nickname']!r}")
                if len(shared) == 2 and b["name"] == b["nickname"]:
                    shared = [f"name and nickname {b['name']!r}"]
                if shared:
                    problems.append(
                        f"{base}: {side[:-1]}s {a['variableName']!r} and "
                        f"{b['variableName']!r} share the {' and the '.join(shared)}")

    # `variableName` becomes a live script param's NickName -- Rhino accepts
    # anything at all in that slot, `VariableName = "Not An Identifier"` is
    # stored without complaint (see docs/write-scripts/identity-properties.md) --
    # so this is the only guard there is. It must always be unique within its
    # own side, both languages: two same-named params on one side can't be told
    # apart, and Grasshopper silently keeps only one of them when it rebuilds
    # the live signature (verified against a canvas 2026-08-26).
    #
    # For C# it must ALSO be unique ACROSS sides: an input and an output become
    # two locals in the same generated Invoke, and live, two params in the same
    # RunScript parameter list -- both are a hard compile error on a clash.
    # Python has neither reason: its RunScript has no fixed output parameter
    # list to collide in (outputs are read back from the exec namespace by
    # name, not declared), and a compiled build never processes a `.py` source
    # at all (docs/ship-a-plugin/dotnet-build.md, "Scope: C# only"). So a Python input and
    # output sharing a label -- an input and output both called `Keys`, say --
    # is fine and common; verified working end to end on a live canvas
    # 2026-08-26 (examples/ring-array-shared-labels.py).
    is_cs = path.endswith(".cs")
    cross_seen = {}
    for side in ("inputs", "outputs"):
        label = side[:-1]
        side_seen = {}
        for p in meta[side]:
            v = p["variableName"]
            if not _IDENT_RE.match(v):
                problems.append(
                    f"{base}: {label} variable name {v!r} is not an identifier")
            elif is_cs and v in _CS_KEYWORDS:
                problems.append(
                    f"{base}: {label} variable name {v!r} is a C# keyword")
            if v in side_seen:
                problems.append(
                    f"{base}: duplicate {label} variable name {v!r} -- "
                    f"Grasshopper can't tell the two params apart")
            else:
                side_seen[v] = label
            if is_cs:
                if v in cross_seen:
                    problems.append(
                        f"{base}: duplicate variable name {v!r} ({cross_seen[v]} and "
                        f"{label}) -- both become locals in one generated Invoke")
                else:
                    cross_seen[v] = label

            # `optional` and `default`, which only a JSON header can spell. Both
            # are input-side ideas; see default_problem for why an output can
            # carry neither. Kept in this loop rather than a pass of its own so
            # every per-param rule reports together and the structure matches
            # its twin. SYNC: Script Forge's WarnDriftAndQuotes.
            problem = default_problem(p, label)
            if problem:
                problems.append(f"{base}: {label} {v!r}: {problem}")
            if label == "output" and not p["optional"]:
                problems.append(
                    f"{base}: output {v!r}: 'optional' is an input-side idea -- "
                    f"RegisterOutputParams has no Optional pass to mirror it")

    # `upgrade-from:` becomes `new Guid("...")` in a generated IGH_UpgradeObject,
    # so a malformed value is a compile error two steps later.
    own = (meta.get("component_guid") or "").strip().lower()
    for value in meta.get("upgrades", []):
        old = value.strip().lower()
        if not _GUID_RE.match(old):
            problems.append(f"{base}: upgrade-from {value!r} is not a guid")
        elif old == own:
            problems.append(
                f"{base}: upgrade-from names this component's own guid")

    # Header/signature drift (C# only; the hints silently win at runtime,
    # so a mismatch is invisible until something computes wrong).
    if path.endswith(".cs"):
        with open(path, "r", encoding="utf-8") as fh:
            sig = parse_runscript_signature(fh.read())
        if sig is not None:
            sig_ins, sig_outs = sig
            # The signature declares VARIABLE names -- the C# identifiers.
            hdr_ins = [p["variableName"] for p in meta["inputs"]]
            hdr_outs = [p["variableName"] for p in meta["outputs"]]
            for name in hdr_ins:
                if name not in sig_ins:
                    problems.append(
                        f"{base}: DRIFT header input {name!r} not in RunScript signature")
            for name in sig_ins:
                if name not in hdr_ins:
                    problems.append(
                        f"{base}: DRIFT RunScript input {name!r} not in header")
            for name in hdr_outs:
                if name not in sig_outs:
                    problems.append(
                        f"{base}: DRIFT header output {name!r} not in RunScript signature")
            for name in sig_outs:
                if name not in hdr_outs:
                    problems.append(
                        f"{base}: DRIFT RunScript output {name!r} not in header")
    return problems


def main(argv):
    args = argv[1:]

    if any(a in ("-h", "--help") for a in args):
        print(__doc__)
        return 0

    check = "--check" in args
    args = [a for a in args if a != "--check"]

    # --root <dir> (defaults to CWD); only meaningful with --all.
    root = root_arg(args)

    do_all = "--all" in args
    args = [a for a in args if a != "--all"]

    # Any leftover flag is unrecognized — don't treat it as a file path.
    bad = [a for a in args if a.startswith("-")]
    if bad:
        print(f"error: unrecognized argument(s): {' '.join(bad)}\n", file=sys.stderr)
        print(__doc__, file=sys.stderr)
        return 2

    if do_all:
        if args:
            print(f"error: --all takes no file arguments (got {args})\n", file=sys.stderr)
            print(__doc__, file=sys.stderr)
            return 2
        targets = all_sources(root)
    elif len(args) == 1:
        if not os.path.isfile(args[0]):
            print(f"error: file not found: {args[0]}", file=sys.stderr)
            return 2
        targets = [args[0]]
    else:
        print(__doc__, file=sys.stderr)
        return 2

    results = {}
    paths = {}
    errors = []
    for path in targets:
        try:
            results[os.path.basename(path)] = parse_header(path)
            paths[os.path.basename(path)] = path
        except HeaderError as e:
            errors.append(str(e))

    if check:
        # Header warnings are deliberately outside `errors`: an unrecognized key
        # is ignored by design, so saying so must not fail the gate a compiled
        # build runs through.
        warnings = []
        for name, meta in results.items():
            problems = check_meta(paths[name], meta)
            errors.extend(problems)
            warnings.extend(meta["warnings"])
            n_in, n_out = len(meta["inputs"]), len(meta["outputs"])
            status = "BAD " if problems else ("WARN" if meta["warnings"] else "OK  ")
            extra = (f" upgrades={meta['upgrades']}" if meta["upgrades"] else "")
            print(f"{status} {name:32s} in={n_in} out={n_out} "
                  f"markers={meta['markers'] or '-'}{extra}")
        for w in warnings:
            print(f"WARN {w}")
        for e in errors:
            print(f"FAIL {e}")
        return 1 if errors else 0

    # Bare path: stdout is machine-readable JSON a caller may be piping, so the
    # failures go to stderr rather than being swallowed. Without this a file
    # that will not parse produced a bare `{}` and exit 1 -- indistinguishable,
    # to anyone not reading $?, from "parsed fine, nothing to report".
    for meta in results.values():
        for w in meta["warnings"]:
            print(f"WARN {w}", file=sys.stderr)
    for e in errors:
        print(f"FAIL {e}", file=sys.stderr)
    if errors and not results:
        print("error: nothing parsed; no metadata to report", file=sys.stderr)

    print(json.dumps(results, indent=2, ensure_ascii=False))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
