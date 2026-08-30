// set-param-value.cs — write a value into any Grasshopper param or input object.
//
// NOT a Grasshopper script component. This is a payload for
// `mcp__rhino__run_csharp`, run against the live, open Grasshopper document.
// The Platform's own tools can set a slider and nothing else; this fills the
// rest of the gap — panels, value lists, toggles, buttons, component input
// params, and Rhino-document geometry references.
//
// It is also how `skills/forge-push` feeds Script Forge its Source and Target
// inputs, so treat it as load-bearing.
//
// Usage:
//   1. Fill in the `edits` array below — the only block you should edit.
//   2. Paste the whole file into mcp__rhino__run_csharp.
//   3. Read the printed per-edit result lines.
//   4. Values land immediately, but the recompute is *scheduled*: read
//      VolatileData back in a SECOND call (or via g1_get_canvas_graph),
//      never in this one. See "Solution safety" below.
//
// Addressing (the `Id` field):
//   "<guid>"              a canvas object, or a param, by InstanceGuid
//   "<guid>:<ParamName>"  an input param of the component with that guid,
//                         looked up by param Name — the readable form, and
//                         what forge-push uses ("<forge guid>:Source")
//
// Kinds:
//   "auto"    infer from the target's type. Correct for everything that is not
//             a Rhino-geometry reference.
//   "ref"     Rhino object reference: Values are Rhino object GUIDs. Sets
//             ReferenceID then calls LoadGeometry — what right-click → "Set One
//             Rhino Object" does.
//   "arc" | "circle" | "line" | "rect"
//             as "ref", plus the manual .Value extraction those four goo types
//             need. LoadGeometry does NOT convert an ArcCurve/LineCurve/
//             PolylineCurve into an Arc/Circle/Line/Rectangle3d, so ReferenceID
//             alone leaves the goo IsValid=false. Param_Plane and Param_Box do
//             convert, so they use "ref".
//   "view"    Param_ModelView: Values are *named-view names*, not GUIDs; the
//             goo is built from a Rhino.DocObjects.ViewInfo.
//   "state"   IGH_StateAwareObject (Gene Pool, …): one Values entry, the
//             LoadState string.
//   "chunk"   general fallback: one Values entry, a base64 GH_LooseChunk read
//             straight into PersistentData. Anything whose goo this file cannot
//             build can still be round-tripped this way — pair it with the
//             matching Write/Serialize_Binary on the capture side.
//
// Resolution order for a plain IGH_Param (ported from the restore engine in
// `Save and Restore State`, a separate project — no file in this repo, and no
// line reference here on purpose, since one into another repo only rots):
//   1. ContextualParameter<T>  → ClearContextualData / AssignContextualData.
//      Checked FIRST: these params (the RhinoCode Get*Parameter family) have no
//      usable PersistentData, and the type is reached by name because it is not
//      in the Grasshopper assembly.
//   2. GH_PersistentParam<T>   → PersistentData.Clear() then Append(goo), with
//      the goo type read off the generic argument. Clear() first is not
//      optional: Append accumulates, so skipping it doubles the data — an
//      item-access input holding N values makes GH solve the component N times
//      and fan its outputs into N branches.
//   3. IGH_StateAwareObject    → LoadState(string).
//   4. base64 GH_LooseChunk    → PersistentData.Read(GH_IReader).
//
// Panels: N values are joined with newlines and Multiline is set to match —
// OFF for several values so the panel emits one item per line, ON for a single
// value so text containing newlines stays one item. Set the text you want the
// downstream component to receive, not the text you want to look at.
//
// Solution safety: every mutation happens inline (run_csharp is on the UI
// thread, outside a solution), but the ExpireSolution calls are deferred into
// ghdoc.ScheduleSolution — expiring an object mid-solution trips GH 8's
// "object expired during a solution" guard and locks the canvas. That deferral
// is also why nothing this file prints reflects post-solve VolatileData.
//
// Two things references do that will look like bugs (verified 2026-08-13,
// Rhino 8, by saving the document and reading the reloaded copy off disk):
//
//   * A "ref" goo is IsValid=false until LoadGeometry runs. GH lazy-loads
//     referenced geometry, so straight after a reopen PersistentData shows
//     "Null Curve" while ReferenceID is intact and VolatileData is correct.
//     Verify a reference through VolatileData, never PersistentData.
//   * The four primitive kinds do NOT stay references. GH_Arc/GH_Circle/
//     GH_Line/GH_Rectangle serialize their Value and not a ReferenceID, so a
//     reopened document has the right geometry with ReferenceID back at
//     Guid.Empty — internalized, no longer tracking the Rhino object. That is
//     the same limitation that forces the manual .Value extraction above, seen
//     from the other end. If you need a live reference to a circle or a line,
//     target a Param_Curve or Param_Geometry with kind "ref" instead.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

