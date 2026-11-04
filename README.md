# grasshopper-toolkit

C# components for Grasshopper (Rhino 8) from my design and fabrication work: lofting from sections, gears, threaded fasteners and springs, CNC outlines and the small utilities I use in most definitions, plus a first structural analysis example in RFEM 6. Each tool has its own folder with its code or definition and a README.

<!-- Add one strong image of a tool at work, then delete these two comment lines:
![grasshopper-toolkit](01-geometry/slice-to-loft/media/slice-to-loft.gif)
-->

## Tools

| Tool | What it does |
| :-- | :-- |
| [slice-to-loft](01-geometry/slice-to-loft) | Section curves → lofts that don't twist, with fillets set by number |
| [gear-generator](01-geometry/gear-generator) | Involute spur gears and their mating racks, with true trochoid root fillets |
| [screws-nuts-springs](01-geometry/screws-nuts-springs) | Metric and inch screws, nuts, tapped holes and compression springs, with the holes to cut for them |
| [retopology](01-geometry/retopology) | An all-quad mesh from a network of edge curves |
| [rfem6-beam-example](02-analysis/rfem6-beam-example) | A two-span steel beam set up in Grasshopper and sent to RFEM 6, with its loads and combinations |
| [data-tree-tools](03-data/data-tree-tools) | Small helpers for data trees |
| [silhouette-outlines](04-fabrication/silhouette-outlines) | Closed outer and inner outlines of solids seen along a plane, ready to cut |
| [pocket-dado-outlines](04-fabrication/pocket-dado-outlines) | The pockets and dados in a solid part, as outlines grouped by depth, for CNC |
| [orient-to-plane](05-utilities/orient-to-plane) | Moves geometry from World XY onto any plane |
| [import-geometry](05-utilities/import-geometry) | Imports a file straight into Grasshopper without leaving it in the Rhino model |

## How it's organised

- `01-geometry` · making form
- `02-analysis` · structural analysis from the Grasshopper model (RFEM 6)
- `03-data` · data trees, lists, sorting
- `04-fabrication` · outlines and cut geometry
- `05-utilities` · small helpers


## Using a script

1. Drop a **C# Script** component on the canvas (Rhino 8).
2. Double-click it, paste the whole `.cs` file and close the editor.
3. Check the inputs and their access mode (right-click → Item / List / Tree Access) against the tool's README.

The RFEM 6 example is a whole definition rather than a script: open its `.gh` and follow its README.

## Related

- [biesse-gcode-exporter](https://github.com/ClipisHip/biesse-gcode-exporter): Biesse CNC programs straight from Grasshopper

## Licence

MIT: use it, change it, share it, and keep the copyright notice. See [LICENSE](LICENSE).

---

Daniel Andújar · computational designer and digital fabricator, Vancouver · [andujar.design](https://andujar.design)
