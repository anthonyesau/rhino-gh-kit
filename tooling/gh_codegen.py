#!/usr/bin/env python3
"""Generate compilable Grasshopper components from canonical inline-script `.cs`.

The kit's authoring model is one canonical `.cs` per component at the project
root: a `@component` header plus a `Script_Instance : GH_ScriptInstance` class,
pushable straight onto a canvas by Script Forge. This module turns that same
unmodified file into a real `GH_Component` subclass that `dotnet` can compile
into a `.gha`, so the canvas stays the iteration surface while releases become
an ordinary compiled plugin.

For each shipped component it emits two files into the output directory:

    Script_<Slug>.g.cs   the canonical source, verbatim, with exactly ONE line
                         rewritten -- the class declaration:
                             public class Script_Instance : GH_ScriptInstance
                         becomes
                             internal sealed partial class Script_<Slug>
                                 : global::GHScriptKit.ScriptBase

    Comp_<Slug>.g.cs     the GH_Component host (identity, ComponentGuid,
                         exposure, icon, markers, param registration) plus a
                         second `partial class Script_<Slug>` carrying the
                         `Invoke(IGH_DataAccess)` glue -- which is how it can
                         call the script's `private void RunScript`.

Everything the host needs comes from the `@component` header and the RunScript
signature, both parsed by gh_meta.py. Nothing is hand-written per component, and
nothing in the output directory should ever be edited: it is regenerated from
scratch on every run.

Two mechanisms let a component reach past what a canvas script component can do
without ever opening a second file for it:

  hook methods    declare a known `On*` method (see HOOKS) and the host emits the
                  matching Grasshopper override, forwarding to it. Inert on the
                  canvas, where nothing calls it.
  upgradeFrom:    one entry per retired ComponentGuid emits a free-standing
                  IGH_UpgradeObject, which Grasshopper discovers by itself.

**Ship list:** every root `.cs` whose header pins a `componentGuid`. That is an
explicit opt-in and it forces the ComponentGuid into source.

Usage:
    python3 tooling/gh_codegen.py                       # scan CWD -> build/gen
    python3 tooling/gh_codegen.py --root <dir>          # scan <dir> instead
    python3 tooling/gh_codegen.py --out <dir>           # output elsewhere
    python3 tooling/gh_codegen.py --resource-prefix MyPlugin.Icons   # required to generate
    python3 tooling/gh_codegen.py --list                # report, generate nothing
    python3 tooling/gh_codegen.py --no-icons            # skip rasterization
"""

import os
import re
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gh_meta  # noqa: E402  -- the one header/signature parser; never re-implement


TEMPLATES = ("ScriptBase.cs", "ScriptComponentBase.cs", "ScriptData.cs")

# The canonical class declaration, and what it becomes. This is the ONLY textual
# transform applied to a canonical source. If a case ever appears that this can't
# express, extend the header grammar -- do not open a second file for a component.
CLASS_DECL = re.compile(
    r"^[ \t]*public\s+class\s+Script_Instance\s*:\s*GH_ScriptInstance[ \t]*$",
    re.MULTILINE,
)

# `exposure:` in the header is spelled levelN; Grasshopper's own names follow.
EXPOSURE = {
    "level1": "primary",
    "level2": "secondary",
    "level3": "tertiary",
    "level4": "quarternary",   # sic -- Grasshopper spells it this way
    "level5": "quinary",
    "level6": "senary",
    "level7": "septenary",
    "hidden": "hidden",
    "obscure": "obscure",
}

