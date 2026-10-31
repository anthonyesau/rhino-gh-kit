// answer-dialogs.cs — see and answer the dialogs that block Rhino on macOS, and
// quit a spawned Rhino past its "save changes?" prompts to load a new build.
// A payload for mcp__rhino__run_csharp; edit only the block marked EDIT THIS.
//
// Rhino asks "save changes?" as a sheet on the document's window, and Eto and
// Grasshopper dialogs run app-modal. Neither can be answered over MCP, and the
// agent's shell can send no clicks or keystrokes (System Events is not
// authorized). This payload reaches them through AppKit, by reflection on the
// Microsoft.macOS assembly (run_csharp cannot `#r` it), and presses a button
// with NSButton.PerformClick, which runs the button's own action: Rhino does
// exactly what it does for a person's click.
//
//   "list"  — print every open sheet and modal window, with its text and
//             buttons. Changes nothing. Run it when a call hangs or returns an
//             empty payload: a later call runs nested inside the modal loop.
//   "click" — press BUTTON on the first dialog that offers it.
//   "quit"  — quit Rhino, discarding every unsaved change. Prints what it
//             discards, clears each Grasshopper document's IsModified (else
//             Grasshopper prompts too), watches for sheets and modals and
//             presses the first DISCARD button each offers, then runs _Exit on
//             the next Idle. The call may come back as an empty-JSON error as
//             Rhino goes. Confirm with `ps -p <pid>`, and read LOG for every
//             dialog met and what was pressed.
//
// "quit" is only for a Rhino the agent spawned, holding files the agent opened.
// Never send it to the user's own Rhino: it throws their unsaved work away.
// How to restart a spawned Rhino: docs/write-scripts/rhino-mcp-platform.md,
// "Restarting a spawned Rhino".

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// --- EDIT THIS ---
var mode = "list";          // "list" | "click" | "quit"
var button = "Cancel";      // "click": the title of the button to press
// "quit": the buttons that discard, in order of preference. Two or more dirty
// documents first raise one app-modal "review these changes?" alert, where
// "Review Changes..." leads to a sheet per document; its "Quit Anyway" SAVES
// them. A saved file's sheet offers "Revert Changes". Matching ignores case,
// curly apostrophes and the ellipsis character.
var discard = new[] { "Revert Changes", "Don't Save", "Review Changes..." };
// "quit": a button to press on one named dialog only, where a generic title
// means discard. Grasshopper's "last chance" list saves through a disk button
// per document; its Close saves nothing.
var discardIn = new[] { ("Grasshopper multi-save", "Close") };
// ---

var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
  "Library", "Logs", "rhino-gh-kit-quit.log");

var mac = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Microsoft.macOS");
if (mac == null)
{
  Console.WriteLine("not on macOS — this payload reaches dialogs through AppKit only");
  return;
}
var appType = mac.GetType("AppKit.NSApplication");
var buttonType = mac.GetType("AppKit.NSButton");
var textType = mac.GetType("AppKit.NSTextField");

// First match by name: a subclass that redeclares a property makes GetProperty ambiguous.
Func<object, string, object> get = (o, name) => o?.GetType()
  .GetProperties(BindingFlags.Public | BindingFlags.Instance)
  .FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0)?.GetValue(o);
Func<string, string> norm = s => (s ?? "").Replace('’', '\'').Replace("…", "...").Trim();
Func<object, string> title = b => norm((string)get(b, "Title"));
Func<string, string, bool> same = (a, b) => string.Equals(norm(a), norm(b), StringComparison.OrdinalIgnoreCase);

// Every sheet and app-modal window open now, with its text and titled buttons.
Func<List<(string Where, List<string> Text, List<object> Buttons)>> dialogs = () =>
{
  var found = new List<(string, List<string>, List<object>)>();
  var windows = new List<object>();
  Action<object, string> add = (w, where) =>
  {
    if (w == null || windows.Any(x => x.Equals(w))) return;
    windows.Add(w);
    var text = new List<string>();
    var buttons = new List<object>();
    Action<object> walk = null;
    walk = v =>
    {
      if (v == null) return;
      if (buttonType.IsInstanceOfType(v)) { if (title(v) != "") buttons.Add(v); }
      else if (textType.IsInstanceOfType(v)) { var s = norm((string)get(v, "StringValue")); if (s != "") text.Add(s); }
      foreach (var c in (IEnumerable)get(v, "Subviews") ?? new object[0]) walk(c);
    };
    walk(get(w, "ContentView"));
    found.Add((where, text, buttons));
  };
  var app = appType.GetProperty("SharedApplication").GetValue(null);
  add(get(app, "ModalWindow"), "modal \"" + get(get(app, "ModalWindow"), "Title") + "\"");
  foreach (var w in (IEnumerable)get(app, "DangerousWindows"))
    if ((bool)get(w, "IsVisible")) add(get(w, "AttachedSheet"), "sheet on \"" + get(w, "Title") + "\"");
  return found;
};
Func<(string Where, List<string> Text, List<object> Buttons), string> describe = d =>
  d.Where + ": " + string.Join(" / ", d.Text) + "  [" + string.Join(", ", d.Buttons.Select(title)) + "]";
// The button as sender: PerformClick(null) throws ArgumentNullException through the bindings.
Action<object> press = b => b.GetType().GetMethods()
  .First(m => m.Name == "PerformClick" && m.GetParameters().Length == 1).Invoke(b, new[] { b });

if (mode == "list")
{
  var all = dialogs();
  Console.WriteLine(all.Count == 0 ? "no sheet or modal window open" : string.Join("\n", all.Select(describe)));
  return;
}

