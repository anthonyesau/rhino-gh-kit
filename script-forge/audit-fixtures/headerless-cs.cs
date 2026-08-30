using System;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(object x, object y, out object a)
  {
    a = "headerless-ok";
  }
}
