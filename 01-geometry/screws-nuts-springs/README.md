# screws-nuts-springs

Screws, nuts, tapped holes and compression springs from a size string, each with the hole or pocket to cut for it. Metric (ISO coarse and fine pitch) and Unified inch (UNC / UNF), with clearances you tune per process, from machining to FDM printing.

<!-- Add a picture of a screw in its tapped hole, then delete these two comment lines:
![screw, nut and spring](media/screws-nuts-springs.png)
-->

| File | Component | Makes |
| :-- | :-- | :-- |
| `ScrewGenerator.cs` | Screw Generator | a screw (socket, button, flat, hex, pan, or headless) and its hole prep: clearance, counterbore or countersink |
| `ThreadTap.cs` | Thread Tap | the internal thread: the solid to subtract, a threaded boss and the tap drill |
| `NutGenerator.cs` | Nut Generator | hex, thin, nylon-insert or square nuts and the pocket that captures them, from the top or the side |
| `NutOpts.cs` | Nut Opts | Nut Generator's options string, built from sliders |
| `ScrewOptions.cs` | Screw Options | every size, head, fit, recess and nut type as lists, plus material and process presets |
| `SpringGenerator.cs` | Spring Generator | a helical compression spring, with its rate, force and safe deflection |
| `SpringOptions.cs` | Spring Options | spring materials, end types and preferred wire sizes, and the material's strength at that wire size |

**Needs** · Rhino 8 · no plugins

## How they fit together

- `Size` is a string: `"M5"`, `"M8x1"`, `"1/4-20"`, `"#8-32"`. With no pitch given you get coarse (metric) or UNC (inch).
- Screw Generator's `Spec` output goes straight into Thread Tap and Nut Generator, so size, hand, tolerance and resolution are set once.
- Wire one plane into Screw Generator, Thread Tap and Nut Generator and the screw lands in its own hole, head bearing on the face.
- Screw Options and Spring Options hand back lists: drop a List Item on them and pick instead of typing.

| Component | Inputs | Outputs |
| :-- | :-- | :-- |
| Screw Generator | Size, Head, Length, ThreadLen, Threads, Fit, Tol, Recess, Opts, Pln | Screw, Prep, Spec, Info |
| Thread Tap | Spec, Length, OuterDia, Tol, Chamfer, Pln | Cutter, Nut, Drill, Info |
| Nut Generator | Spec, Type, Entry, Tol, Opts, Pln | Nut, Prep, Info |
| Nut Opts | Sink, Slot, Bore, AF, H, Chamfer | Opts, Info |
| Screw Options | Preset, Filter | Sizes, Heads, Fits, Recesses, Presets, Nuts, Entries, Tol, Fit, Chamfer, ScrewOpts, MinSize, Info |
| Spring Generator | InnerDia, OuterDia, Wire, Length, Coils, Ends, Material, Deflect, Pln | Spring, Helix, Rate, Force, Info |
| Spring Options | Material, Wire | Materials, Ends, Wires, G, Sut, TauAllow, Info |

All inputs are Item access. Each file's header comment explains every input, and the [docs](docs) folder goes further:

- [SPEC](docs/SPEC.md): the shared placement convention and thread profile, so the parts mate
- [OPTIONS](docs/OPTIONS.md): every string the components accept
- [RECOMMENDATIONS](docs/RECOMMENDATIONS.md): what to set for printing or machining, and why
- [SPRINGS](docs/SPRINGS.md): the spring components, and the one thing worth knowing about spring materials

---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence  
Written with AI assistance; I designed the logic and tested it.
