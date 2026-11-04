# retopology

Builds an all-quad mesh from a network of edge curves: you draw or extract the edge flow, and it finds the quads.

| File | Component |
| :-- | :-- |
| `MeshFromChains.cs` | Mesh From Chains |

**Inputs** · `Edges` (list of curves) · `CollapseTolerance` · `ExtensionTolerance`  
**Outputs** · `OutputMesh` · `Chains` · `MissingFaces` · `NakedVertices` · `InteriorVertices` · `DebugInfo`  
**Needs** · Rhino 8 · no plugins

## How it works

1. Reads the edges and builds the vertex network, sorting vertices into interior and naked.
2. Merges clusters of vertices that sit within `CollapseTolerance` into one.
3. Finds every quad A-B-C-D in the network, including the ones touching the boundary. No triangles, no pentagons.
4. Draws the boundary faces it could not close as polylines in `MissingFaces`, so you can see where the edge network needs another curve.

---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence  
Written with AI assistance; I designed the logic and tested it.