// ─── EDIT THIS ───────────────────────────────────────────────────────────────
var edits = new (string Id, string Kind, object[] Values)[]
{
  ("00000000-0000-0000-0000-000000000000", "auto", new object[] { "replace me" }),
};
// ─────────────────────────────────────────────────────────────────────────────

var rdoc = __rhino_doc__;
var ghdoc = Instances.ActiveCanvas?.Document ?? (Instances.DocumentServer.DocumentCount > 0 ? Instances.DocumentServer[0] : null);
if (ghdoc == null) { Console.WriteLine("ERROR: no active Grasshopper document"); return; }

// Index canvas objects AND every component param, because the thing you want to
// set is usually a component input, which is addressed by its own InstanceGuid.
var byId = new Dictionary<Guid, IGH_DocumentObject>();
var inputsOf = new Dictionary<Guid, List<IGH_Param>>();
foreach (var o in ghdoc.Objects)
{
  byId[o.InstanceGuid] = o;
  var comp = o as IGH_Component;
  if (comp == null) continue;
  inputsOf[o.InstanceGuid] = comp.Params.Input.ToList();
  foreach (var p in comp.Params.Input) byId[p.InstanceGuid] = p;
  foreach (var p in comp.Params.Output) byId[p.InstanceGuid] = p;
}

Func<object, decimal> toDec = o => o is decimal m ? m : Convert.ToDecimal(o, CultureInfo.InvariantCulture);
Func<object, bool> toBool = o => o is bool b ? b : bool.Parse(Convert.ToString(o));

// Walk a type's inheritance for an open generic base, matched by name so types
// outside the Grasshopper assembly (ContextualParameter<T>) still resolve.
Func<Type, string, Type> findGenericBase = (t, name) =>
{
  while (t != null)
  {
    if (t.IsGenericType && t.GetGenericTypeDefinition().Name == name) return t;
    t = t.BaseType;
  }
  return null;
};

// Param_Geometry's element type is the abstract IGH_GeometricGoo. Walk the
// Rhino geometry's inheritance until Grasshopper.Kernel.Types.GH_<Name> resolves
// (LineCurve → Curve → GH_Curve, and so on).
Func<GeometryBase, Type> concreteGoo = geom =>
{
  var asm = typeof(GH_Curve).Assembly;
  for (var t = geom.GetType(); t != null && t != typeof(object); t = t.BaseType)
  {
    var gt = asm.GetType("Grasshopper.Kernel.Types.GH_" + t.Name);
    if (gt != null) return gt;
  }
  return null;
};

