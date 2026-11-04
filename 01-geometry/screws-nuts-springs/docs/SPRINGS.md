# SPRINGS — helical compression springs

Two components in the same family as the screw tools: same frame, same idea of
a picker feeding a generator.

    SpringGenerator.cs   the spring, its rate, and what it can take
    SpringOptions.cs     materials, end types and a preferred wire series, as lists

---

## The two diameters, and which one wins

A spring is defined by three of: inside diameter, outside diameter, wire. Give any
two and the third follows, because **OD = ID + 2 × wire**.

| you give | you get |
|---|---|
| `InnerDia` + `OuterDia` | wire = (OD − ID) / 2 |
| `InnerDia` + `Wire` | OD = ID + 2 × wire |
| `OuterDia` + `Wire` | ID = OD − 2 × wire |

**`Wire` wins over `OuterDia`** when all three are given, and says so in `Info`.
That is deliberate: wire comes in the sizes it comes in, and the bore or the shaft
the spring has to clear is usually the thing you cannot change. The outside is the
one with room to move.

The **mean** diameter `D = OD − wire` is what every formula below actually uses.
Not the outside, not the inside.

## Ends

| value | active coils | solid length | what it is |
|---|---|---|---|
| `closed-ground` | Nt − 2 | d × Nt | the shop default. Stands square, seats flat. |
| `closed` | Nt − 2 | d × (Nt+1) | squared but not ground. Cheaper, sits slightly cocked. |
| `open-ground` | Nt − 1 | d × Nt | rare |
| `open` | Nt | d × (Nt+1) | a plain helix, cut off. Will not stand up on its own. |

`Coils` is the **total**, ends included. The dead end coils carry no load, which is
why a 6-coil closed-ground spring is a 4-coil spring as far as the rate is concerned.

---

## The numbers, and where they come from

Shigley, *Mechanical Engineering Design*, chapter 10.

```
rate          k    = G d^4 / (8 D^3 Na)
shear stress  tau  = K_B (8 F D) / (pi d^3)
Bergstrasser  K_B  = (4C + 2) / (4C - 3),   C = D/d
strength      Sut  = A / d^m                 table 10-4
allowable     tau_allow = pct x Sut          table 10-6
```

Table 10–6's percentages **already include** the K factor, so the corrected stress is
what gets compared — there is no second factor hiding anywhere.

Validated against Shigley's own Example 10–1 (no.16 music wire, OD 7/16", squared
ends, 12.5 turns): every value inside 0.4% of the book, the difference being the
book's rounding.

| | this | book |
|---|---|---|
| index C | 10.82 | 10.8 |
| K_B | 1.1241 | 1.124 |
| Sut | 323.6 kpsi | 324 |
| allowable | 145.6 kpsi | 146 |
| force at yield | 6.43 lbf | 6.46 |
| rate | 4.116 lbf/in | 4.13 |

`Rate` is **N/mm** and `Force` is **N**, always, whatever the document units are.
`Info` prints lbf/in and lbf beside them.

## Materials

| material | ASTM | G (GPa) | allowable | cost | for |
|---|---|---|---|---|---|
| `music-wire` | A228 | 81.7 | 45% of Sut | 2.6× | small springs. Strongest where it counts, cheap. Rusts. |
| `oil-tempered` | A229 | 77.2 | 50% of Sut | 1.3× | bigger wire, still cheap |
| `hard-drawn` | A227 | 79.3 | 45% of Sut | 1.0× | the cheap one, where load matters less than cost |
| `chrome-vanadium` | A232 | 77.2 | 50% of Sut | 3.1× | impact and shock |
| `chrome-silicon` | A401 | 77.2 | 50% of Sut | 4.0× | shock and heat. Valve-spring material. |
| `stainless-302` | A313 | 69.0 | 35% of Sut | 9.0× | wet. ~15% less stiff than steel, so a softer spring. |
| `phosphor-bronze` | B159 | 41.4 | 35% of Sut | 8.0× | non-magnetic, conductive. Half steel's modulus. |

**Strength runs with wire size, hard.** `Sut = A/d^m`, so the same music wire is
about 2450 MPa at 0.5 mm and 1885 MPa at 3 mm — thin wire is stronger wire. It is
also why a spring does not simply scale: double every dimension and the rate doubles,
but the stress limit drops.

| music wire | 0.3 mm | 0.5 | 1.0 | 2.0 | 3.0 | 6.0 |
|---|---|---|---|---|---|---|
| Sut, MPa | 2633 | 2445 | 2211 | 2000 | 1885 | 1705 |

---

## What `Info` will tell you off about

**Spring index `C = D/d`.** Under 4 the wire is being bent round too tight a radius
to coil reliably and the inside of every coil is overstressed. Over 12 it is floppy,
tangles with its neighbours in a box, and buckles. **5 to 9 is the comfortable range**
and most catalogue springs live there.

**Buckling.** A spring squarely seated at both ends buckles sideways when the free
length passes about **5.3 × the mean diameter** — before it ever reaches solid. On
ends that do not seat square it is more like 2.6. Past that, guide it: over a rod or
inside a bore.

**Taking a set.** If the stress at solid exceeds the allowable, bottoming the spring
out shortens it permanently. `Info` gives a factor of safety at solid: above 1 you can
bottom it out as often as you like, below 1 you must design a stop, or accept that
the free length after the first bottoming is not the one you drew.

---

## What this will not tell you

**Fatigue.** Every number is for a **static** load. A spring that is squashed and
released a million times is a different calculation with a lower allowable stress. If
it cycles, treat the safe force here as an upper bound.

**Printed springs.** There is no PLA or TPU in the material list, on purpose. Spring
wire works in **torsion**, which for an FDM part means shearing one layer against the
next — the weakest thing such a part does. The modulus is anisotropic, rate-dependent
and creeps under load. Running those numbers through this formula would produce
something that looks authoritative and is fiction. Print one and measure it: hang a
known mass on it and divide.
