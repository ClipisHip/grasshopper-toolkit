<!--
  Draft notes (hidden on GitHub):
  - Add a screenshot of the model in RFEM 6 as media/rfem6-model.png, then
    uncomment the image line below.
  - Next step that would make it a real analysis example: read deflections or
    utilisation back into Grasshopper and show them on the canvas.
  - The Run toggle is saved off, so opening the file sends nothing to RFEM.
-->

# rfem6-beam-example

A small example of driving RFEM 6 from Grasshopper with Dlubal's plugin. A two-span steel beam is drawn from three points, and its material, cross-section, supports, loads, design situations and load combinations are all defined on the canvas and sent to RFEM 6 in one step.

Built by following Dlubal's tutorial *RFEM 6 Tutorial with Rhino & Grasshopper*: [Continuous Beam, part 1](https://www.dlubal.com/en/support-and-learning/learning/videos/002937) and [part 2](https://www.dlubal.com/en/support-and-learning/learning/videos/002968), made here with the current Rhino 8 version of their plugin. Dlubal's own [model from the tutorial](https://www.dlubal.com/en/downloads-and-information/examples-and-tutorials/models-to-download/003123) is on their site.

<!-- ![the beam in RFEM 6](media/rfem6-model.png) -->

**Inputs** · the two spans and the imposed load (sliders)  
**Output** · an RFEM 6 model, ready to calculate  
**Needs** · Rhino 8 · RFEM 6 · Dlubal's RFEM 6 plugin for Grasshopper  
**File** · [rfem6-beam-example.gh](rfem6-beam-example.gh)

## What it sets up

| In RFEM 6 | Set in Grasshopper |
| :-- | :-- |
| Members | two beams on one line, 2 m and 4 m long by default |
| Material and section | S235 · IPE 300 |
| Supports | pinned at the left end, rollers at the middle and right supports |
| Load cases | LC1 self-weight · LC2 imposed load, a uniform downward load on both spans set by a slider |
| Design situations | ULS (EQU, permanent and transient) · SLS |
| Combinations | CO1 ULS: 1.35·LC1 · CO2 ULS: 1.35·LC1 + 1.5·LC2 · CO1 SLS: LC1 · CO2 SLS: LC1 + LC2 |
| Analysis | static, geometrically linear |

## Run it

1. Open RFEM 6. The plugin talks to it on localhost, port 9000.
2. Open the definition and log in with the **Dlubal Login** button.
3. Set the spans and the load with the sliders, then turn the **Run** toggle on to send the model. Units are metres.


---

Daniel Andújar · [andujar.design](https://andujar.design) · MIT licence
