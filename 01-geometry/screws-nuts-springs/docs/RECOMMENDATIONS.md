# RECOMMENDATIONS — what to actually set, and why

The numbers here are the `ScrewOptions` presets written out with their reasoning.
They are **starting points that work**, not guarantees: your printer, your
filament and your slicer all move the answer.

---

## 1. Which path are you on?

Decide this first, because the two paths want different things and almost every
mistake comes from following advice meant for the other one.

### A — a real screw, off the shelf

A steel M5 cap screw out of the drawer. **You are not making a thread.** You need
a hole for it to pass through, and something at the far end for it to bite into.

    ScrewGenerator   Size, Head, Length, Fit, Tol, Recess   ->  use PREP
                     Threads 0
    ThreadTap        not in your graph at all

- **`Threads 0`.** `Prep` is built from the clearance table and the head
  dimensions and never looks at the thread, so you get an identical `Prep` and a
  far faster regen. `Screw` still draws, as a plain shank, which is all you need
  to eye up fit and interference.
- **`Fit` + `Tol`, and do not stack them.** `Fit` is the *joint's* positional
  allowance; `Tol` is your *printer's* error. `loose` already contains slop, so
  `loose` plus a big `Tol` pays for it twice. Use `normal` + your measured `Tol`.
- **Any size works.** An off-the-shelf screw does not care what your printer can
  resolve. Nothing in the size floor applies to you.
- **The far end is `NutGenerator`.** A captive nut in a printed pocket, which
  is the strongest thing you can do in plastic — see *Captive nuts* below.

### B — a printed thread

Plastic screw into plastic. Both halves come off the machine.

    ScrewGenerator   ->  Screw          the fastener itself
    ThreadTap        ->  Cutter         subtract for the threaded hole

- **The size floor applies, hard.** See *Tol is absolute, threads are not*. FDM
  does not do small printed threads, and no amount of care changes that.
- **`Chamfer 1`.** Not optional on a printed hole.

### C — a real screw into a printed thread

The common hybrid: a steel M4 going into a printed boss. The screw is exact, so
**the printed hole carries the whole error**.

    ScrewGenerator   ->  Prep     for the part the screw passes THROUGH
    ThreadTap        ->  Cutter   for the part it threads INTO

Settings as B, and the size floor still applies — it is the printed half that
decides, not the steel one. Below the floor, switch that end to a heat-set insert
and you are back on path A.

### Which gets which number

| | `Fit` | `Tol` on Prep | `Tol` on ThreadTap | size floor |
|---|---|---|---|---|
| A — real screw, clearance hole | normal | measured | — | none |
| B — printed thread | — | — | measured | applies |
| C — hybrid | normal | measured | measured | applies |

The stakes are not symmetric, which is why the advice differs: too much `Tol` on
a clearance hole gives you slop you will probably never notice. Too much on a
thread strips it.

### Captive nuts

A real steel nut costs pennies, cannot strip, and does not care what your
printer can resolve. Below M5 it is not just the best answer, it is the only
one — a printed M4 thread has under half its flank left at any sane clearance.

    NutGenerator   Spec <- ScrewGenerator.Spec   Type hex   Entry top
                   Tol 0 (inherits)   Pln <- the far face
        -> subtract Prep. Pocket, bolt bore, done.

**Which entry.** `top` if the face is reachable and points up on the printer —
no overhang, nut drops in. `side` when the nut has to be buried: the slot runs
out to an edge and the nut slides in. `side` + `slot=0` embeds it completely,
for pausing the print — `Info` gives you the pause height.

**Roof and floor are bridges.** A `side` slot needs `sink=` big enough for a
real roof: three layers minimum, 0.6–0.8 mm, more if anything bears on it. A
`top` pocket printed opening-down has a hexagonal bridge for a floor, which
prints better than it sounds but is worth a slow first layer over it.

**Clearance is the same `Tol` as everywhere else** — your printer's error, per
flat. 0.15–0.20 on FDM. Too tight and the nut will not go in; too loose and it
spins in the pocket when you tighten, which is the failure you actually notice.
Err tight and open it with a file.

**Measure the nut.** M10, M12 and M14 exist in two across-flats and the wrong
one is a scrap part — see OPTIONS.md. `af=` in `Opts` takes your caliper
reading and overrides the table.

