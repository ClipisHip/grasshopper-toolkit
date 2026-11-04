# orient-to-plane

Moves any geometry from World XY onto a target plane in one step, and leaves the originals where they were.

| File | Component |
| :-- | :-- |
| `OrientExpress.cs` | Orient Express |

**Inputs** · `brepToOrient` (List access, any geometry) · `plane`  
**Output** · `orientedBrep`  
**Needs** · Rhino 8 · no plugins

It applies one plane-to-plane transform to a copy of each object, so a part modelled flat on XY lands on any face, frame or cut plane.

---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence  
Written with AI assistance; I designed the logic and tested it.