# Type-hint vocabulary, per docs/write-scripts/csharp-type-hints.md. `add` is the
# GH_*ParamManager method; `param` is used instead where the manager has no
# dedicated Add (Guid), via AddParameter(IGH_Param, ...). `clr` is the type a
# RunScript parameter is declared with at item access; `goo` is what rides the
# wire, needed for tree access and for the object unwrap; `zero` is the C#
# expression a local of that type is initialized to before DA fills it.
#
# `zero` is NOT a header `default:`. It is the value the generated local starts
# at so `DA.GetData(i, ref x)` has something to overwrite, and it is per TYPE. A
# header default is per PARAM, is user-supplied, and is emitted into the
# Add*Parameter call instead -- see _DEFAULT_LITERALS.
HINTS = {
    "bool":         dict(add="AddBooleanParameter",   clr="bool",                  goo="GH_Boolean", zero="false"),
    "int":          dict(add="AddIntegerParameter",   clr="int",                   goo="GH_Integer", zero="0"),
    "double":       dict(add="AddNumberParameter",    clr="double",                goo="GH_Number",  zero="0.0"),
    "string":       dict(add="AddTextParameter",      clr="string",                goo="GH_String",  zero="null"),
    "DateTime":     dict(add="AddTimeParameter",      clr="System.DateTime",       goo="GH_Time",    zero="default(System.DateTime)"),
    "Color":        dict(add="AddColourParameter",    clr="System.Drawing.Color",  goo="GH_Colour",  zero="default(System.Drawing.Color)"),
    "Point3d":      dict(add="AddPointParameter",     clr="Rhino.Geometry.Point3d",   goo="GH_Point",     zero="Rhino.Geometry.Point3d.Origin"),
    "Vector3d":     dict(add="AddVectorParameter",    clr="Rhino.Geometry.Vector3d",  goo="GH_Vector",    zero="Rhino.Geometry.Vector3d.Zero"),
    "Plane":        dict(add="AddPlaneParameter",     clr="Rhino.Geometry.Plane",     goo="GH_Plane",     zero="Rhino.Geometry.Plane.WorldXY"),
    "Interval":     dict(add="AddIntervalParameter",  clr="Rhino.Geometry.Interval",  goo="GH_Interval",  zero="default(Rhino.Geometry.Interval)"),
    "Transform":    dict(add="AddTransformParameter", clr="Rhino.Geometry.Transform", goo="GH_Transform", zero="Rhino.Geometry.Transform.Identity"),
    "Curve":        dict(add="AddCurveParameter",     clr="Rhino.Geometry.Curve",     goo="GH_Curve",     zero="null"),
    "Brep":         dict(add="AddBrepParameter",      clr="Rhino.Geometry.Brep",      goo="GH_Brep",      zero="null"),
    "Mesh":         dict(add="AddMeshParameter",      clr="Rhino.Geometry.Mesh",      goo="GH_Mesh",      zero="null"),
    "Surface":      dict(add="AddSurfaceParameter",   clr="Rhino.Geometry.Surface",   goo="GH_Surface",   zero="null"),
    "Box":          dict(add="AddBoxParameter",       clr="Rhino.Geometry.Box",       goo="GH_Box",       zero="Rhino.Geometry.Box.Unset"),
    "GeometryBase": dict(add="AddGeometryParameter",  clr="Rhino.Geometry.GeometryBase", goo="IGH_GeometricGoo", zero="null"),
    "Guid":         dict(param="Grasshopper.Kernel.Parameters.Param_Guid",
                         clr="System.Guid", goo="GH_Guid", zero="System.Guid.Empty"),
    # "No Type Hint". Verified on Rhino 8.33: the canvas UNWRAPS the goo
    # (ScriptVariable semantics), so the adapter must too -- see ScriptData.cs.
    "object":       dict(add="AddGenericParameter",   clr="object", goo="IGH_Goo", zero="null", unwrap=True),
}

ACCESS = {"item": "GH_ParamAccess.item",
          "list": "GH_ParamAccess.list",
          "tree": "GH_ParamAccess.tree"}

# gh_meta validates a param's `access` against these same tokens, so the two
# lists drifting apart would let a header validate and then fail to generate.
assert set(ACCESS) == set(gh_meta.ACCESS_MODES), "ACCESS drifted from gh_meta.ACCESS_MODES"


