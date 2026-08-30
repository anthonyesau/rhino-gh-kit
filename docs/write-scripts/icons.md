# Component icons

Each component's `@component` header declares an `icon:` path, conventionally
into an `icons/` folder at the project root.
Both consumers want a **raster** bitmap, so the SVG is rasterized to a 24×24 PNG
(`sips` on macOS) — by Script Forge before it stamps the icon via
`SetIconOverride` on every forge pass, and by `gh_codegen.py` into the embedded
resources of a compiled build. The canvas icon slot is `Bitmap` only, never SVG;
the SVG (light + optional `-dark` variant) is what gets committed.

## Naming convention

`icons/<kebab-case-of-component-name>.svg`, matching the header `icon:` field —
one SVG per component, resolved relative to the **canvas**, never to the source
file. This repo's own `examples/` keeps its icons in `examples/icons/`, alongside
the `examples/*.cs` / `*.py` they belong to, and the canvas that forges them
(`examples/script-forge-examples.gh`) is saved in that same folder — so the
headers say the bare `icons/<name>.svg` and resolve correctly from there. The
`List Stats` component (`examples/list-stats.cs`) uses `icons/list-stats.svg`.

Forging an `examples/` source from a canvas saved somewhere else — a forge rig at
the repo root, say — therefore comes up one segment short and logs a
`svg not found` warning. Icon stamping is best-effort, so the forge itself still
succeeds; save the canvas inside `examples/` if you need the icon.

Keep the source SVGs square and legible at 24×24. If you add a dark-theme
variant, use the `-dark.svg` suffix (e.g. `script-forge-dark.svg`); the publish
dialog takes light and dark separately.

## Rasterizing SVG → PNG for the canvas

Use **`sips`** — it's built into macOS (zero install) and rasterizes SVG at the
**target resolution** (true vector render, not an upscale), so the output is
crisp.

```bash
# one icon
sips -s format png -z 24 24 icons/list-stats.svg --out icons/list-stats.png

# whole set
for f in icons/*.svg; do
  sips -s format png -z 24 24 "$f" --out "icons/$(basename "$f" .svg).png"
done
```

`-z H W` sets the output pixel size; 24×24 matches the Grasshopper canvas icon
slot (`Internal_Icon_24x24`). Render larger (e.g. `-z 48 48`) if you want extra
crispness for high-DPI displays and are willing to let GH downscale.

Alternatives if you ever need them: `rsvg-convert` (from `librsvg`) or the npm
`@resvg/resvg-js` package, both higher-fidelity on complex SVG but requiring an
install. `qlmanage -t` also works but is soft and adds padding — prefer `sips`.

Generated PNGs are build intermediates — the SVGs are the committed source of
truth, and `.gitignore` covers `icons/*.png` so the rasters stay out of the repo.