**Pull the nut into its seat, don't push the part.** A captive nut in plastic
holds because the pocket walls stop it turning, not because the pocket is
tight. Give the bolt enough thread past the nut and let the joint clamp.

---

## 2. What Tol actually is, and how to get your number

**`Tol` is not a design choice. It is a measurement of your machine.**

Model a 5.00 mm hole, print it, measure it: it comes out around 4.80. The plastic
squashes inward, the layers bulge into the bore, the part shrinks as it cools.
Your machine made the hole 0.10 mm smaller **on the radius** than you asked for.

So a thread modelled at nominal physically cannot accept its screw — there is
less space in there than the screw occupies. `Tol` is how much you enlarge the
model to cancel that error out.

    what you model   ->   what the machine makes   ->   what fits
    nominal + Tol         nominal + Tol - error         nominal

`Tol` = your process's hole shrinkage, plus a little so the thread *turns* rather
than merely fits.

**Bigger is not safer.** Every bit of `Tol` comes off the flank, and the flank is
the only thing carrying load (*Tol is absolute*). Too little and it will not assemble; too much
and it strips. You want the SMALLEST number that goes together, not the
comfortable-looking one.

### Getting your number

1. **Measure directly — one print.** A block with a plain hole modelled at
   10.00 mm. Caliper it. Reads 9.78? Radial error 0.11. Start at 0.15: the error
   plus a running fit.
2. **Bracket it — authoritative.** The test pair below. Better than (1), because
   thread flanks do not shrink quite like a plain bore does.

Use (1) to get close and (2) to confirm.

**Then you are done, permanently.** The error is absolute — your printer is off
by the same 0.1 mm whether it is making an M3 or an M20 — so **one number covers
every size you will ever print on that machine in that material.** Measure it
once, write it on the wall, stop thinking about it.

That same fact is why small threads fail. An absolute 0.1 mm is nothing against
an M10's 0.8 mm thread depth and most of an M3's 0.27.

**Machining is the easy case: `Tol = 0`.** A mill or a tap makes the hole the
size you asked for, and a tap carries its own allowance. Adding your own on top
just gives you a loose thread.

The presets are estimates of a typical machine's error per material. They are for
before you have measured — not a substitute for measuring.

---

## 3. Do this once, before you trust any of it

Print a **test pair**, not the real part.

    ScrewGenerator   Size M5   Head socket   Length 12   Tol <preset>
    ThreadTap        Spec <- from the screw   Length 6   OuterDia 12   Chamfer 1

Six millimetres of M5 is about 90 seconds of printing and it tells you
everything. Run three at `Tol` 0.15 / 0.20 / 0.25 and keep the one that turns
with light finger pressure and no wobble. Write the number on the wall. It is a
property of **your machine**, and it will hold across every size you print after.

A thread that needs pliers is too tight; one you can rock side to side has lost
most of its flank contact and will strip under load.

---

## 4. Materials

`Tol` is a **radial** clearance in millimetres, applied to the internal thread,
so the hole opens by twice this on diameter. It is the same knob in both scripts.

| preset | Tol | Fit | Chamfer | why that number |
|---|---|---|---|---|
| `machined` | 0 | close | 0.5 | Cut with a real tap and die. The basic profile is correct as-is; the tap carries its own allowance. |
| `tap-it` | 0 | close | 0.5 | Print or mill the `Drill` output, then run a real tap through it. |
| `resin` | 0.08 | close | 0.5 | SLA/DLP holds fine features, so it needs the least. Wash and cure **fully** first — green parts creep and the fit drifts for a day. |
| `resin-tough` | 0.12 | normal | 0.5 | Tough and engineering resins stay softer longer. More room. |
| `pla` | 0.20 | normal | 1.0 | The FDM baseline. Stiff and brittle, so threads shear instead of yielding — buy back strength with length, not with tightness. |
| `petg` | 0.25 | normal | 1.0 | Stringing and ooze land in the thread roots, which is exactly where you cannot reach to clean. Always more than PLA. |
| `abs` / `asa` | 0.22 | normal | 1.0 | Plus shrinkage. On anything over ~60 mm go to 0.30, because the hole shrinks toward its own centre and a long part does it unevenly. |
| `pc` | 0.22 | normal | 1.0 | Strong threads, but only with a hot chamber — without one the layers part before the thread strips. |
| `nylon` | 0.30 | loose | 1.0 | Swells with moisture, so the fit moves with the weather. Dry it, and expect it to loosen again. Very forgiving once it fits. |
| `sls-mjf` | 0.25 | normal | 1.0 | Powder packs into thread roots and does not all come out. Bead blast, then test. |
| `first-try` | 0.35 | loose | 1.0 | Untuned printer, unknown filament, or a part you cannot afford to reprint. Sloppy, but it **will** go together. |
| `render` | 0 | close | 1.0 | Presentation only. Zero clearance and a heavy mesh. Do not send this to a printer. |