// Build one goo. `kind` selects the Rhino-reference behaviours; "auto" casts the
// raw value through IGH_Goo.CastFrom, which covers every primitive param.
Func<Type, object, string, IGH_Goo> buildGoo = null;
buildGoo = (itemType, raw, kind) =>
{
  if (kind == "view")
  {
    var name = Convert.ToString(raw);
    Rhino.DocObjects.ViewInfo vi = null;
    for (int i = 0; i < rdoc.NamedViews.Count; i++)
      if (rdoc.NamedViews[i].Name == name) { vi = rdoc.NamedViews[i]; break; }
    if (vi == null) { Console.WriteLine("    named view '" + name + "' not found"); return null; }
    var vt = typeof(GH_Curve).Assembly.GetType("Grasshopper.Rhinoceros.Display.ModelView");
    if (vt == null) { Console.WriteLine("    ModelView type unavailable"); return null; }
    return (IGH_Goo) vt.GetConstructor(new[] { typeof(Rhino.DocObjects.ViewInfo) }).Invoke(new object[] { vi });
  }

  if (kind == "ref" || kind == "arc" || kind == "circle" || kind == "line" || kind == "rect")
  {
    Guid oid;
    if (!Guid.TryParse(Convert.ToString(raw), out oid)) { Console.WriteLine("    not a GUID: " + raw); return null; }
    var rhObj = rdoc.Objects.FindId(oid);
    if (rhObj == null) { Console.WriteLine("    object " + oid + " not in the Rhino document"); return null; }

    var gooType = itemType;
    if (gooType == null || gooType.IsAbstract || gooType.IsInterface)
    {
      gooType = concreteGoo(rhObj.Geometry);
      if (gooType == null) { Console.WriteLine("    no concrete goo for " + rhObj.Geometry.GetType().Name); return null; }
    }

    var goo = (IGH_Goo) Activator.CreateInstance(gooType);
    var refProp = gooType.GetProperty("ReferenceID");
    if (refProp != null) refProp.SetValue(goo, oid);
    var lg = gooType.GetMethod("LoadGeometry", new[] { typeof(Rhino.RhinoDoc) });
    if (lg != null) lg.Invoke(goo, new object[] { rdoc });

    // Only the four primitive-value kinds touch Value. Several annotation goos
    // shadow Value (one inherited, one redeclared) and a bare GetProperty("Value")
    // on those throws AmbiguousMatchException.
    if (kind != "ref")
    {
      var vp = gooType.GetProperty("Value");
      var geom = rhObj.Geometry;
      if (kind == "arc" && geom is ArcCurve ac) vp.SetValue(goo, ac.Arc);
      else if (kind == "circle" && geom is ArcCurve ac2 && ac2.Arc.IsCircle) vp.SetValue(goo, new Circle(ac2.Arc.Plane, ac2.Radius));
      else if (kind == "line" && geom is LineCurve lc) vp.SetValue(goo, lc.Line);
      else if (kind == "rect")
      {
        Polyline poly;
        var crv = geom as Curve;
        if (crv != null && crv.TryGetPolyline(out poly) && poly.Count >= 4)
        {
          Point3d p0 = poly[0], p1 = poly[1], p3 = poly[poly.Count - 2];
          vp.SetValue(goo, new Rectangle3d(new Plane(p0, p1 - p0, p3 - p0), p1, p3));
        }
        else Console.WriteLine("    " + oid + " is not a 4-corner polyline; Value left unset");
      }
      else Console.WriteLine("    kind '" + kind + "' does not match geometry " + geom.GetType().Name + "; Value left unset");
    }
    return goo;
  }

  // "auto": pick a concrete goo, then let CastFrom do the conversion. Do NOT use
  // Activator.CreateInstance(gooType, value) — GH_Boolean's bool ctor misbinds
  // and silently yields Value=False.
  var target = itemType;
  if (target == null || target.IsAbstract || target.IsInterface)
  {
    if (raw is bool) target = typeof(GH_Boolean);
    else if (raw is int || raw is long) target = typeof(GH_Integer);
    else if (raw is double || raw is float || raw is decimal) target = typeof(GH_Number);
    else if (raw is Guid) target = typeof(GH_Guid);
    else target = typeof(GH_String);
  }
  var g = Activator.CreateInstance(target) as IGH_Goo;
  if (g == null) { Console.WriteLine("    cannot construct " + target.Name); return null; }
  if (!g.CastFrom(raw))
  {
    // CastFrom refuses some string→T conversions that the goo's own ctor takes.
    var sc = target.GetConstructor(new[] { typeof(string) });
    if (sc != null) return (IGH_Goo) sc.Invoke(new object[] { Convert.ToString(raw) });
    Console.WriteLine("    " + target.Name + ".CastFrom refused " + (raw == null ? "null" : raw.GetType().Name));
    return null;
  }
  return g;
};

