# silhouette-outlines

Flat outlines of solids, seen along a plane's normal: the outer boundary and the holes, as closed curves sorted largest first. For laser and CNC profiles, footprints and shadows.

| File | Component | How it finds the outline |
| :-- | :-- | :-- |
| `ClippingMaster.cs` | Clipping Master | the Brep's own silhouette curves, projected to the plane |
| `SilhouetteCaster.cs` | Silhouette Caster | meshes the Brep first and uses the mesh outline, for better edge detection on complex parts, and closes curves that come out nearly closed |

**Inputs** · `geometry` (List access) · `projPlane` (Item access)  
**Outputs** · `outer` · `inner` · `groupIndex`, as trees with one branch per input object  
**Needs** · Rhino 8 · no plugins

The largest closed curve of each object is always its outer boundary; the curves inside it are the holes.

---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence  
Written with AI assistance; I designed the logic and tested it.
