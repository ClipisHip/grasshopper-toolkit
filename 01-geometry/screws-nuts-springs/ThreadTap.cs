// -----------------------------------------------------------------------------
// screws-nuts-springs · Thread Tap                          grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
// ===========================================================================
//  THREAD TAP  -  Grasshopper C# component (RhinoCommon)
//  The INTERNAL thread - the nut side - for any size ScrewGenerator makes.
//  Give it "M5" or "1/4-20" and it hands you the solid to subtract, the
//  threaded boss, and the tap drill.
//  ---------------------------------------------------------------------
//  INPUTS (Item access):
//     Spec      string  WIRE ScrewGenerator's Spec OUTPUT STRAIGHT IN and this
//                       component inherits the size, the hand, the tolerance and
//                       the segment count - four inputs you no longer have twice.
//                       Or type a bare size by hand: "M5", "M8x1", "1/4-20",
//                       "#8-32", "3/8".  Full form, all keys optional:
//                         M5 hand=R tol=0.20 seg=96 class=6H
//     Length    double  depth of thread, along -Pln.ZAxis
//     OuterDia  double  outside diameter of the Nut boss, 0 = auto (2 x d)
//     Tol       double  radial clearance override, model units.  LEAVE AT 0 to
//                       use whatever Spec brought with it; set a number to
//                       overrule it.  0 machining, 0.08 resin, 0.15-0.25 FDM.
//     Chamfer   double  IN PITCHES, NOT MILLIMETRES.  1 = one pitch, whatever
//                       the size.  A 45 deg funnel at BOTH ends of the hole so a
//                       screw can find its start.  0 = none.
//                         0.5   cutting metal - the machinist's chamfer, D + P
//                         1.0   printing - the first printed thread has nothing
//                               under it and curls, so remove it outright
//                       It is in pitches so you never look a number up: 1 means
//                       one pitch on M3 and on 1/2-13 alike.  Info prints what
//                       it worked out to in model units.
//     Pln       Plane   mouth of the hole, see below
//  OUTPUTS:
//     Cutter    Brep    SUBTRACT this from your part to get the tapped hole
//     Nut       Brep    a cylindrical boss of OuterDia with the hole already in it
//     Drill     Brep    the plain tap-drill hole, cylinder + 118 deg point
//     Info      string  report + warnings
//
//  EVERY VALID STRING is listed in OPTIONS.md, and the ScrewOptions component
//  hands them to you as lists you can drop a List Item on.
// ===========================================================================


