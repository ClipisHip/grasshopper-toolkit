# gear-generator

Involute spur gears and the racks that mesh with them, as closed planar curves ready to extrude, cut or print.

<!-- Add a picture of a gear and rack, then delete these two comment lines:
![gear and rack](media/gear-generator.png)
-->

| File | Component | Makes |
| :-- | :-- | :-- |
| `InvoluteGearProfile.cs` | Involute Gear | one gear profile, plus its tip, pitch, base and root circles |
| `InvoluteRackProfile.cs` | Involute Rack | the mating rack, and the plane that places the gear on it |

**Needs** · Rhino 8 · no plugins

## Gear

Inputs, all Item access:

| Input | Type | What it is |
| :-- | :-- | :-- |
| `Module` | double | normal module, e.g. 2.0 |
| `Pressure` | double | pressure angle in degrees, e.g. 20 |
| `Backlash` | double | backlash in %, e.g. 2 (0 = none) |
| `Teeth` | int | number of teeth, e.g. 24 |
| `Shift` | double | profile shift coefficient x, e.g. 0 |
| `Addendum` | double | ha* coefficient (0 → 1.00) |
| `Dedendum` | double | hf* coefficient (0 → 1.25) |
| `RootFillet` | double | cutter tip radius / m (0 → 0.38) |
| `Samples` | int | points per flank (0 → 48) |
| `Pln` | Plane | base plane |

Outputs: `Gear` (closed planar curve) · `Circles` (tip, pitch, base, root) · `Info` (report and warnings).

## Rack

Inputs, all Item access: `Module` and `Pressure` (must match the gear) · `Backlash` (in %, taken out of the rack teeth) · `RackTeeth` (how many to draw) · `GearTeeth` (teeth on the mating gear, which sets the axis height).

Outputs: `Rack` (closed planar curve) · `GearPlane` (wire it straight into the gear's `Pln`) · `Info`.

## How it works

- The root fillet is the true trochoid traced by the tip round of a standard basic-rack cutter, so it is tangent to the involute flank and gives the correct undercut on low tooth counts.
- Backlash thins each tooth by (Backlash / 100) · πm / 2 at the pitch circle, so two identical gears mesh with a total circumferential backlash of (Backlash / 100) · πm. With a rack the two add up: put it all on one part or split it, the pair stays conjugate either way.
- The rack's datum line lies on the base plane's X axis with the teeth pointing +Y and one tooth centred at x = 0. The gear's axis sits at (0, m·z/2 + x·m), and `GearPlane` is already turned 180° so a tooth space faces the rack tooth at x = 0.

---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence  
Written with AI assistance; I designed the logic and tested it.
