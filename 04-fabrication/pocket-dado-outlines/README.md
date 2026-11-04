# pocket-dado-outlines

Finds every pocket and dado in a solid part and draws the outlines to machine them, level by level.

| File | Component |
| :-- | :-- |
| `BlackRabbet.cs` | Black Rabbet |

**Inputs** · `Breps` (List access) · `RefPlane` · `ExtensionDist` · `StockThickness`  
**Outputs** · `Report` · `Outlines` · `OriginalBoundaries` · `OffsetBoundaries` · `EdgesToOffset` · `EdgesOriginal` · `Surfaces` · `Heights` · `Depths` · `FaceCounts`  
**Needs** · Rhino 8 · no plugins

## How it works

1. Collects the faces parallel to `RefPlane` and groups them by height.
2. Takes the top level as the stock face, matching `StockThickness` when you give it one, and leaves it out.
3. For each lower level, walks the edges into one continuous loop, pushes the edges flagged in `EdgesToOffset` out by `ExtensionDist` so the cutter clears them, and returns the closed outline with its height and depth.

`Report` lists what it found on every level. Start there when an outline looks wrong.

---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence  
Written with AI assistance; I designed the logic and tested it.