# --------------------------------------------------------------- hooks -------
# A compiled component can do things a canvas script component cannot -- append
# menu items, persist per-instance settings, own custom attributes. Those live on
# the *host* (`Comp_<Slug>`), not on the script class, so a source file cannot
# override them directly. Instead the host FORWARDS to optional well-known
# methods on the script class, emitted only when the source declares one.
#
# Convention, not configuration: declaring the method IS the opt-in. On canvas
# nothing calls it, so the same file stays valid there -- which is the whole
# point, and what keeps the one-file rule intact.
#
# This is how McNeel's own ScriptEditor-generated `ProjectComponent_Base` works;
# it simply forwards a duller set.
#
#   decl  the override emitted on the host
#   body  its statements; SCRIPT is replaced by the cast to the script class
#
# The required hook signature is `internal` on the script class, and is the
# `decl` line's parameters with the return type shown below. A mismatch is a
# compile error naming the file, which is a fine way to find out.
HOOKS = [
    dict(hook="OnAppendMenuItems",
         sig="internal void OnAppendMenuItems(System.Windows.Forms.ToolStripDropDown menu)",
         decl="protected override void AppendAdditionalComponentMenuItems"
              "(System.Windows.Forms.ToolStripDropDown menu)",
         body=["base.AppendAdditionalComponentMenuItems(menu);",
               "SCRIPT.OnAppendMenuItems(menu);"]),

    dict(hook="OnWrite",
         sig="internal void OnWrite(GH_IO.Serialization.GH_IWriter writer)",
         decl="public override bool Write(GH_IO.Serialization.GH_IWriter writer)",
         body=["if (!base.Write(writer)) return false;",
               "SCRIPT.OnWrite(writer);",
               "return true;"]),

    dict(hook="OnRead",
         sig="internal void OnRead(GH_IO.Serialization.GH_IReader reader)",
         decl="public override bool Read(GH_IO.Serialization.GH_IReader reader)",
         body=["if (!base.Read(reader)) return false;",
               "SCRIPT.OnRead(reader);",
               "return true;"]),

    # Returning null means `use the default attributes`, so a hook can decide
    # per instance. A nested helper type declared inside the script class
    # compiles in both worlds, which is what makes this single-file.
    dict(hook="OnCreateAttributes",
         sig="internal IGH_Attributes OnCreateAttributes(IGH_Component owner)",
         decl="public override void CreateAttributes()",
         body=["IGH_Attributes custom = SCRIPT.OnCreateAttributes(this);",
               "if (custom != null) Attributes = custom; else base.CreateAttributes();"]),

    # Returns true to also run Grasshopper's default downstream expiry.
    dict(hook="OnExpireDownStreamObjects",
         sig="internal bool OnExpireDownStreamObjects()",
         decl="protected override void ExpireDownStreamObjects()",
         body=["if (SCRIPT.OnExpireDownStreamObjects()) base.ExpireDownStreamObjects();"]),
]

# Comments are stripped before hook detection, so prose mentioning a hook name
# (this file's own docs, a source's explanatory comment) cannot trigger one.
_LINE_COMMENT = re.compile(r"//[^\n]*")
_BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.DOTALL)


def declared_hooks(text):
    """Which HOOKS the source declares, in HOOKS order.

    Matches a method *declaration* -- optional modifiers, a return type, the
    hook name, an open paren -- not a call or a mention.
    """
    code = _BLOCK_COMMENT.sub(" ", text)
    code = _LINE_COMMENT.sub("", code)
    found = []
    for h in HOOKS:
        pattern = re.compile(
            r"^[ \t]*(?:(?:public|internal|protected|private|static|virtual|override|sealed|async|unsafe|new)\s+)*"
            r"[A-Za-z_][\w.<>,\[\]]*\s+" + h["hook"] + r"\s*\(",
            re.MULTILINE)
        if pattern.search(code):
            found.append(h)
    return found


class CodegenError(Exception):
    pass


# ------------------------------------------------------------------- helpers --

def slug(name):
    """'GH Object to Guid' -> 'GHObjectToGuid'. Used for the generated type names."""
    parts = [p for p in re.split(r"[^A-Za-z0-9]+", name) if p]
    s = "".join(p[0].upper() + p[1:] for p in parts)
    if not s:
        raise CodegenError("component name yields an empty slug: %r" % name)
    return ("C" + s) if s[0].isdigit() else s


def cs_string(text):
    """A C# double-quoted literal, and the ONE escaper on this side.

    gh_meta hands out descriptions already decoded -- `json.loads` resolves the
    JSON escapes, including the `\\n` token -- so a description
    reaching here is ordinary text carrying real newlines, and escaping it back
    into a literal is all that is left to do. Keep it the only escaper: split the
    decoding across two, and every stamping path has to remember to call the
    right one.

    gh_meta --check already bans `"` in descriptions and labels (it breaks the
    ScriptEditor builder), but escaping anyway keeps the generator correct
    rather than merely lucky."""
    if text is None:
        return "null"
    out = text.replace("\\", "\\\\").replace('"', '\\"')
    out = out.replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t")
    return '"%s"' % out


# A declared `default` as a C# literal of the hint's own type, one emitter per
# hint that may carry one. Reached only after --check has agreed the JSON kind
# matches the hint, so these convert; they do not validate.
#
# `double` goes through float() first because C# resolves Add*Parameter's
# overloads on the LITERAL's type: a JSON `0` emitted bare would bind the int
# overload on a double param. repr() round-trips exactly and always leaves a `.`
# or an exponent behind, so the result is a C# double either way.
_DEFAULT_LITERALS = {
    "bool":   lambda v: "true" if v else "false",
    "int":    lambda v: "%d" % v,
    "double": lambda v: repr(float(v)),
    "string": cs_string,
}

