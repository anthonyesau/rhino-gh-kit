using System;
using System.Drawing;
using System.Reflection;

using Grasshopper.Kernel;

namespace ScriptForge
{
  public class ScriptForgeInfo : GH_AssemblyInfo
  {
    public override string Name => "Script Forge";

    public override Bitmap Icon => null;

    public override string Description =>
      "Builds or updates Grasshopper script components from source text or source files. " +
      "Feed it .cs / .py sources carrying an @component header and it forges live C# and " +
      "Python 3 components complete with params, type hints, identity, per-param tooltips " +
      "and icons — one Source branch per script, N components per run.";

    // Pinned, never regenerated: an installed copy upgrades in place only while
    // this id holds. Change it and the next install appears alongside its
    // predecessor as a duplicate rather than replacing it.
    public override Guid Id => new Guid("2bc1a899-2f22-4c51-a0ce-b8b9991dddc5");

    public override string AuthorName => "Anthony Esau";

    public override string AuthorContact => "https://www.anthonyesau.com/";

    // Grasshopper surfaces this one: GH_AssemblyInfo.Version is virtual but its
    // default getter delegates straight here (verified 2026-08-06 by overriding
    // AssemblyVersion alone on a runtime subclass and reading Version back), so
    // overriding AssemblyVersion is sufficient and overriding Version as well
    // would be dead code.
    //
    // Read from the INFORMATIONAL version, not Assembly.GetName().Version. The
    // latter is a numeric a.b.c.d and can never carry a prerelease tag, so it
    // reports a `0.2.0-beta` build as a bare `0.2.0.0` and the beta designation
    // is invisible in Grasshopper. The SDK stamps AssemblyInformationalVersion
    // from <Version> in the csproj, which keeps that the single source of truth
    // (publish.sh already gates it against yak/manifest.yml). .NET 8 appends
    // `+<git sha>` to it by default, hence the trim.
    public override string AssemblyVersion
    {
      get
      {
        var attr = GetType().Assembly
          .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
        if (attr == null || string.IsNullOrEmpty(attr.InformationalVersion))
          return GetType().Assembly.GetName().Version.ToString();

        string v = attr.InformationalVersion;
        int plus = v.IndexOf('+');
        return plus < 0 ? v : v.Substring(0, plus);
      }
    }
  }
}
