# slice-to-loft

Four C# Script components that turn a stack of section curves into curves that loft straight, without twist, with fillets you control by number rather than by luck. A fifth, optional component reads the levels off a solid instead of cutting it at heights you pick.

<!-- Add the demo GIF, then delete these two comment lines:
![demo](media/slice-to-loft.gif)
-->

**Inputs** · section curves and their planes, or a Brep through 05 · fillet radii  
**Outputs** · aligned curves for Loft (*Straight sections*) · Rail polylines to check the result  
**Needs** · Rhino 8 · no plugins  
<!-- **Example** · [examples/slice-to-loft-example.gh](examples/slice-to-loft-example.gh) -->

| # | File | Component | Job |
| :-- | :-- | :-- | :-- |
| 01 | `01-SliceSorter.cs` | Slice Sorter | Sorts the sections into a tree of loft series, `{io ; group}`: inner and outer per level, regions chained across levels |
| 02 | `02-Simplify.cs` | Segment Simplifier | Merges collinear lines and co-circular arcs, so each section carries as few points as its shape allows |
| 03 | `03-Fillet.cs` | Kink Filleter | Rounds corners pass by pass with the radii you give it: convex, concave or both |
| 04 | `04-Align.cs` | Loft Aligner | Flips, re-seams and adds points to every curve to match its branch's reference, so the loft can't twist |
| 05 | `05-BrepLevelEdges.cs` | Brep Level Edges | Optional front end: the real edges parallel to a plane, grouped into the levels the solid actually has |

## How to use

Paste each `.cs` file into a **C# Script** component, header included: it documents every parameter right on the component. Then set each input's access mode as in the table under *Parameter set-up*.

## Chain

```
section curves ──► 01 SliceSorter ──► 02 Simplify ──► 03 Fillet ──► 04 Align ──► Loft
                       Crv                S              F   Ref        A
                                                         └──► Ref ──────┘

brep ──► 05 BrepLevelEdges ──┬─ J (flatten) ──► 01.C
                             └─ Pl ───────────► 01.PL
```

Wire 03.F into 04.C **and** 03.Ref into 04.Ref. Then Loft 04.A with *Straight sections*.

Component 05 is an alternative front end: set its `Jn` input to true, flatten its `J` output into 01.C and wire `Pl` into 01.PL. The rest of the chain is unchanged.

## Parameter set-up

**Type hints do not matter.** Every input arrives as `object` and the script normalises it, so leave the hints at their default.

**Access mode does matter.** Right-click each input → Item / List / Tree Access. With Item access on a tree input, Grasshopper runs the script once per curve instead of once per tree, which quietly destroys the branching. The full table is in each file's header; the pattern is:

| Component | Needs |
| :-- | :-- |
| 01 | `C` **List**, `PL` **List**, everything else Item |
| 02 | `C` **Tree** |
| 03 | `C` **Tree**, `R` **List**, everything else Item |
| 04 | `C` **Tree**, `Ref` **Tree** |
| 05 | `B` **List**, everything else Item |

Every component checks its own access modes and prints `*** WRONG ACCESS MODE` at the top of `Info` when one is wrong. **If a component seems to do nothing, read `Info` first.** Every `Tol` input takes 0 to mean "use the document tolerance".

## The tree

Component 01 emits paths of the form `{io ; group}`:

- **io**: 0 = outer boundary, 1 = inner (hole). Inner and outer are decided on each level alone: a curve is inner when an odd number of curves at its own level contain it.
- **group**: a region chained across levels, so `{0;1}` at the bottom is the same feature as `{0;1}` at the top. That is what keeps the loft from crossing itself.

A curve continues an existing branch only when both of these hold:

1. **Lineage.** It sits inside the same region the branch's last curve sits inside. A hole in one boss can never join a branch of holes in another, because their parents are different branches.
2. **Overlap.** Seen along the plane normal, the two regions cover each other by at least `MO` (intersection over union, 0 to 1, default 0.15). A small boss and the main outline can both be centred on the axis, but they barely overlap in area, so they can't be confused.

Each branch therefore holds one curve per level, bottom to top: a ready-made loft series. `Par` gives, per branch, the branch its region sits inside.

## Adding radii on component 03

`R` is a list, tried in order: pass 1 rounds everything it can with `R[0]`, pass 2 picks up what is left with `R[1]`, and so on. `Rem` tells you how many of the corners you asked for are still sharp; while it isn't 0 you can keep adding values.

You can also hit the component's **+** and add inputs named `R1`, `R2`, `R3`… They are read in numeric order and appended after `R`, so there is no signature to edit. Values already applied are never overwritten by a later pass.

`Max` is the last resort: it rounds whatever is still sharp with the largest radius whose tangent run eats at most **half** of each neighbouring segment, and only touches corners no radius has claimed. `Sel` picks which corners to round (0 both, 1 convex only, 2 concave only); `Cvx` and `Ccv` show which corners are which.

## Why the fillets fit

A corner is only accepted if the arc's tangent run fits inside what is *left* of both neighbouring segments. Within a pass the cheapest corners are served first, so a segment shorter than two tangent runs still gets one of its two ends rounded instead of neither.

## Why the loft does not twist

Component 04 does three things per branch, none of which changes a curve's shape:

1. **Flip**: every curve is turned to the reference's direction.
2. **Seam**: the reference's seam point is carried along `P`'s Z axis onto each curve, and the seam is moved there.
3. **Points**: each curve is split until it carries the same number of points as the reference. Splitting a line in two leaves the same line, so nothing moves.

`Rail` is the output to look at: a polyline through point *k* of every level. If no rail crosses or spirals, the loft is clean. If one corkscrews, that branch's regions were probably chained badly upstream, so check component 01.

`Ev` on component 04: false follows the reference curve's own point distribution (better for matching shapes); true spreads the added points evenly between the real kinks (steadier when the levels differ a lot).

## Reading the levels off the solid (component 05)

Component 05 takes a Brep and a reference plane and returns the edges that lie parallel to that plane, grouped into the levels the shape actually has, plus those levels' heights (`H`) and planes (`Pl`).

An edge qualifies when **every tangent along it** sits within `AT` degrees of the plane. That is stricter than checking how much the edge rises and falls: a circle tilted by five degrees barely changes height over its whole circumference, so a height test would call it flat, while the tangent test reports the five degrees. For draft, `AT` = 5 against the **YZ** plane keeps vertical edges drafted up to five degrees, and `Ang` tells you what each one measured.

`Ei` and `Bi` run parallel to `E`: each edge's index in its own brep's edge list (the order Deconstruct Brep uses) and which brep it came from. `LT` sets how far apart two heights can be and still count as one level; raise it if a level arrives split in two.

## Notes

- Component 02's `Pre` runs RhinoCommon's own `Curve.Simplify` first. Leave it on when your sections come from a Brep intersection as NURBS rather than lines and arcs.
- On component 02, `Na` (points after) is the number to drive down. A segment with `Kind` = 2 is freeform: it is what blocks further reduction on that curve.

---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence  
Written with AI assistance; I designed the logic and tested it.