# gh_meta decides which hints MAY carry a default; this module must be able to
# emit one for each, and each must be a hint it otherwise knows. Asserting
# against the emitters rather than against HINTS is the point -- HINTS holds
# plenty of hints (Point3d, Interval, Colour) that have a GH value overload and
# still have no JSON spelling, so a subset check against it would never fire.
assert set(gh_meta.DEFAULTABLE) == set(_DEFAULT_LITERALS) <= set(HINTS), (
    "the hints that may carry a default must be exactly those with a literal "
    "emitter here, and every one of them must be a known hint")


def hint_of(param, where):
    h = param["hint"]
    if h not in HINTS:
        raise CodegenError(
            "%s: unknown type hint %r on param %r. Add it to HINTS in gh_codegen.py "
            "(vocabulary: docs/write-scripts/csharp-type-hints.md)." % (where, h, param["variableName"]))
    return HINTS[h]


def clr_type(param, where):
    """The C# type a RunScript parameter of this hint+access is declared with."""
    info = hint_of(param, where)
    if param["access"] == "list":
        return "List<%s>" % info["clr"]
    if param["access"] == "tree":
        return "DataTree<%s>" % info["clr"]
    return info["clr"]


# Namespaces a source may reasonably have `using`-imported, so `Guid` and
# `System.Guid` (or `Point3d` and `Rhino.Geometry.Point3d`) are the same type to
# the drift check. Comparison is textual by necessity -- there is no compiler
# here -- so it has to know that much.
_NS = ("System.", "Rhino.Geometry.", "System.Drawing.", "System.Collections.Generic.")


def normalize_type(text):
    """Strip namespace qualifiers and whitespace so two spellings compare equal."""
    t = " ".join(text.split()).replace(", ", ",")
    for ns in _NS:
        t = t.replace(ns, "")
    return t


# ------------------------------------------------------------------ emitters --

def emit_registration(params, manager, where, allow_defaults):
    """RegisterInputParams / RegisterOutputParams bodies.

    The two GH identity slots are the header's two label slots: `name` becomes
    Name (the tooltip title, and what DA.GetData(name) resolves against),
    `nickname` becomes NickName (what Grasshopper draws on the param). Neither is
    the variable name, which exists only as a generated C# local and reaches no
    user-facing surface. With none of the three spelled out separately in the
    header all three are the same string, which is the long-standing behaviour
    unchanged.

    `allow_defaults` is False on the output side, where a declared default is a
    hard error rather than a silent drop. GH_OutputParamManager has no
    value-taking overload of any Add*Parameter, so it could not be emitted even
    if it meant something -- gh_meta --check rejects it first, and raising here
    keeps the generator honest rather than merely lucky.
    """
    lines = []
    for p in params:
        info = hint_of(p, where)
        args = "%s, %s,\n      %s,\n      %s" % (
            cs_string(p["name"]), cs_string(p["nickname"]),
            cs_string(p["description"]), ACCESS[p["access"]])
        if p["default"] is not None:
            if not allow_defaults:
                raise CodegenError(
                    "%s: param %r declares a default on the output side; "
                    "GH_OutputParamManager has no Add*Parameter overload that "
                    "takes one" % (where, p["variableName"]))
            args += ", %s" % _DEFAULT_LITERALS[p["hint"]](p["default"])
        if "param" in info:
            lines.append("    %s.AddParameter(new %s(), %s);" % (manager, info["param"], args))
        else:
            lines.append("    %s.%s(%s);" % (manager, info["add"], args))
    return lines


def emit_read(param, index, where):
    """Statements that declare a local for `param` and fill it from DA."""
    info = hint_of(param, where)
    # The local is the C# identifier the RunScript signature declares.
    name, access = param["variableName"], param["access"]
    out = []

    if access == "item":
        if info.get("unwrap"):
            # Mirror the canvas: a No-Type-Hint param delivers the unwrapped value.
            out.append("    object %s = null;" % name)
            out.append("    { IGH_Goo _g = null;")
            out.append("      if (DA.GetData(%d, ref _g)) %s = global::GHScriptKit.ScriptData.Unwrap(_g); }"
                       % (index, name))
        else:
            out.append("    %s %s = %s;" % (info["clr"], name, info["zero"]))
            out.append("    DA.GetData(%d, ref %s);" % (index, name))

    elif access == "list":
        if info.get("unwrap"):
            out.append("    List<object> %s = new List<object>();" % name)
            out.append("    { List<IGH_Goo> _gs = new List<IGH_Goo>();")
            out.append("      if (DA.GetDataList(%d, _gs))" % index)
            out.append("        foreach (IGH_Goo _g in _gs) %s.Add(global::GHScriptKit.ScriptData.Unwrap(_g)); }"
                       % name)
        else:
            out.append("    List<%s> %s = new List<%s>();" % (info["clr"], name, info["clr"]))
            out.append("    DA.GetDataList(%d, %s);" % (index, name))

    else:  # tree
        goo = info["goo"]
        out.append("    DataTree<%s> %s = new DataTree<%s>();" % (info["clr"], name, info["clr"]))
        out.append("    { GH_Structure<%s> _s;" % goo)
        out.append("      if (DA.GetDataTree(%d, out _s))" % index)
        if info.get("unwrap"):
            out.append("        %s = global::GHScriptKit.ScriptData.ToTree<%s, object>("
                       "_s, _g => global::GHScriptKit.ScriptData.Unwrap(_g)); }" % (name, goo))
        elif info["clr"] == "string":
            out.append("        %s = global::GHScriptKit.ScriptData.ToStringTree(_s); }" % name)
        else:
            out.append("        %s = global::GHScriptKit.ScriptData.ToTree<%s, %s>(_s, _g => _g.Value); }"
                       % (name, goo, info["clr"]))
    return out


