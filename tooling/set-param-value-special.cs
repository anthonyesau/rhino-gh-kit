// set-param-value-special.cs — the kinds set-param-value.cs does not handle:
// Rhino-model references ("ref", "arc", "circle", "line", "rect"), named views
// ("view"), saved state ("state") and raw serialized data ("chunk").
// A payload for mcp__rhino__run_csharp; edit only the `edits` array.
// How to use both: skills/set-param-value/SKILL.md.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

// --- EDIT THIS ---
var edits = new (string Id, string Kind, object[] Values)[]
{
  ("00000000-0000-0000-0000-000000000000", "ref", new object[] { "<rhino object guid>" }),
};
// ---

var kinds = new[] { "ref", "arc", "circle", "line", "rect", "view", "state", "chunk" };
var otherScript = "set-param-value.cs";
var rdoc = __rhino_doc__;

// Param_Geometry's element type is the abstract IGH_GeometricGoo: walk the Rhino
// geometry's inheritance until Grasshopper.Kernel.Types.GH_<Name> resolves.
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

// One goo per raw value: a named view, or a reference to a Rhino object.
Func<Type, object, string, IGH_Goo> buildGoo = (itemType, raw, kind) =>
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

  if (!Guid.TryParse(Convert.ToString(raw), out var oid)) { Console.WriteLine("    not a GUID: " + raw); return null; }
  var rhObj = rdoc.Objects.FindId(oid);
  if (rhObj == null) { Console.WriteLine("    object " + oid + " not in the Rhino document"); return null; }
  var gooType = itemType;
  if (gooType == null || gooType.IsAbstract || gooType.IsInterface)
  {
    gooType = concreteGoo(rhObj.Geometry);
    if (gooType == null) { Console.WriteLine("    no concrete goo for " + rhObj.Geometry.GetType().Name); return null; }
  }
  var goo = (IGH_Goo) Activator.CreateInstance(gooType);
  gooType.GetProperty("ReferenceID")?.SetValue(goo, oid);
  gooType.GetMethod("LoadGeometry", new[] { typeof(Rhino.RhinoDoc) })?.Invoke(goo, new object[] { rdoc });
  if (kind == "ref") return goo;

  // LoadGeometry does not turn an ArcCurve/LineCurve/PolylineCurve into an
  // Arc/Circle/Line/Rectangle3d, so those four kinds set Value by hand. Only they
  // touch Value: on some annotation goos a bare GetProperty("Value") is ambiguous.
  var vp = gooType.GetProperty("Value");
  var geom = rhObj.Geometry;
  if (kind == "arc" && geom is ArcCurve ac) vp.SetValue(goo, ac.Arc);
  else if (kind == "circle" && geom is ArcCurve ac2 && ac2.Arc.IsCircle) vp.SetValue(goo, new Circle(ac2.Arc.Plane, ac2.Radius));
  else if (kind == "line" && geom is LineCurve lc) vp.SetValue(goo, lc.Line);
  else if (kind == "rect" && geom is Curve crv && crv.TryGetPolyline(out var poly) && poly.Count >= 4)
  {
    Point3d p0 = poly[0], p1 = poly[1], p3 = poly[poly.Count - 2];
    vp.SetValue(goo, new Rectangle3d(new Plane(p0, p1 - p0, p3 - p0), p1, p3));
  }
  else Console.WriteLine("    kind '" + kind + "' does not match geometry " + geom.GetType().Name + "; Value left unset");
  return goo;
};

// "state" and "chunk" bypass goo building. Returns null to hand the object on
// to the shared param path below.
Func<IGH_DocumentObject, string, object[], bool?> setObject = (obj, kind, values) =>
{
  if (kind == "state")
  {
    // IGH_StateAwareObject (Gene Pool, …): one value, the LoadState string.
    var ls = obj.GetType().GetMethod("LoadState", new[] { typeof(string) });
    if (ls == null) { Console.WriteLine("    no LoadState(string) on " + obj.GetType().Name); return false; }
    ls.Invoke(obj, new object[] { Convert.ToString(values.FirstOrDefault()) ?? string.Empty });
    Console.WriteLine("    loaded state");
    return true;
  }
  if (kind == "chunk")
  {
    // One value: a base64 GH_LooseChunk read straight into PersistentData.
    var pd = obj.GetType().GetProperty("PersistentData")?.GetValue(obj);
    var read = pd?.GetType().GetMethod("Read", new[] { typeof(GH_IReader) });
    if (read == null) { Console.WriteLine("    no PersistentData.Read on " + obj.GetType().Name); return false; }
    pd.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(pd, null);
    var b64 = Convert.ToString(values.FirstOrDefault());
    if (string.IsNullOrEmpty(b64)) { Console.WriteLine("    cleared (empty chunk)"); return true; }
    var chunk = new GH_LooseChunk("data");
    chunk.Deserialize_Binary(Convert.FromBase64String(b64));
    read.Invoke(pd, new object[] { chunk });
    Console.WriteLine("    read chunk (" + b64.Length + " b64 chars)");
    return true;
  }
  return null;
};

// --- SHARED with set-param-value.cs — keep byte-identical ---
var ghdoc = Instances.ActiveCanvas?.Document ?? (Instances.DocumentServer.DocumentCount > 0 ? Instances.DocumentServer[0] : null);
if (ghdoc == null) { Console.WriteLine("ERROR: no active Grasshopper document"); return; }