// ===========================================================================
//  HOW IT IS PLACED - and it matches ScrewGenerator exactly
//
//     Pln.Origin sits at the MOUTH of the hole.  The thread runs along
//     -Pln.ZAxis, the same way a screw drives.
//
//          ----------------------  Pln      <- z = 0, the face
//               -Z  )WWW(                   <- Length deep
//                   )WWW(
//
//     So: wire ONE plane into ScrewGenerator's Pln and into this Pln, and the
//     screw lands in its own hole, head bearing on the face.
//
//  WHAT "CLASS" ACTUALLY CHANGES
//     A solid model is one size, not a band, so only the FUNDAMENTAL DEVIATION
//     matters - where the tolerance zone starts.  ISO 965-1:
//         H   EI = 0            the basic profile.  6H, 7H, "nominal" - all
//                               the same solid.  The grade number only sets
//                               how much BIGGER the hole is allowed to get.
//         G   EI = +(15 + 11P) micrometres on diameter
//     Unified inch classes 1B / 2B / 3B all have EI = 0 too, so on the inch
//     side Class does nothing and Tol is your only handle.  That is not a
//     shortcut - it is what the standards say.
//
//  TOL IS WHERE THE FIT LIVES
//     Tol is added to the RADIUS of every thread diameter, so the hole opens
//     up by 2 x Tol on diameter.  A printed M5 nut at Tol 0 will not accept a
//     printed M5 screw; 0.15-0.20 usually does on a well-tuned FDM machine,
//     0.05-0.10 on resin.  Print one 6 mm tall test nut before you commit.
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
		string Spec,
		double Length,
		double OuterDia,
		double Tol,
		double Chamfer,
		Plane Pln,
		ref object Cutter,
		ref object Nut,
		ref object Drill,
		ref object Info)
    {
        List<string> log = new List<string>();
    System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;

    double tol = 0.001;
    if (Rhino.RhinoDoc.ActiveDoc != null) tol = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

    // =======================================================================
    //  SIZES.  Same names ScrewGenerator uses, so Spec wires straight in.
    //     metric  name  d  P(coarse)        inch  name  d  tpiUNC  tpiUNF
    // =======================================================================
    string[] MET = new string[]{
      "M1.6  1.6   0.35", "M2    2.0   0.40", "M2.5  2.5   0.45", "M3    3.0   0.50",
      "M3.5  3.5   0.60", "M4    4.0   0.70", "M5    5.0   0.80", "M6    6.0   1.00",
      "M8    8.0   1.25", "M10  10.0   1.50", "M12  12.0   1.75", "M14  14.0   2.00",
      "M16  16.0   2.00", "M20  20.0   2.50", "M24  24.0   3.00"
    };
    string[] INC = new string[]{
      "#0   0.0600   -   80", "#1   0.0730  64   72", "#2   0.0860  56   64",
      "#3   0.0990  48   56", "#4   0.1120  40   48", "#5   0.1250  40   44",
      "#6   0.1380  32   40", "#8   0.1640  32   36", "#10  0.1900  24   32",
      "#12  0.2160  24   28", "1/4  0.2500  20   28", "5/16 0.3125  18   24",
      "3/8  0.3750  16   24", "7/16 0.4375  14   20", "1/2  0.5000  13   20",
      "5/8  0.6250  11   18", "3/4  0.7500  10   16", "1    1.0000   8   12"
    };

    // =======================================================================
    //  PARSE
    // =======================================================================
    // =======================================================================
    //  SPEC.  First token is the size; anything after it is key=value settings
    //  that ScrewGenerator wrote, or that you typed yourself.
    // =======================================================================
    string sraw = (Spec == null) ? "" : Spec.Trim();
    if (sraw.Length == 0) sraw = "M5";
    string[] stok = sraw.Split(new char[]{' ', ',', ';', '\t', '\n', '\r'},
                               StringSplitOptions.RemoveEmptyEntries);

    int    thr     = 1;                             // hand: 0 plain, 1 right, 2 left
    double specTol = -1.0;                          // <0 = Spec carried none
    int    nSeg    = 96;
    string cs      = "6H";
    for (int i = 1; i < stok.Length; i++)
    {
      string t = stok[i].ToLowerInvariant();
      int eq = t.IndexOf('=');
      if (eq < 1 || eq == t.Length - 1)
      { log.Add("Spec: \"" + stok[i] + "\" is not key=value - ignored."); continue; }
      string ky = t.Substring(0, eq), vl = t.Substring(eq + 1);
      if (ky == "hand")
        thr = vl.StartsWith("l") ? 2 : (vl.StartsWith("n") || vl.StartsWith("p") || vl == "0") ? 0 : 1;
      else if (ky == "class") cs = vl.ToUpperInvariant();
      else
      {
        double vv;
        if (!double.TryParse(vl, System.Globalization.NumberStyles.Float, IC, out vv))
        { log.Add("Spec: \"" + stok[i] + "\" has no number - ignored."); continue; }
        if      (ky == "tol") specTol = vv;
        else if (ky == "seg" || ky == "segments") nSeg = (int)vv;
        else log.Add("Spec: unknown key \"" + ky + "\" - ignored. Valid: hand, tol, seg, class.");
      }
    }

    string raw = stok[0].ToUpperInvariant();
    raw = raw.Replace("UNC", "").Replace("UNF", "").Replace("UN", "").Trim();
    if (raw.Length == 0) raw = "M5";

    bool   metric  = raw.StartsWith("M") && raw.Length > 1 &&
                     (char.IsDigit(raw[1]) || raw[1] == '.');
    double wantDia = 0.0, wantP = 0.0;

    if (metric)
    {
      string[] mp = raw.Substring(1).Split(new char[]{'X', '*'}, StringSplitOptions.RemoveEmptyEntries);
      if (!double.TryParse(mp[0], System.Globalization.NumberStyles.Float, IC, out wantDia))
      { Info = "Could not read the metric size \"" + stok[0] + "\".  Try M5 or M8x1."; return; }
      if (mp.Length > 1) double.TryParse(mp[1], System.Globalization.NumberStyles.Float, IC, out wantP);
    }
    else
    {
      string[] ip = raw.Replace("\"", "").Split(new char[]{'-'}, StringSplitOptions.RemoveEmptyEntries);
      string dsg = ip[0].Trim();
      double tpi = 0.0;
      if (ip.Length > 1) double.TryParse(ip[1].Trim(), System.Globalization.NumberStyles.Float, IC, out tpi);
      bool hash = dsg.StartsWith("#");
      if (hash) dsg = dsg.Substring(1);
      if (dsg.Contains("/"))
      {
        string[] fr = dsg.Split('/');
        double nu, de;
        if (fr.Length == 2 &&
            double.TryParse(fr[0], System.Globalization.NumberStyles.Float, IC, out nu) &&
            double.TryParse(fr[1], System.Globalization.NumberStyles.Float, IC, out de) && de > 0)
          wantDia = nu / de;
      }
      else
      {
        double v;
        if (!double.TryParse(dsg, System.Globalization.NumberStyles.Float, IC, out v))
        { Info = "Could not read the inch size \"" + stok[0] + "\".  Try 1/4-20, #8-32 or 3/8."; return; }
        // a bare integer 0..12 is a gauge number, anything else is a diameter
        bool gauge = hash || (!dsg.Contains(".") && v >= 0.0 && v <= 12.0);
        // "1-8" and "1-12" are the ONE INCH bolt, not a #1 screw.  #1 comes in 64
        // and 72 tpi and nothing an inch across is finer than 20, so the tpi
        // tells them apart - and a written "#" settles it outright.
        if (gauge && !hash && v == 1.0 && tpi < 40.0) gauge = false;
        wantDia = gauge ? 0.060 + 0.013 * v : v;
      }
      if (tpi > 0.0) wantP = 1.0 / tpi;
    }

    string[] tbl = metric ? MET : INC;
    int row = -1; double bestErr = 1e9;
    for (int i = 0; i < tbl.Length; i++)
    {
      string[] f = tbl[i].Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
      double e = Math.Abs(double.Parse(f[1], IC) - wantDia);
      if (e < bestErr) { bestErr = e; row = i; }
    }
    if (row < 0 || bestErr > (metric ? 0.30 : 0.012))
    { Info = "\"" + stok[0] + "\" is not in the table.  Metric M1.6-M24, inch #0-1\"."; return; }

    string[] fld = tbl[row].Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
    string nm = fld[0];
    double d  = double.Parse(fld[1], IC);
    double P;
    if (metric) P = double.Parse(fld[2], IC);
    else
    {
      double t2;
      if (fld[2] != "-" && double.TryParse(fld[2], System.Globalization.NumberStyles.Float, IC, out t2)) P = 1.0 / t2;
      else { double.TryParse(fld[3], System.Globalization.NumberStyles.Float, IC, out t2); P = 1.0 / t2; }
    }
    if (wantP > 1e-9) P = wantP;

    double Pmm = metric ? P : P * 25.4;            // the ISO deviation formula wants millimetres
    string canon = metric
      ? nm + (Math.Abs(P - double.Parse(fld[2], IC)) > 1e-6 ? "x" + P.ToString("0.###", IC) : "")
      : nm + "-" + (1.0 / P).ToString("0.##", IC);

    // ---- units --------------------------------------------------------------
    double uf = 1.0, mm = 1.0;
    if (Rhino.RhinoDoc.ActiveDoc != null)
    {
      UnitSystem doc = Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem;
      if (doc != UnitSystem.None && doc != UnitSystem.Unset)
      {
        uf = RhinoMath.UnitScale(metric ? UnitSystem.Millimeters : UnitSystem.Inches, doc);
        mm = RhinoMath.UnitScale(UnitSystem.Millimeters, doc);
      }
    }
    if (uf <= 0.0 || double.IsNaN(uf)) uf = 1.0;
    if (mm <= 0.0 || double.IsNaN(mm)) mm = 1.0;
    d *= uf; P *= uf;

    // =======================================================================
    //  OPTIONS
    // =======================================================================
    bool posG = metric && cs.Contains("G");
    double EI = posG ? (15.0 + 11.0 * Pmm) * 0.001 * mm : 0.0;    // diametral, doc units
    if (!metric && cs.Contains("G"))
      log.Add("Unified inch has no G position - 1B/2B/3B all sit on EI = 0. class= ignored, use Tol.");

    // Tol input overrules Spec; leave it at 0 and whatever Spec carried is used.
    double xt = (Tol > 0.0) ? Tol : (specTol > 0.0 ? specTol : 0.0);
    bool   inherited = (Tol <= 0.0) && (specTol > 0.0);
    double c  = 0.5 * EI + xt;                      // total radial opening

    if (thr < 0 || thr > 2) thr = 1;

    if (nSeg <= 0) nSeg = 96;
    nSeg = Math.Max(32, Math.Min(720, nSeg));
    nSeg = 16 * (int)Math.Round(nSeg / 16.0);

    double L = (Length > 1e-9) ? Length : 2.0 * d;
    double Ro = ((OuterDia > 1e-9) ? OuterDia : 2.0 * d) * 0.5;

    // =======================================================================
    //  THE PROFILE.  Identical zig-zag to the external thread - it is the same
    //  line, the external thread keeps the material inside it and the internal
    //  thread keeps it outside.  ISO 68-1 / ASME B1.1 basic form:
    //     major D  = d                 root of the internal thread, flat P/8
    //     minor D1 = d - 1.0825 P      crest of the internal thread, flat P/4
    //  Both opened by c.
    // =======================================================================
    double H  = 0.8660254037844386 * P;
    double Rmaj = 0.5 * d + c;
    double Rmin = Rmaj - 0.625 * H;
    double D1   = d - 1.25 * H;                     // = d - 1.0825 P, exactly as built

    if (Rmin <= 0.05 * d)
    { Info = "Pitch " + P.ToString("0.###") + " is too coarse for " + nm + "."; return; }
    if (L < 0.8 * d)
      log.Add("Thread depth is under one diameter. Aim for 1 d in steel, 1.5-2 d in aluminium or plastic.");

    // Chamfer arrives in PITCHES, so one number suits every size in the table.
    double chamP = (Chamfer > 0.0) ? Chamfer : 0.0;
    double lead  = chamP * P;
    if (lead > 0.3 * L)
    {
      lead = 0.3 * L;
      log.Add("Chamfer " + chamP.ToString("0.##") + " x pitch is over 30% of Length at each end - " +
              "clamped to " + lead.ToString("0.###") + ". The flared turns carry almost no load.");
    }

    // the lead-in flares the mouth, so the boss has to be wider than the flare
    if (Ro <= Rmaj + lead + 0.1 * d)
    {
      Ro = Rmaj + lead + 0.1 * d;
      log.Add("OuterDia would not clear the chamfer - the Nut boss was opened to " +
              (2.0 * Ro).ToString("0.###") + ". Cutter is unaffected.");
    }
    if (Ro <= Rmaj + 0.2 * d)
      log.Add("The Nut boss has under 0.2 d of wall around the thread - it will strip or split.");

    double[] rad = new double[nSeg];
    for (int i = 0; i < nSeg; i++)
    {
      double u = 1.0 - (double)i / nSeg;
      if (u >= 1.0) u -= 1.0;
      double r;
      if      (u <= 0.0625 || u >= 0.9375) r = Rmaj;
      else if (u <  0.375)                 r = Rmaj - (u - 0.0625) / 0.3125 * (Rmaj - Rmin);
      else if (u <= 0.625)                 r = Rmin;
      else                                 r = Rmin + (u - 0.625) / 0.3125 * (Rmaj - Rmin);
      rad[i] = r;
    }
    if (thr == 0) for (int i = 0; i < nSeg; i++) rad[i] = Rmin;   // plain bore at the minor dia

    double[] flar = new double[nSeg];
    for (int i = 0; i < nSeg; i++) flar[i] = rad[i] + lead;

    // =======================================================================
    //  SKIN.  Same construction as ScrewGenerator: one constant- or tapering-
    //  radius helix per profile sample, ruled surfaces between neighbours.  The
    //  zones share boundary curves exactly, so it all JOINS - no Boolean.
    // =======================================================================
    List<double[]> zr0 = new List<double[]>(), zr1 = new List<double[]>();
    List<double>   zz0 = new List<double>(),   zz1 = new List<double>();

    if (lead > 1e-9)
    {
      zz0.Add(0.0);      zz1.Add(lead);     zr0.Add(flar); zr1.Add(rad);
      zz0.Add(lead);     zz1.Add(L - lead); zr0.Add(rad);  zr1.Add(rad);
      zz0.Add(L - lead); zz1.Add(L);        zr0.Add(rad);  zr1.Add(flar);
    }
    else { zz0.Add(0.0); zz1.Add(L); zr0.Add(rad); zr1.Add(rad); }

    List<Brep> skin = new List<Brep>();
    Point3d[] rimT = new Point3d[nSeg + 1], rimB = new Point3d[nSeg + 1];
    double angAcc = 0.0;
    for (int z = 0; z < zz0.Count; z++)
    {
      double za = zz0[z], zb = zz1[z];
      if (zb - za < 1e-9) continue;
      double turns = (zb - za) / P;
      NurbsCurve[] rail = new NurbsCurve[nSeg];
      for (int i = 0; i < nSeg; i++)
      {
        double a  = 2.0 * Math.PI * i / nSeg + angAcc;
        double r0 = zr0[z][i], r1 = zr1[z][i];
        rail[i] = NurbsCurve.CreateSpiral(new Point3d(0, 0, za), Vector3d.ZAxis,
                                          new Point3d(r0 * Math.Cos(a), r0 * Math.Sin(a), za),
                                          P, turns, r0, r1);
        if (rail[i] == null) { Info = "Helix failed - check Spec and Length."; return; }
        if (z == 0)                 rimT[i] = rail[i].PointAtStart;
        if (z == zz0.Count - 1)     rimB[i] = rail[i].PointAtEnd;
      }
      for (int i = 0; i < nSeg; i++)
      {
        NurbsSurface rsf = NurbsSurface.CreateRuledSurface(rail[i], rail[(i + 1) % nSeg]);
        if (rsf != null) skin.Add(rsf.ToBrep());
        else
        {
          Brep[] lf = Brep.CreateFromLoft(new Curve[]{rail[i], rail[(i + 1) % nSeg]},
                                          Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
          if (lf != null && lf.Length > 0) skin.Add(lf[0]);
        }
      }
      angAcc += 2.0 * Math.PI * turns;
    }
    if (skin.Count == 0) { Info = "Could not build the thread skin."; return; }
    rimT[nSeg] = rimT[0]; rimB[nSeg] = rimB[0];

    // handedness: ask CreateSpiral which way it winds, do not assume
    bool rhAPI = true;
    NurbsCurve probe = NurbsCurve.CreateSpiral(Point3d.Origin, Vector3d.ZAxis,
                                               new Point3d(1, 0, 0), 1.0, 0.25, 1.0, 1.0);
    if (probe != null)
    {
      Point3d pa = probe.PointAtStart, pb = probe.PointAt(probe.Domain.ParameterAt(0.3));
      double da = Math.Atan2(pb.Y, pb.X) - Math.Atan2(pa.Y, pa.X);
      while (da >  Math.PI) da -= 2.0 * Math.PI;
      while (da < -Math.PI) da += 2.0 * Math.PI;
      rhAPI = (da * (pb.Z - pa.Z)) > 0.0;
    }
    bool mirror = (thr != 0) && ((thr != 2) != rhAPI);

    // ---- CUTTER : cap the skin at both ends ---------------------------------
    List<Brep> cut = new List<Brep>();
    foreach (Brep b in skin) cut.Add(b.DuplicateBrep());
    Brep[] cj = Brep.JoinBreps(cut, tol);
    if (cj == null || cj.Length == 0) { Info = "Join failed - try more Segments or a coarser model tolerance."; return; }
    Brep cutter = cj[0];
    if (cj.Length > 1)
    {
      double bv = -1.0;
      foreach (Brep b in cj) { double v = b.GetBoundingBox(true).Volume; if (v > bv) { bv = v; cutter = b; } }
      log.Add("Skin came back in " + cj.Length + " pieces - the biggest was kept.");
    }
    Brep cc = cutter.CapPlanarHoles(tol);
    if (cc != null) cutter = cc;
    if (!cutter.IsSolid) log.Add("Cutter is not watertight - raise Segments or loosen the model tolerance.");

    // ---- NUT : same skin inside, a cylinder outside, annular ends -----------
    Brep nut = null;
    List<Brep> nf = new List<Brep>();
    foreach (Brep b in skin) nf.Add(b.DuplicateBrep());
    Brep wall = new Cylinder(new Circle(new Plane(Point3d.Origin, Vector3d.ZAxis), Ro), L).ToBrep(false, false);
    if (wall != null) nf.Add(wall);

    Curve loT = new Polyline(rimT).ToPolylineCurve();
    Curve loB = new Polyline(rimB).ToPolylineCurve();
    Curve ciT = new ArcCurve(new Circle(new Plane(Point3d.Origin, Vector3d.ZAxis), Ro));
    Curve ciB = new ArcCurve(new Circle(new Plane(new Point3d(0, 0, L), Vector3d.ZAxis), Ro));
    Brep[] aT = Brep.CreatePlanarBreps(new Curve[]{ciT, loT}, tol);
    Brep[] aB = Brep.CreatePlanarBreps(new Curve[]{ciB, loB}, tol);
    if (aT != null && aT.Length > 0) nf.Add(aT[0]); else log.Add("Nut end face failed at the mouth.");
    if (aB != null && aB.Length > 0) nf.Add(aB[0]); else log.Add("Nut end face failed at the far end.");

    Brep[] nj = Brep.JoinBreps(nf, tol);
    if (nj != null && nj.Length > 0)
    {
      nut = nj[0];
      double bv = -1.0;
      foreach (Brep b in nj) { double v = b.GetBoundingBox(true).Volume; if (v > bv) { bv = v; nut = b; } }
      if (!nut.IsSolid) { Brep n2 = nut.CapPlanarHoles(tol); if (n2 != null) nut = n2; }
      if (!nut.IsSolid) log.Add("Nut is not watertight.");
    }
    else log.Add("Nut join failed.");

    // ---- DRILL : the hole you make BEFORE tapping, point and all ------------
    //  d - P is the 75%-engagement rule and it lands on the standard tap drill
    //  for every size in the table.  118 deg is the everyday twist-drill point.
    //
    //  NOTE Tol is deliberately NOT added here.  This is the hole a REAL TAP
    //  goes into, and a real tap cuts the basic form and carries its own
    //  allowance.  Opening the drill by a print clearance on top would put it
    //  wider than the thread crests and there would be no thread left to cut.
    double dDrill = d - P;
    double pt118  = 0.5 * dDrill / Math.Tan(RhinoMath.ToRadians(59.0));
    List<Point3d> dp = new List<Point3d>();
    dp.Add(new Point3d(0.5 * dDrill, 0, -0.5 * mm));
    dp.Add(new Point3d(0.5 * dDrill, 0, L));
    dp.Add(new Point3d(0.0,          0, L + pt118));
    Brep drill = null;
    RevSurface drv = RevSurface.Create(new Polyline(dp).ToPolylineCurve(),
                                       new Line(new Point3d(0, 0, -1.0), new Point3d(0, 0, 1.0)));
    if (drv != null)
    {
      Brep db = Brep.CreateFromRevSurface(drv, true, true);
      if (db != null)
      {
        if (!db.IsSolid) { Brep d2 = db.CapPlanarHoles(tol); if (d2 != null) db = d2; }
        if (db.SolidOrientation == BrepSolidOrientation.Inward) db.Flip();
        drill = db;
      }
    }
    if (drill == null) log.Add("Drill revolve failed.");

    // =======================================================================
    //  PLACE.  Local +Z runs into the part, so the target frame is Pln spun
    //  180 deg about its own X - a proper rotation, the thread keeps its hand.
    // =======================================================================
    if (mirror)
    {
      Transform mi = Transform.Mirror(Plane.WorldZX);
      cutter.Transform(mi);
      if (nut != null) nut.Transform(mi);
    }
    if (cutter.SolidOrientation == BrepSolidOrientation.Inward) cutter.Flip();
    if (nut != null && nut.SolidOrientation == BrepSolidOrientation.Inward) nut.Flip();

    Plane bp = Pln.IsValid ? Pln : Plane.WorldXY;
    Plane tp = new Plane(bp.Origin, bp.XAxis, -bp.YAxis);
    Transform xf = Transform.PlaneToPlane(Plane.WorldXY, tp);
    cutter.Transform(xf);
    if (nut != null)   nut.Transform(xf);
    if (drill != null) drill.Transform(xf);

    // =======================================================================
    //  REPORT
    // =======================================================================
    double d2p    = d - 0.6495 * P;
    double engage = 76.98 * (d - dDrill) / P;       // % of full thread the drill leaves
    double depth  = 0.5 * (d - D1);                 // thread depth per flank
    double flank  = (depth > 1e-12) ? (depth - c) / depth : 0.0;   // left after clearance
    double turns2 = L / P;
    string unitTxt = "";
    if (Rhino.RhinoDoc.ActiveDoc != null)
      unitTxt = " " + Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem.ToString().ToLowerInvariant();

    if (turns2 * nSeg > 60000.0)
      log.Add("This is " + (turns2 * nSeg).ToString("0") + " thread samples. Drop Segments or Length if it crawls.");
    if (xt <= 0.0 && EI <= 0.0)
      log.Add("Tol 0 and class H is the BASIC profile - a nominal screw is a zero-clearance fit. Fine for a boolean, will not print.");
    if (flank <= 0.0)
      log.Add("CLEARANCE EXCEEDS THE WHOLE THREAD DEPTH (" + depth.ToString("0.###") +
              "). There is no thread here at all - a plain bore would do the same job. " +
              "Drop Tol, or step up a size.");
    else if (flank < 0.5)
      log.Add("Only " + (100.0 * flank).ToString("0") + "% of the flank is left after clearance" +
              " - the thread depth is " + depth.ToString("0.###") + " and you have taken " +
              c.ToString("0.###") + " off it. It will strip. Drop Tol, or use a bigger screw, " +
              "or a heat-set insert.");
    else if (flank < 0.7)
      log.Add((100.0 * flank).ToString("0") + "% of the flank is left after clearance. " +
              "Workable, but give it extra Length.");

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.AppendLine(canon + "   internal / nut thread   " + (metric ? "ISO metric" : "Unified inch") +
                  (thr == 0 ? "   PLAIN BORE (no thread modelled)" : (thr == 2 ? "   LEFT HAND" : "   right hand")));
    sb.AppendLine("--");
    sb.AppendLine("basic major D       " + d.ToString("0.####") + unitTxt);
    sb.AppendLine("basic pitch  D2     " + d2p.ToString("0.####"));
    sb.AppendLine("basic minor  D1     " + D1.ToString("0.####"));
    sb.AppendLine("pitch P             " + P.ToString("0.####") +
                  (metric ? "" : "   (" + (1.0 / P * uf).ToString("0.#") + " tpi)"));
    sb.AppendLine("--");
    sb.AppendLine("class               " + (posG ? "G,  EI +" + (EI / mm * 1000.0).ToString("0") + " um on dia"
                                                 : "H,  EI 0 - the basic profile"));
    sb.AppendLine("extra Tol           " + xt.ToString("0.###") + " on radius" +
                  (xt > 0.0 ? "  (" + (2.0 * xt).ToString("0.###") + " on diameter)" : "") +
                  (inherited ? "   <- inherited from Spec" : ""));
    sb.AppendLine("AS CUT  major       " + (2.0 * Rmaj).ToString("0.####") +
                  "     minor " + (2.0 * Rmin).ToString("0.####"));
    sb.AppendLine("--");
    sb.AppendLine("depth               " + L.ToString("0.###") + "  =  " +
                  (L / d).ToString("0.##") + " d,   " + turns2.ToString("0.#") + " turns");
    sb.AppendLine("chamfer             " + (lead > 0.0
                  ? chamP.ToString("0.##") + " x pitch = " + lead.ToString("0.###") +
                    " at 45 deg, both ends,  mouth opens to " + (2.0 * (Rmaj + lead)).ToString("0.###")
                  : "none - a screw will be fiddly to start"));
    sb.AppendLine("nut boss            " + (2.0 * Ro).ToString("0.###") + " outside,  wall " +
                  (Ro - Rmaj).ToString("0.###"));
    sb.AppendLine("--");
    sb.AppendLine("flank left          " + (100.0 * flank).ToString("0") + "%   of the " +
                  depth.ToString("0.###") + " thread depth, after " + c.ToString("0.###") + " clearance");
    sb.AppendLine("tap drill           " + dDrill.ToString("0.###") + "   -> " +
                  engage.ToString("0") + "% engagement.  For a REAL TAP - no Tol added, " +
                  "a tap carries its own");
    sb.AppendLine("Drill output        that cylinder plus a 118 deg point, " + pt118.ToString("0.##") + " past the depth");
    if (log.Count > 0) { sb.AppendLine("--"); foreach (string s2 in log) sb.AppendLine("! " + s2); }

    Cutter = cutter;
    Nut    = nut;
    Drill  = drill;
    Info   = sb.ToString();
    }
}
