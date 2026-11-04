// -----------------------------------------------------------------------------
// screws-nuts-springs · Spring Options                      grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
// ===========================================================================
//  SPRING OPTIONS  -  Grasshopper C# component (RhinoCommon)
//  The material list, the end-type list and a preferred wire series as LISTS
//  you can drop a List Item on - plus what the chosen material is actually
//  worth at the wire size you picked.
//  ---------------------------------------------------------------------
//  INPUTS (Item access, both optional):
//     Material  string  one of the names in the Materials output
//     Wire      double  wire diameter in model units.  Strength is size
//                       dependent - thin wire is STRONGER - so the numbers
//                       below mean nothing without it.
//  OUTPUTS (set the first three to LIST access):
//     Materials list    every Material string                <- List
//     Ends      list    every Ends string                    <- List
//     Wires     list    preferred wire diameters, in MODEL UNITS, ready to
//                       wire into SpringGenerator.Wire       <- List
//     G         double  shear modulus, GPa
//     Sut       double  tensile strength at that wire size, MPa
//     TauAllow  double  allowable shear stress, MPa - static, K factor included
//     Info      string  the properties, what they cost, and what to pick
// ===========================================================================


// ===========================================================================
//  HOW TO WIRE IT
//     SpringOptions.Materials -> List Item -> SpringGenerator.Material
//                                          -> SpringOptions.Material
//     SpringOptions.Ends      -> List Item -> SpringGenerator.Ends
//     SpringOptions.Wires     -> List Item -> SpringGenerator.Wire
//                                          -> SpringOptions.Wire
//
//  THE ONE THING WORTH KNOWING ABOUT SPRING MATERIALS
//     Strength runs with wire SIZE, hard.  Sut = A/d^m, so the same music wire
//     is about 2450 MPa at 0.5 mm and about 1900 MPa at 3 mm.  Thin wire is
//     stronger wire.  That is why every number here needs a Wire to mean
//     anything, and why a spring does not simply scale.
//
//  CHOOSING
//     music-wire       the default for small springs.  Strongest in the sizes
//                      that matter, cheap, and it takes a good finish.  Rusts.
//     hard-drawn       the cheap one.  Use it where load matters less than cost.
//     oil-tempered     better than hard-drawn for bigger wire, still cheap.
//     chrome-silicon   the one for shock and heat.  Valve-spring material.
//     chrome-vanadium  similar, a little softer, good for impact.
//     stainless-302    when it will get wet.  Roughly 15% less stiff than steel,
//                      so the same spring is a softer spring - not just rustproof.
//     phosphor-bronze  non-magnetic and conductive.  Half the modulus of steel:
//                      a very soft spring for its size.  Electrical contacts.
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
		string Material,
		double Wire,
		ref object Materials,
		ref object Ends,
		ref object Wires,
		ref object G,
		ref object Sut,
		ref object TauAllow,
		ref object Info)
    {
        List<string> log = new List<string>();
    System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;

    double mm = 1.0;
    if (Rhino.RhinoDoc.ActiveDoc != null)
    {
      UnitSystem du = Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem;
      if (du != UnitSystem.None && du != UnitSystem.Unset)
        mm = RhinoMath.UnitScale(UnitSystem.Millimeters, du);
    }
    if (mm <= 0.0 || double.IsNaN(mm)) mm = 1.0;

    // =======================================================================
    //  Shigley tables 10-4, 10-5 and 10-6.  Same numbers SpringGenerator uses.
    //     name  ASTM  G[GPa]  tau%Sut  dmin  cost   bands "dmax:A:m"
    //  cost is Shigley's relative cost of wire, hard-drawn = 1.0.
    // =======================================================================
    string[] MAT = new string[]{
      "music-wire       A228  81.7  45  0.10  2.6    6.5:2211:0.145",
      "oil-tempered     A229  77.2  50  0.50  1.3   12.7:1855:0.187",
      "hard-drawn       A227  79.3  45  0.70  1.0   12.7:1783:0.190",
      "chrome-vanadium  A232  77.2  50  0.80  3.1   11.1:2005:0.168",
      "chrome-silicon   A401  77.2  50  1.60  4.0    9.5:1974:0.108",
      "stainless-302    A313  69.0  35  0.30  9.0    2.5:1867:0.146  5:2065:0.263  10:2911:0.478",
      "phosphor-bronze  B159  41.4  35  0.10  8.0    0.6:1000:0      2:913:0.028   7.5:932:0.064"
    };
    string[] EN = new string[]{ "closed-ground", "closed", "open-ground", "open" };
    // preferred wire diameters, mm - the sizes wire is actually drawn to
    double[] WR = new double[]{
      0.2, 0.25, 0.3, 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2,
      1.4, 1.6, 1.8, 2.0, 2.2, 2.5, 2.8, 3.0, 3.5, 4.0, 4.5, 5.0, 5.5, 6.0, 6.5
    };

    List<string> mats = new List<string>();
    foreach (string r in MAT) mats.Add(r.Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries)[0]);
    List<double> wires = new List<double>();
    foreach (double w in WR) wires.Add(w * mm);

    // =======================================================================
    //  RESOLVE
    // =======================================================================
    string ms = (Material == null) ? "" : Material.Trim().ToLowerInvariant().Replace("_", "-").Replace(" ", "-");
    int mi = -1;
    for (int i = 0; i < MAT.Length; i++)
    {
      string[] f = MAT[i].Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries);
      if (f[0] == ms || f[1].ToLowerInvariant() == ms) { mi = i; break; }
    }
    if (mi < 0 && ms.Length > 0)
      for (int i = 0; i < MAT.Length; i++)
        if (MAT[i].Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries)[0].StartsWith(ms)) { mi = i; break; }

    double oG = 0.0, oSut = 0.0, oTau = 0.0;
    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.AppendLine("MATERIALS   " + string.Join(", ", mats.ToArray()));
    sb.AppendLine("ENDS        " + string.Join(", ", EN));
    sb.AppendLine("WIRES       " + WR.Length + " preferred sizes, " + (WR[0] * mm).ToString("0.###") +
                  " to " + (WR[WR.Length - 1] * mm).ToString("0.###") + " in model units");
    sb.AppendLine("--");

    if (mi < 0)
    {
      if (ms.Length > 0) log.Add("Material \"" + Material + "\" is not in the list.");
      sb.AppendLine("No material chosen. Wire Materials -> List Item -> Material to see its");
      sb.AppendLine("properties, and give Wire as well - strength depends on wire size.");
      sb.AppendLine();
      sb.AppendLine("Sut = A / d^m, so the SAME material is stronger as thinner wire:");
      sb.AppendLine("   music wire   0.5 mm  " + (2211.0 / Math.Pow(0.5, 0.145)).ToString("0") + " MPa");
      sb.AppendLine("                1.0 mm  " + (2211.0).ToString("0") + " MPa");
      sb.AppendLine("                3.0 mm  " + (2211.0 / Math.Pow(3.0, 0.145)).ToString("0") + " MPa");
      sb.AppendLine("                6.0 mm  " + (2211.0 / Math.Pow(6.0, 0.145)).ToString("0") + " MPa");
    }
    else
    {
      string[] f = MAT[mi].Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries);
      oG = double.Parse(f[2], IC);
      double tauPc = double.Parse(f[3], IC), dmin = double.Parse(f[4], IC), cost = double.Parse(f[5], IC);
      sb.AppendLine("MATERIAL    " + f[0] + "   (ASTM " + f[1] + ")");
      sb.AppendLine("  G         " + oG.ToString("0.#") + " GPa" +
                    (oG < 70.0 ? "   - well under steel's ~80, so a soft spring for its size" : ""));
      sb.AppendLine("  allowable " + tauPc.ToString("0") + "% of Sut, static, K factor already in it");
      sb.AppendLine("  cost      " + cost.ToString("0.#") + " x hard-drawn wire");

      double d_mm = (Wire > 1e-12) ? Wire / mm : 0.0;
      if (d_mm <= 0.0)
      {
        sb.AppendLine("--");
        sb.AppendLine("Give Wire and this fills in. Across the tabulated range for " + f[1] + ":");
        for (int i = 6; i < f.Length; i++)
        {
          string[] bs = f[i].Split(':');
          if (bs.Length != 3) continue;
          double bmax = double.Parse(bs[0], IC), A = double.Parse(bs[1], IC), m2 = double.Parse(bs[2], IC);
          double lo = (i == 6) ? dmin : double.Parse(f[i - 1].Split(':')[0], IC);
          sb.AppendLine("   " + lo.ToString("0.##") + " - " + bmax.ToString("0.##") + " mm :  Sut " +
                        (A / Math.Pow(bmax, m2)).ToString("0") + " to " + (A / Math.Pow(lo, m2)).ToString("0") + " MPa");
        }
      }
      else
      {
        double A = 0.0, m2 = 0.0, bmax = 0.0;
        for (int i = 6; i < f.Length; i++)
        {
          string[] bs = f[i].Split(':');
          if (bs.Length != 3) continue;
          bmax = double.Parse(bs[0], IC); A = double.Parse(bs[1], IC); m2 = double.Parse(bs[2], IC);
          if (d_mm <= bmax) break;
        }
        oSut = A / Math.Pow(d_mm, m2);
        oTau = 0.01 * tauPc * oSut;
        sb.AppendLine("--");
        sb.AppendLine("AT WIRE     " + Wire.ToString("0.####") + "   (" + d_mm.ToString("0.###") + " mm)");
        sb.AppendLine("  Sut       " + oSut.ToString("0") + " MPa");
        sb.AppendLine("  allowable " + oTau.ToString("0") + " MPa shear");
        if (d_mm < dmin)
          log.Add("Wire " + d_mm.ToString("0.###") + " mm is below the " + dmin.ToString("0.##") +
                  " mm this material is tabulated from - the strength is an extrapolation.");
        if (d_mm > bmax)
          log.Add("Wire " + d_mm.ToString("0.###") + " mm is above the " + bmax.ToString("0.##") +
                  " mm this material is tabulated to - the strength is an extrapolation.");
      }
    }
    sb.AppendLine("--");
    sb.AppendLine("Static loading. A spring that cycles wants a fatigue allowable, which is");
    sb.AppendLine("lower. No printed materials here on purpose - spring wire works in TORSION,");
    sb.AppendLine("which is an FDM part's weakest direction, and any modulus would be fiction.");
    if (log.Count > 0) { sb.AppendLine("--"); foreach (string s2 in log) sb.AppendLine("! " + s2); }

    Materials = mats;
    Ends      = new List<string>(EN);
    Wires     = wires;
    G         = oG;
    Sut       = oSut;
    TauAllow  = oTau;
    Info      = sb.ToString();
    }
}
