# data-tree-tools

Small helpers for Grasshopper data trees.

| File | Component | Does |
| :-- | :-- | :-- |
| `TreeBranchShifter.cs` | Tree Branches Shifter | moves every branch one place forward, wrapping round: branch *i* goes to path `{(i + 1) % n}` |

**Input** · `T` (Tree access) · **Output** · `A`  
**Needs** · Rhino 8 · no plugins

To shift by more than one place, change the `+ 1` in the code; `- 1` shifts backwards. It's written in Rhino 8's script mode (no class), so paste it as it is.

---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence  
Written with AI assistance; I designed the logic and tested it.