def emit_write(param, index, where):
    access, name = param["access"], param["variableName"]
    if access == "item":
        return ["    DA.SetData(%d, %s);" % (index, name)]
    if access == "list":
        return ["    DA.SetDataList(%d, %s);" % (index, name)]
    return ["    DA.SetDataTree(%d, %s);" % (index, name)]


def build_comp(meta, sig, text, src_name, resource_prefix):
    """The Comp_<Slug>.g.cs text."""
    where = src_name
    s = slug(meta["name"])
    ins, outs = meta["inputs"], meta["outputs"]

    by_name = {p["variableName"]: p for p in ins + outs}

    L = []
    L.append("// GENERATED by gh_codegen.py from %s -- do not edit." % src_name)
    L.append("// Every value below is derived from that file's @component header and its")
    L.append("// RunScript signature. Regenerated from scratch on every build.")
    L.append("")
    L.append("using System;")
    L.append("using System.Collections.Generic;")
    L.append("")
    L.append("using Grasshopper;")
    L.append("using Grasshopper.Kernel;")
    L.append("using Grasshopper.Kernel.Data;")
    L.append("using Grasshopper.Kernel.Types;")
    L.append("")

    interfaces = ""
    if meta["markers"]:
        interfaces = ",\n%s global::GHScriptKit.IScriptMarkers" % (" " * (len("public class Comp_%s : " % s)))

    L.append("public class Comp_%s : global::GHScriptKit.ScriptComponentBase%s" % (s, interfaces))
    L.append("{")
    L.append("  public Comp_%s() : base(" % s)
    L.append("      new Script_%s()," % s)
    L.append("      %s," % cs_string(meta["name"]))
    L.append("      %s," % cs_string(meta["nickname"]))
    L.append("      %s," % cs_string(meta["description"]))
    L.append("      %s," % cs_string(meta["category"] or "Extra"))
    L.append("      %s)" % cs_string(meta["subcategory"] or ""))
    L.append("  { }")
    L.append("")
    L.append('  public override Guid ComponentGuid => new Guid("%s");' % meta["component_guid"])

    exposure = (meta.get("exposure") or "").strip().lower() if isinstance(meta.get("exposure"), str) else ""
    if exposure:
        if exposure not in EXPOSURE:
            raise CodegenError("%s: unknown exposure %r" % (where, exposure))
        L.append("")
        L.append("  public override GH_Exposure Exposure => GH_Exposure.%s;   // header: exposure %s"
                 % (EXPOSURE[exposure], exposure))

    icon = meta.get("icon")
    if icon:
        stem = os.path.splitext(os.path.basename(icon))[0]
        L.append("")
        L.append('  protected override string IconResourceName => "%s.%s.png";' % (resource_prefix, stem))
        L.append('  protected override string IconResourceNameDark => "%s.%s-dark.png";' % (resource_prefix, stem))

    if meta["markers"]:
        L.append("")
        L.append("  // header: %s" % ", ".join("marker: " + m for m in meta["markers"]))
        L.append("  // Compiled components have no script Text for a sibling to scan, so the")
        L.append("  // tags are advertised through the interface instead.")
        L.append("  public string[] Markers => new[] { %s };"
                 % ", ".join(cs_string(m) for m in meta["markers"]))

    L.append("")
    L.append("  protected override void RegisterInputParams(GH_InputParamManager pManager)")
    L.append("  {")
    L.extend(emit_registration(ins, "pManager", where, allow_defaults=True))
    if ins:
        L.append("")
        L.append("    // Script-component inputs are Optional by default (verified on Rhino 8.33),")
        L.append("    // so `optional` defaults true and matching that is the common case, not a")
        L.append("    // cosmetic one. `optional: false` opts a param out deliberately: an unwired")
        L.append("    // non-optional input stops SolveInstance from running at all, which is")
        L.append("    // exactly what it asks for -- but it also strands anything on THIS component")
        L.append("    // that detects 'unwired' by reading SourceCount, because nothing runs to read")
        L.append("    // it. A param wanting a value when unwired declares a `default` instead.")
        for i, p in enumerate(ins):
            L.append("    pManager[%d].Optional = %s;   // header: %s"
                     % (i, "true" if p["optional"] else "false", p["variableName"]))
    L.append("  }")
    L.append("")
    L.append("  protected override void RegisterOutputParams(GH_OutputParamManager pManager)")
    L.append("  {")
    L.extend(emit_registration(outs, "pManager", where, allow_defaults=False))
    L.append("  }")
    L.append("")
    L.append("  protected override void SolveInstance(IGH_DataAccess DA)")
    L.append("  {")
    L.append("    _script.Iteration = DA.Iteration;")
    L.append("    ((Script_%s) _script).Invoke(DA);" % s)
    L.append("  }")

    cast = "((Script_%s) _script)" % s
    for h in declared_hooks(text):
        L.append("")
        L.append("  // %s declares `%s`, so the host forwards to it." % (src_name, h["hook"]))
        L.append("  %s" % h["decl"])
        L.append("  {")
        for line in h["body"]:
            L.append("    %s" % line.replace("SCRIPT", cast))
        L.append("  }")

    L.append("}")
    L.append("")

    for i, old in enumerate(meta.get("upgrades", [])):
        L.append("// header: upgrade-from %s" % old)
        L.append("// A free-standing type -- Grasshopper's GH_ComponentServer.ParseGHA")
        L.append("// discovers every IGH_UpgradeObject in a .gha on its own. Emitted from")
        L.append("// the header, so it costs no hand-written file.")
        L.append("//")
        L.append("// Measured on Rhino 8.33: the swap is USER-INITIATED (Solution > Upgrade")
        L.append("// Components); nothing applies an upgrader at document load, and")
        L.append("// IGH_UpgradeObject.Upgrade takes a live object -- so the old component")
        L.append("// must still be registered (obsolete/hidden) for this to be reachable.")
        L.append("public class Upgrade_%s_%d : IGH_UpgradeObject" % (s, i + 1))
        L.append("{")
        L.append("  // Only breaks ties between upgraders sharing an UpgradeFrom; the")
        L.append("  // generator rejects duplicates within this assembly, so it is fixed.")
        L.append("  public DateTime Version => new DateTime(2000, 1, 1);")
        L.append('  public Guid UpgradeFrom => new Guid("%s");' % old.strip().lower())
        L.append('  public Guid UpgradeTo => new Guid("%s");' % meta["component_guid"])
        L.append("")
        L.append("  public IGH_DocumentObject Upgrade(IGH_DocumentObject target, GH_Document document)")
        L.append("  {")
        L.append("    return GH_UpgradeUtil.SwapComponents(target as IGH_Component, UpgradeTo);")
        L.append("  }")
        L.append("}")
        L.append("")
    L.append("// Emitted into the same partial class as the script body so it can reach the")
    L.append("// script's `private void RunScript`. Locals are declared with the types the")
    L.append("// signature actually uses, and the call is emitted in *signature* order --")
    L.append("// which need not match the header order that fixes the DA indices.")
    L.append("partial class Script_%s" % s)
    L.append("{")
    L.append("  internal void Invoke(IGH_DataAccess DA)")
    L.append("  {")

    for i, p in enumerate(ins):
        L.extend(emit_read(p, i, where))
    if ins and outs:
        L.append("")
    for p in outs:
        L.append("    %s %s;" % (clr_type(p, where), p["variableName"]))
    L.append("")

    call = []
    for sp in sig:
        p = by_name[sp["name"]]
        call.append(("out " if sp["dir"] == "out" else "") + sp["name"])
    L.append("    RunScript(%s);" % ", ".join(call))

    if outs:
        L.append("")
        for i, p in enumerate(outs):
            L.extend(emit_write(p, i, where))
    L.append("  }")
    L.append("}")
    L.append("")
    return "\n".join(L)


