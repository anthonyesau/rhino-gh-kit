// gh-script-kit template — copied verbatim into build/gen/ by tooling/gh_codegen.py.
//
// The base class for every generated Comp_<Slug>. It owns the one script
// instance, keeps its ambient properties fresh across solves, and resolves the
// icon from an embedded resource. Everything component-specific — identity,
// ComponentGuid, exposure, param registration, the RunScript call — is emitted
// by the generator into the derived class.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

using Grasshopper.Kernel;

namespace GHScriptKit
{
  /// Implemented by a generated host whose header declared one or more `marker:`
  /// tags. On canvas those tags are discovered by scanning a sibling's script
  /// `Text`; a compiled component has no Text, so it advertises them here instead.
  public interface IScriptMarkers
  {
    string[] Markers { get; }
  }

  public abstract class ScriptComponentBase : GH_Component
  {
    protected readonly ScriptBase _script;

    protected ScriptComponentBase(ScriptBase script,
                                  string name, string nickname, string description,
                                  string category, string subcategory)
      : base(name, nickname, description, category, subcategory)
    {
      // One instance per component, created here and kept for the component's
      // lifetime: several scripts carry state (caches, _paramsInitialized)
      // across solves and would misbehave if re-created per solve.
      _script = script;
      if (_script != null) _script.Component = this;

      // Params exist by now: GH_Component's constructor calls PostConstructor(),
      // which runs RegisterInputParams/RegisterOutputParams, before this body.
      _identity = Snapshot(this);
      _inputIdentity = SnapshotAll(Params.Input);
      _outputIdentity = SnapshotAll(Params.Output);
    }

    /// Resource name of the 24x24 PNG for this component, or null for no icon.
    protected virtual string IconResourceName => null;

    /// Light-ink variant used when Rhino is in dark mode, or null if the project
    /// ships no `<stem>-dark.svg` for this component.
    protected virtual string IconResourceNameDark => null;

    protected override void BeforeSolveInstance()
    {
      base.BeforeSolveInstance();
      if (_script == null) return;
      _script.GrasshopperDocument = OnPingDocument();
      _script.RhinoDocument = Rhino.RhinoDoc.ActiveDoc;
      _script.ClearPrinted();
    }

    #region Identity refresh on document load

    // Why this exists: GH_InstanceDescription.Write puts Name, NickName and
    // Description into the .gh for the component AND for every param, and its
    // Read pushes them straight back onto the live object. The constructor has
    // already stamped this build's wording by then, so Read overwrites it with
    // whatever the document was last saved against -- an instance placed on a
    // canvas a year ago keeps that year-old tooltip through any number of
    // assembly updates, and re-saving only writes the stale text back out.
    // Freshly placed instances look correct because nothing deserializes over
    // them. (Read against Rhino 8's Grasshopper.dll: for a component that is not
    // IGH_VariableParameterComponent, GH_ComponentParamServer.Read reuses the
    // params the constructor made and calls Read on each, matched by index --
    // and already re-applies each param's Access afterwards for exactly this
    // reason. Restoring identity the same way follows GH's own precedent.)
    //
    // So: snapshot the assembly's wording at construction, before any archive is
    // read, and re-stamp it afterwards. Name and Description are restored
    // unconditionally -- GH offers no UI to edit either, so a difference can
    // only be staleness. NickName is user-editable (F2 on a component, rename on
    // a param), so it is only refreshed when the archive proves the user never
    // touched it: Write records the nickname this build stamped alongside the
    // live one, and Read refreshes only when the two still agree. Documents
    // saved before this shipped carry no such record, so their nicknames are
    // left alone.
    //
    // With one exception, and it is the one that bites. GH matches saved params
    // to live params BY INDEX, so a build that REMOVED a param shifts every
    // label after it: drop the first two of four outputs and the two survivors
    // read back wearing the removed params' nicknames. (Script Forge 0.4.0-beta
    // did exactly that, and its Success/Log drew as ComponentId/Objects.) A
    // nickname arriving through a shifted mapping is not evidence of anything,
    // let alone of a user's rename -- so when the archive's param COUNT differs
    // from the live one, the stamp check is skipped and the build's own
    // nicknames are restored outright. Name and Description need no such rule:
    // they are restored unconditionally already.
    //
    // Save the shifted state once and it becomes permanent -- the next Write
    // records this build's stamp beside the wrong nickname, and every later
    // Read then sees a legitimate-looking rename. Hence the count check, which
    // catches it on the FIRST load, before anything is written back.

    sealed class Identity
    {
      internal string Name;
      internal string NickName;
      internal string Description;
    }

    readonly Identity _identity;
    readonly Identity[] _inputIdentity;
    readonly Identity[] _outputIdentity;

