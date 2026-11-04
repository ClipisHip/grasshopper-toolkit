// -----------------------------------------------------------------------------
// screws-nuts-springs · Spring Generator                    grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
// ===========================================================================
//  SPRING GENERATOR  -  Grasshopper C# component (RhinoCommon)
//  A helical compression spring, and what it will actually do: rate, force,
//  stress, and how hard you can push it before it takes a set.
//  ---------------------------------------------------------------------
//  INPUTS (Item access):
//     InnerDia  double  inside diameter - the bore it must clear, or the shaft
//                       it slides over
//     OuterDia  double  outside diameter - the hole it must fit in
//     Wire      double  wire diameter.  LEAVE AT 0 and it is (OuterDia -
//                       InnerDia) / 2.  Give it a number and it WINS: OuterDia
//                       is recomputed as InnerDia + 2 x Wire, because wire comes
//                       in the sizes it comes in and the bore is usually what
//                       you cannot change.
//     Length    double  FREE length - the length at rest, uncompressed
//     Coils     double  TOTAL coils, ends included.  Fractions are fine.
//     Ends      string  "closed-ground" | "closed" | "open" | "open-ground"
//                       closed-ground is the usual shop default: it stands up
//                       square and seats flat.
//     Material  string  music-wire | oil-tempered | hard-drawn | chrome-vanadium
//                       | chrome-silicon | stainless-302 | phosphor-bronze
//     Deflect   double  a working compression to report the force at.
//                       0 = report at solid and at the safe limit instead.
//     Pln       Plane   the seat the spring stands on; it grows along +Pln.ZAxis
//  OUTPUTS:
//     Spring    Brep    the solid
//     Helix     Curve   the wire centreline
//     Rate      double  spring rate in NEWTONS PER MILLIMETRE, always, whatever
//                       the document units are
//     Force     double  NEWTONS at Deflect, or at the safe limit if Deflect is 0
//     Info      string  the full report, in both metric and inch
// ===========================================================================