// Index canvas objects and every component param: the target is usually a
// component input, addressed by its own InstanceGuid or "<component>:<name>".
var byId = new Dictionary<Guid, IGH_DocumentObject>();
var inputsOf = new Dictionary<Guid, List<IGH_Param>>();
foreach (var o in ghdoc.Objects)
{
  byId[o.InstanceGuid] = o;
  if (!(o is IGH_Component comp)) continue;
  inputsOf[o.InstanceGuid] = comp.Params.Input.ToList();
  foreach (var p in comp.Params.Input) byId[p.InstanceGuid] = p;
  foreach (var p in comp.Params.Output) byId[p.InstanceGuid] = p;
}

// An open generic base by name, so ContextualParameter<T> (outside the
// Grasshopper assembly) resolves too.
Func<Type, string, Type> findGenericBase = (t, name) =>
{
  for (; t != null; t = t.BaseType)
    if (t.IsGenericType && t.GetGenericTypeDefinition().Name == name) return t;
  return null;
};

// A plain param: ContextualParameter<T> first (no usable PersistentData), then
// GH_PersistentParam<T> — Clear() before Append, since Append accumulates.
Func<IGH_Param, string, object[], bool> setParam = (param, kind, values) =>
{
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

  var pd = param.GetType().GetProperty("PersistentData")?.GetValue(param);
  if (pd == null) { Console.WriteLine("    " + param.GetType().Name + " has no PersistentData and no ContextualParameter base"); return false; }
  var gen = pd.GetType().GetGenericArguments();
  var gooType = gen.Length > 0 ? gen[0] : null;
  pd.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(pd, null);
  if (values.Length == 0) { Console.WriteLine("    cleared"); return true; }

  var append = (gooType != null ? pd.GetType().GetMethod("Append", new[] { gooType }) : null)
            ?? pd.GetType().GetMethods().FirstOrDefault(m => m.Name == "Append" && m.GetParameters().Length == 1);
  if (append == null) { Console.WriteLine("    no PersistentData.Append on " + param.GetType().Name); return false; }
  int built = 0;
  foreach (var raw in values)
  {
    var goo = buildGoo(gooType, raw, kind);
    if (goo == null) continue;
    try { append.Invoke(pd, new object[] { goo }); built++; Console.WriteLine("    + " + goo); }
    catch (Exception ex) { Console.WriteLine("    append failed: " + (ex.InnerException ?? ex).Message); }
  }
  if (built == 0) { Console.WriteLine("    nothing appended"); return false; }
  Console.WriteLine("    persistent data: " + built + "/" + values.Length + " item(s)");
  return true;
};

var touched = new List<IGH_DocumentObject>();
int ok = 0, fail = 0;
foreach (var (id, kindRaw, values) in edits)
{
  var kind = string.IsNullOrEmpty(kindRaw) ? "auto" : kindRaw.ToLowerInvariant();
  if (!kinds.Contains(kind)) { Console.WriteLine("FAIL " + id + ": kind '" + kind + "' is handled by " + otherScript); fail++; continue; }

  var colon = id.IndexOf(':');
  var namePart = colon < 0 ? null : id.Substring(colon + 1);
  if (!Guid.TryParse((colon < 0 ? id : id.Substring(0, colon)).Trim(), out var gid)) { Console.WriteLine("FAIL " + id + ": not a GUID"); fail++; continue; }

  IGH_DocumentObject obj = null;
  if (namePart == null) byId.TryGetValue(gid, out obj);
  else if (!inputsOf.TryGetValue(gid, out var ins)) { Console.WriteLine("FAIL " + id + ": no component with that guid"); fail++; continue; }
  else
  {
    obj = ins.FirstOrDefault(p => string.Equals(p.Name, namePart, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(p.NickName, namePart, StringComparison.OrdinalIgnoreCase));
    if (obj == null) { Console.WriteLine("FAIL " + id + ": no input named '" + namePart + "' (have: " + string.Join(", ", ins.Select(p => p.Name)) + ")"); fail++; continue; }
  }
  if (obj == null) { Console.WriteLine("FAIL " + id + ": not found on the canvas"); fail++; continue; }

  Console.WriteLine("· " + obj.GetType().Name + " '" + obj.NickName + "' (" + kind + ")");
  bool done = false;
  try
  {
    var handled = setObject(obj, kind, values);
    if (handled.HasValue) done = handled.Value;
    else if (obj is IGH_Param param) done = setParam(param, kind, values);
    else Console.WriteLine("    unsupported object type " + obj.GetType().FullName);
  }
  catch (Exception ex) { Console.WriteLine("    " + ex.GetType().Name + " — " + (ex.InnerException ?? ex).Message); }
  if (done) { ok++; touched.Add(obj); } else fail++;
}

// Expiring mid-solution locks the canvas, so every expiry is deferred into a
// scheduled solution; nothing printed here reflects post-solve data.
if (touched.Count > 0)
  ghdoc.ScheduleSolution(5, d => { foreach (var o in touched) o.ExpireSolution(false); });
Console.WriteLine();
Console.WriteLine(ok + " set, " + fail + " failed. " + (touched.Count > 0 ? "Solution scheduled — read VolatileData in a separate call." : "Nothing scheduled."));
// --- END SHARED ---
