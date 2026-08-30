# File naming — style guide

The convention for every tracked path in this repo.

## The hard rule — applies to every file and directory

Use only the **POSIX Portable Filename Character Set** (IEEE Std 1003.1):

```
A–Z  a–z  0–9  .  _  -
```

Plus three constraints that character set alone doesn't cover:

- No leading `-` in any path segment — a file named `-foo` is parsed as a
  command-line *option*; you need `./-foo` or `--` to touch it.
- No trailing `.` or space (Windows silently strips them).
- No Windows reserved basename: `CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`,
  `LPT1`–`LPT9` — reserved with *any* extension, so `nul.txt` is out too.

Note what this permits: **uppercase and underscore both conform.** The choice
between kebab, snake, and Pascal below is convention, not correctness.

## Pick the case by role, not by extension

Ask one question: **does a toolchain read this filename?**

| The file… | Convention | Applies to | Example |
|---|---|---|---|
| is imported as a module | `snake_case` | `tooling/*.py` | `gh_meta.py` |
| declares a type named for the file | `PascalCase` | `src/`, `tooling/templates/*.cs` | `ScriptBase.cs` |
| is a conventional root file | `ALL-CAPS` | repo root only | `README.md`, `LICENSE` |
| is anything else — content | `lowercase-kebab` | everything else | `script-forge.md` |

The first two are the language's own rule, and they apply *because the mechanism
is really there*: a Python module filename becomes an identifier at `import`, and
.NET/Java tooling expects one public type per file named for the file. Where that
mechanism is absent, the rule is decoration — so content gets one flat convention.

**Worked example.** Every C# script component here declares
`public class Script_Instance`, not a type named for its file. So the .NET rule
doesn't apply to them, and they're content: `examples/list-stats.cs`, not
`ListStats.cs`. Likewise no `examples/*.py` is ever imported, so PEP 8 has no
purchase there either.

## The four forms, and their real tradeoffs

| Form | Customary in | Watch out for |
|---|---|---|
| `lowercase-kebab` | web, docs, URLs, CSS, npm, k8s, Rust crates | `-` isn't a word character in most editors, so double-click selects one segment |
| `snake_case` | Python, C, Ruby, SQL | Shift on every separator; runs against the docs/URL convention |
| `PascalCase` | .NET, Java, React components | Case-only collisions on case-insensitive filesystems; acronym bikeshedding (`SciFiX` / `SciFIX`) |
| `ALL-CAPS` | `README`, `LICENSE`, `CHANGELOG`, `Makefile` | Only earns its place at the repo root, where ASCII sort floats it to the top |

## Traps

**Spaces.** Make cannot express a space in a target or prerequisite *at all*.
Unquoted `$file` word-splits in shell. `xargs` without `-0` breaks. Markdown links
need `%20`, which the VSCode extension can't navigate in any encoding. This
project already got bitten: `yak install "Script Forge" <version>` prints usage
and bails, which is why the package is `ScriptForge`.

**Case-only differences.** APFS, HFS+, NTFS and exFAT are case-insensitive but
case-preserving. Two files differing only in case cannot coexist, and
`git mv Foo.md foo.md` fails outright — use a two-step through a temp name or
`git mv -f`. Verify with `git ls-files`, not `ls`.

**Non-ASCII.** Worse than a space, not better. HFS+ stores names decomposed (NFD)
while Linux and Windows use composed (NFC), so one visual name is two byte
sequences; git needs `core.precomposeunicode` to paper over it. Nothing typeable
without copy-paste. This includes the em dash — `foo — bar.md` is two problems.

**Dates and versions.** ISO 8601 `YYYY-MM-DD`, because it sorts chronologically as
text. Semver for versions. Join fields with `-`, never a space:
`0.0.1-2026-07-11`, not `0.0.1 2026-07-11`.

## One name per concept

Where a file has related artifacts, the stem should be identical across all of
them — that turns naming drift into something a script can catch:

```
remap-number.cs          source
icons/remap-number.svg   icon
"name": "Remap Number"   @component header
```

The rule here is `stem == kebab(header["name"])`, enforced by
`tooling/check_filenames.py` over the 32 header-carrying sources in
`gh_meta.all_sources()`'s scope — the repo root and `examples/`. Two scratch
fixtures are exempted by name in that script (`examples/mystery.cs`,
`examples/swap-script-test.cs`); see the comment on `STEM_EXEMPT`.

## Checking

```bash
tooling/check_filenames.py                              # both passes, as run by publish.sh
git ls-files | grep ' '                                 # spaces
git ls-files | LC_ALL=C grep -P '[^\x00-\x7F]'          # non-ASCII
grep -rIn '%20' --exclude-dir=.git .                    # escaped links, the downstream symptom
```

## No exceptions

Every tracked path obeys the rule, and `check_filenames.py` has no exemption list
for it.

Shipped binaries are the case that would otherwise want one — released artifacts
carry names chosen by a packager, not by this repo, and renaming one rewrites
what was published. Attach them to a GitHub Release instead of tracking them:
assets live outside the git object database, so the gate never sees them. See
[publishing.md](publishing.md).