    const string StampedSelf = "ScriptKitStampedNickName";
    const string StampedInput = "ScriptKitStampedInputNickName";
    const string StampedOutput = "ScriptKitStampedOutputNickName";

    static Identity Snapshot(IGH_InstanceDescription obj)
    {
      return new Identity { Name = obj.Name, NickName = obj.NickName, Description = obj.Description };
    }

    static Identity[] SnapshotAll(IList<IGH_Param> @params)
    {
      Identity[] snapshot = new Identity[@params.Count];
      for (int i = 0; i < @params.Count; i++) snapshot[i] = Snapshot(@params[i]);
      return snapshot;
    }

    public override bool Write(GH_IO.Serialization.GH_IWriter writer)
    {
      bool ok = base.Write(writer);
      try
      {
        writer.SetString(StampedSelf, _identity.NickName ?? string.Empty);
        WriteStamped(writer, StampedInput, _inputIdentity);
        WriteStamped(writer, StampedOutput, _outputIdentity);
      }
      catch { /* the record is an optimization; never fail a save over it */ }
      return ok;
    }

    static void WriteStamped(GH_IO.Serialization.GH_IWriter writer, string key, Identity[] snapshot)
    {
      for (int i = 0; i < snapshot.Length; i++)
        writer.SetString(key, i, snapshot[i].NickName ?? string.Empty);
    }

    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
      bool ok = base.Read(reader);
      try
      {
        Name = _identity.Name;
        Description = _identity.Description;

        string stamped = null;
        if (reader.TryGetString(StampedSelf, ref stamped) && string.Equals(NickName, stamped, StringComparison.Ordinal))
          NickName = _identity.NickName;

        RestoreParams(reader, StampedInput, Params.Input, _inputIdentity,
                      SavedParamCount(reader, "param_input"));
        RestoreParams(reader, StampedOutput, Params.Output, _outputIdentity,
                      SavedParamCount(reader, "param_output"));
      }
      catch { /* a document that loads with stale wording beats one that fails to load */ }
      return ok;
    }

    // How many params of one side the archive actually holds. GH writes them as
    // `param_input` / `param_output` chunks with a running index, so probing
    // upward until one is missing is the count. Cheap: it runs twice per
    // component load and stops at the first gap.
    static int SavedParamCount(GH_IO.Serialization.GH_IReader reader, string chunkName)
    {
      int n = 0;
      while (reader.ChunkExists(chunkName, n)) n++;
      return n;
    }

    static void RestoreParams(GH_IO.Serialization.GH_IReader reader, string key,
                              IList<IGH_Param> @params, Identity[] snapshot, int savedCount)
    {
      // Index-matched, the same way GH read them back. A count mismatch means
      // this is not the param list the snapshot describes, so leave it be.
      if (@params.Count != snapshot.Length) return;

      // The archive described a different number of params than this build has,
      // so every label GH restored may belong to a different slot. Nothing read
      // out of it can testify to a user's intent -- take the build's wording.
      bool shifted = savedCount != @params.Count;

      for (int i = 0; i < @params.Count; i++)
      {
        IGH_Param param = @params[i];
        param.Name = snapshot[i].Name;
        param.Description = snapshot[i].Description;

        if (shifted) { param.NickName = snapshot[i].NickName; continue; }

        string stamped = null;
        if (reader.TryGetString(key, i, ref stamped) && string.Equals(param.NickName, stamped, StringComparison.Ordinal))
          param.NickName = snapshot[i].NickName;
      }
    }

    #endregion

    #region Icon

    static readonly Dictionary<string, Bitmap> s_icons = new Dictionary<string, Bitmap>();

    protected override Bitmap Icon
    {
      get
      {
        // Dark mode gets the light-ink variant when the project ships one. GH
        // caches whatever Icon returns, so a theme flip mid-session keeps the
        // old bitmap until GH_Component.DestroyIconCache runs — same as every
        // other plugin, and not worth fighting.
        string resource = IconResourceName;
        if (Rhino.Runtime.HostUtils.RunningInDarkMode && !string.IsNullOrEmpty(IconResourceNameDark))
          resource = IconResourceNameDark;
        if (string.IsNullOrEmpty(resource)) return null;

        lock (s_icons)
        {
          Bitmap cached;
          if (s_icons.TryGetValue(resource, out cached)) return cached;

          Bitmap loaded = null;
          try
          {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
              if (s != null) loaded = new Bitmap(s);
          }
          catch { /* a missing or corrupt icon must not stop the component loading */ }

          s_icons[resource] = loaded;   // cache the null too, so we try only once
          return loaded;
        }
      }
    }

    #endregion
  }
}
