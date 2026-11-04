# import-geometry

Brings a file straight into Grasshopper: anything Rhino can import (3DM, STEP, DXF, OBJ…). It runs Rhino's Import, keeps the geometry and deletes the imported objects from the Rhino model, so the document stays clean.

| File | Component |
| :-- | :-- |
| `ImportGeometry.cs` | Import Geometry |

**Inputs** · `filepath` (text) · `import` (bool) · `reset` (bool)  
**Output** · `geometry`  
**Needs** · Rhino 8 · no plugins

Put a Button on `import`. `reset` clears what was loaded.

---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence  
Written with AI assistance; I designed the logic and tested it.