# ---------------------------------------------------------------- validation --

def cross_check(meta, sig, src_name):
    """The header is the param *declaration*; the signature is what gets called.
    gh_meta --check already reports name drift; here it is fatal, because the
    generated Invoke cannot be written at all if the two disagree."""
    hdr = {p["variableName"]: p for p in meta["inputs"] + meta["outputs"]}
    problems = []

    for sp in sig:
        p = hdr.get(sp["name"])
        if p is None:
            problems.append("RunScript param %r has no header entry" % sp["name"])
            continue
        want = clr_type(p, src_name)
        got = sp["type"]
        if normalize_type(want) != normalize_type(got):
            problems.append(
                "param %r: header says %s|%s (-> %s) but RunScript declares %s"
                % (sp["name"], p["hint"], p["access"], want, got))

    sig_names = {sp["name"] for sp in sig}
    for p in meta["inputs"]:
        if p["variableName"] not in sig_names:
            problems.append("input %r is not a RunScript parameter" % p["variableName"])
    for p in meta["outputs"]:
        if p["variableName"] not in sig_names:
            problems.append("output %r is not a RunScript parameter" % p["variableName"])

    sig_dir = {sp["name"]: sp["dir"] for sp in sig}
    for p in meta["inputs"]:
        if sig_dir.get(p["variableName"]) == "out":
            problems.append("input %r is an `out` parameter in RunScript" % p["variableName"])
    for p in meta["outputs"]:
        if sig_dir.get(p["variableName"]) == "in":
            problems.append("output %r is a by-value parameter in RunScript" % p["variableName"])

    return problems


