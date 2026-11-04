// -----------------------------------------------------------------------------
// silhouette-outlines · Clipping Master                     grasshopper-toolkit
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
		List<GeometryBase> geometry,
		Plane projPlane,
		ref object outer,
		ref object inner,
		ref object groupIndex)
{
  var outerCurves = new DataTree<Curve>();
  var innerCurves = new DataTree<Curve>();
  var indices = new DataTree<int>();
  
  double tol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

  for(int i = 0; i < geometry.Count; i++)
  {
    GH_Path path = new GH_Path(i);
    
    // Get brep
    Brep brep = null;
    if(geometry[i] is Brep)
      brep = geometry[i] as Brep;
    else if(geometry[i] is Extrusion)
      brep = (geometry[i] as Extrusion).ToBrep();
    
    if(brep == null) continue;
    
    // Get silhouette curves (outline when viewed perpendicular to plane)
    Vector3d direction = projPlane.Normal;
    Point3d viewPoint = projPlane.Origin + direction * 1000; // Far point along normal
    
    Curve[] silhouettes = Brep.CreateContourCurves(brep, projPlane);
    
    if(silhouettes == null || silhouettes.Length == 0) continue;
    
    // Project silhouette curves to the plane
    List<Curve> projectedCurves = new List<Curve>();
    for(int j = 0; j < silhouettes.Length; j++)
    {
      Transform xform = Transform.PlanarProjection(projPlane);
      Curve curveCopy = silhouettes[j].DuplicateCurve();
      curveCopy.Transform(xform);
      projectedCurves.Add(curveCopy);
    }
    
    // Join curves if needed
    Curve[] joined = Curve.JoinCurves(projectedCurves, tol);
    
    if(joined.Length == 0) continue;
    
    // Find closed curves
    List<Curve> closedCurves = new List<Curve>();
    for(int j = 0; j < joined.Length; j++)
    {
      if(joined[j].IsClosed)
      {
        closedCurves.Add(joined[j]);
      }
    }
    
    if(closedCurves.Count == 0) continue;
    
    // Sort by area - largest first
    List<Curve> sorted = new List<Curve>(closedCurves);
    sorted.Sort((a, b) => 
    {
      double areaA = Math.Abs(AreaMassProperties.Compute(a).Area);
      double areaB = Math.Abs(AreaMassProperties.Compute(b).Area);
      return areaB.CompareTo(areaA);
    });
    
    // Classify as outer or inner
    List<Curve> outerBounds = new List<Curve>();
    List<Curve> innerBounds = new List<Curve>();
    
    if(sorted.Count == 1)
    {
      outerBounds.Add(sorted[0]);
    }
    else
    {
      outerBounds.Add(sorted[0]); // Largest is always outer
      
      for(int j = 1; j < sorted.Count; j++)
      {
        Point3d testPt = sorted[j].PointAtNormalizedLength(0.5);
        PointContainment pc = sorted[0].Contains(testPt, projPlane, tol);
        
        if(pc == PointContainment.Inside)
          innerBounds.Add(sorted[j]);
        else
          outerBounds.Add(sorted[j]);
      }
    }
    
    // Output
    outerCurves.AddRange(outerBounds, path);
    innerCurves.AddRange(innerBounds, path);
    
    for(int j = 0; j < outerBounds.Count + innerBounds.Count; j++)
    {
      indices.Add(i, path);
    }
  }
  
  outer = outerCurves;
  inner = innerCurves;
  groupIndex = indices;
}
}