---

## 5. How much thread to engage

Depth of the tapped hole, as a multiple of the screw diameter. This matters far
more than tightness — a loose thread that is long enough holds; a perfect thread
that is 3 mm deep does not.

| material the thread is cut in | depth | M5 works out at |
|---|---|---|
| steel | 1.0 d | 5 mm |
| aluminium, brass | 1.5 d | 7.5 mm |
| printed PLA / PETG / ABS | 2.0 d | 10 mm |
| printed nylon, soft resin | 2.5 d | 12.5 mm |
| end grain in wood | don't — use an insert | |

Past about 3 d you gain almost nothing: the first few turns carry most of the
load and the rest is along for the ride.

`ThreadTap` warns in `Info` when `Length` is under one diameter.

---

## 6. The catch nobody tells you: Tol is absolute, threads are not

A printer's error is a fixed number of millimetres. It does not get smaller when
the screw does. But the **thread depth** does — it is `0.5413 × pitch`, and the
pitch shrinks with the screw. So the same `Tol` that is comfortable on an M8
removes most of the thread on an M3.

What is left of the flank after clearance, at the PLA preset of 0.20:

| size | thread depth | flank left at Tol 0.20 | verdict |
|---|---|---|---|
| M3 | 0.271 | **26 %** | will strip |
| M4 | 0.379 | **47 %** | will strip |
| M5 | 0.433 | 54 % | workable |
| M6 | 0.541 | 63 % | workable |
| M8 | 0.677 | 70 % | fine |
| M10 | 0.812 | 75 % | fine |

Inch, at 0.22:

| size | thread depth | flank left | verdict |
|---|---|---|---|
| #4-48 | 0.286 | **23 %** | will strip |
| #8-32 | 0.430 | **49 %** | will strip |
| 1/4-20 | 0.687 | 68 % | workable |
| 3/8-16 | 0.859 | 74 % | fine |

**This is the real reason small printed threads fail**, and it is not a fault in
the model — it is arithmetic. Your printer cannot hold 0.05 mm, and 0.05 mm is
what an M3 thread needs to keep its flanks.

`ThreadTap` now prints `flank left` in `Info` every run and warns below 70 %.
Read that line before you print anything. Under 50 % it will strip, and no
amount of thread length rescues it.

### The size floor for every preset

`ScrewOptions` now computes this and puts it on a `MinSize` output, so you get it
at the moment you pick the preset rather than after the part fails. *Workable*
means 50 % of the flank survives — it holds if you give it length. *Comfortable*
is 70 %.

| preset | Tol | workable from | comfortable from |
|---|---|---|---|
| `resin` | 0.08 | M1.6 / #1-64 | M3 / #3-48 |
| `resin-tough` | 0.12 | M2.5 / #2-56 | M5 / #6-32 |
| `pla` | 0.20 | M5 / #6-32 | M8 / 1/4-20 |
| `abs` `asa` `pc` | 0.22 | M6 / #10-24 | M10 / 5/16-18 |
| `petg` `sls-mjf` | 0.25 | M6 / #10-24 | M12 / 3/8-16 |
| `nylon` | 0.30 | M8 / 1/4-20 | M14 / 1/2-13 |
| `first-try` | 0.35 | M10 / 5/16-18 | M20 / 5/8-11 |
| `machined` `tap-it` | 0 | any | any |