# --------------------------------------------------------------------- icons --

def rasterize(icon_rel, root, out_dir):
    """SVG -> 24x24 PNG via sips, for the light icon and its `-dark` variant.

    Grasshopper draws a stamped icon at the bitmap's native pixel size, so 24x24
    is the canvas slot size and not an arbitrary choice.
    """
    made, missing = [], []
    src = os.path.join(root, icon_rel)
    stem = os.path.splitext(os.path.basename(icon_rel))[0]
    base = os.path.dirname(src)

    for variant, name in ((src, stem + ".png"),
                          (os.path.join(base, stem + "-dark.svg"), stem + "-dark.png")):
        if not os.path.isfile(variant):
            missing.append(os.path.relpath(variant, root))
            continue
        dst = os.path.join(out_dir, name)
        r = subprocess.run(["sips", "-s", "format", "png", "-Z", "24", variant, "--out", dst],
                           capture_output=True, text=True)
        if r.returncode != 0:
            raise CodegenError("sips failed on %s: %s" % (variant, r.stderr.strip()))
        made.append(name)
    return made, missing


# ---------------------------------------------------------------------- main --

def main(argv):
    args = argv[1:]
    if any(a in ("-h", "--help") for a in args):
        print(__doc__)
        return 0

    def opt(flag, default):
        if flag in args:
            i = args.index(flag)
            if i + 1 >= len(args):
                sys.exit("error: %s needs an argument" % flag)
            v = args[i + 1]
            del args[i:i + 2]
            return v
        return default

    root = os.path.abspath(opt("--root", os.getcwd()))
    out_dir = os.path.abspath(opt("--out", os.path.join(root, "build", "gen")))
    resource_prefix = opt("--resource-prefix", None)
    list_only = "--list" in args
    no_icons = "--no-icons" in args
    args = [a for a in args if a not in ("--list", "--no-icons")]
    if args:
        sys.exit("error: unrecognized argument(s): %s" % " ".join(args))
    if not os.path.isdir(root):
        sys.exit("error: not a directory: %s" % root)
    # Required whenever we actually emit code: the prefix is baked into every
    # IconResourceName and has to match the csproj's EmbeddedResource LogicalName.
    # There is no safe default -- guessing one produces a build that compiles
    # cleanly and silently shows no icons, so make the omission loud instead.
    # `--list` writes nothing, so it needs no prefix.
    if resource_prefix is None and not list_only:
        sys.exit("error: --resource-prefix is required (e.g. --resource-prefix "
                 "MyPlugin.Icons; must match <RootNamespace>.Icons in the csproj). "
                 "Via publish.sh it defaults to GHA_NAME without its .gha suffix.")

    kit = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    tpl_dir = os.path.join(kit, "tooling", "templates")

    # ---- gather ------------------------------------------------------------
    shipped, skipped, errors = [], [], []
    for fname in sorted(os.listdir(root)):
        if not fname.endswith(".cs"):
            continue
        path = os.path.join(root, fname)
        try:
            meta = gh_meta.parse_header(path)
        except gh_meta.HeaderError as e:
            skipped.append((fname, "no usable header (%s)" % e))
            continue
        if not meta["component_guid"]:
            skipped.append((fname, "no component guid -- not on the ship list"))
            continue

        with open(path, "r", encoding="utf-8-sig") as fh:
            text = fh.read()

        sig = gh_meta.parse_runscript_params(text)
        if sig is None:
            errors.append("%s: no RunScript declaration found" % fname)
            continue

        errors.extend("%s: %s" % (fname, p) for p in gh_meta.check_meta(path, meta))
        errors.extend("%s: %s" % (fname, p) for p in cross_check(meta, sig, fname))

        if not CLASS_DECL.search(text):
            errors.append("%s: no canonical `public class Script_Instance : "
                          "GH_ScriptInstance` declaration to rewrite" % fname)
            continue
        if len(CLASS_DECL.findall(text)) != 1:
            errors.append("%s: more than one Script_Instance declaration" % fname)
            continue

        shipped.append(dict(fname=fname, path=path, meta=meta, sig=sig, text=text,
                            slug=slug(meta["name"])))

    seen = {}
    for c in shipped:
        if c["slug"] in seen:
            errors.append("slug collision %r: %s and %s" % (c["slug"], seen[c["slug"]], c["fname"]))
        seen[c["slug"]] = c["fname"]

    guids = {}
    for c in shipped:
        g = c["meta"]["component_guid"].lower()
        if g in guids:
            errors.append("duplicate guid %s: %s and %s" % (g, guids[g], c["fname"]))
        guids[g] = c["fname"]

    # Two components claiming the same retired guid is ambiguous: GH_ComponentServer
    # keys upgraders by UpgradeFrom and keeps only the newest Version, so one of the
    # two would silently never fire. Reject it here instead.
    upgrades = {}
    for c in shipped:
        for old in c["meta"].get("upgrades", []):
            key = old.strip().lower()
            if key in upgrades:
                errors.append("duplicate upgrade-from %s: %s and %s"
                              % (key, upgrades[key], c["fname"]))
            upgrades[key] = c["fname"]

    for fname, why in skipped:
        print("skip  %-34s %s" % (fname, why))

    if errors:
        for e in errors:
            print("FAIL  %s" % e, file=sys.stderr)
        return 1

    if list_only:
        for c in shipped:
            m = c["meta"]
            extra = ""
            if m["markers"]:
                extra += "  markers=" + ",".join(m["markers"])
            if m.get("upgrades"):
                extra += "  upgrade-from=" + ",".join(m["upgrades"])
            hooks = declared_hooks(c["text"])
            if hooks:
                extra += "  hooks=" + ",".join(h["hook"] for h in hooks)
            print("ship  %-34s -> Comp_%-24s %s > %s  in=%d out=%d%s"
                  % (c["fname"], c["slug"], m["category"], m["subcategory"],
                     len(m["inputs"]), len(m["outputs"]), extra))
        print("\n%d component(s) on the ship list." % len(shipped))
        return 0

    # ---- emit --------------------------------------------------------------
    # From scratch every time: a stale .g.cs from a renamed or retired component
    # would otherwise keep compiling into the plugin forever.
    if os.path.isdir(out_dir):
        shutil.rmtree(out_dir)
    icon_dir = os.path.join(out_dir, "icons")
    os.makedirs(icon_dir)

    for tpl in TEMPLATES:
        src = os.path.join(tpl_dir, tpl)
        if not os.path.isfile(src):
            sys.exit("error: missing kit template: %s" % src)
        shutil.copyfile(src, os.path.join(out_dir, tpl))

    missing_icons = []
    for c in shipped:
        body = CLASS_DECL.sub(
            "internal sealed partial class Script_%s : global::GHScriptKit.ScriptBase" % c["slug"],
            c["text"], count=1)
        with open(os.path.join(out_dir, "Script_%s.g.cs" % c["slug"]), "w", encoding="utf-8") as fh:
            fh.write(body)

        comp = build_comp(c["meta"], c["sig"], c["text"], c["fname"], resource_prefix)
        with open(os.path.join(out_dir, "Comp_%s.g.cs" % c["slug"]), "w", encoding="utf-8") as fh:
            fh.write(comp)

        if not no_icons and c["meta"].get("icon"):
            _, missing = rasterize(c["meta"]["icon"], root, icon_dir)
            missing_icons.extend("%s: %s" % (c["fname"], m) for m in missing)

    for m in missing_icons:
        print("warn  missing icon %s" % m)

    print("\n%d component(s) -> %s" % (len(shipped), os.path.relpath(out_dir, root)))
    print("      %d template(s), %d .g.cs, %d icon(s)"
          % (len(TEMPLATES), len(shipped) * 2, len(os.listdir(icon_dir))))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv))
    except CodegenError as e:
        print("FAIL  %s" % e, file=sys.stderr)
        sys.exit(1)