if (mode == "click")
{
  var all = dialogs();
  var hit = all.Select(d => (d, b: d.Buttons.FirstOrDefault(x => same(title(x), button)))).FirstOrDefault(p => p.b != null);
  if (hit.b == null)
  {
    Console.WriteLine("no open dialog offers \"" + button + "\"");
    foreach (var d in all) Console.WriteLine(describe(d));
    return;
  }
  press(hit.b);
  Console.WriteLine("pressed \"" + title(hit.b) + "\" on " + describe(hit.d));
  return;
}

if (mode != "quit")
{
  Console.WriteLine("unknown mode \"" + mode + "\" — use list, click or quit");
  return;
}

// --- quit ------------------------------------------------------------------
// Rhino 8 Mac autosaves a dirty saved file in place, so its unsaved changes
// may already be on disk: only the sheet's "Revert Changes" puts the file back.
// Never clear such a document's NSDocument change count to dodge the sheet —
// AppKit then keeps the autosaved changes. A never-saved document is the
// exception: its prompt is an NSSavePanel drawn out of process, whose buttons
// no NSButton walk can reach, and it has no file to keep, so its change count
// is cleared before _Exit instead. RhinoDoc.Modified = false alone stops no
// prompt, and RhinoApp.Exit() returns without quitting.
Directory.CreateDirectory(Path.GetDirectoryName(log));
var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
Action<string> note = line => File.AppendAllText(log, DateTime.Now.ToString("s") + " [" + pid + "] " + line + "\n");
note("quit armed");

foreach (var d in Rhino.RhinoDoc.OpenDocuments())
  if (d.Modified)
  {
    var line = "discarding unsaved changes: " + d.Name + " (" + (string.IsNullOrEmpty(d.Path) ? "never saved" : d.Path) + ")";
    Console.WriteLine(line); note(line);
  }
var server = Grasshopper.Instances.DocumentServer;
if (server != null)
  foreach (Grasshopper.Kernel.GH_Document g in server) // the server enumerates as object
    if (g.IsModified)
    {
      var line = "discarding unsaved changes: " + g.DisplayName + " (" + (string.IsNullOrEmpty(g.FilePath) ? "never saved" : g.FilePath) + ")";
      Console.WriteLine(line); note(line);
      g.IsModified = false;
    }

// Swept from Idle (which keeps firing while a sheet is up) and from a timer
// (for a modal session, if Idle stops). Each button is pressed once; each
// dialog with no discard button is logged once and left open.
var pressed = new List<object>();
var reported = new HashSet<string>();
var clock = System.Diagnostics.Stopwatch.StartNew();
var busy = false;
Action sweep = () =>
{
  if (busy || clock.ElapsedMilliseconds < 200) return;
  busy = true; clock.Restart();
  try
  {
    foreach (var d in dialogs())
    {
      var b = discard.Select(t => d.Buttons.FirstOrDefault(x => same(title(x), t))).FirstOrDefault(x => x != null)
        ?? discardIn.Where(r => d.Where.EndsWith("\"" + r.Item1 + "\""))
             .Select(r => d.Buttons.FirstOrDefault(x => same(title(x), r.Item2))).FirstOrDefault(x => x != null);
      if (b == null) { if (reported.Add(describe(d))) note(describe(d) + " -> no discard button, left open"); continue; }
      if (pressed.Any(x => ReferenceEquals(x, b))) continue;
      pressed.Add(b);
      note(describe(d) + " -> pressed \"" + title(b) + "\"");
      press(b);
    }
  }
  catch (Exception ex) { note("sweep failed: " + ex.GetBaseException().Message); }
  finally { busy = false; }
};
Rhino.RhinoApp.Idle += (s, e) => sweep();
var timer = new System.Threading.Timer(_ => Rhino.RhinoApp.InvokeOnUiThread(sweep), null, 250, 250);
AppDomain.CurrentDomain.SetData("rhino-gh-kit.quit-timer", timer); // root it past this payload

// Clearing and quitting share one Idle tick, so nothing re-dirties in between.
// _Exit names a live document: RunScript against one that has closed crashes
// Rhino (EXC_BAD_ACCESS in CRhinoDoc::GetMRDocument).
var cleared = Enum.Parse(mac.GetType("AppKit.NSDocumentChangeType"), "Cleared");
EventHandler exit = null;
exit = (s, e) =>
{
  Rhino.RhinoApp.Idle -= exit;
  var app = appType.GetProperty("SharedApplication").GetValue(null);
  foreach (var w in (IEnumerable)get(app, "DangerousWindows"))
  {
    var nsdoc = get(get(w, "WindowController"), "Document");
    if (nsdoc == null || get(nsdoc, "FileUrl") != null || !(bool)get(nsdoc, "IsDocumentEdited")) continue;
    nsdoc.GetType().GetMethods().First(m => m.Name == "UpdateChangeCount" && m.GetParameters().Length == 1)
      .Invoke(nsdoc, new[] { cleared });
    note("cleared never-saved \"" + get(w, "Title") + "\"");
  }
  foreach (var d in Rhino.RhinoDoc.OpenDocuments()) if (string.IsNullOrEmpty(d.Path)) d.Modified = false;
  // Again here: a solve since the payload ran can re-dirty a Grasshopper document.
  if (server != null) foreach (Grasshopper.Kernel.GH_Document g in server) g.IsModified = false;
  var live = Rhino.RhinoDoc.OpenDocuments().FirstOrDefault();
  if (live == null) { note("no open document to run _Exit against"); return; }
  note("_Exit");
  Rhino.RhinoApp.RunScript(live.RuntimeSerialNumber, "_Exit", false);
};
Rhino.RhinoApp.Idle += exit;
Console.WriteLine("quitting on the next Idle; log: " + log);
