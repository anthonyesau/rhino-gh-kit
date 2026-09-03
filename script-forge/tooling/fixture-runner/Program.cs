// Reflects into script-forge.cs's private static header parser (ParseHeader /
// WarnDriftAndQuotes) and reports one JSON verdict per file argument, so
// tooling/test_fixtures.py can compare them against gh_meta.py's on the same
// fixture set without either parser knowing the other exists. See that
// module's docstring for what "outcome" means on each side.
//
// Reflection, not a source change, reaches the parser: script-forge.cs is
// canonical and must stay byte-for-byte Forge-pushable (CLAUDE.md, "The one
// rule"), and ParseHeader / WarnDriftAndQuotes are private statics nested
// inside Script_Instance with no public surface to call instead.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

class FixtureRunner
{
    static int Main(string[] args)
    {
        // gh_codegen.py names the generated class Script_<Slug>, derived from
        // the component's header name -- not worth hardcoding and re-deriving
        // here. Every generated script class derives from GHScriptKit.ScriptBase
        // (see build/gen/ScriptBase.cs), and it is the only one in this build
        // (one @component source), so finding it by base type is both correct
        // and immune to the slug's exact spelling.
        var type = typeof(FixtureRunner).Assembly.GetTypes()
            .SingleOrDefault(t => t.BaseType?.FullName == "GHScriptKit.ScriptBase")
            ?? throw new MissingMemberException("no generated Script_<Slug> : GHScriptKit.ScriptBase found -- was build/gen regenerated?");

        var parseHeader = type.GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(type.Name + ".ParseHeader not found -- did it get renamed?");
        var warnMethod = type.GetMethod("WarnDriftAndQuotes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(type.Name + ".WarnDriftAndQuotes not found -- did it get renamed?");
        var headerMetaType = type.GetNestedType("HeaderMeta", BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.Name + ".HeaderMeta not found -- did it get renamed?");

        var results = new List<Dictionary<string, object>>();

        foreach (var path in args)
        {
            var text = File.ReadAllText(path);
            var entry = new Dictionary<string, object> { ["file"] = Path.GetFileName(path) };
            results.Add(entry);

            object meta;
            try
            {
                meta = parseHeader.Invoke(null, new object[] { text });
            }
            catch (TargetInvocationException tie)
            {
                entry["outcome"] = "error";
                entry["error"] = (tie.InnerException ?? tie).Message;
                continue;
            }

            if (meta == null)
            {
                // ParseHeader's own contract: no @component marker at all is not
                // a parse failure, it is a headerless source -- the Forge creates
                // one with stock defaults. gh_meta.py has no such state; every
                // file it is pointed at is required to carry a header.
                entry["outcome"] = "headerless";
                continue;
            }

            entry["outcome"] = "ok";
            entry["name"] = headerMetaType.GetField("Name").GetValue(meta);
            entry["description"] = headerMetaType.GetField("Desc").GetValue(meta);
            var pinnedGuid = (Guid)headerMetaType.GetField("PinnedGuid").GetValue(meta);
            entry["instanceGuid"] = pinnedGuid == Guid.Empty ? null : pinnedGuid.ToString();

            // Every param's stored Access, inputs then outputs. The header
            // parser is what normalizes it, and ApplyDef's GH_ParamAccess
            // ladder reads exactly this string -- so pinning it here pins the
            // access a forged param actually gets, without a live canvas.
            var headerParamType = type.GetNestedType("HeaderParam", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(type.Name + ".HeaderParam not found -- did it get renamed?");
            var accessField = headerParamType.GetField("Access");
            var access = new List<string>();
            foreach (var side in new[] { "Ins", "Outs" })
                foreach (var d in (IEnumerable)headerMetaType.GetField(side).GetValue(meta))
                    access.Add((string)accessField.GetValue(d));
            entry["access"] = access;

            bool isPython = path.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
            var log = (IList)Activator.CreateInstance(typeof(List<string>));
            warnMethod.Invoke(null, new object[] { text, meta, isPython, log });
            entry["warnings"] = log.Cast<string>().ToList();
        }

        Console.WriteLine(JsonSerializer.Serialize(results));
        return 0;
    }
}
