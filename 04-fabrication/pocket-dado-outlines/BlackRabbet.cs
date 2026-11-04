// -----------------------------------------------------------------------------
// pocket-dado-outlines · Black Rabbet                       grasshopper-toolkit
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
		List<Brep> Breps,
		Plane RefPlane,
		double ExtensionDist,
		double StockThickness,
		ref object Report,
		ref object Outlines,
		ref object OriginalBoundaries,
		ref object OffsetBoundaries,
		ref object EdgesToOffset,
		ref object EdgesOriginal,
		ref object Surfaces,
		ref object Heights,
		ref object Depths,
		ref object FaceCounts)
    {
// POCKET & DADO OUTLINE GENERATOR - v17
// ======================================
// Better height grouping, improved intersection logic
// 
// INPUTS:
//   Breps (List<Brep>) - Type hint: Brep, Access: List
//   RefPlane (Plane) - Type hint: Plane, Access: Item
//   ExtensionDist (double) - Type hint: double, Access: Item
//   StockThickness (double) - Type hint: double, Access: Item
//
// OUTPUTS:
//   Report, Outlines, OriginalBoundaries, OffsetBoundaries,
//   EdgesToOffset, EdgesOriginal, Surfaces, Heights, Depths, FaceCounts

double _angleTolerance = 0.01;
double _distTolerance = 0.001;
double _heightTolerance = 0.005; // 5 thou for grouping heights
double _collinearTolerance = 0.02;
double _testOffsetZ = 0.05;

// Helper function for 2D line-line intersection
Func<Line, Line, Point3d> LineLineIntersect2D = (line1, line2) => {
  double x1 = line1.From.X, y1 = line1.From.Y;
  double x2 = line1.To.X, y2 = line1.To.Y;
  double x3 = line2.From.X, y3 = line2.From.Y;
  double x4 = line2.To.X, y4 = line2.To.Y;
  
  double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
  
  if (Math.Abs(denom) < 0.0001)
    return Point3d.Unset;
  
  double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
  
  double ix = x1 + t * (x2 - x1);
  double iy = y1 + t * (y2 - y1);
  
  return new Point3d(ix, iy, line1.From.Z);
};

if (Breps == null || Breps.Count == 0)
{
  Report = "Error: No breps provided";
  return;
}

var reportLines = new List<string>();
reportLines.Add("POCKET & DADO OUTLINE GENERATOR v17");
reportLines.Add("===================================");
reportLines.Add("");

Vector3d refNormal = RefPlane.ZAxis;

// ================================================================
// COLLECT ALL HORIZONTAL FACES
// ================================================================
var allFaceData = new List<object[]>();

foreach (var brep in Breps)
{
  if (brep == null || !brep.IsValid) continue;
  
  for (int i = 0; i < brep.Faces.Count; i++)
  {
    var face = brep.Faces[i];
    if (!face.IsPlanar(_distTolerance)) continue;
    
    var cu = face.Domain(0).Mid;
    var cv = face.Domain(1).Mid;
    Vector3d normal = face.NormalAt(cu, cv);
    normal.Unitize();
    
    double dot = Math.Abs(Vector3d.Multiply(normal, refNormal));
    if (dot < 1 - _angleTolerance) continue;
    
    Point3d centerPt = face.PointAt(cu, cv);
    double height = Vector3d.Multiply(centerPt - RefPlane.Origin, RefPlane.ZAxis);
    
    var facePlane = new Plane(RefPlane.Origin + RefPlane.ZAxis * height, RefPlane.XAxis, RefPlane.YAxis);
    allFaceData.Add(new object[] { face, brep, i, height, facePlane });
  }
}

if (allFaceData.Count == 0)
{
  reportLines.Add("No horizontal faces found.");
  Report = string.Join("\n", reportLines);
  return;
}

// ================================================================
// GROUP FACES BY HEIGHT (with tolerance for grouping)
// ================================================================
var heightGroups = new Dictionary<double, List<object[]>>();

foreach (var fd in allFaceData)
{
  double h = (double)fd[3];
  
  // Find existing group within tolerance
  double? matchingHeight = null;
  foreach (var existingHeight in heightGroups.Keys)
  {
    if (Math.Abs(h - existingHeight) < _heightTolerance)
    {
      matchingHeight = existingHeight;
      break;
    }
  }
  
  if (matchingHeight.HasValue)
    heightGroups[matchingHeight.Value].Add(fd);
  else
    heightGroups[h] = new List<object[]> { fd };
}

// Calculate total area per height group
var heightAreas = new Dictionary<double, double>();
foreach (var kvp in heightGroups)
{
  double totalArea = 0;
  foreach (var fd in kvp.Value)
  {
    var face = (BrepFace)fd[0];
    var amp = AreaMassProperties.Compute(face);
    if (amp != null) totalArea += Math.Abs(amp.Area);
  }
  heightAreas[kvp.Key] = totalArea;
}

// ================================================================
// IDENTIFY TOP LEVEL (highest Z or largest area)
// ================================================================
var sortedHeights = heightGroups.Keys.OrderByDescending(h => h).ToList();
double topLevel = sortedHeights[0]; // Default to highest

// If StockThickness provided, try to match it
if (StockThickness > _distTolerance)
{
  foreach (var h in sortedHeights)
  {
    if (Math.Abs(h - StockThickness) < _heightTolerance)
    {
      topLevel = h;
      break;
    }
  }
}

reportLines.Add("Height levels found: " + heightGroups.Count);
foreach (var h in sortedHeights)
{
  string marker = (Math.Abs(h - topLevel) < _heightTolerance) ? " <-- TOP (skipped)" : "";
  reportLines.Add("  Z=" + h.ToString("F4") + "\": " + heightGroups[h].Count + " faces, area=" + heightAreas[h].ToString("F1") + " sq\"" + marker);
}
reportLines.Add("");
reportLines.Add("Top level: " + topLevel.ToString("F4") + "\"");
reportLines.Add("Extension distance: " + ExtensionDist.ToString("F4") + "\"");

// ================================================================
// FILTER: Remove top level faces
// ================================================================
var pocketHeights = new List<double>();
foreach (var h in sortedHeights)
{
  if (Math.Abs(h - topLevel) > _heightTolerance)
    pocketHeights.Add(h);
}

int totalPocketFaces = 0;
foreach (var h in pocketHeights)
  totalPocketFaces += heightGroups[h].Count;

reportLines.Add("Pocket/dado faces: " + totalPocketFaces);
reportLines.Add("");

if (pocketHeights.Count == 0)
{
  reportLines.Add("No pocket/dado faces found below top level.");
  Report = string.Join("\n", reportLines);
  return;
}

// ================================================================
// OUTPUT DATA STRUCTURES
// ================================================================
var outlineTree = new DataTree<Curve>();
var originalBoundaryTree = new DataTree<Curve>();
var offsetBoundaryTree = new DataTree<Curve>();
var edgesToOffsetTree = new DataTree<Curve>();
var edgesOriginalTree = new DataTree<Curve>();
var surfaceTree = new DataTree<Brep>();

var heightList = new List<double>();
var depthList = new List<double>();
var countList = new List<int>();

// ================================================================
// PROCESS EACH HEIGHT LEVEL
// ================================================================
int branchIndex = 0;
foreach (var height in pocketHeights)
{
  var path = new GH_Path(branchIndex);
  var faces = heightGroups[height];
  
  double depthVal = topLevel - height;
  heightList.Add(height);
  depthList.Add(depthVal);
  countList.Add(faces.Count);
  
  reportLines.Add("Level " + branchIndex + ": Z=" + height.ToString("F4") + "\" (depth=" + depthVal.ToString("F4") + "\", " + faces.Count + " faces)");
  
  foreach (var fd in faces)
  {
    BrepFace theFace = (BrepFace)fd[0];
    Brep theBrep = (Brep)fd[1];
    int faceIdx = (int)fd[2];
    double faceHeight = (double)fd[3];
    Plane facePlane = (Plane)fd[4];
    
    var faceSrf = theFace.DuplicateFace(false);
    if (faceSrf != null) surfaceTree.Add(faceSrf, path);
    
    var loops = theFace.Loops;
    if (loops.Count == 0) continue;
    var outerLoop = loops[0];
    
    // ==============================================================
    // STEP 1: Collect edges and determine offset status
    // ==============================================================
    var rawEdges = new List<object[]>();
    
    foreach (var trim in outerLoop.Trims)
    {
      var edge = trim.Edge;
      if (edge == null) continue;
      
      Curve edgeCurve = edge.DuplicateCurve();
      bool isLinear = edgeCurve.IsLinear(_distTolerance);
      
      BrepFace adjacentFace = null;
      var adjIndices = edge.AdjacentFaces();
      foreach (int idx in adjIndices)
      {
        if (idx != faceIdx)
        {
          adjacentFace = theBrep.Faces[idx];
          break;
        }
      }
      
      bool needsOffset = true;
      
      if (adjacentFace != null)
      {
        Point3d edgeMid = edgeCurve.PointAtNormalizedLength(0.5);
        
        var adjCU = adjacentFace.Domain(0).Mid;
        var adjCV = adjacentFace.Domain(1).Mid;
        Vector3d adjNormal = adjacentFace.NormalAt(adjCU, adjCV);
        adjNormal.Unitize();
        double adjDot = Math.Abs(Vector3d.Multiply(adjNormal, refNormal));
        bool adjIsHorizontal = adjDot > 1 - _angleTolerance;
        
        if (adjIsHorizontal)
        {
          Point3d adjCenter = adjacentFace.PointAt(adjCU, adjCV);
          double adjHeight = Vector3d.Multiply(adjCenter - RefPlane.Origin, RefPlane.ZAxis);
          needsOffset = !(adjHeight > faceHeight + _distTolerance);
        }
        else
        {
          Point3d testPoint = edgeMid + RefPlane.ZAxis * _testOffsetZ;
          double uTest, vTest;
          if (adjacentFace.ClosestPoint(testPoint, out uTest, out vTest))
          {
            Point3d ptOnSrf = adjacentFace.PointAt(uTest, vTest);
            double distToSurface = testPoint.DistanceTo(ptOnSrf);
            PointFaceRelation relation = adjacentFace.IsPointOnFace(uTest, vTest);
            
            bool isOnTrimmedFace = (relation == PointFaceRelation.Interior || 
                                    relation == PointFaceRelation.Boundary) &&
                                   distToSurface < _testOffsetZ * 0.5;
            needsOffset = !isOnTrimmedFace;
          }
        }
      }
      
      rawEdges.Add(new object[] { edgeCurve, needsOffset, isLinear });
    }
    
    if (rawEdges.Count < 3) continue;
    
    // ==============================================================
    // STEP 2: Reorder edges to form a continuous loop
    // ==============================================================
    var orderedEdges = new List<object[]>();
    var usedIndices = new HashSet<int>();
    
    orderedEdges.Add(rawEdges[0]);
    usedIndices.Add(0);
    Point3d currentEnd = ((Curve)rawEdges[0][0]).PointAtEnd;
    
    int safetyCounter = 0;
    while (orderedEdges.Count < rawEdges.Count && safetyCounter < rawEdges.Count * 2)
    {
      safetyCounter++;
      bool foundNext = false;
      double bestDist = double.MaxValue;
      int bestIdx = -1;
      bool bestReverse = false;
      
      for (int i = 0; i < rawEdges.Count; i++)
      {
        if (usedIndices.Contains(i)) continue;
        
        Curve crv = (Curve)rawEdges[i][0];
        Point3d start = crv.PointAtStart;
        Point3d end = crv.PointAtEnd;
        
        double distToStart = currentEnd.DistanceTo(start);
        double distToEnd = currentEnd.DistanceTo(end);
        
        if (distToStart < bestDist)
        {
          bestDist = distToStart;
          bestIdx = i;
          bestReverse = false;
        }
        if (distToEnd < bestDist)
        {
          bestDist = distToEnd;
          bestIdx = i;
          bestReverse = true;
        }
      }
      
      if (bestIdx >= 0 && bestDist < _distTolerance * 100)
      {
        Curve crv = (Curve)rawEdges[bestIdx][0];
        if (bestReverse)
        {
          Curve reversed = crv.DuplicateCurve();
          reversed.Reverse();
          orderedEdges.Add(new object[] { reversed, rawEdges[bestIdx][1], rawEdges[bestIdx][2] });
          currentEnd = crv.PointAtStart;
        }
        else
        {
          orderedEdges.Add(rawEdges[bestIdx]);
          currentEnd = crv.PointAtEnd;
        }
        usedIndices.Add(bestIdx);
        foundNext = true;
      }
      
      if (!foundNext) break;
    }
    
    rawEdges = orderedEdges;
    
    // ==============================================================
    // STEP 3: Merge collinear LINEAR edges only
    // ==============================================================
    var mergedEdges = new List<object[]>();
    
    int mergeIdx = 0;
    while (mergeIdx < rawEdges.Count)
    {
      Curve baseCurve = (Curve)rawEdges[mergeIdx][0];
      bool baseNeedsOffset = (bool)rawEdges[mergeIdx][1];
      bool baseIsLinear = (bool)rawEdges[mergeIdx][2];
      
      if (!baseIsLinear)
      {
        mergedEdges.Add(new object[] { baseCurve, baseNeedsOffset, false });
        mergeIdx++;
        continue;
      }
      
      Point3d mergeStart = baseCurve.PointAtStart;
      Point3d mergeEnd = baseCurve.PointAtEnd;
      
      Vector3d baseDir = mergeEnd - mergeStart;
      if (baseDir.Length < _distTolerance)
      {
        mergeIdx++;
        continue;
      }
      baseDir.Unitize();
      
      bool anyHasWallAbove = !baseNeedsOffset;
      
      int checkIdx = mergeIdx + 1;
      while (checkIdx < rawEdges.Count)
      {
        Curve nextCurve = (Curve)rawEdges[checkIdx][0];
        bool nextNeedsOffset = (bool)rawEdges[checkIdx][1];
        bool nextIsLinear = (bool)rawEdges[checkIdx][2];
        
        if (!nextIsLinear) break;
        
        Vector3d nextDir = nextCurve.PointAtEnd - nextCurve.PointAtStart;
        if (nextDir.Length < _distTolerance)
        {
          checkIdx++;
          continue;
        }
        nextDir.Unitize();
        
        double dotProd = Math.Abs(Vector3d.Multiply(baseDir, nextDir));
        if (dotProd < 1 - _collinearTolerance) break;
        
        mergeEnd = nextCurve.PointAtEnd;
        if (!nextNeedsOffset) anyHasWallAbove = true;
        checkIdx++;
      }
      
      bool mergedNeedsOffset = !anyHasWallAbove;
      Curve merged = new LineCurve(mergeStart, mergeEnd);
      mergedEdges.Add(new object[] { merged, mergedNeedsOffset, true });
      mergeIdx = checkIdx;
    }
    
    int edgeCount = mergedEdges.Count;
    if (edgeCount < 3) continue;
    
    // ==============================================================
    // STEP 4: Compute outward perpendicular for each edge
    // ==============================================================
    var originalEdges = new Curve[edgeCount];
    var needsOffsetFlags = new bool[edgeCount];
    var isLinearFlags = new bool[edgeCount];
    var perpDirs = new Vector3d[edgeCount];
    int offsetCount = 0;
    
    double testDist = 0.01;
    
    for (int i = 0; i < edgeCount; i++)
    {
      originalEdges[i] = (Curve)mergedEdges[i][0];
      needsOffsetFlags[i] = (bool)mergedEdges[i][1];
      isLinearFlags[i] = (bool)mergedEdges[i][2];
      
      Point3d mid = originalEdges[i].PointAtNormalizedLength(0.5);
      
      Vector3d tangent = originalEdges[i].TangentAt(originalEdges[i].Domain.Mid);
      tangent.Unitize();
      Vector3d perp1 = Vector3d.CrossProduct(tangent, RefPlane.ZAxis);
      perp1.Unitize();
      Vector3d perp2 = -perp1;
      
      Point3d testPt1 = mid + perp1 * testDist;
      Point3d testPt2 = mid + perp2 * testDist;
      
      double u1, v1, u2, v2;
      theFace.ClosestPoint(testPt1, out u1, out v1);
      theFace.ClosestPoint(testPt2, out u2, out v2);
      
      PointFaceRelation rel1 = theFace.IsPointOnFace(u1, v1);
      PointFaceRelation rel2 = theFace.IsPointOnFace(u2, v2);
      
      if (rel1 == PointFaceRelation.Exterior)
        perpDirs[i] = perp1;
      else if (rel2 == PointFaceRelation.Exterior)
        perpDirs[i] = perp2;
      else
      {
        var faceCenter = theFace.PointAt(theFace.Domain(0).Mid, theFace.Domain(1).Mid);
        Vector3d toCenter = faceCenter - mid;
        toCenter.Z = 0;
        perpDirs[i] = (Vector3d.Multiply(perp1, toCenter) > 0) ? perp2 : perp1;
      }
      
      if (needsOffsetFlags[i])
      {
        edgesToOffsetTree.Add(originalEdges[i], path);
        offsetCount++;
      }
      else
      {
        edgesOriginalTree.Add(originalEdges[i], path);
      }
    }
    
    int curvedCount = 0;
    foreach (var f in isLinearFlags) if (!f) curvedCount++;
    reportLines.Add("  Face: " + edgeCount + " edges (" + curvedCount + " curved), " + offsetCount + " need offset");
    
    // Build original boundary
    var origJoined = Curve.JoinCurves(originalEdges, _distTolerance * 100);
    Curve originalBoundary = null;
    if (origJoined != null && origJoined.Length > 0)
    {
      originalBoundary = origJoined[0];
      if (!originalBoundary.IsClosed && originalBoundary.IsClosable(_distTolerance * 100))
        originalBoundary.MakeClosed(_distTolerance * 100);
      originalBoundaryTree.Add(originalBoundary, path);
    }
    
    // No offsets needed - use original boundary
    if (offsetCount == 0 || originalBoundary == null)
    {
      if (originalBoundary != null)
        outlineTree.Add(originalBoundary, path);
      continue;
    }
    
    // ==============================================================
    // STEP 5: Build working curves (offset or keep original)
    // ==============================================================
    var workingCurves = new Curve[edgeCount];
    
    for (int i = 0; i < edgeCount; i++)
    {
      if (needsOffsetFlags[i])
      {
        if (isLinearFlags[i])
        {
          Point3d p1 = originalEdges[i].PointAtStart + perpDirs[i] * ExtensionDist;
          Point3d p2 = originalEdges[i].PointAtEnd + perpDirs[i] * ExtensionDist;
          workingCurves[i] = new LineCurve(p1, p2);
        }
        else
        {
          var offsetPos = originalEdges[i].Offset(facePlane, ExtensionDist, _distTolerance, CurveOffsetCornerStyle.Sharp);
          var offsetNeg = originalEdges[i].Offset(facePlane, -ExtensionDist, _distTolerance, CurveOffsetCornerStyle.Sharp);
          
          Curve bestOffset = null;
          
          if (offsetPos != null && offsetPos.Length > 0 && offsetNeg != null && offsetNeg.Length > 0)
          {
            var faceCenter = theFace.PointAt(theFace.Domain(0).Mid, theFace.Domain(1).Mid);
            Point3d midPos = offsetPos[0].PointAtNormalizedLength(0.5);
            Point3d midNeg = offsetNeg[0].PointAtNormalizedLength(0.5);
            
            double distPos = midPos.DistanceTo(faceCenter);
            double distNeg = midNeg.DistanceTo(faceCenter);
            
            bestOffset = (distPos > distNeg) ? offsetPos[0] : offsetNeg[0];
          }
          else if (offsetPos != null && offsetPos.Length > 0)
            bestOffset = offsetPos[0];
          else if (offsetNeg != null && offsetNeg.Length > 0)
            bestOffset = offsetNeg[0];
          
          workingCurves[i] = bestOffset ?? originalEdges[i];
        }
        
        offsetBoundaryTree.Add(workingCurves[i], path);
      }
      else
      {
        workingCurves[i] = originalEdges[i];
      }
    }
    
    // ==============================================================
    // STEP 6: Find corner points by intersecting adjacent curves
    // ==============================================================
    var cornerPoints = new Point3d[edgeCount];
    
    for (int i = 0; i < edgeCount; i++)
    {
      int nextIdx = (i + 1) % edgeCount;
      
      Curve currCurve = workingCurves[i];
      Curve nextCurve = workingCurves[nextIdx];
      
      Point3d expectedCorner = currCurve.PointAtEnd;
      Point3d cornerPt = expectedCorner;
      bool foundIntersection = false;
      
      // Method 1: Direct curve-curve intersection
      var ccx = Rhino.Geometry.Intersect.Intersection.CurveCurve(currCurve, nextCurve, _distTolerance, _distTolerance);
      if (ccx != null && ccx.Count > 0)
      {
        double minDist = double.MaxValue;
        foreach (var evt in ccx)
        {
          double d = evt.PointA.DistanceTo(expectedCorner);
          if (d < minDist)
          {
            minDist = d;
            cornerPt = evt.PointA;
            foundIntersection = true;
          }
        }
      }
      
      // Method 2: Extend curves and try again
      if (!foundIntersection)
      {
        double extendAmount = ExtensionDist * 20;
        
        Curve currExt = null;
        Curve nextExt = null;
        
        if (isLinearFlags[i])
        {
          Vector3d dir = currCurve.PointAtEnd - currCurve.PointAtStart;
          dir.Unitize();
          currExt = new LineCurve(currCurve.PointAtStart - dir * extendAmount, currCurve.PointAtEnd + dir * extendAmount);
        }
        else
        {
          currExt = currCurve.Extend(CurveEnd.Both, extendAmount, CurveExtensionStyle.Line);
          if (currExt == null) currExt = currCurve;
        }
        
        if (isLinearFlags[nextIdx])
        {
          Vector3d dir = nextCurve.PointAtEnd - nextCurve.PointAtStart;
          dir.Unitize();
          nextExt = new LineCurve(nextCurve.PointAtStart - dir * extendAmount, nextCurve.PointAtEnd + dir * extendAmount);
        }
        else
        {
          nextExt = nextCurve.Extend(CurveEnd.Both, extendAmount, CurveExtensionStyle.Line);
          if (nextExt == null) nextExt = nextCurve;
        }
        
        var ccxExt = Rhino.Geometry.Intersect.Intersection.CurveCurve(currExt, nextExt, _distTolerance, _distTolerance);
        if (ccxExt != null && ccxExt.Count > 0)
        {
          double minDist = double.MaxValue;
          foreach (var evt in ccxExt)
          {
            double d = evt.PointA.DistanceTo(expectedCorner);
            if (d < minDist)
            {
              minDist = d;
              cornerPt = evt.PointA;
              foundIntersection = true;
            }
          }
        }
        
        // Method 3: Line-line intersection for linear edges
        if (!foundIntersection && isLinearFlags[i] && isLinearFlags[nextIdx])
        {
          Point3d intPt = LineLineIntersect2D(
            new Line(currExt.PointAtStart, currExt.PointAtEnd),
            new Line(nextExt.PointAtStart, nextExt.PointAtEnd));
          
          if (intPt != Point3d.Unset)
          {
            cornerPt = intPt;
            foundIntersection = true;
          }
        }
      }
      
      cornerPoints[i] = cornerPt;
    }
    
    // ==============================================================
    // STEP 7: Build final curves from corner to corner
    // ==============================================================
    var finalCurves = new List<Curve>();
    
    for (int i = 0; i < edgeCount; i++)
    {
      int prevIdx = (i - 1 + edgeCount) % edgeCount;
      
      Point3d startPt = cornerPoints[prevIdx];
      Point3d endPt = cornerPoints[i];
      
      if (startPt.DistanceTo(endPt) < _distTolerance)
        continue;
      
      if (isLinearFlags[i])
      {
        finalCurves.Add(new LineCurve(startPt, endPt));
      }
      else
      {
        Curve workCrv = workingCurves[i];
        
        double tStart, tEnd;
        workCrv.ClosestPoint(startPt, out tStart);
        workCrv.ClosestPoint(endPt, out tEnd);
        
        if (Math.Abs(tEnd - tStart) > _distTolerance)
        {
          double tMin = Math.Min(tStart, tEnd);
          double tMax = Math.Max(tStart, tEnd);
          
          Curve trimmed = workCrv.Trim(tMin, tMax);
          if (trimmed != null && trimmed.GetLength() > _distTolerance)
          {
            if (trimmed.PointAtStart.DistanceTo(startPt) > trimmed.PointAtEnd.DistanceTo(startPt))
              trimmed.Reverse();
            finalCurves.Add(trimmed);
          }
          else
          {
            finalCurves.Add(new LineCurve(startPt, endPt));
          }
        }
        else
        {
          finalCurves.Add(new LineCurve(startPt, endPt));
        }
      }
    }
    
    // ==============================================================
    // STEP 8: Join and close the outline
    // ==============================================================
    if (finalCurves.Count == 0) continue;
    
    var finalJoined = Curve.JoinCurves(finalCurves, ExtensionDist);
    
    if (finalJoined != null && finalJoined.Length > 0)
    {
      Curve finalOutline = finalJoined[0];
      
      if (!finalOutline.IsClosed)
      {
        double gap = finalOutline.PointAtStart.DistanceTo(finalOutline.PointAtEnd);
        
        if (gap < ExtensionDist * 2 && finalOutline.IsClosable(gap + _distTolerance))
        {
          finalOutline.MakeClosed(gap + _distTolerance);
        }
        else if (gap < ExtensionDist * 3)
        {
          var closingLine = new LineCurve(finalOutline.PointAtEnd, finalOutline.PointAtStart);
          var withClosing = Curve.JoinCurves(new Curve[] { finalOutline, closingLine }, _distTolerance * 10);
          if (withClosing != null && withClosing.Length > 0)
            finalOutline = withClosing[0];
        }
        
        if (!finalOutline.IsClosed)
          reportLines.Add("    Note: Gap = " + gap.ToString("F4") + "\"");
      }
      
      outlineTree.Add(finalOutline, path);
    }
    else
    {
      reportLines.Add("    Warning: Could not join " + finalCurves.Count + " curves");
      foreach (var c in finalCurves)
        outlineTree.Add(c, path);
    }
  }
  
  branchIndex++;
}

reportLines.Add("");
reportLines.Add("Processing complete.");
reportLines.Add("Total outlines: " + outlineTree.DataCount);

Report = string.Join("\n", reportLines);
Outlines = outlineTree;
OriginalBoundaries = originalBoundaryTree;
OffsetBoundaries = offsetBoundaryTree;
EdgesToOffset = edgesToOffsetTree;
EdgesOriginal = edgesOriginalTree;
Surfaces = surfaceTree;
Heights = heightList;
Depths = depthList;
FaceCounts = countList;
}
}
    