// ===========================================================================
//  WHERE THE NUMBERS COME FROM
//     Shigley's Mechanical Engineering Design, chapter 10.
//       rate        k    = G d^4 / (8 D^3 Na)
//       stress      tau  = K_B (8 F D) / (pi d^3)
//       Bergstrasser  K_B = (4C + 2) / (4C - 3),  C = D/d
//       strength    Sut  = A / d^m              (table 10-4)
//       allowable   tau_allow = pct x Sut       (table 10-6)
//     D is the MEAN coil diameter, not the outside: D = OuterDia - Wire.
//     Table 10-6's percentages ALREADY INCLUDE the K factor, so the corrected
//     stress is what gets compared - that is why no extra factor appears.
//
//  WHAT IT WILL NOT TELL YOU
//     Fatigue.  Every number here is for a STATIC load - a spring that gets
//     squashed and released a million times is a different calculation and
//     wants a different, lower, allowable stress.  If it cycles, treat the safe
//     force here as an upper bound and look up Zimmerli's data.
//
//     Printed springs.  There is no PLA or TPU in the material list on purpose.
//     Spring wire is loaded in TORSION, which for a printed part means shearing
//     one layer against the next - the weakest thing an FDM part does - and the
//     modulus is anisotropic, rate-dependent and creeps.  A number from this
//     formula would look authoritative and be fiction.  Print one and measure it.
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
		double InnerDia,
		double OuterDia,
		double Wire,
		double Length,
		double Coils,
		string Ends,
		string Material,
		double Deflect,
		Plane Pln,
		ref object Spring,
		ref object Helix,
		ref object Rate,
		ref object Force,
		ref object Info)
    {
        List<string> log = new List<string>();
    System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;

    double tol = 0.001, aTol = 0.5;
    if (Rhino.RhinoDoc.ActiveDoc != null)
    {
      tol  = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
      aTol = Rhino.RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
    }
    double mm = 1.0;                                // document units per millimetre
    if (Rhino.RhinoDoc.ActiveDoc != null)
    {
      UnitSystem du = Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem;
      if (du != UnitSystem.None && du != UnitSystem.Unset)
        mm = RhinoMath.UnitScale(UnitSystem.Millimeters, du);
    }
    if (mm <= 0.0 || double.IsNaN(mm)) mm = 1.0;

    // =======================================================================
    //  MATERIALS.  Shigley tables 10-4, 10-5 and 10-6.
    //     name  ASTM  G[GPa]  tau%Sut  dmin[mm]   then bands "dmax:A:m"
    //  Sut = A / d^m with d in mm and A in MPa.mm^m, so Sut comes out in MPa.
    //  The bands run small to large; the first whose dmax covers the wire wins.
    // =======================================================================
    string[] MAT = new string[]{
      "music-wire       A228  81.7  45  0.10   6.5:2211:0.145",
      "oil-tempered     A229  77.2  50  0.50  12.7:1855:0.187",
      "hard-drawn       A227  79.3  45  0.70  12.7:1783:0.190",
      "chrome-vanadium  A232  77.2  50  0.80  11.1:2005:0.168",
      "chrome-silicon   A401  77.2  50  1.60   9.5:1974:0.108",
      "stainless-302    A313  69.0  35  0.30   2.5:1867:0.146  5:2065:0.263  10:2911:0.478",
      "phosphor-bronze  B159  41.4  35  0.10   0.6:1000:0      2:913:0.028   7.5:932:0.064"
    };

    string ms = (Material == null) ? "" : Material.Trim().ToLowerInvariant().Replace("_", "-").Replace(" ", "-");
    int mi = -1;
    for (int i = 0; i < MAT.Length; i++)
    {
      string nmi = MAT[i].Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries)[0];
      if (nmi == ms) { mi = i; break; }
    }
    if (mi < 0 && ms.Length > 0)
      for (int i = 0; i < MAT.Length; i++)
      {
        string[] f = MAT[i].Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries);
        if (f[0].StartsWith(ms) || f[1].ToLowerInvariant() == ms) { mi = i; break; }
      }
    if (mi < 0)
    {
      if (ms.Length > 0)
      {
        List<string> have = new List<string>();
        foreach (string r in MAT) have.Add(r.Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries)[0]);
        log.Add("Material \"" + Material + "\" is not one of: " + string.Join(", ", have.ToArray()) +
                ".  Using music-wire.");
      }
      mi = 0;
    }
    string[] mf  = MAT[mi].Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries);
    string matNm = mf[0], matStd = mf[1];
    double Ggpa  = double.Parse(mf[2], IC);
    double tauPc = double.Parse(mf[3], IC);
    double dMin  = double.Parse(mf[4], IC);

    // =======================================================================
    //  GEOMETRY IN, IN DOCUMENT UNITS
    // =======================================================================
    double ID = InnerDia, OD = OuterDia, d = Wire;
    if (d > 1e-12)
    {
      if (ID <= 1e-12 && OD > 1e-12) ID = OD - 2.0 * d;     // only an OD was given
      if (ID <= 1e-12) { Info = "Give InnerDia (or OuterDia) as well as Wire."; return; }
      double odWas = OD;
      OD = ID + 2.0 * d;
      if (odWas > 1e-12 && Math.Abs(odWas - OD) > 1e-9)
        log.Add("Wire " + d.ToString("0.###") + " wins over OuterDia " + odWas.ToString("0.###") +
                " - the outside is now " + OD.ToString("0.###") + ". That is the documented behaviour.");
    }
    else
    {
      if (ID <= 1e-12 || OD <= 1e-12) { Info = "Give InnerDia and OuterDia, or one of them and Wire."; return; }
      d = 0.5 * (OD - ID);
      if (d <= 1e-12) { Info = "OuterDia must be bigger than InnerDia - that difference is two wire diameters."; return; }
    }

    double L0 = Length;
    double Nt = Coils;
    if (L0 <= 1e-12) { Info = "Length is the FREE length of the spring and it has to be positive."; return; }
    if (Nt <= 0.5)   { Info = "Coils is the TOTAL number of coils - give it something above 1."; return; }

    string es = (Ends == null) ? "closed-ground" : Ends.Trim().ToLowerInvariant().Replace("_", "-").Replace(" ", "-");
    bool ground = es.Contains("ground");
    bool closed = !(es.StartsWith("open") || es.StartsWith("plain"));
    string endNm = (closed ? "closed" : "open") + (ground ? "-ground" : "");

    double ce = closed ? 1.0 : 0.0;                 // dead coils at EACH end
    double Na = Nt - (closed ? 2.0 : (ground ? 1.0 : 0.0));
    if (Na <= 0.3)
    { Info = "Coils " + Nt.ToString("0.##") + " leaves " + Na.ToString("0.##") + " active coils once the " +
             endNm + " ends are taken off. Add coils, or use open ends."; return; }

    double Dm = OD - d;                             // MEAN coil diameter
    double C  = Dm / d;
    double Ls = ground ? d * Nt : d * (Nt + 1.0);   // solid length

    if (Ls >= L0)
    { Info = "Solid length " + Ls.ToString("0.###") + " is not shorter than the free length " +
             L0.ToString("0.###") + " - there is no travel. Fewer coils, thinner wire, or more length."; return; }

    // centreline: dead coils touch at pitch d, the active ones share what is left
    double zEnd = ground ? 0.40 * d : 0.50 * d;     // ground ends get a real flat cut on them
    double span = L0 - 2.0 * zEnd;
    double p    = (span - 2.0 * ce * d) / Na;
    if (p < d)
    { Info = "At " + L0.ToString("0.###") + " free length those " + Nt.ToString("0.##") +
             " coils sit " + p.ToString("0.###") + " apart, which is less than the " + d.ToString("0.###") +
             " wire - they would already be touching. Longer, or fewer coils."; return; }

    // =======================================================================
    //  THE PHYSICS.  Everything in millimetres and newtons, converted back at
    //  the end, so the formulae are the textbook ones with no unit games.
    // =======================================================================
    double d_mm = d / mm, D_mm = Dm / mm, L0_mm = L0 / mm, Ls_mm = Ls / mm;
    double G_mpa = Ggpa * 1000.0;

    double bandA = 0.0, bandM = 0.0, bandMax = 0.0;
    for (int i = 5; i < mf.Length; i++)
    {
      string[] bs = mf[i].Split(':');
      if (bs.Length != 3) continue;
      double bmax = double.Parse(bs[0], IC);
      bandA = double.Parse(bs[1], IC); bandM = double.Parse(bs[2], IC); bandMax = bmax;
      if (d_mm <= bmax) break;
    }
    if (d_mm < dMin)
      log.Add("Wire " + d_mm.ToString("0.###") + " mm is below the " + dMin.ToString("0.##") +
              " mm that " + matStd + " is tabulated from - the strength figure is an extrapolation.");
    if (d_mm > bandMax)
      log.Add("Wire " + d_mm.ToString("0.###") + " mm is above the " + bandMax.ToString("0.##") +
              " mm that " + matStd + " is tabulated to - the strength figure is an extrapolation.");

    double Sut  = bandA / Math.Pow(d_mm, bandM);            // MPa
    double tauA = 0.01 * tauPc * Sut;                       // MPa, K factor included
    double KB   = (4.0 * C + 2.0) / (4.0 * C - 3.0);        // Bergstrasser
    double KW   = (4.0 * C - 1.0) / (4.0 * C - 4.0) + 0.615 / C;

    double k_Nmm = G_mpa * Math.Pow(d_mm, 4.0) / (8.0 * Math.Pow(D_mm, 3.0) * Na);   // N/mm
    double ySol  = L0_mm - Ls_mm;                           // travel to solid, mm
    double FSol  = k_Nmm * ySol;                            // N
    double tSol  = KB * 8.0 * FSol * D_mm / (Math.PI * Math.Pow(d_mm, 3.0));         // MPa
    double FAll  = tauA * Math.PI * Math.Pow(d_mm, 3.0) / (KB * 8.0 * D_mm);         // N
    double yAll  = FAll / k_Nmm;                            // mm
    double nSol  = (tSol > 1e-12) ? tauA / tSol : 999.0;    // factor of safety at solid

    double yUse = (Deflect > 1e-12) ? Deflect / mm : 0.0;   // mm
    double FUse = k_Nmm * yUse;
    double tUse = KB * 8.0 * FUse * D_mm / (Math.PI * Math.Pow(d_mm, 3.0));

    // =======================================================================
    //  THE SOLID.  Sample the variable-pitch centreline, pipe it, and grind the
    //  ends flat with a box at each end if they are ground.
    // =======================================================================
    int per = 64;
    int nPt = (int)Math.Max(24.0, Math.Ceiling(per * Nt));
    if (nPt > 20000) { nPt = 20000; log.Add("Coil count is very high - the centreline was capped at 20000 samples."); }
    List<Point3d> cpt = new List<Point3d>();
    for (int i = 0; i <= nPt; i++)
    {
      double t = Nt * (double)i / nPt;              // turns travelled
      double z;
      if (t <= ce)            z = zEnd + t * d;
      else if (t <= ce + Na)  z = zEnd + ce * d + (t - ce) * p;
      else                    z = zEnd + ce * d + Na * p + (t - ce - Na) * d;
      double a = 2.0 * Math.PI * t;
      cpt.Add(new Point3d(0.5 * Dm * Math.Cos(a), 0.5 * Dm * Math.Sin(a), z));
    }
    Curve rail = Curve.CreateInterpolatedCurve(cpt, 3);
    if (rail == null) { Info = "Could not build the wire centreline."; return; }

    Brep[] pipe = Brep.CreatePipe(rail, 0.5 * d, false, PipeCapMode.Round, true, tol, RhinoMath.ToRadians(aTol));
    if (pipe == null || pipe.Length == 0) { Info = "Pipe failed - check the wire diameter against the coil diameter."; return; }
    Brep spring = pipe[0];
    for (int i = 1; i < pipe.Length; i++)
    {
      Brep[] uj = Brep.CreateBooleanUnion(new Brep[]{spring, pipe[i]}, tol);
      if (uj != null && uj.Length > 0) spring = uj[0];
    }
    if (spring.SolidOrientation == BrepSolidOrientation.Inward) spring.Flip();

    if (ground)
    {
      double big = 1.5 * OD, tall = 4.0 * d;
      Interval bi = new Interval(-big, big);
      Brep[] cut = new Brep[]{
        Brep.CreateFromBox(new Box(Plane.WorldXY, bi, bi, new Interval(-tall, 0.0))),
        Brep.CreateFromBox(new Box(Plane.WorldXY, bi, bi, new Interval(L0, L0 + tall)))
      };
      foreach (Brep cb in cut)
      {
        if (cb == null) continue;
        Brep[] df = Brep.CreateBooleanDifference(spring, cb, tol);
        if (df != null && df.Length > 0)
        {
          Brep best = df[0]; double bv = -1.0;
          foreach (Brep bb in df) { double v = bb.GetBoundingBox(true).Volume; if (v > bv) { bv = v; best = bb; } }
          spring = best;
          if (spring.SolidOrientation == BrepSolidOrientation.Inward) spring.Flip();
        }
        else log.Add("Could not grind one end flat - it was left as the cut wire.");
      }
    }

    Plane bp = Pln.IsValid ? Pln : Plane.WorldXY;
    Transform xf = Transform.PlaneToPlane(Plane.WorldXY, bp);
    spring.Transform(xf);
    rail.Transform(xf);

    // =======================================================================
    //  WARNINGS THAT ARE WORTH THE INK
    // =======================================================================
    if (C < 4.0)
      log.Add("Spring index C = " + C.ToString("0.##") + " is below 4. Below about 4 the wire is being " +
              "bent round too tight a radius to coil reliably, and the inside of every coil is overstressed.");
    else if (C > 12.0)
      log.Add("Spring index C = " + C.ToString("0.##") + " is above 12. It will be floppy, tangle with its " +
              "neighbours in a box, and buckle easily. 5 to 9 is the comfortable range.");
    double slender = L0_mm / D_mm;
    if (ground && closed && slender > 5.26)
      log.Add("Free length is " + slender.ToString("0.#") + " x the mean diameter. Over about 5.3, a spring " +
              "squarely seated at both ends buckles sideways before it reaches solid - guide it over a rod " +
              "or inside a bore, or use a shorter, fatter one.");
    else if (slender > 2.6 && !(ground && closed))
      log.Add("Free length is " + slender.ToString("0.#") + " x the mean diameter, on ends that do not seat " +
              "square. Anything over about 2.6 will buckle - guide it.");
    if (nSol < 1.0)
      log.Add("PUSHED TO SOLID THIS SPRING TAKES A PERMANENT SET. Stress at solid is " +
              tSol.ToString("0") + " MPa against " + tauA.ToString("0") + " allowable. It is safe to " +
              yAll.ToString("0.##") + " mm of travel, not the " + ySol.ToString("0.##") + " it has. Either keep " +
              "it off solid by design, or accept it will shorten the first time it bottoms out.");
    if (Deflect > 1e-12 && yUse > ySol)
      log.Add("Deflect " + Deflect.ToString("0.###") + " is more travel than the spring has - it goes solid at " +
              (ySol * mm).ToString("0.###") + ". Past that it is a solid tube, not a spring.");
    else if (Deflect > 1e-12 && tUse > tauA)
      log.Add("At " + Deflect.ToString("0.###") + " of travel the stress is " + tUse.ToString("0") +
              " MPa against " + tauA.ToString("0") + " allowable - it will take a set. Safe travel is " +
              (yAll * mm).ToString("0.###") + ".");

    // =======================================================================
    //  REPORT
    // =======================================================================
    double lbfin = k_Nmm * 5.71014715;              // N/mm -> lbf/in
    double N2lbf = 0.2248089431;
    string ut = "";
    if (Rhino.RhinoDoc.ActiveDoc != null) ut = " " + Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem.ToString().ToLowerInvariant();

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.AppendLine("COMPRESSION SPRING   " + matNm + " (" + matStd + ")   " + endNm + " ends");
    sb.AppendLine("--");
    sb.AppendLine("wire        d       " + d.ToString("0.####") + ut);
    sb.AppendLine("outside     OD      " + OD.ToString("0.###") + "        inside ID  " + (OD - 2.0 * d).ToString("0.###"));
    sb.AppendLine("mean        D       " + Dm.ToString("0.###") + "        index  C = D/d = " + C.ToString("0.##"));
    sb.AppendLine("free length L0      " + L0.ToString("0.###") + "        solid  Ls = " + Ls.ToString("0.###"));
    sb.AppendLine("coils       total   " + Nt.ToString("0.##") + "          active Na = " + Na.ToString("0.##"));
    sb.AppendLine("pitch               " + p.ToString("0.###") + "        travel to solid " + (ySol * mm).ToString("0.###"));
    sb.AppendLine("--");
    sb.AppendLine("RATE        " + k_Nmm.ToString("0.###") + " N/mm        (" + lbfin.ToString("0.##") + " lbf/in)");
    sb.AppendLine("  G  " + Ggpa.ToString("0.#") + " GPa     Sut " + Sut.ToString("0") + " MPa     " +
                  "allowable shear " + tauA.ToString("0") + " MPa  (" + tauPc.ToString("0") + "% of Sut)");
    sb.AppendLine("  Bergstrasser K_B " + KB.ToString("0.###") + "   (Wahl K_W " + KW.ToString("0.###") + ")");
    sb.AppendLine("--");
    if (Deflect > 1e-12)
    {
      sb.AppendLine("AT " + Deflect.ToString("0.###") + ut + " OF TRAVEL");
      sb.AppendLine("  force        " + FUse.ToString("0.##") + " N   (" + (FUse * N2lbf).ToString("0.##") + " lbf)");
      sb.AppendLine("  stress       " + tUse.ToString("0") + " MPa   " +
                    (tUse <= tauA ? "within the " + tauA.ToString("0") + " allowable"
                                  : "OVER the " + tauA.ToString("0") + " allowable"));
      sb.AppendLine("  length there " + (L0 - Deflect).ToString("0.###"));
      sb.AppendLine("--");
    }
    sb.AppendLine("SAFE LIMIT  " + FAll.ToString("0.##") + " N  (" + (FAll * N2lbf).ToString("0.##") +
                  " lbf)  at " + (yAll * mm).ToString("0.###") + ut + " of travel");
    sb.AppendLine("AT SOLID    " + FSol.ToString("0.##") + " N  (" + (FSol * N2lbf).ToString("0.##") +
                  " lbf)  at " + (ySol * mm).ToString("0.###") + ut + ",  stress " + tSol.ToString("0") + " MPa");
    sb.AppendLine("            factor of safety at solid  " + nSol.ToString("0.##") +
                  (nSol >= 1.0 ? "   - it can be bottomed out safely" : "   - IT CANNOT BE BOTTOMED OUT"));
    sb.AppendLine("--");
    sb.AppendLine("Static loading only. A spring that cycles wants a fatigue calculation and a");
    sb.AppendLine("lower allowable than this. Rate and Force outputs are N/mm and N regardless");
    sb.AppendLine("of document units.");
    if (log.Count > 0) { sb.AppendLine("--"); foreach (string s2 in log) sb.AppendLine("! " + s2); }

    Spring = spring;
    Helix  = rail;
    Rate   = k_Nmm;
    Force  = (Deflect > 1e-12) ? FUse : FAll;
    Info   = sb.ToString();
    }
}
