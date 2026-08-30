/* @component
{
  "name":        "Audit Base64 Icon",
  "description": "Icon embedded as base64 PNG, no file on disk.",
  "icon":        "base64:iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAAXNSR0IArs4c6QAAAERlWElmTU0AKgAAAAgAAYdpAAQAAAABAAAAGgAAAAAAA6ABAAMAAAABAAEAAKACAAQAAAABAAAAGKADAAQAAAABAAAAGAAAAADiNXWtAAACYElEQVRIDe2VPWhTURTHm7wXUqMOAZMoBIlTjPlABwfXgptdFBd1sptYg4JrZ5cKlRYcHBTqB+gUJy3oWnQovCSkAXWQgCaBgIjEkI/n7wTu4+bl5SHasXc5953//3zec++bm9tfe9GBRCJx8F/9BPwMk8nkgWg0+jwQCFyAt2RZ1pN0On0iHA5v27Yd8bKF2x2NRucqlcpnwU0vktLhfB1HRzB6itHO2MA028g+unl0G8FgcKD4IuF32+32d13nuc/lctcKhUIzk8kccxPALoLZ+Xz+jhtzf3u2CMOTED+QzSVK3XIbyTcBSoiF4XB4qlqtfvXiiC7oBqTvlP8S/dos52KD45siDcNYFzlrGW4glUo9JPP5crl8Hcx24+qbPv+Ix+O/SWY5FouVW61WTWG6nKhA+g64OBgMriCHOtFrTxJr6Hc46AdM12EvjnMG2Wy2BFHGsURWnxSZVoTRL1DVbVr2VumVxO4s+DY29xnju0qvpFMBBKXbUznhVVpENqv9fv90rVb79heRDKbpI7x4r9fL1Ov1n24bpwIBaMEm4rVpms+QUwMgHH0xzkW+z3Dhbnk5F+6Uk1AotBWJRIpMyFEm473uUN/T++NU+wrdGw57Rcf0/UQFAjQajS4HepltkZad18n6Xs2/ug86pu+nAghIRrsEuUGGm7OeCmiLcFb8brH4mjhkUeiLA3xEf9ME+oJc5YwsWnOI7HfhJdB5Pna09l6z2fwlvnxf006ns8yL+gLeVYK8Q1pcwhgBQmQvt3gJKX6cha7LzX5MgPFz7QB+m//54fj53cfGHfgDrBDrV8XwViEAAAAASUVORK5CYII=",

  "inputs": [
    { "name": "A", "type": "double", "access": "item",
      "description": "In." }
  ],

  "outputs": [
    { "name": "B", "type": "double", "access": "item",
      "description": "Out." }
  ]
}
*/
using System; using Grasshopper.Kernel;
public class Script_Instance : GH_ScriptInstance { private void RunScript(double A, out object B){ B=A; } }
