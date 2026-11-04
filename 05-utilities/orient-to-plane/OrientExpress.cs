// -----------------------------------------------------------------------------
// orient-to-plane · Orient Express                          grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
// Grasshopper Script Instance
#region Usings
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
    #region Notes
    /* 
      Members:
        RhinoDoc RhinoDocument
        GH_Document GrasshopperDocument
        IGH_Component Component
        int Iteration

      Methods (Virtual & overridable):
        Print(string text)
        Print(string format, params object[] args)
        Reflect(object obj)
        Reflect(object obj, string method_name)
    */
    #endregion

    private void RunScript(
		List<GeometryBase> brepToOrient,
		Plane plane,
		ref object orientedBrep)
    {
        //Origin XY Plane Creation
        Plane xy = Plane.WorldXY;
        //XY Plane Deconstruction
        Point3d xyOrigin = xy.Origin;
        Vector3d xAxis = xy.XAxis;
        //Input Plane Deconstruction
        Point3d inPlaneOrigin = plane.Origin;
        Vector3d inPlaneXAxis = plane.XAxis;


        // AI Script

        List<GeometryBase> orientedGeometries = new List<GeometryBase>();
        Transform orientationTransform = Transform.PlaneToPlane(xy, plane);

        foreach (GeometryBase geo in brepToOrient) {
            GeometryBase cloneGeo = geo.DuplicateShallow();
            cloneGeo.Transform(orientationTransform);
            orientedGeometries.Add(cloneGeo);
        }

        orientedBrep = orientedGeometries;
        
    }
}
