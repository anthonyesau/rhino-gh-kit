# Ring Array — copies one piece of geometry to a ring of points, rotating each
# copy outward. Wire a Polygon into Geometry for a rosette.
#
# Configures itself at solve time: param names, type hints, access, tooltips,
# component name, description and a drawn icon. Nothing to set up by hand,
# including the half no UI can reach.
#
# ONE file for both ways of running it — pasted into the editor, or read off disk
# through a `script` param. Nothing here switches on which; the two things that
# could have needed a switch both handle it themselves:
#
#   gh_owned()      counts the params Grasshopper owns instead of assuming index 0.
#                   A `script` param sits at input 0 and the `out` print stream at
#                   output 0, either can be switched off from the right-click menu,
#                   and neither exists on a freshly dropped component. Assume a
#                   number instead of counting and you name your first param onto
#                   the script param, drop your last one, and loop forever on a
#                   shape check that can never pass.
#   report_source() returns immediately when there is no `script` param, so it
#                   costs a pasted component nothing.
#
# Script mode, not SDK mode. An SDK-mode RunScript signature would declare the three
# inputs for free (see "ring-array-stock.py"), but not the two outputs, and not a
# tooltip, name, description or icon — so build_params() would still be needed for
# the output side and set_param_text() for all of it. It would also do nothing at all
# when linked: the editor sync that reads a signature fires on an editor SAVE, which
# reading a file off disk never performs. One imperative pass covers every route.

import math

import System.Drawing as SD
import Grasshopper
import Rhino.Geometry as rg
from Grasshopper.Kernel import (GH_ParamAccess, GH_ParameterSide, GH_IconDisplayMode,
                                GH_RuntimeMessageLevel)

NAME = "Ring Array"
NICK = "RingArr"
BLURB = "Copies one piece of geometry to a ring of points."

# name, type hint, access, tooltip.  Hint names are the PYTHON spelling: "float"
# where C# says "double", "str" for text. An unrecognised name is ignored.
INPUTS = [
    ("Geometry", "GeometryBase", "item", "Geometry to copy. Wire a Polygon component here."),
    ("Radius",   "float",        "item", "Radius of the ring, in model units."),
    ("Count",    "int",          "item", "How many copies to place around the ring."),
]
OUTPUTS = [
    ("Points",  "The ring of points, one per copy."),
    ("Arrayed", "The input geometry copied and rotated to each point."),
]

comp = ghenv.Component


def gh_owned(params):
    """How many leading params belong to Grasshopper rather than to us: the
    `script` ScriptParam on the input side, the `out` print stream on the
    output side. Counted, never assumed — either can be switched off from the
    right-click menu, and neither exists on a freshly dropped component."""
    n = 0
    for p in params:
        if p.GetType().Name == "ScriptVariableParam":
            break
        n += 1
    return n


def ours(params):
    return list(params)[gh_owned(params):]


def report_source():
    """Script-param housekeeping: name the linked file in the component's caption,
    and warn if the param holds more than one script."""
    params = comp.Params.Input
    if params.Count == 0 or params[0].GetType().Name != "ScriptParam":
        return
    try:
        names = [g.Value.Name for g in params[0].VolatileData.AllData(False) if g is not None]
    except Exception:
        names = []
    if len(names) > 1:
        comp.AddRuntimeMessage(
            GH_RuntimeMessageLevel.Warning,
            "The script param holds %d scripts and Grasshopper runs every one of "
            "them. Clear the param, then set exactly one file." % len(names))
    comp.Message = names[-1] if names else None


def set_identity():
    """Component-level metadata. Description is what the tooltip shows now and is
    regenerated on load; Tooltip is the durable slot archived into the .gh and
    restored over Description on open. Write both."""
    comp.Name = NAME
    comp.NickName = NICK
    comp.Description = BLURB
    comp.Tooltip = BLURB

    # The canvas icon slot is a plain Bitmap. Re-stamped every solve — one this
    # small costs nothing, and there is nowhere on a .NET object to hang a flag.
    bmp = SD.Bitmap(24, 24)
    g = SD.Graphics.FromImage(bmp)
    g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias
    g.Clear(SD.Color.Transparent)
    g.DrawEllipse(SD.Pen(SD.Color.FromArgb(220, 40, 60, 90), 2.0), 4, 4, 16, 16)
    for k in range(6):
        a = 2 * math.pi * k / 6
        g.FillEllipse(SD.Brushes.MediumSeaGreen, 11 + 8 * math.cos(a), 11 + 8 * math.sin(a), 3, 3)
    g.Dispose()
    comp.SetIconOverride(bmp)
    comp.IconDisplayMode = GH_IconDisplayMode.icon


