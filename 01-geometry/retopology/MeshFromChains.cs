// -----------------------------------------------------------------------------
// retopology · Mesh From Chains                             grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// V27 - QUADS ONLY
/// - No triangles, no pentagons
/// - Debug output for boundary face detection
/// </summary>

public class Script_Instance : GH_ScriptInstance
{
    private void RunScript(
        List<Curve> Edges,
        double CollapseTolerance,
        double ExtensionTolerance,
        ref object OutputMesh,
        ref object Chains,
        ref object MissingFaces,
        ref object NakedVertices,
        ref object InteriorVertices,
        ref object DebugInfo)
    {
        List<string> debug = new List<string>();
        
        if (Edges == null || Edges.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No edges provided");
            return;
        }
        
        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
        
        if (CollapseTolerance <= 0) CollapseTolerance = tolerance * 10;
        if (ExtensionTolerance <= 0) ExtensionTolerance = tolerance * 100;
        
        // ============================================================
        // STEP 1: Parse edges
        // ============================================================
        
        Dictionary<string, Point3d> vertices = new Dictionary<string, Point3d>();
        Dictionary<string, List<int>> vertexToEdges = new Dictionary<string, List<int>>();
        List<EdgeData> allEdges = new List<EdgeData>();
        
        foreach (var curve in Edges)
        {
            if (curve == null) continue;
            
            Point3d p1 = curve.PointAtStart;
            Point3d p2 = curve.PointAtEnd;
            
            if (p1.DistanceTo(p2) < tolerance) continue;
            
            string k1 = PtKey(p1, tolerance);
            string k2 = PtKey(p2, tolerance);
            
            bool isDup = allEdges.Any(e => 
                (e.K1 == k1 && e.K2 == k2) || (e.K1 == k2 && e.K2 == k1));
            if (isDup) continue;
            
            if (!vertices.ContainsKey(k1))
            {
                vertices[k1] = p1;
                vertexToEdges[k1] = new List<int>();
            }
            if (!vertices.ContainsKey(k2))
            {
                vertices[k2] = p2;
                vertexToEdges[k2] = new List<int>();
            }
            
            int idx = allEdges.Count;
            allEdges.Add(new EdgeData { Index = idx, K1 = k1, K2 = k2, Used = false });
            
            vertexToEdges[k1].Add(idx);
            vertexToEdges[k2].Add(idx);
        }
        
        debug.Add(string.Format("Edges: {0}, Vertices: {1}", allEdges.Count, vertices.Count));
        
        // ============================================================
        // STEP 2: Build adjacency and classify vertices
        // ============================================================
        
        Dictionary<string, HashSet<string>> adj = new Dictionary<string, HashSet<string>>();
        foreach (var e in allEdges)
        {
            if (!adj.ContainsKey(e.K1)) adj[e.K1] = new HashSet<string>();
            if (!adj.ContainsKey(e.K2)) adj[e.K2] = new HashSet<string>();
            adj[e.K1].Add(e.K2);
            adj[e.K2].Add(e.K1);
        }
        
        HashSet<string> boundaryKeys = new HashSet<string>();
        List<Point3d> nakedPts = new List<Point3d>();
        List<Point3d> interiorPts = new List<Point3d>();
        
        foreach (var kvp in vertexToEdges)
        {
            if (kvp.Value.Count == 1)
            {
                boundaryKeys.Add(kvp.Key);
                nakedPts.Add(vertices[kvp.Key]);
            }
            else
            {
                interiorPts.Add(vertices[kvp.Key]);
            }
        }
        
        debug.Add(string.Format("Original boundary: {0}", boundaryKeys.Count));
        
        // ============================================================
        // STEP 3: COLLAPSE clusters
        // ============================================================
        
        Dictionary<string, string> collapsedTo = new Dictionary<string, string>();
        int clustersMerged = 0;
        
        List<string> boundaryList = boundaryKeys.ToList();
        HashSet<string> processed = new HashSet<string>();
        
        for (int i = 0; i < boundaryList.Count; i++)
        {
            string b1 = boundaryList[i];
            if (processed.Contains(b1) || collapsedTo.ContainsKey(b1)) continue;
            
            List<string> cluster = new List<string> { b1 };
            Point3d p1 = vertices[b1];
            
            for (int j = i + 1; j < boundaryList.Count; j++)
            {
                string b2 = boundaryList[j];
                if (processed.Contains(b2) || collapsedTo.ContainsKey(b2)) continue;
                
                if (p1.DistanceTo(vertices[b2]) <= CollapseTolerance)
                    cluster.Add(b2);
            }
            
            if (cluster.Count > 1)
            {
                Point3d centroid = Point3d.Origin;
                foreach (string k in cluster) centroid += vertices[k];
                centroid /= cluster.Count;
                
                string survivor = cluster[0];
                vertices[survivor] = centroid;
                
                for (int c = 1; c < cluster.Count; c++)
                {
                    collapsedTo[cluster[c]] = survivor;
                    processed.Add(cluster[c]);
                }
                clustersMerged++;
            }
            processed.Add(b1);
        }
        
        foreach (string collapsed in collapsedTo.Keys)
            boundaryKeys.Remove(collapsed);
        
        debug.Add(string.Format("Collapsed: {0} clusters, {1} vertices", clustersMerged, collapsedTo.Count));
        
        // ============================================================
        // STEP 4: Build mesh - ALL QUADS (interior + boundary)
        // ============================================================
        
        Mesh mesh = new Mesh();
        
        Dictionary<string, int> keyToIdx = new Dictionary<string, int>();
        foreach (var kvp in vertices)
        {
            if (!collapsedTo.ContainsKey(kvp.Key))
                keyToIdx[kvp.Key] = mesh.Vertices.Add(kvp.Value);
        }
        
        HashSet<string> addedFaces = new HashSet<string>();
        int quadCount = 0;
        
        // Find ALL quads - including those with boundary vertices
        foreach (string kA in adj.Keys)
        {
            if (collapsedTo.ContainsKey(kA)) continue;
            if (!keyToIdx.ContainsKey(kA)) continue;
            
            var neighborsA = adj[kA]
                .Where(k => !collapsedTo.ContainsKey(k) && keyToIdx.ContainsKey(k))
                .ToList();
            
            for (int i = 0; i < neighborsA.Count; i++)
            {
                string kB = neighborsA[i];
                if (!adj.ContainsKey(kB)) continue;
                
                for (int j = i + 1; j < neighborsA.Count; j++)
                {
                    string kD = neighborsA[j];
                    if (!adj.ContainsKey(kD)) continue;
                    
                    // For a quad: B and D should NOT be directly connected
                    if (adj[kB].Contains(kD)) continue;
                    
                    // Find C: connected to both B and D, but NOT to A
                    foreach (string kC in adj[kB])
                    {
                        if (collapsedTo.ContainsKey(kC)) continue;
                        if (!keyToIdx.ContainsKey(kC)) continue;
                        if (kC == kA || kC == kD) continue;
                        if (adj[kA].Contains(kC)) continue; // C should not connect to A
                        
                        if (adj[kD].Contains(kC))
                        {
                            // Found quad A-B-C-D
                            var keys = new List<string> { kA, kB, kC, kD };
                            keys.Sort();
                            string fid = string.Join("|", keys);
                            
                            if (!addedFaces.Contains(fid))
                            {
                                addedFaces.Add(fid);
                                mesh.Faces.AddFace(keyToIdx[kA], keyToIdx[kB], keyToIdx[kC], keyToIdx[kD]);
                                quadCount++;
                            }
                        }
                    }
                }
            }
        }
        
        debug.Add(string.Format("Quads found: {0}", quadCount));
        
        // ============================================================
        // STEP 5: Find MISSING boundary faces (output as polylines)
        // ============================================================
        
        List<Polyline> missingFaces = new List<Polyline>();
        
        // For each boundary vertex, check if it's part of a face
        HashSet<string> boundaryInFace = new HashSet<string>();
        
        foreach (var faceId in addedFaces)
        {
            var faceKeys = faceId.Split('|');
            foreach (string k in faceKeys)
            {
                if (boundaryKeys.Contains(k))
                    boundaryInFace.Add(k);
            }
        }
        
        int boundaryWithoutFace = 0;
        foreach (string bKey in boundaryKeys)
        {
            if (!boundaryInFace.Contains(bKey))
            {
                boundaryWithoutFace++;
                
                // Try to find what face this should belong to
                if (!adj.ContainsKey(bKey)) continue;
                
                string intKey = adj[bKey].FirstOrDefault(k => !boundaryKeys.Contains(k) && !collapsedTo.ContainsKey(k));
                if (intKey == null) continue;
                
                // Get all neighbors of the interior vertex
                var intNeighbors = adj[intKey]
                    .Where(k => !collapsedTo.ContainsKey(k) && keyToIdx.ContainsKey(k))
                    .ToList();
                
                // Create a polyline showing this potential face area
                List<Point3d> facePts = new List<Point3d>();
                facePts.Add(vertices[bKey]);
                facePts.Add(vertices[intKey]);
                
                // Add neighbors of interior (sorted by angle)
                Point3d center = vertices[intKey];
                var sortedNeighbors = intNeighbors
                    .Where(k => k != bKey)
                    .Select(k => new { Key = k, Pt = vertices[k] })
                    .OrderBy(x => Math.Atan2(x.Pt.Y - center.Y, x.Pt.X - center.X))
                    .ToList();
                
                foreach (var n in sortedNeighbors.Take(3))
                    facePts.Add(n.Pt);
                
                if (facePts.Count >= 3)
                {
                    facePts.Add(facePts[0]); // Close
                    missingFaces.Add(new Polyline(facePts));
                }
            }
        }
        
        debug.Add(string.Format("Boundary vertices without face: {0}", boundaryWithoutFace));
        debug.Add(string.Format("Missing face outlines: {0}", missingFaces.Count));
        
        // ============================================================
        // STEP 6: Build chains
        // ============================================================
        
        foreach (var e in allEdges) e.Used = false;
        
        List<List<Point3d>> allChains = new List<List<Point3d>>();
        
        foreach (string startKey in boundaryKeys)
        {
            if (collapsedTo.ContainsKey(startKey)) continue;
            if (!vertexToEdges.ContainsKey(startKey)) continue;
            
            var edgeIndices = vertexToEdges[startKey].Where(i => !allEdges[i].Used).ToList();
            
            foreach (int startEdgeIdx in edgeIndices)
            {
                if (allEdges[startEdgeIdx].Used) continue;
                
                var chain = BuildChain(startEdgeIdx, startKey, allEdges, vertexToEdges, 
                    vertices, boundaryKeys, collapsedTo, tolerance);
                
                if (chain.Count >= 2)
                    allChains.Add(chain);
            }
        }
        
        debug.Add(string.Format("Chains: {0}", allChains.Count));
        
        // ============================================================
        // Finalize
        // ============================================================
        
        mesh.Faces.CullDegenerateFaces();
        mesh.Vertices.CullUnused();
        mesh.Normals.ComputeNormals();
        mesh.UnifyNormals();
        mesh.Normals.ComputeNormals();
        mesh.Compact();
        
        debug.Add(string.Format("Final mesh faces: {0}", mesh.Faces.Count));
        
        // ============================================================
        // OUTPUT
        // ============================================================
        
        DataTree<Polyline> chainTree = new DataTree<Polyline>();
        for (int i = 0; i < allChains.Count; i++)
            chainTree.Add(new Polyline(allChains[i]), new GH_Path(i));
        
        OutputMesh = mesh;
        Chains = chainTree;
        MissingFaces = missingFaces;
        NakedVertices = nakedPts;
        InteriorVertices = interiorPts;
        DebugInfo = string.Join("\n", debug);
        
        foreach (var line in debug)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, line);
    }
    
    private List<Point3d> BuildChain(
        int startEdgeIdx, string startKey, List<EdgeData> allEdges,
        Dictionary<string, List<int>> vertexToEdges, Dictionary<string, Point3d> vertices,
        HashSet<string> boundaryKeys, Dictionary<string, string> collapsedTo, double tolerance)
    {
        List<Point3d> pts = new List<Point3d>();
        
        var edge = allEdges[startEdgeIdx];
        if (edge.Used) return pts;
        
        string actualStart = startKey;
        while (collapsedTo.ContainsKey(actualStart))
            actualStart = collapsedTo[actualStart];
        
        if (!vertices.ContainsKey(actualStart)) return pts;
        
        edge.Used = true;
        pts.Add(vertices[actualStart]);
        
        string otherKey = (edge.K1 == startKey) ? edge.K2 : edge.K1;
        string actualOther = otherKey;
        while (collapsedTo.ContainsKey(actualOther))
            actualOther = collapsedTo[actualOther];
        
        if (!vertices.ContainsKey(actualOther)) return pts;
        
        Vector3d currentDir = vertices[actualOther] - vertices[actualStart];
        currentDir.Unitize();
        
        pts.Add(vertices[actualOther]);
        
        string currentKey = otherKey;
        
        if (boundaryKeys.Contains(actualOther) && !collapsedTo.ContainsKey(actualOther))
            return pts;
        
        int maxIter = allEdges.Count;
        for (int iter = 0; iter < maxIter; iter++)
        {
            if (!vertexToEdges.ContainsKey(currentKey)) break;
            
            int bestIdx = -1;
            double bestAngle = double.MaxValue;
            string bestNextKey = null;
            Vector3d bestNextDir = Vector3d.Zero;
            
            foreach (int edgeIdx in vertexToEdges[currentKey])
            {
                var e = allEdges[edgeIdx];
                if (e.Used) continue;
                
                string nextKey = (e.K1 == currentKey) ? e.K2 : e.K1;
                string actualNext = nextKey;
                while (collapsedTo.ContainsKey(actualNext))
                    actualNext = collapsedTo[actualNext];
                
                if (!vertices.ContainsKey(actualNext)) continue;
                
                string actualCurrent = currentKey;
                while (collapsedTo.ContainsKey(actualCurrent))
                    actualCurrent = collapsedTo[actualCurrent];
                if (!vertices.ContainsKey(actualCurrent)) continue;
                
                Vector3d outDir = vertices[actualNext] - vertices[actualCurrent];
                outDir.Unitize();
                
                double angle = Vector3d.VectorAngle(currentDir, outDir);
                
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    bestIdx = edgeIdx;
                    bestNextKey = nextKey;
                    bestNextDir = outDir;
                }
            }
            
            if (bestIdx < 0 || bestAngle > Math.PI / 2)
                break;
            
            allEdges[bestIdx].Used = true;
            
            string actualBestNext = bestNextKey;
            while (collapsedTo.ContainsKey(actualBestNext))
                actualBestNext = collapsedTo[actualBestNext];
            
            pts.Add(vertices[actualBestNext]);
            
            if (boundaryKeys.Contains(actualBestNext) && !collapsedTo.ContainsKey(actualBestNext))
                break;
            
            currentDir = bestNextDir;
            currentKey = bestNextKey;
        }
        
        return pts;
    }
    
    private string PtKey(Point3d pt, double tol)
    {
        int d = (int)Math.Max(0, -Math.Log10(tol) + 1);
        return string.Format("{0},{1},{2}",
            Math.Round(pt.X, d), Math.Round(pt.Y, d), Math.Round(pt.Z, d));
    }
    
    private class EdgeData
    {
        public int Index;
        public string K1, K2;
        public bool Used;
    }
}
