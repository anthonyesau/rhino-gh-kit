# Ring Array — copies one piece of geometry to a ring of points, rotating each
# copy outward. Wire a Polygon into Geometry for a rosette.
#
# The logic and nothing else: no param tooltips, no component name, no icon.
#
# SDK mode, so the RunScript signature below declares the three INPUTS the same way
# the C# file does — pasting it names them, orders them, and selects the GeometryBase
# / float / int type hints. The annotations are ordinary Python type hints and Rhino's
# guide calls them static-analysis-only; that is true of execution, but the editor
# reads them and they become real converters on the params.
#
# The two OUTPUTS are not declarable in Python at any price. `return Points, Arrayed`
# returns values, not names: the component keeps its stock `a` until you build and
# rename the outputs by hand. That one row is the whole gap against C#.
#
# Unwired, this raises — deliberately. An unwired input arrives as None in both Python
# modes (a NameError needs a name with no param behind it), so range(Count) fails with
# 'NoneType' object cannot be interpreted as an integer until Count is wired.
# C# gets 0 for the same input and quietly does nothing, which is the less honest
# failure of the two.

"""Grasshopper Script"""
import math

import Rhino                      # the annotations below resolve against this
import Grasshopper                # GH_ScriptInstance, the SDK-mode base class
import Rhino.Geometry as rg

class MyComponent(Grasshopper.Kernel.GH_ScriptInstance):
    def RunScript(self, Geometry: Rhino.Geometry.GeometryBase, Radius: float, Count: int):
        Points = []
        Arrayed = []

        for i in range(Count):
            t = 2 * math.pi * i / max(Count, 1)
            pt = rg.Point3d(Radius * math.cos(t), Radius * math.sin(t), 0.0)
            Points.append(pt)

            if Geometry is None:
                continue
            frame = rg.Plane(pt, rg.Vector3d.ZAxis)
            frame.Rotate(t, rg.Vector3d.ZAxis, pt)
            copy = Geometry.Duplicate()
            copy.Transform(rg.Transform.PlaneToPlane(rg.Plane.WorldXY, frame))
            Arrayed.append(copy)
        
        return Points, Arrayed