// The four-step resolution order for a plain param.
Func<IGH_Param, string, object[], bool> setParam = (param, kind, values) =>
{
  // 1. ContextualParameter<T> — no PersistentData; assign contextual data instead.
  var ctx = findGenericBase(param.GetType(), "ContextualParameter`1");
  if (ctx != null)
  {
    var itemType = ctx.GetGenericArguments()[0];
    ctx.GetMethod("ClearContextualData", Type.EmptyTypes)?.Invoke(param, null);
    var list = (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
    foreach (var raw in values) { var goo = buildGoo(itemType, raw, kind); if (goo != null) list.Add(goo); }
    var assign = ctx.GetMethod("AssignContextualData", new[] { typeof(IEnumerable) });
    if (assign == null) { Console.WriteLine("    no AssignContextualData on " + param.GetType().Name); return false; }
    assign.Invoke(param, new object[] { list });
    Console.WriteLine("    contextual data: " + list.Count + " item(s)");
    return true;
  }

  // 2. GH_PersistentParam<T> — clear, then append one goo per value.
  var pdProp = param.GetType().GetProperty("PersistentData");
  var pd = pdProp?.GetValue(param);
  if (pd != null)
  {
    var gen = pd.GetType().GetGenericArguments();
    var itemType = gen.Length > 0 ? gen[0] : null;
    pd.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(pd, null);

    if (kind == "chunk")
    {
      var b64 = Convert.ToString(values.FirstOrDefault());
      if (string.IsNullOrEmpty(b64)) { Console.WriteLine("    cleared (empty chunk)"); return true; }
      var chunk = new GH_LooseChunk("data");
      chunk.Deserialize_Binary(Convert.FromBase64String(b64));
      var read = pd.GetType().GetMethod("Read", new[] { typeof(GH_IReader) });
      if (read == null) { Console.WriteLine("    no PersistentData.Read on " + param.GetType().Name); return false; }
      read.Invoke(pd, new object[] { chunk });
      Console.WriteLine("    read chunk (" + b64.Length + " b64 chars)");
      return true;
    }

    if (values.Length == 0) { Console.WriteLine("    cleared"); return true; }

    // Append(T) if the exact overload exists, else the single-argument one.
    var append = (itemType != null ? pd.GetType().GetMethod("Append", new[] { itemType }) : null)
              ?? pd.GetType().GetMethods().FirstOrDefault(m => m.Name == "Append" && m.GetParameters().Length == 1);
    if (append == null) { Console.WriteLine("    no PersistentData.Append on " + param.GetType().Name); return false; }

    int built = 0;
    foreach (var raw in values)
    {
      var goo = buildGoo(itemType, raw, kind);
      if (goo == null) continue;
      try { append.Invoke(pd, new object[] { goo }); built++; Console.WriteLine("    + " + goo.ToString()); }
      catch (Exception ex) { Console.WriteLine("    append failed: " + (ex.InnerException ?? ex).Message); }
    }
    if (built == 0) { Console.WriteLine("    nothing appended"); return false; }
    Console.WriteLine("    persistent data: " + built + "/" + values.Length + " item(s)");
    return true;
  }

  // 3. IGH_StateAwareObject.
  var loadState = param.GetType().GetMethod("LoadState", new[] { typeof(string) });
  if (loadState != null)
  {
    loadState.Invoke(param, new object[] { Convert.ToString(values.FirstOrDefault()) ?? string.Empty });
    Console.WriteLine("    loaded state");
    return true;
  }

  Console.WriteLine("    " + param.GetType().Name + " has no PersistentData, no ContextualParameter base and no LoadState");
  return false;
};

// ─── apply ───────────────────────────────────────────────────────────────────
var touched = new List<IGH_DocumentObject>();
int ok = 0, fail = 0;

foreach (var (id, kindRaw, values) in edits)
{
  var kind = string.IsNullOrEmpty(kindRaw) ? "auto" : kindRaw.ToLowerInvariant();
  var label = id;
  IGH_DocumentObject obj = null;

  var colon = id.IndexOf(':');
  var guidPart = colon < 0 ? id : id.Substring(0, colon);
  var namePart = colon < 0 ? null : id.Substring(colon + 1);

  Guid gid;
  if (!Guid.TryParse(guidPart.Trim(), out gid)) { Console.WriteLine("FAIL " + label + ": not a GUID"); fail++; continue; }

  if (namePart == null) byId.TryGetValue(gid, out obj);
  else
  {
    List<IGH_Param> ins;
    if (!inputsOf.TryGetValue(gid, out ins)) { Console.WriteLine("FAIL " + label + ": no component with that guid"); fail++; continue; }
    obj = ins.FirstOrDefault(p => string.Equals(p.Name, namePart, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(p.NickName, namePart, StringComparison.OrdinalIgnoreCase));
    if (obj == null)
    {
      Console.WriteLine("FAIL " + label + ": no input named '" + namePart + "' (have: " + string.Join(", ", ins.Select(p => p.Name)) + ")");
      fail++; continue;
    }
  }
  if (obj == null) { Console.WriteLine("FAIL " + label + ": not found on the canvas"); fail++; continue; }

  Console.WriteLine("· " + obj.GetType().Name + " '" + obj.NickName + "' (" + kind + ")");
  bool done = false;
  try
  {
    if (kind == "state" && !(obj is IGH_Param))
    {
      var ls = obj.GetType().GetMethod("LoadState", new[] { typeof(string) });
      if (ls == null) Console.WriteLine("    no LoadState(string) on " + obj.GetType().Name);
      else { ls.Invoke(obj, new object[] { Convert.ToString(values.FirstOrDefault()) ?? string.Empty }); Console.WriteLine("    loaded state"); done = true; }
    }
    else if (obj is GH_NumberSlider slider)
    {
      var was = slider.Slider.Value;
      slider.Slider.Value = toDec(values.FirstOrDefault());
      Console.WriteLine("    " + was + " -> " + slider.Slider.Value);
      done = true;
    }
    else if (obj is GH_BooleanToggle toggle)
    {
      toggle.Value = toBool(values.FirstOrDefault());
      Console.WriteLine("    " + toggle.Value);
      done = true;
    }
    else if (obj is GH_ButtonObject button)
    {
      // A button is a GH_Param<IGH_Goo>, NOT a GH_PersistentParam — it has no
      // PersistentData, no ContextualParameter base and no LoadState, so it
      // falls through every step of setParam. ButtonDown is the only writable
      // state it has.
      //
      // Setting it is a HOLD, not a press: the value is whatever ButtonDown was
      // during the solve, and the expiry here is deferred like every other one.
      // A press is therefore two calls — true, then false — with the downstream
      // solve happening between them. One call with `true` leaves the button
      // held down on the canvas.
      button.ButtonDown = toBool(values.FirstOrDefault());
      Console.WriteLine("    ButtonDown=" + button.ButtonDown + (button.ButtonDown ? " (held — send false in a second call to release)" : " (released)"));
      done = true;
    }
    else if (obj is GH_Panel panel)
    {
      // Multiline OFF splits the text into one item per line — that is how a
      // panel feeds a list downstream. See the header note.
      panel.UserText = string.Join("\n", values.Select(v => Convert.ToString(v) ?? string.Empty));
      panel.Properties.Multiline = values.Length <= 1;
      Console.WriteLine("    " + values.Length + " line(s), multiline=" + panel.Properties.Multiline);
      done = true;
    }
    else if (obj is GH_ValueList vlist)
    {
      // Values are item names (case-insensitive) or 0-based indices.
      var want = new HashSet<int>();
      foreach (var v in values)
      {
        var s = Convert.ToString(v);
        int idx = vlist.ListItems.FindIndex(li => string.Equals(li.Name, s, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) int.TryParse(s, out idx);
        if (idx >= 0 && idx < vlist.ListItems.Count) want.Add(idx);
        else Console.WriteLine("    no item '" + s + "' (have: " + string.Join(", ", vlist.ListItems.Select(li => li.Name)) + ")");
      }
      if (want.Count == 0) { Console.WriteLine("    nothing selected — left unchanged"); }
      else
      {
        for (int i = 0; i < vlist.ListItems.Count; i++) vlist.ListItems[i].Selected = want.Contains(i);
        Console.WriteLine("    selected [" + string.Join(",", want.OrderBy(n => n)) + "] of " + vlist.ListItems.Count);
        done = true;
      }
    }
    else if (obj is GH_ColourSwatch swatch)
    {
      swatch.SwatchColour = System.Drawing.ColorTranslator.FromHtml(Convert.ToString(values.FirstOrDefault()));
      Console.WriteLine("    " + swatch.SwatchColour);
      done = true;
    }
    else if (obj is IGH_Param p2) done = setParam(p2, kind, values);
    else Console.WriteLine("    unsupported object type " + obj.GetType().FullName);
  }
  catch (Exception ex)
  {
    Console.WriteLine("    " + ex.GetType().Name + ": " + (ex.InnerException ?? ex).Message);
  }

  if (done) { ok++; touched.Add(obj); } else fail++;
}

// Defer every expiry into the PostProcess window, then let the scheduled
// solution recompute what changed.
if (touched.Count > 0)
  ghdoc.ScheduleSolution(5, d => { foreach (var o in touched) o.ExpireSolution(false); });

Console.WriteLine();
Console.WriteLine(ok + " set, " + fail + " failed. " + (touched.Count > 0 ? "Solution scheduled — read VolatileData in a separate call." : "Nothing scheduled."));