Read that table once and it reframes the whole business: **FDM does not do small
printed threads.** Not because the geometry is wrong, but because 0.2 mm of
printer error is most of an M3's thread. This is also why `resin` reaches so much
further down — 0.08 mm of clearance leaves an M3 with 70 % of its flank.

Fine pitches are shallower than coarse at the same diameter, so they need one
size more again.

Three ways out, in order of how well they work:

1. **Use a bigger screw.** M5 is the smallest that prints honestly in FDM.
2. **Use a captive nut** (`NutGenerator`) or a **heat-set insert**. Below M4
   one of these is the answer, not a printed thread.
3. **Print the `Drill` hole and cut a real thread** with a tap. Works down to
   M3 because the tap does not care about your printer's tolerance.

---

## 7. When **not** to print the thread

Printed threads are a real option, and they are also the wrong answer surprisingly often.

**Use a heat-set insert instead when:**
- the size is **M4 / #8 or smaller** — see *Tol is absolute*, there is not enough flank left once
  your clearance comes off. This is the common case and people ignore it.
- the joint will be taken apart more than a handful of times
- it carries real load in PLA

Model it with `ThreadTap` at `Threads`/`hand=none` (a plain bore) and set the
diameter from your insert's datasheet, or just use the `Drill` output.

**Tap it instead when** the part is machined, or when it is printed but the load
matters. Take `Drill` from `ThreadTap`, subtract that, and run a real tap in.
Cutting a thread through printed layers gives a much better flank than printing
one, because there are no bridges and no elephant's foot in the root.

**Use a captive nut instead when** the load is high and the part is plastic.
Print a hexagonal pocket, drop a steel nut in. Nothing printed will beat it.

---

## 8. The mouth chamfer — what it is and what to set

`ThreadTap.Chamfer` is **in pitches, not millimetres**. `1` means one pitch on an
M3 and one pitch on a 1/2-13 alike, and `ThreadTap` does the multiply itself
because it already knows the pitch from `Spec`. So there is nothing to look up.

    0.5   cutting metal        the machinist's chamfer, opens the mouth to D + P
    1.0   printing             removes the unsupported first thread outright
    0     none                 valid, but a screw will be fiddly to start

### What it actually does

Over the first `Chamfer × pitch` of depth, every thread radius is pushed outward,
tapering from full flare at the mouth back to nothing at the bottom of the
chamfer. The thread form is unchanged — it just gets shallower as it nears the
surface. That is exactly what a **taper tap** leaves behind, which is why taper
taps exist.

    Chamfer = 0                       Chamfer = 1   (one pitch, M5)

   ──┬──────────┬──                  ──┬──╲            ╱──┬──
     │WWWWWWWWWW│                      │   ╲WW      WW╱   │   <- flared, 0.8 deep
     │WWWWWWWWWW│                      │WWWWWWWWWWWWWW│
     │WWWWWWWWWW│                      │WWWWWWWWWWWWWW│

### Why you want it

- **It stops cross-threading.** The screw self-centres instead of catching half
  a thread and jamming at an angle.
- **On a printed hole it removes the thread that was going to fail anyway.** The
  very first turn has nothing underneath it, so it droops into the bore. Better
  to not print it than to print it badly.
- **It survives elephant's foot.** A squashed first layer makes the mouth
  undersize; the chamfer is already wider than that.

### Don't overdo it on a shallow hole

The flared turns are shallower than full depth, so they carry little load — they
are sacrificial. Chamfer 1.5 mm at **both** ends of a 6 mm deep hole and half
your engagement is funnel. Keep the total under about a fifth of `Length`.
`ThreadTap` hard-clamps each end to `0.3 × Length` and says so in `Info`, but
that clamp is a backstop, not a target.

### What it works out to

You should never need this — it is here so the number is not a black box.
`Info` prints the figure it used every run.

| size | pitch | `0.5` = | `1.0` = |
|---|---|---|---|
| M3 | 0.50 | 0.25 | 0.50 |
| M4 | 0.70 | 0.35 | 0.70 |
| M5 | 0.80 | 0.40 | 0.80 |
| M6 | 1.00 | 0.50 | 1.00 |
| M8 | 1.25 | 0.62 | 1.25 |
| M10 | 1.50 | 0.75 | 1.50 |
| M12 | 1.75 | 0.88 | 1.75 |
| M16 | 2.00 | 1.00 | 2.00 |

