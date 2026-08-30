/* @component
{
  "name":        "Color Blend",
  "nickname":    "CBlend",
  "description": "Linearly blends two colors and reports the result as a hex string - demonstrates the Color and string type hints.",
  "icon":        "icons/color-blend.svg",
  "category":    "Display",
  "subcategory": "Colour",

  "inputs": [
    { "name": "ColorA", "nickname": "C1", "type": "Color", "access": "item",
      "description": "The color at blend factor 0." },
    { "name": "ColorB", "nickname": "C2", "type": "Color", "access": "item",
      "description": "The color at blend factor 1." },
    { "name": "Blend", "nickname": "t", "type": "double", "access": "item",
      "description": "Blend factor between 0 and 1 (clamped)." }
  ],

  "outputs": [
    { "name": "Mixed", "nickname": "C", "type": "Color", "access": "item",
      "description": "The blended color." },
    { "name": "Hex", "nickname": "H", "type": "string", "access": "item",
      "description": "The blended color as an RGB hex string like #FF8800." }
  ]
}
*/

using System;
using System.Drawing;

using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(Color ColorA, Color ColorB, double Blend, out object Mixed, out object Hex)
  {
    double t = Math.Max(0.0, Math.Min(1.0, Blend));
    Func<int, int, int> mix = (a, b) => (int)Math.Round(a + (b - a) * t);

    var c = Color.FromArgb(
      mix(ColorA.A, ColorB.A),
      mix(ColorA.R, ColorB.R),
      mix(ColorA.G, ColorB.G),
      mix(ColorA.B, ColorB.B));

    Mixed = c;
    Hex = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
  }
}
