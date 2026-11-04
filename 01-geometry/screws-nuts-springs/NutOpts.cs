// -----------------------------------------------------------------------------
// screws-nuts-springs · Nut Opts                            grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
// ===========================================================================
//  NUT OPTS  -  Grasshopper C# component (RhinoCommon)
//  Builds the Opts string for NutGenerator out of numbers, so you can put a
//  slider on each one instead of typing key=value into a panel.
//  ---------------------------------------------------------------------
//  INPUTS (Item access).  EVERY ONE IS OPTIONAL: leave it at 0 and that key is
//  left out entirely, which is how you tell NutGenerator to use its own default.
//     Sink     double  nut top this far below Pln.  0 = flush with the face.
//     Slot     double  side slot length from the bolt axis, for Entry side.
//                      It must reach your part's edge.
//                      NEGATIVE = embed: emits slot=0, a roofed pocket with no
//                      way in, for pausing the print and dropping the nut in.
//     Bore     double  how far the bolt bore reaches below Pln - your plate
//                      thickness.  0 = NutGenerator's own (pocket floor + 2 d).
//     AF       double  YOUR nut's measured across-flats.  Overrides the table.
//     H        double  your nut's measured height.  Overrides the table.
//     Chamfer  double  bore countersink IN PITCHES, not millimetres.  0.5 is
//                      what a real nut has, 1 if you are printing the nut.
//                      NEGATIVE = none: emits chamfer=0.
//  OUTPUTS:
//     Opts     string  wire it into NutGenerator.Opts
//     Info     string  what it built, and what it left out
// ===========================================================================


// ===========================================================================
//  WHY NEGATIVE MEANS ZERO
//     Zero already means something here - "I did not set this, use the
//     default" - so it cannot also mean "set this to zero".  Two of the six
//     have a real use for zero:
//         Slot     0 = a roofed pocket with no slot at all
//         Chamfer  0 = no countersink
//     so on those two a NEGATIVE number asks for zero.  Drag the slider below
//     zero and Info will tell you what it did.  The other four have no use for
//     zero and simply ignore a negative.
//
//  ALL LENGTHS ARE MODEL UNITS except Chamfer, which is a multiple of the
//  thread pitch - NutGenerator does the multiply, so one number suits M3 and
//  1/2-13 alike.
// ===========================================================================

#region Usings
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
    #region Notes
    /* 
      Members:
        RhinoDoc RhinoDocument
        GH_Document GrasshopperDocument
        IGH_Component Component
        int Iteration

      Methods (Virtual & overridable):
        Print(string text)
        Print(string format, params object[] args)
        Reflect(object obj)
        Reflect(object obj, string method_name)
    */
    #endregion

    private void RunScript(
		double Sink,
		double Slot,
		double Bore,
		double AF,
		double H,
		double Chamfer,
		ref object Opts,
		ref object Info)
    {
        System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;

    List<string> part = new List<string>();
    List<string> said = new List<string>();
    List<string> left = new List<string>();
    List<string> log  = new List<string>();

    if (Sink > 0.0)
    {
      part.Add("sink=" + Sink.ToString("0.####", IC));
      said.Add("  sink=" + Sink.ToString("0.####", IC) + "    nut top " + Sink.ToString("0.###") +
               " below Pln.  On Entry side this is also the roof over it.");
    }
    else { left.Add("sink"); if (Sink < 0.0) log.Add("Sink is negative and there is no use for that - ignored. 0 sits the nut flush with Pln."); }

    if (Slot > 0.0)
    {
      part.Add("slot=" + Slot.ToString("0.####", IC));
      said.Add("  slot=" + Slot.ToString("0.####", IC) + "    side slot that far from the bolt axis, along " +
               "+Pln.X.  Only does anything on Entry side, and it has to REACH YOUR PART'S EDGE.");
    }
    else if (Slot < 0.0)
    {
      part.Add("slot=0");
      said.Add("  slot=0       EMBEDDED: a roofed pocket with no way in. Pause the print at the " +
               "height Info gives you and drop the nut in.  Give Sink a real roof to go with it.");
    }
    else left.Add("slot");

    if (Bore > 0.0)
    {
      part.Add("bore=" + Bore.ToString("0.####", IC));
      said.Add("  bore=" + Bore.ToString("0.####", IC) + "    bolt bore reaches that far below Pln.  " +
               "Your plate thickness, for a clean through hole.");
    }
    else { left.Add("bore"); if (Bore < 0.0) log.Add("Bore is negative - ignored."); }

    if (AF > 0.0)
    {
      part.Add("af=" + AF.ToString("0.####", IC));
      said.Add("  af=" + AF.ToString("0.####", IC) + "      YOUR nut's across-flats, over the standard. " +
               "This is the answer to the ISO / DIN split on M10, M12 and M14.");
    }
    else { left.Add("af"); if (AF < 0.0) log.Add("AF is negative - ignored."); }

    if (H > 0.0)
    {
      part.Add("h=" + H.ToString("0.####", IC));
      said.Add("  h=" + H.ToString("0.####", IC) + "       your nut's measured height, over the standard.");
    }
    else { left.Add("h"); if (H < 0.0) log.Add("H is negative - ignored."); }

    if (Chamfer > 0.0)
    {
      part.Add("chamfer=" + Chamfer.ToString("0.####", IC));
      said.Add("  chamfer=" + Chamfer.ToString("0.####", IC) + " bore countersink, " +
               Chamfer.ToString("0.##") + " x PITCH.  NutGenerator does the multiply.");
      if (Chamfer > 2.5)
        log.Add("Chamfer " + Chamfer.ToString("0.##") + " is in PITCHES, not millimetres - that is " +
                Chamfer.ToString("0.#") + " whole threads of countersink at each end. Did you mean a length? " +
                "0.5 is a real nut, 1 is generous for printing.");
    }
    else if (Chamfer < 0.0)
    {
      part.Add("chamfer=0");
      said.Add("  chamfer=0    no countersink at either end of the bore. A screw will be fiddly to start.");
    }
    else left.Add("chamfer");

    string outp = string.Join(" ", part.ToArray());

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    if (part.Count == 0)
    {
      sb.AppendLine("Opts  ->  (empty)");
      sb.AppendLine();
      sb.AppendLine("Nothing set, so NutGenerator uses all of its own defaults:");
      sb.AppendLine("   nut flush with Pln, slot 2 x across-flats, bore 2 d past the pocket,");
      sb.AppendLine("   the standard's across-flats and height, and a 0.5 x pitch countersink.");
      sb.AppendLine("An empty string is perfectly good to wire in - it is not an error.");
    }
    else
    {
      sb.AppendLine("Opts  ->  " + outp);
      sb.AppendLine();
      foreach (string s2 in said) sb.AppendLine(s2);
    }
    if (left.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine("left out, so NutGenerator's default stands:  " + string.Join(", ", left.ToArray()));
    }
    sb.AppendLine();
    sb.AppendLine("Lengths are model units. Chamfer is PITCHES. Slot and Chamfer take a");
    sb.AppendLine("NEGATIVE number to mean zero, because 0 already means \"leave it alone\".");
    if (log.Count > 0) { sb.AppendLine("--"); foreach (string s2 in log) sb.AppendLine("! " + s2); }

    Opts = outp;
    Info = sb.ToString();
    }
}