Inch, in inches:

| size | pitch | `0.5` = | `1.0` = |
|---|---|---|---|
| #6-32 / #8-32 / #10-32 | 0.0312 | 0.016 | 0.031 |
| #10-24 | 0.0417 | 0.021 | 0.042 |
| 1/4-20 | 0.0500 | 0.025 | 0.050 |
| 1/4-28 | 0.0357 | 0.018 | 0.036 |
| 5/16-18 | 0.0556 | 0.028 | 0.056 |
| 3/8-16 | 0.0625 | 0.031 | 0.063 |
| 1/2-13 | 0.0769 | 0.038 | 0.077 |

> **On the name.** This used to be called `Lead`, which was a bad choice: in
> threading, *lead* properly means how far a screw advances in one turn (for a
> single-start thread, the pitch itself). It is a chamfer. It is now called one.

---

## 9. Printing them well

**Orientation is most of it.** A thread whose axis is vertical prints as a stack
of rings and comes out well. A thread lying on its side is a stack of
overhanging arcs, and the underside of every turn droops. If you can only print
it lying down, add 0.05–0.10 to `Tol` and expect to chase it with a tap.

**Chamfer earns its place.** Covered in full under *The mouth chamfer* — set it to `1` for anything
printed. It costs nothing and it is the difference between a thread that starts
and one you fight.

**The screw side wants a chamfer too**, and gets one automatically: the tip is
chamfered 45° down to the root diameter, which is what a real screw has.

**Elephant's foot eats the first thread.** If your first layer squishes, the
mouth of a printed hole is undersize. Either chamfer the part itself, or raise
`Chamfer`.

**Segments**: leave it at 96. Drop it to 48 while you lay the model out and put
it back before you export — the mesh cost is real and you will notice it at
`seg=192` on a long screw.

---

## 10. Where each clearance actually goes

Worth being clear, because two different `Tol`s do two different jobs:

    ScrewGenerator.Fit + .Tol   ->  the CLEARANCE BORE in Prep
                                    the hole the screw passes THROUGH
    ThreadTap.Tol               ->  the THREAD in the part it screws INTO

They are usually the same number and `Spec` carries it across for you, which is
why `ThreadTap.Tol` can be left at 0. Override it when they should differ — a
sloppy pass-through hole with a snug thread, for instance.

The screw itself is always drawn at basic nominal. It is the reference part, and
none of the clearance lives on it.

---

## 11. When it does not fit

| symptom | what it is | fix |
|---|---|---|
| screw will not start | first thread curled, or elephant's foot | `Chamfer` to `1` on `ThreadTap` |
| starts then binds | `Tol` too small | +0.05 and print the test pair again |
| goes in but rocks | `Tol` too large | −0.05. Check you are not also on `loose` `Fit` |
| strips on first use | check `flank left` in `Info` — under 50 %? | bigger screw, or an insert. Length will not save it |
| fits in PLA, not in PETG | it is the material, not the model | use the preset for what you are actually printing |
| fine on one printer, tight on another | it always is | `Tol` is per-machine, measure it again |
| head does not sit flush | `Recess`, not `Length` | `sink` for flat heads, `bore` for the rest |
| head sits too deep | `sink=` in `Opts` is doing it | set `sink=0` or drop it from `Opts` |

---

## 12. Machining, for completeness

Set `Tol` to 0 and `Fit` to `close` or `normal`. The geometry is the basic ISO
profile and a real tap already carries its allowance — adding your own on top
gives you a sloppy thread for no reason.

`Drill` on `ThreadTap` is the tap-drill hole at `d − P`, which is the
~77 % engagement rule and lands on the standard drill for every size in the
table. `Tol` is deliberately **not** added to it: this is the hole a real tap
goes into, and a tap cuts the basic form and carries its own allowance. Opening
the drill by a print clearance on top would put it wider than the thread crests
and leave nothing to cut. `Info` prints the engagement it leaves. Below 100 % is not a compromise:
75–80 % is the usual target, because the last 20 % of thread depth buys about
5 % more strength and roughly doubles the torque needed to cut it.