def set_param_text():
    """Per-param metadata. ToolTip is the durable slot — writing it writes
    Description too. Writing Description alone is replaced by the converter's
    generic text on the next load or source push."""
    access = {"item": GH_ParamAccess.item,
              "list": GH_ParamAccess.list,
              "tree": GH_ParamAccess.tree}
    for name, hint, acc, tip in INPUTS:
        p = next((q for q in comp.Params.Input if q.NickName == name), None)
        if p is None:
            continue
        p.PrettyName = name
        p.ToolTip = tip
        p.TypeHints.Select(hint)     # a Converter on the param, unrelated to the code
        p.Access = access[acc]
    for name, tip in OUTPUTS:
        p = next((q for q in comp.Params.Output if q.NickName == name), None)
        if p is not None:
            p.PrettyName = name
            p.ToolTip = tip          # outputs stay unhinted: a hint buys nothing here


def shape_is_right():
    return ([p.NickName for p in ours(comp.Params.Input)] == [s[0] for s in INPUTS]
            and [p.NickName for p in ours(comp.Params.Output)] == [s[0] for s in OUTPUTS])


def build_params():
    """A script component implements IGH_VariableParameterComponent — that is what
    the zoom-in + / - widgets drive — so it can build its own params. Do it OUTSIDE
    the solution, or GH 8's 'object expired during a solution' guard locks the
    canvas. Offsets come from gh_owned(), so the script param is never touched."""
    def fit(side, params, names):
        offset = gh_owned(params)
        want = len(names) + offset
        while params.Count > want:
            p = params[params.Count - 1]
            comp.DestroyParameter(side, params.Count - 1)
            comp.Params.UnregisterParameter(p)
        while params.Count < want:
            p = comp.CreateParameter(side, params.Count)
            if side == GH_ParameterSide.Input:
                comp.Params.RegisterInputParam(p)
            else:
                comp.Params.RegisterOutputParam(p)
        for i, n in enumerate(names):
            params[i + offset].VariableName = n   # == NickName == the Python global

    def rebuild(doc):
        fit(GH_ParameterSide.Input, comp.Params.Input, [s[0] for s in INPUTS])
        fit(GH_ParameterSide.Output, comp.Params.Output, [s[0] for s in OUTPUTS])
        set_param_text()      # hints and tooltips land with the params, not a solve later
        comp.Params.OnParametersChanged()
        comp.ExpireSolution(False)

    comp.OnPingDocument().ScheduleSolution(
        5, Grasshopper.Kernel.GH_Document.GH_ScheduleDelegate(rebuild))


# --- run ----------------------------------------------------------------------
set_identity()
set_param_text()
report_source()

if not shape_is_right():
    build_params()
    Points, Arrayed = [], []
else:
    # globals() here because THIS script builds its own params: on the first solve of a
    # freshly dropped component the names below have no param behind them yet, and a name
    # with no param raises NameError. Once the param exists an unwired input is bound to
    # None like any other, so an ordinary headered component reads its inputs by bare name
    # and needs none of this. (C# hands RunScript a default-valued argument instead:
    # 0.0 for a double, null for geometry.)
    geometry = globals().get("Geometry")
    radius = globals().get("Radius") or 0.0
    count = int(globals().get("Count") or 0)

    Points, Arrayed = [], []
    for i in range(count):
        t = 2 * math.pi * i / max(count, 1)
        pt = rg.Point3d(radius * math.cos(t), radius * math.sin(t), 0.0)
        Points.append(pt)

        if geometry is None:
            continue
        frame = rg.Plane(pt, rg.Vector3d.ZAxis)
        frame.Rotate(t, rg.Vector3d.ZAxis, pt)
        copy = geometry.Duplicate()
        copy.Transform(rg.Transform.PlaneToPlane(rg.Plane.WorldXY, frame))
        Arrayed.append(copy)
