// -----------------------------------------------------------------------------
// screws-nuts-springs · Screw Generator                     grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
// ===========================================================================
//  SCREW GENERATOR  -  Grasshopper C# component (RhinoCommon)
//  One threaded fastener plus the matching hole prep, from a size string.
//  Metric (ISO 261/262 coarse + any fine pitch) and Unified inch (UNC/UNF).
//  ---------------------------------------------------------------------
//  INPUTS (Item access):
//     Size      string  "M5", "M8x1", "M10x1.25", "1/4-20", "#8-32", "3/8"
//                       No pitch given -> coarse (metric) / UNC (inch).
//     Head      string  "socket" | "button" | "flat" | "hex" | "pan" | "none"
//                       socket = ISO 4762 / DIN 912 / ASME B18.3 cap screw
//                       flat   = countersunk, 90 deg metric, 82 deg inch
//                       none   = threaded rod / stud / grub screw
//     Length    double  under-head length.  For "flat" it is the TOTAL length,
//                       head included - that is the standard convention.
//     ThreadLen double  length of the threaded portion, 0 = fully threaded
//     Threads   int     0 plain shank (fast preview), 1 right hand, 2 left hand
//     Fit       string  clearance class for Prep: "close" | "normal" | "loose"
//                       (ISO 273 fine/medium/coarse; inch = drill-chart values)
//     Tol       double  EXTRA radial clearance on Prep, model units.  Adds to
//                       the Fit table.  0.15-0.30 is usual for FDM.
//     Recess    string  "none" | "bore" (counterbore) | "sink" (countersink)
//                       | "auto"  ->  sink for flat heads, none for the rest
//     Opts      string  the three you rarely touch, as key=value, space separated.
//                       Leave it EMPTY for the defaults.  All optional:
//                         sink=<n>  extra recess depth below the part face
//                                   (0, the default, finishes the head flush)
//                         prep=<n>  how far Prep reaches BELOW Pln, measured the
//                                   same way for every head (default: 2 mm past
//                                   the tip).  Set it to your plate thickness
//                                   for a clean through hole.
//                         seg=<n>   thread samples per turn, snapped to a
//                                   multiple of 16 (default 96; 32 draft, 192 show)
//                       e.g.   sink=2 seg=192
//     Pln       Plane   seating plane, see below
//  OUTPUTS:
//     Screw     Brep    the solid fastener
//     Prep      Brep    solid to Boolean-DIFFERENCE out of the part
//     Spec      string  size + hand + tolerance + segments.  Wire it straight
//                       into ThreadTap and it inherits the lot - including
//                       Threads 0, so switching the screw to a plain shank
//                       switches the tapped hole to a plain bore with it.
//     Info      string  report + warnings
//
//  EVERY VALID STRING for Size / Head / Fit / Recess is listed in OPTIONS.md,
//  and the ScrewOptions component hands them to you as lists you can drop a
//  List Item on.
// ===========================================================================


// ===========================================================================
//  HOW IT IS PLACED
//
//     Pln.Origin sits on the PART FACE, and the screw drives along -Pln.ZAxis.
//       - socket, button, hex, pan:  the head bears its underside on that face
//         and stands proud, on the +Z side.
//       - flat (countersunk):  the head sits IN the face, flush, so Pln is the
//         head TOP.  Length is the TOTAL length for these, head included, which
//         means two flat heads of the same Length line up at the top and reach
//         the same depth no matter how their head heights differ.
//
//              +Z   [][]  head              <- k tall, above the plane
//          ----------------------  Pln      <- z = 0, seating face
//              -Z   |WWW|  shank            <- Length long, below the plane
//                   |WWW|
//                    \ /   tip chamfer
//
//     Prep is built in the same frame: the clearance bore runs DOWN from the
//     plane, the counterbore / countersink runs UP from it, because that is
//     the material you remove to seat the head.  So for a plain through hole
//     Prep lives entirely below Pln; ask for "bore" or "sink" and part of it
//     pokes above, which is exactly what you want to subtract.
//
//  WHAT IS NOMINAL AND WHAT IS NOT
//     The thread is the ISO 68-1 / ASME B1.1 BASIC profile: 60 deg flanks,
//     crest flat P/8 at the major diameter, root flat P/4 at the minor.  No
//     allowance is taken off - this is a nominal screw, the reference part.
//     All the clearance lives in Prep (Fit + Tol) and in ThreadTap (Class +
//     Tol).  A real 6g screw is ~0.02 mm under this; ignore it, or add it to
//     Tol on the mating side.
// ===========================================================================

// ---------------------------------------------------------------------------
//  paste into RunScript( ... )
//    private void RunScript(string Size, string Head, double Length,
//                           double ThreadLen, int Threads, string Fit,
//                           double Tol, string Recess, string Opts, Plane Pln,
//                           ref object Screw, ref object Prep,
//                           ref object Spec, ref object Info)
// ---------------------------------------------------------------------------
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
		string Size,
		string Head,
		double Length,
		double ThreadLen,
		int Threads,
		string Fit,
		double Tol,
		string Recess,
		string Opts,
		Plane Pln,
		ref object Screw,
		ref object Prep,
		ref object Spec,
		ref object Info)
    {
         List<string> log = new List<string>();
    System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;

    double tol  = 0.001;
    double aTol = 0.5;
    if (Rhino.RhinoDoc.ActiveDoc != null)
    {
      tol  = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
      aTol = Rhino.RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
    }

    // =======================================================================
    //  THE DATABASE.  Metric is millimetres, inch is inches; both get scaled
    //  to the document unit further down.
    //
    //  metric   name  d  P  clearC clearN clearL  capDk capK capS  btnDk btnK
    //                 btnS  cskDk  hexS hexK  panDk panK
    //    cap = ISO 4762 / DIN 912      btn = ISO 7380-1      csk = ISO 10642
    //    hex = ISO 4017 / DIN 933      pan = ISO 7045 / DIN 7985
    //    clearance = ISO 273 fine / medium / coarse
    //    Button above M16 and pan above M10 are extrapolated on the same
    //    ratios as the tabulated sizes - flagged in Info when you land on one.
    // =======================================================================
    string[] MET = new string[]{
    // name   d      P     clC   clN   clL   capDk capK  capS  btnDk btnK  btnS  cskDk hexS  hexK  panDk panK
      "M1.6   1.6   0.35   1.7   1.8   2.0    3.0   1.6   1.5   3.0   1.05  1.0   3.2   3.2   1.1   3.2   1.3",
      "M2     2.0   0.40   2.2   2.4   2.6    3.8   2.0   1.5   3.8   1.30  1.3   4.0   4.0   1.4   4.0   1.6",
      "M2.5   2.5   0.45   2.7   2.9   3.1    4.5   2.5   2.0   4.7   1.50  1.5   5.0   5.0   1.7   5.0   2.0",
      "M3     3.0   0.50   3.2   3.4   3.6    5.5   3.0   2.5   5.7   1.65  2.0   6.0   5.5   2.0   6.0   2.4",
      "M3.5   3.5   0.60   3.7   3.9   4.2    6.5   3.5   2.5   6.6   1.95  2.0   7.0   6.0   2.4   7.0   2.6",
      "M4     4.0   0.70   4.3   4.5   4.8    7.0   4.0   3.0   7.6   2.20  2.5   8.0   7.0   2.8   8.0   3.1",
      "M5     5.0   0.80   5.3   5.5   5.8    8.5   5.0   4.0   9.5   2.75  3.0  10.0   8.0   3.5  10.0   3.8",
      "M6     6.0   1.00   6.4   6.6   7.0   10.0   6.0   5.0  10.5   3.30  4.0  12.0  10.0   4.0  12.0   4.6",
      "M8     8.0   1.25   8.4   9.0  10.0   13.0   8.0   6.0  14.0   4.40  5.0  16.0  13.0   5.3  16.0   6.0",
      "M10   10.0   1.50  10.5  11.0  12.0   16.0  10.0   8.0  17.5   5.50  6.0  20.0  16.0   6.4  20.0   7.5",
      "M12   12.0   1.75  13.0  13.5  14.5   18.0  12.0  10.0  21.0   6.60  8.0  24.0  18.0   7.5  24.0   9.0",
      "M14   14.0   2.00  15.0  15.5  16.5   21.0  14.0  12.0  24.0   7.70  8.0  27.0  21.0   8.8  28.0  10.5",
      "M16   16.0   2.00  17.0  17.5  18.5   24.0  16.0  14.0  28.0   8.80 10.0  30.0  24.0  10.0  32.0  12.0",
      "M20   20.0   2.50  21.0  22.0  24.0   30.0  20.0  17.0  35.0  11.00 12.0  36.0  30.0  12.5  40.0  15.0",
      "M24   24.0   3.00  25.0  26.0  28.0   36.0  24.0  19.0  42.0  13.20 14.0  43.0  36.0  15.0  48.0  18.0"
    };

    // =======================================================================
    //  inch     name  d  tpiUNC tpiUNF  clearC clearN clearL  capDk capK capS
    //                 btnDk btnK btnS  cskDk  hexS hexK  panDk panK
    //    cap and csk = ASME B18.3 (csk head angle 82 deg).  hex = B18.2.1 above
    //    1/4, hex machine screw below.  button = ASME B18.3 button head socket
    //    cap screw.  pan = ANSI/ASME B18.6.3 pan head machine screw, which stops
    //    at 3/4 - the 1" row is the only extrapolated one in here.
    //    "-" = the standard does not list that size.
    // =======================================================================
    string[] INC = new string[]{
    // name   d       UNC  UNF   clC     clN     clL     capDk  capK   capS   btnDk  btnK   btnS   cskDk  hexS   hexK   panDk  panK
      "#0    0.0600    -   80   0.0670  0.0730  0.0760  0.096  0.060  0.050  0.114  0.032  0.035  0.138  0.125  0.044  0.116  0.039",
      "#1    0.0730   64   72   0.0810  0.0860  0.0890  0.118  0.073  0.062  0.139  0.039  0.050  0.168  0.156  0.050  0.142  0.046",
      "#2    0.0860   56   64   0.0938  0.0990  0.1040  0.140  0.086  0.078  0.164  0.046  0.050  0.197  0.188  0.055  0.167  0.053",
      "#3    0.0990   48   56   0.1065  0.1130  0.1160  0.161  0.099  0.078  0.188  0.052  0.062  0.226  0.188  0.060  0.193  0.060",
      "#4    0.1120   40   48   0.1160  0.1285  0.1360  0.183  0.112  0.094  0.213  0.059  0.062  0.255  0.188  0.050  0.219  0.068",
      "#5    0.1250   40   44   0.1360  0.1405  0.1495  0.205  0.125  0.094  0.238  0.066  0.078  0.281  0.219  0.060  0.245  0.075",
      "#6    0.1380   32   40   0.1440  0.1495  0.1590  0.226  0.138  0.109  0.262  0.073  0.078  0.307  0.250  0.068  0.270  0.082",
      "#8    0.1640   32   36   0.1695  0.1770  0.1850  0.270  0.164  0.141  0.312  0.087  0.094  0.359  0.281  0.084  0.322  0.096",
      "#10   0.1900   24   32   0.1960  0.2010  0.2130  0.312  0.190  0.156  0.361  0.101  0.125  0.411  0.312  0.100  0.373  0.110",
      "#12   0.2160   24   28   0.2210  0.2280  0.2420  0.343  0.216  0.156  0.410  0.114  0.125  0.462  0.344  0.111  0.425  0.125",
      "1/4   0.2500   20   28   0.2570  0.2660  0.2770  0.375  0.250  0.188  0.437  0.132  0.156  0.531  0.438  0.156  0.492  0.144",
      "5/16  0.3125   18   24   0.3230  0.3320  0.3480  0.469  0.312  0.250  0.547  0.166  0.188  0.656  0.500  0.203  0.615  0.178",
      "3/8   0.3750   16   24   0.3860  0.3970  0.4130  0.562  0.375  0.312  0.656  0.199  0.250  0.781  0.562  0.234  0.740  0.212",
      "7/16  0.4375   14   20   0.4531  0.4687  0.4843  0.656  0.438  0.375  0.766  0.232  0.250  0.844  0.625  0.281  0.863  0.247",
      "1/2   0.5000   13   20   0.5156  0.5312  0.5625  0.750  0.500  0.375  0.875  0.265  0.312  0.938  0.750  0.312  0.987  0.281",
      "5/8   0.6250   11   18   0.6406  0.6562  0.6875  0.938  0.625  0.500  1.094  0.331  0.375  1.188  0.938  0.391  1.172  0.350",
      "3/4   0.7500   10   16   0.7656  0.7812  0.8125  1.125  0.750  0.625  1.312  0.397  0.500  1.438  1.125  0.469  1.435  0.419",
      "1     1.0000    8   12   1.0156  1.0312  1.0625  1.500  1.000  0.750  1.750  0.530  0.625  1.938  1.500  0.641  1.970  0.560"
    };

    // =======================================================================
    //  PARSE THE SIZE STRING
    // =======================================================================
    string raw = (Size == null) ? "" : Size.Trim().ToUpperInvariant();
    if (raw.Length == 0) raw = "M5";
    raw = raw.Replace("UNC", "").Replace("UNF", "").Replace("UN", "").Trim();

    bool   metric  = raw.StartsWith("M") && raw.Length > 1 &&
                     (char.IsDigit(raw[1]) || raw[1] == '.');
    double wantDia = 0.0;          // in the native unit of the family
    double wantP   = 0.0;          // 0 = use the table default
    string canon   = raw;

    if (metric)
    {
      string body = raw.Substring(1);
      string[] mp = body.Split(new char[]{'X', '*'}, StringSplitOptions.RemoveEmptyEntries);
      if (!double.TryParse(mp[0], System.Globalization.NumberStyles.Float, IC, out wantDia))
      { Info = "Could not read the metric size \"" + Size + "\".  Try M5 or M8x1."; return; }
      if (mp.Length > 1) double.TryParse(mp[1], System.Globalization.NumberStyles.Float, IC, out wantP);
    }
    else
    {
      string body = raw.Replace("\"", "");
      string[] ip = body.Split(new char[]{'-'}, StringSplitOptions.RemoveEmptyEntries);
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
        { Info = "Could not read the inch size \"" + Size + "\".  Try 1/4-20, #8-32 or 3/8."; return; }
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

    // ---- find the row -------------------------------------------------------
    string[] tbl = metric ? MET : INC;
    int      row = -1;
    double   bestErr = 1e9;
    for (int i = 0; i < tbl.Length; i++)
    {
      string[] f = tbl[i].Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
      double dv = double.Parse(f[1], IC);
      double e  = Math.Abs(dv - wantDia);
      if (e < bestErr) { bestErr = e; row = i; }
    }
    if (row < 0 || bestErr > (metric ? 0.30 : 0.012))
    { Info = "\"" + Size + "\" is not in the table.  Metric M1.6-M24, inch #0-1\"."; return; }

    string[] fld = tbl[row].Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
    string   nm  = fld[0];
    double   d   = double.Parse(fld[1], IC);

    int o = metric ? 1 : 2;                       // inch has two pitch columns
    double P;
    if (metric) P = double.Parse(fld[2], IC);
    else
    {
      string uc = fld[2], uf = fld[3];
      double t2;
      if (uc != "-" && double.TryParse(uc, System.Globalization.NumberStyles.Float, IC, out t2)) P = 1.0 / t2;
      else { double.TryParse(uf, System.Globalization.NumberStyles.Float, IC, out t2); P = 1.0 / t2; }
    }
    if (wantP > 1e-9) P = wantP;                  // explicit pitch wins

    double clC = double.Parse(fld[o + 2], IC);
    double clN = double.Parse(fld[o + 3], IC);
    double clL = double.Parse(fld[o + 4], IC);
    double capDk = double.Parse(fld[o + 5], IC), capK = double.Parse(fld[o + 6], IC), capS = double.Parse(fld[o + 7], IC);
    double btnDk = double.Parse(fld[o + 8], IC), btnK = double.Parse(fld[o + 9], IC), btnS = double.Parse(fld[o + 10], IC);
    double cskDk = double.Parse(fld[o + 11], IC);
    double hexS  = double.Parse(fld[o + 12], IC), hexK = double.Parse(fld[o + 13], IC);
    double panDk = double.Parse(fld[o + 14], IC), panK = double.Parse(fld[o + 15], IC);

    // canonical name, for the Spec output and for ThreadTap
    if (metric)
    {
      double pc = double.Parse(fld[2], IC);
      canon = nm + (Math.Abs(P - pc) > 1e-6 ? "x" + P.ToString("0.###", IC) : "");
    }
    else
    {
      double tpiNow = 1.0 / P;
      canon = nm + "-" + tpiNow.ToString("0.##", IC);
    }

    // ---- unit scale ---------------------------------------------------------
    double uf2 = 1.0;
    if (Rhino.RhinoDoc.ActiveDoc != null)
    {
      UnitSystem doc = Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem;
      if (doc != UnitSystem.None && doc != UnitSystem.Unset)
        uf2 = RhinoMath.UnitScale(metric ? UnitSystem.Millimeters : UnitSystem.Inches, doc);
    }
    if (uf2 <= 0.0 || double.IsNaN(uf2)) uf2 = 1.0;

    double mm = 1.0;                              // one millimetre, in document units
    if (Rhino.RhinoDoc.ActiveDoc != null)
    {
      UnitSystem doc2 = Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem;
      if (doc2 != UnitSystem.None && doc2 != UnitSystem.Unset)
        mm = RhinoMath.UnitScale(UnitSystem.Millimeters, doc2);
    }
    if (mm <= 0.0 || double.IsNaN(mm)) mm = 1.0;

    d *= uf2; P *= uf2; clC *= uf2; clN *= uf2; clL *= uf2;
    capDk *= uf2; capK *= uf2; capS *= uf2;
    btnDk *= uf2; btnK *= uf2; btnS *= uf2;
    cskDk *= uf2; hexS *= uf2; hexK *= uf2; panDk *= uf2; panK *= uf2;

    // =======================================================================
    //  OPTIONS
    // =======================================================================
    string hs = (Head == null) ? "socket" : Head.Trim().ToLowerInvariant();
    int hd = 0;                                   // 0 cap 1 button 2 csk 3 hex 4 pan 5 none
    if (hs.StartsWith("but") || hs.StartsWith("dome") || hs.StartsWith("bhc")) hd = 1;
    else if (hs.StartsWith("fla") || hs.StartsWith("csk") || hs.StartsWith("cou") || hs.StartsWith("fhc")) hd = 2;
    else if (hs.StartsWith("hex") && !hs.Contains("soc")) hd = 3;
    else if (hs.StartsWith("pan") || hs.StartsWith("che") || hs.StartsWith("rou")) hd = 4;
    else if (hs.StartsWith("non") || hs.StartsWith("stu") || hs.StartsWith("rod") ||
             hs.StartsWith("set") || hs.StartsWith("gru") || hs.StartsWith("thr")) hd = 5;

    string fs = (Fit == null) ? "normal" : Fit.Trim().ToLowerInvariant();
    double clr = clN; string fitName = "normal (ISO 273 medium)";
    if (fs.StartsWith("cl") || fs.StartsWith("fi") || fs.StartsWith("ti"))
    { clr = clC; fitName = "close (ISO 273 fine)"; }
    else if (fs.StartsWith("lo") || fs.StartsWith("co") || fs.StartsWith("fr"))
    { clr = clL; fitName = "loose (ISO 273 coarse)"; }
    if (!metric) fitName = fitName.Replace("ISO 273 ", "").Replace("fine", "close drill")
                                  .Replace("medium", "free drill").Replace("coarse", "loose drill");

    string rs = (Recess == null) ? "auto" : Recess.Trim().ToLowerInvariant();
    int rc2 = 3;                                  // 0 none 1 counterbore 2 countersink 3 auto
    if (rs.StartsWith("no")) rc2 = 0;
    else if (rs.StartsWith("bo") || rs.StartsWith("cb") || rs.StartsWith("counterb")) rc2 = 1;
    else if (rs.StartsWith("si") || rs.StartsWith("cs") || rs.StartsWith("counters")) rc2 = 2;
    if (rc2 == 3) rc2 = (hd == 2) ? 2 : 0;        // flat heads sink, everything else sits proud

    int thr = Threads;
    if (thr < 0 || thr > 2) thr = 1;

    // ---- Opts : space separated key=value, every one optional ---------------
    int    nSeg    = 96;
    double optSink = 0.0;
    double optPrep = 0.0;
    string orw = (Opts == null) ? "" : Opts.Trim().ToLowerInvariant();
    if (orw.Length > 0)
    {
      string[] toks = orw.Split(new char[]{' ', ',', ';', '\t', '\n', '\r'},
                                StringSplitOptions.RemoveEmptyEntries);
      foreach (string t in toks)
      {
        int eq = t.IndexOf('=');
        if (eq < 1 || eq == t.Length - 1)
        { log.Add("Opts: \"" + t + "\" is not key=value - ignored."); continue; }
        string ky = t.Substring(0, eq), vl = t.Substring(eq + 1);
        double vv;
        if (!double.TryParse(vl, System.Globalization.NumberStyles.Float, IC, out vv))
        { log.Add("Opts: \"" + t + "\" has no number - ignored."); continue; }
        if      (ky == "seg"  || ky == "segments") nSeg    = (int)vv;
        else if (ky == "sink")                     optSink = vv;
        else if (ky == "prep" || ky == "preplen")  optPrep = vv;
        else if (ky == "slot" || ky == "af" || ky == "s" || ky == "h" || ky == "height" ||
                 ky == "chamfer" || ky == "bore")
          log.Add("Opts: \"" + ky + "\" is a NutGenerator key - this is ScrewGenerator, which takes " +
                  "sink, prep and seg. The two Opts inputs are not interchangeable.");
        else log.Add("Opts: unknown key \"" + ky + "\" - ignored. Valid: sink, prep, seg.");
      }
    }
    if (nSeg <= 0) nSeg = 96;
    nSeg = Math.Max(32, Math.Min(720, nSeg));
    nSeg = 16 * (int)Math.Round(nSeg / 16.0);     // put the profile breaks on samples

    double xt = (Tol > 0.0) ? Tol : 0.0;          // extra radial clearance on Prep

    // =======================================================================
    //  HEAD SIZES
    // =======================================================================
    double dk = capDk, k = capK, sock = capS;
    double tanH = metric ? 1.0 : Math.Tan(RhinoMath.ToRadians(41.0));   // 90 vs 82 deg included
    // The land under a countersunk head.  It exists ONLY to keep the annular
    // face between the cone's bottom edge and the shank from being zero-width,
    // so it wants to be as small as the model tolerance allows - every bit of
    // it comes straight off the modelled head height.  It was 0.04 d, which
    // cost an M6 head 0.24 of its 3.3 mm.
    double eps  = Math.Max(4.0 * tol, 0.01 * d);
    double Rc   = 0.5 * d;                        // crest radius
    double H    = 0.8660254037844386 * P;         // fundamental triangle
    double Rr   = Rc - 0.625 * H;                 // root radius, = 0.5*(d - 1.0825P)

    if (Rr <= 0.05 * d) { Info = "Pitch " + P.ToString("0.###") + " is too coarse for " + nm + "."; return; }

    if (hd == 1) { dk = btnDk; k = btnK;  sock = btnS; }
    else if (hd == 2) { dk = cskDk; sock = capS; k = (0.5 * dk - Rc - eps) / tanH; }
    else if (hd == 3) { dk = hexS;  k = hexK;  sock = 0.0; }
    else if (hd == 4) { dk = panDk; k = panK;  sock = 0.0; }
    else if (hd == 5) { dk = 0.0;   k = 0.0;   sock = 0.0; }

    if (hd == 2 && k <= 1e-9) { Info = "Countersunk head does not resolve for " + nm + "."; return; }
    if (hd == 1 && (btnDk <= 0.0)) log.Add("Button head data for " + nm + " is extrapolated, not tabulated.");
    if (hd == 4 && !metric && d > 0.75 * 25.4 * mm)
      log.Add("ANSI B18.6.3 stops at 3/4 - this pan head is extrapolated past the standard.");
    if (hd == 1 && metric && d > 16.0 * uf2) log.Add("Button head above M16 is extrapolated.");
    if (hd == 4 && metric && d > 10.0 * uf2) log.Add("Pan head above M10 is extrapolated.");

    // =======================================================================
    //  LENGTHS
    // =======================================================================
    double L = (Length > 1e-9) ? Length : 4.0 * d;
    double shankLen = (hd == 2) ? L - k : L;      // countersunk length includes the head
    if (shankLen <= 0.5 * d)
    { Info = "Length " + L.ToString("0.##") + " leaves no shank under a " +
             k.ToString("0.##") + " tall head."; return; }

    double thrL = (ThreadLen > 1e-9) ? Math.Min(ThreadLen, shankLen) : shankLen;
    if (thr == 0) thrL = 0.0;
    double plainL = shankLen - thrL;

    double tip  = Math.Min(Rc - Rr, 0.3 * shankLen);          // 45 deg point chamfer
    if (thrL > 1e-9) tip = Math.Min(tip, 0.4 * thrL);         // never eat the whole thread
    else             tip = 0.0;
    double run  = Math.Min(P, 0.3 * thrL);                    // thread run-out under the shank
    if (plainL <= 1e-9) run = 0.0;                            // fully threaded: no run-out needed
    else run = Math.Min(run, 0.5 * plainL);

    // =======================================================================
    //  THREAD SECTION.  radius against angle at z = 0, for a right-hand start.
    //  One pitch of the basic profile, measured from the middle of a crest:
    //     0    .. 1/16   crest flat  (P/8 wide)
    //     1/16 .. 3/8    flank down  (30 deg off the axis)
    //     3/8  .. 5/8    root  flat  (P/4 wide)
    //     5/8  .. 15/16  flank up
    //     15/16.. 1      crest flat
    // =======================================================================
    double[] rad = new double[nSeg];
    for (int i = 0; i < nSeg; i++)
    {
      double u = 1.0 - (double)i / nSeg;
      if (u >= 1.0) u -= 1.0;
      double r;
      if      (u <= 0.0625 || u >= 0.9375) r = Rc;
      else if (u <  0.375)                 r = Rc - (u - 0.0625) / 0.3125 * (Rc - Rr);
      else if (u <= 0.625)                 r = Rr;
      else                                 r = Rr + (u - 0.625) / 0.3125 * (Rc - Rr);
      rad[i] = r;
    }

    // which way does CreateSpiral wind?  ask it, do not assume.
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
    bool wantRH = (thr != 2);
    bool mirror = (thr != 0) && (wantRH != rhAPI);

    // =======================================================================
    //  SHANK.  Four zones stacked head-to-tip, every zone one CreateSpiral per
    //  rail, every rail a constant- or linearly-tapering-radius helix.  The
    //  zones share their boundary curves exactly, so the skin JOINS - no
    //  Boolean anywhere in the shank.
    //     Z1  plain      all rails at Rc          (skipped when fully threaded)
    //     Z2  run-out    Rc -> the thread profile (skipped when fully threaded)
    //     Z3  thread     the profile, constant
    //     Z4  point      profile -> flat at Rr    (45 deg chamfer)
    // =======================================================================
    List<double[]> zr0 = new List<double[]>(), zr1 = new List<double[]>();
    List<double>   zz0 = new List<double>(),   zz1 = new List<double>();

    double[] flat = new double[nSeg];
    double[] point = new double[nSeg];
    for (int i = 0; i < nSeg; i++)
    {
      flat[i]  = Rc;
      point[i] = Math.Max(Rr, rad[i] - tip);
    }

    double zc = 0.0;
    if (plainL > 1e-9)
    {
      zz0.Add(zc); zz1.Add(zc + plainL - run); zr0.Add(flat); zr1.Add(flat);   // Z1
      zc += plainL - run;
      if (run > 1e-9) { zz0.Add(zc); zz1.Add(zc + run); zr0.Add(flat); zr1.Add(rad); zc += run; }  // Z2
    }
    if (thrL > 1e-9)
    {
      zz0.Add(zc); zz1.Add(shankLen - tip); zr0.Add(rad); zr1.Add(rad);        // Z3
      zz0.Add(shankLen - tip); zz1.Add(shankLen); zr0.Add(rad); zr1.Add(point);// Z4
    }
    else
    {
      zz0.Add(zc); zz1.Add(shankLen); zr0.Add(flat); zr1.Add(flat);            // plain preview
    }

    List<Brep> skin = new List<Brep>();
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
        if (rail[i] == null) { Info = "Helix failed - check Size and Length."; return; }
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
    if (skin.Count == 0) { Info = "Could not build the shank skin."; return; }

    // The shank's top rim, built from the very points the skin starts on.  It
    // is a circle of Rc when there is a plain shank and the thread section when
    // the screw is threaded to the head - either way the head stitches to it.
    double[] r0z = zr0[0];
    Point3d[] rim = new Point3d[nSeg + 1];
    double rimMax = 0.0;
    for (int i = 0; i < nSeg; i++)
    {
      double a = 2.0 * Math.PI * i / nSeg;
      rim[i] = new Point3d(r0z[i] * Math.Cos(a), r0z[i] * Math.Sin(a), 0.0);
      if (r0z[i] > rimMax) rimMax = r0z[i];
    }
    rim[nSeg] = rim[0];
    Curve topLoop = new Polyline(rim).ToPolylineCurve();

    // =======================================================================
    //  HEAD.  Lateral surface only - the underside is an annulus stitched
    //  straight onto the shank rim, so head and shank join without a Boolean.
    // =======================================================================
    List<Brep> hdF = new List<Brep>();
    Line axis = new Line(new Point3d(0, 0, -1.0), new Point3d(0, 0, 1.0));

    if (hd == 3)                                     // ---- external hex ----
    {
      double Rhex = hexS / Math.Sqrt(3.0);           // circumradius from across-flats
      double hc   = 0.10 * k;                        // top chamfer
      Point3d[] hxA = new Point3d[7], hxB = new Point3d[7];
      for (int i = 0; i < 6; i++)
      {
        double a = Math.PI / 3.0 * i;
        hxA[i] = new Point3d(Rhex * Math.Cos(a), Rhex * Math.Sin(a), 0.0);
        hxB[i] = new Point3d(Rhex * Math.Cos(a), Rhex * Math.Sin(a), -k + hc);
      }
      hxA[6] = hxA[0]; hxB[6] = hxB[0];
      Curve hxLo = new Polyline(hxA).ToPolylineCurve();
      Curve hxHi = new Polyline(hxB).ToPolylineCurve();
      Brep[] side = Brep.CreateFromLoft(new Curve[]{hxLo, hxHi}, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
      if (side != null && side.Length > 0) hdF.Add(side[0]);

      Point3d[] hxT = new Point3d[7];
      for (int i = 0; i < 6; i++)
      {
        double a = Math.PI / 3.0 * i;
        hxT[i] = new Point3d(0.93 * Rhex * Math.Cos(a), 0.93 * Rhex * Math.Sin(a), -k);
      }
      hxT[6] = hxT[0];
      Curve hxTop = new Polyline(hxT).ToPolylineCurve();
      Brep[] cham = Brep.CreateFromLoft(new Curve[]{hxHi, hxTop}, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
      if (cham != null && cham.Length > 0) hdF.Add(cham[0]);
      Brep[] capT = Brep.CreatePlanarBreps(new Curve[]{hxTop}, tol);
      if (capT != null && capT.Length > 0) hdF.Add(capT[0]);

      Brep[] ann = Brep.CreatePlanarBreps(new Curve[]{hxLo, topLoop}, tol);
      if (ann != null && ann.Length > 0) hdF.Add(ann[0]); else log.Add("Head underside failed.");
    }
    else if (hd != 5)                                // ---- revolved heads ----
    {
      PolyCurve sil = new PolyCurve();
      double rBot = 0.5 * dk;                        // radius where the head meets z = 0

      if (hd == 0)                                   // socket cap: chamfered cylinder
      {
        double ch = 0.055 * dk;
        sil.Append(new LineCurve(new Point3d(0.5 * dk - ch, 0, -k), new Point3d(0.5 * dk, 0, -k + ch)));
        sil.Append(new LineCurve(new Point3d(0.5 * dk, 0, -k + ch), new Point3d(0.5 * dk, 0, 0)));
        Brep[] ct = Brep.CreatePlanarBreps(
          new Curve[]{ new ArcCurve(new Circle(new Plane(new Point3d(0, 0, -k), Vector3d.ZAxis), 0.5 * dk - ch)) }, tol);
        if (ct != null && ct.Length > 0) hdF.Add(ct[0]);
      }
      else if (hd == 2)                              // countersunk: plain cone
      {
        rBot = rimMax + eps;
        sil.Append(new LineCurve(new Point3d(0.5 * dk, 0, -k), new Point3d(rBot, 0, 0)));
        Brep[] ct = Brep.CreatePlanarBreps(
          new Curve[]{ new ArcCurve(new Circle(new Plane(new Point3d(0, 0, -k), Vector3d.ZAxis), 0.5 * dk)) }, tol);
        if (ct != null && ct.Length > 0) hdF.Add(ct[0]);
      }
      else                                           // button / pan: dome on a land
      {
        double hdm  = (hd == 1) ? 0.80 * k : 0.70 * k;
        double rTop = (hd == 1) ? 0.0 : 0.50 * (0.5 * dk);
        Arc arc = new Arc(new Point3d(rTop, 0, -k), new Vector3d(1, 0, 0), new Point3d(0.5 * dk, 0, -k + hdm));
        if (!arc.IsValid) { Info = "Dome arc failed for " + nm + "."; return; }
        sil.Append(new ArcCurve(arc));
        sil.Append(new LineCurve(new Point3d(0.5 * dk, 0, -k + hdm), new Point3d(0.5 * dk, 0, 0)));
        if (rTop > 1e-9)
        {
          Brep[] ct = Brep.CreatePlanarBreps(
            new Curve[]{ new ArcCurve(new Circle(new Plane(new Point3d(0, 0, -k), Vector3d.ZAxis), rTop)) }, tol);
          if (ct != null && ct.Length > 0) hdF.Add(ct[0]);
        }
      }

      if (rBot <= rimMax + 1e-9)
      { Info = "Head " + dk.ToString("0.###") + " is not wider than the shank - pick a bigger head."; return; }

      RevSurface rev = RevSurface.Create(sil, axis);
      if (rev == null) { Info = "Head revolve failed."; return; }
      Brep rb = Brep.CreateFromRevSurface(rev, false, false);
      if (rb == null) { Info = "Head revolve failed."; return; }
      hdF.Add(rb);

      Circle bc = new Circle(Plane.WorldXY, rBot);
      Brep[] ann = Brep.CreatePlanarBreps(new Curve[]{ new ArcCurve(bc), topLoop }, tol);
      if (ann != null && ann.Length > 0) hdF.Add(ann[0]); else log.Add("Head underside failed.");
    }

    // =======================================================================
    //  JOIN
    // =======================================================================
    List<Brep> all = new List<Brep>();
    all.AddRange(skin); all.AddRange(hdF);
    Brep[] jn = Brep.JoinBreps(all, tol);
    if (jn == null || jn.Length == 0) { Info = "Join failed - try more Segments or a coarser model tolerance."; return; }

    Brep screw = jn[0];
    if (jn.Length > 1)
    {
      double bv = -1.0;
      foreach (Brep b in jn) { double v = b.GetBoundingBox(true).Volume; if (v > bv) { bv = v; screw = b; } }
      log.Add("Skin came back in " + jn.Length + " pieces - the biggest was kept.");
    }
    if (!screw.IsSolid)
    {
      Brep cp = screw.CapPlanarHoles(tol);
      if (cp != null) screw = cp;
    }
    if (!screw.IsSolid) log.Add("Screw is not watertight - raise Segments or loosen the model tolerance.");

    // ORIENTATION FIRST, BEFORE ANY BOOLEAN.  A difference against an
    // inward-oriented solid does not subtract - it operates on the complement,
    // and the drive recess comes out as a lump ON the head instead of a pocket
    // IN it.  JoinBreps picks its own orientation and which way it lands
    // depends on the face set, so a countersunk head can differ from a cap.
    if (screw.SolidOrientation == BrepSolidOrientation.Inward) screw.Flip();

    // ---- drive recess -------------------------------------------------------
    if (sock > 1e-9 && hd != 5 && k > 1e-9)
    {
      double tdep = k * (hd == 2 ? 0.80 : (hd == 1 ? 0.60 : 0.55));
      double Rs   = sock / Math.Sqrt(3.0);
      double ov   = 0.2 * k;
      Point3d[] hx = new Point3d[7];
      for (int i = 0; i < 6; i++)
      {
        double a = Math.PI / 3.0 * i;
        hx[i] = new Point3d(Rs * Math.Cos(a), Rs * Math.Sin(a), -k - ov);
      }
      hx[6] = hx[0];
      Extrusion ex = Extrusion.Create(new Polyline(hx).ToPolylineCurve(), tdep + ov, true);
      if (ex != null)
      {
        double vWas = screw.IsSolid ? screw.GetVolume() : 0.0;
        Brep[] df = Brep.CreateBooleanDifference(screw, ex.ToBrep(), tol);
        if (df != null && df.Length > 0)
        {
          Brep best = df[0]; double bv = -1.0;
          foreach (Brep b in df) { double v = b.GetBoundingBox(true).Volume; if (v > bv) { bv = v; best = b; } }
          double vNow = best.IsSolid ? best.GetVolume() : 0.0;
          // a difference that did not shrink the solid went the wrong way round
          if (vWas > 0.0 && vNow > 0.0 && vNow >= vWas)
            log.Add("The hex socket Boolean added material instead of removing it - " +
                    "head left solid. Please report this with the Size and Head you used.");
          else screw = best;
        }
        else log.Add("Hex socket Boolean failed - head left solid.");
      }
    }
    else if (hd == 4 && k > 1e-9)                   // pan head gets a straight slot
    {
      double sw = 0.17 * d, sd = 0.45 * k, ov = 0.2 * k;
      Interval ix = new Interval(-0.72 * dk, 0.72 * dk);
      Interval iy = new Interval(-0.5 * sw, 0.5 * sw);
      Interval iz = new Interval(-k - ov, -k + sd);
      Brep slot = Brep.CreateFromBox(new Box(Plane.WorldXY, ix, iy, iz));
      double vWas = screw.IsSolid ? screw.GetVolume() : 0.0;
      Brep[] df = Brep.CreateBooleanDifference(screw, slot, tol);
      if (df != null && df.Length > 0)
      {
        Brep best = df[0]; double bv = -1.0;
        foreach (Brep b in df) { double v = b.GetBoundingBox(true).Volume; if (v > bv) { bv = v; best = b; } }
        double vNow = best.IsSolid ? best.GetVolume() : 0.0;
        if (vWas > 0.0 && vNow > 0.0 && vNow >= vWas)
          log.Add("The slot Boolean added material instead of removing it - head left solid.");
        else screw = best;
      }
      else log.Add("Slot Boolean failed - head left solid.");
    }

    if (mirror) screw.Transform(Transform.Mirror(Plane.WorldZX));   // a mirror flips it again
    if (screw.SolidOrientation == BrepSolidOrientation.Inward) screw.Flip();

    // =======================================================================
    //  PREP.  Fully axisymmetric, so it is one revolve - a bore, and where a
    //  recess is asked for, a counterbore cylinder or a countersink cone on
    //  top of it.  Everything above z = 0 is material you take off to seat
    //  the head; everything below is the through hole.
    // =======================================================================
    double rBore = 0.5 * clr + xt;
    // prep= is measured BELOW PLN, the same as everything else you can see, so
    // on a countersunk head - where the build frame starts k above Pln and the
    // whole thing is shifted down at the end - it has to lose that k here.
    double zShift = (hd == 2) ? k : 0.0;
    double pLen   = (optPrep > 1e-9) ? optPrep - zShift : shankLen + 2.0 * mm;
    if (pLen <= 0.5 * d)
    {
      pLen = shankLen + 2.0 * mm;
      log.Add("Opts prep=" + optPrep.ToString("0.###") + " does not even clear the head - " +
              "ignored, using " + (pLen + zShift).ToString("0.###") + " below the face.");
    }
    double snk   = (optSink > 0.0) ? optSink : 0.0;
    double over  = 0.5 * mm;                         // break-out past the part face

    List<Point3d> pp = new List<Point3d>();
    string prepTxt;

    if (rc2 == 2 && 0.5 * (hd == 2 ? cskDk : dk) + xt <= rBore + 0.02 * d)
    { rc2 = 0; log.Add("The clearance bore is already as wide as the head - countersink dropped."); }

    if (rc2 == 1 && hd != 5 && k > 1e-9)             // ---- counterbore ----
    {
      double rCb = 0.5 * dk + xt;
      if (hd == 3) rCb = 0.5 * (hexS / Math.Sqrt(3.0)) * 2.0 + xt;   // clear the corners
      double zTop = -(k + snk + over);
      double ch   = Math.Min(0.4 * (rCb - rBore), 0.10 * d);
      pp.Add(new Point3d(rCb,       0, zTop));
      pp.Add(new Point3d(rCb,       0, 0.0));
      pp.Add(new Point3d(rBore + ch, 0, 0.0));
      pp.Add(new Point3d(rBore,     0, ch));
      pp.Add(new Point3d(rBore,     0, pLen));
      prepTxt = "counterbore dia " + (2.0 * rCb).ToString("0.###") + " x " +
                (k + snk).ToString("0.###") + " deep";
    }
    else if (rc2 == 2 && k > 1e-9)                   // ---- countersink ----
    {
      double rCs  = 0.5 * (hd == 2 ? cskDk : dk) + xt;
      double zTop = -(k + snk + over);
      // same cone angle as the head, so the head seats on the cone, not on an edge
      double rAtTop = rCs + over * tanH;
      double zMeet  = -(k + snk) + (rCs - rBore) / tanH;
      if (zMeet + 0.5 * mm > pLen) pLen = zMeet + 0.5 * mm;
      pp.Add(new Point3d(rAtTop, 0, zTop));
      pp.Add(new Point3d(rBore,  0, zMeet));
      pp.Add(new Point3d(rBore,  0, pLen));
      prepTxt = "countersink dia " + (2.0 * rCs).ToString("0.###") + " at " +
                (metric ? "90" : "82") + " deg included";
    }
    else                                             // ---- plain through hole ----
    {
      pp.Add(new Point3d(rBore, 0, -over));
      pp.Add(new Point3d(rBore, 0, pLen));
      prepTxt = "no head recess - the head sits proud";
    }

    Brep prep = null;
    RevSurface prv = RevSurface.Create(new Polyline(pp).ToPolylineCurve(), axis);
    if (prv != null)
    {
      Brep pb = Brep.CreateFromRevSurface(prv, true, true);
      if (pb != null)
      {
        if (!pb.IsSolid) { Brep c2 = pb.CapPlanarHoles(tol); if (c2 != null) pb = c2; }
        if (pb.SolidOrientation == BrepSolidOrientation.Inward) pb.Flip();
        prep = pb;
      }
    }
    if (prep == null) log.Add("Prep revolve failed.");

    // =======================================================================
    //  PLACE.  Local +Z runs into the part, so the target frame is Pln spun
    //  180 deg about its own X - a proper rotation, the thread keeps its hand.
    // =======================================================================
    // A COUNTERSUNK HEAD SITS FLUSH, so its datum is the head TOP - that IS the
    // part face.  Every other head bears its underside on the face.  Built with
    // the cone/shank junction at zero, so shift the whole thing down by k and
    // the rule becomes the same one for all of them: Pln is the part's face.
    // Without this, two sizes with different head heights line up on a junction
    // that sits a different depth below the face in each, so their tops AND
    // their tips disagree even though both are the same total length.
    if (hd == 2)
    {
      Transform dn = Transform.Translation(0.0, 0.0, k);
      screw.Transform(dn);
      if (prep != null) prep.Transform(dn);
    }

    Plane bp = Pln.IsValid ? Pln : Plane.WorldXY;
    Plane tp = new Plane(bp.Origin, bp.XAxis, -bp.YAxis);
    Transform xf = Transform.PlaneToPlane(Plane.WorldXY, tp);
    screw.Transform(xf);
    if (prep != null) prep.Transform(xf);

    // =======================================================================
    //  REPORT
    // =======================================================================
    double d2 = d - 0.6495 * P;                     // pitch diameter
    double d1 = 2.0 * Rr;                           // basic minor, exactly as built
    double turnsTot = thrL / P;
    string unitTxt = "";
    if (Rhino.RhinoDoc.ActiveDoc != null) unitTxt = " " + Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem.ToString().ToLowerInvariant();

    if (turnsTot * nSeg > 60000.0)
      log.Add("This is " + (turnsTot * nSeg).ToString("0") + " thread samples. Drop Segments or ThreadLen if it crawls.");
    if (thrL > 1e-9 && thrL < 0.8 * d)
      log.Add("Threaded length is under one diameter - very little engagement.");
    string hName = (hd == 0 ? "socket" : hd == 1 ? "button" : hd == 2 ? "countersunk" :
                    hd == 3 ? "hex" : hd == 4 ? "pan" : "headless");
    if (rc2 == 1 && hd == 2)
      log.Add("Recess \"bore\" under a countersunk head: a flat-bottomed pocket cannot seat a " +
              "cone. Use \"sink\", or \"auto\".");
    if (rc2 == 2 && hd != 2 && hd != 5)
      log.Add("Recess \"sink\" under a " + hName + " head: that head bears on a FLAT underside " +
              "and a cone will not seat it - it will sit on the rim and rock. Use \"bore\" to " +
              "sink it, \"none\" to leave it proud, or \"auto\" to be picked for. " +
              "(Left over from a flat-head test?)");
    if (rc2 == 2 && hd == 5)
      log.Add("Recess \"sink\" on a headless part does nothing useful - there is no head to seat.");
    if (xt > 0.35 * (clr - d)) log.Add("Tol is large next to the Fit clearance - check the hole is not sloppy.");

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.AppendLine(canon + "   " + (metric ? "ISO metric" : "Unified inch") +
                  (thr == 0 ? "   PLAIN SHANK (no thread modelled)" : (thr == 2 ? "   LEFT HAND" : "   right hand")));
    sb.AppendLine("head                " +
      (hd == 0 ? "socket cap (ISO 4762 / B18.3)" : hd == 1 ? "button (ISO 7380)" :
       hd == 2 ? "countersunk (" + (metric ? "ISO 10642, 90" : "B18.3, 82") + " deg)" :
       hd == 3 ? "hex (ISO 4017 / B18.2.1)" : hd == 4 ? "pan (ISO 7045)" : "none - stud / rod"));
    sb.AppendLine("--");
    sb.AppendLine("major d             " + d.ToString("0.####") + unitTxt);
    sb.AppendLine("pitch P             " + P.ToString("0.####") +
                  (metric ? "" : "   (" + (1.0 / P * uf2).ToString("0.#") + " tpi)"));
    sb.AppendLine("pitch dia d2        " + d2.ToString("0.####"));
    sb.AppendLine("minor dia d1        " + d1.ToString("0.####") + "   (basic, root flat P/4)");
    sb.AppendLine("thread depth        " + (Rc - Rr).ToString("0.####") + " per flank");
    if (k > 1e-9)
      sb.AppendLine("head                dia " + dk.ToString("0.###") + ", " + k.ToString("0.###") + " tall" +
                    (hd == 2 ? " at " + (metric ? "90" : "82") + " deg included" : "") +
                    (sock > 1e-9 ? ", hex socket " + sock.ToString("0.##") : ""));
    if (hd == 2)
    {
      sb.AppendLine("   head height is the CONE height worked out from dk. The standard lists");
      sb.AppendLine("   k a little larger - that extra is the blend at the bottom of the cone,");
      sb.AppendLine("   which a cone cannot have. Diameter and angle are exact, so a real screw");
      sb.AppendLine("   still seats flush in the countersink. SPEC.md section 4.");
      sb.AppendLine("   Pln is the head TOP, which is the part face - a flat head sits flush.");
      sb.AppendLine("   Length " + L.ToString("0.###") + " is TOTAL, head included, so the tip is " +
                    L.ToString("0.###") + " below Pln. Two sizes of the same");
      sb.AppendLine("   Length line up at the top and reach the same depth, whatever their heads do.");
    }
    sb.AppendLine("--");
    sb.AppendLine("length              " + L.ToString("0.###") +
                  (hd == 2 ? "  total, head included" : "  under the head"));
    sb.AppendLine("shank               " + shankLen.ToString("0.###") +
                  "   threaded " + thrL.ToString("0.###") +
                  (plainL > 1e-9 ? ", plain " + plainL.ToString("0.###") : " (full)"));
    if (thrL > 1e-9) sb.AppendLine("turns               " + turnsTot.ToString("0.#") +
                                   "   at " + nSeg + " samples each");
    sb.AppendLine("--");
    sb.AppendLine("prep bore           " + (2.0 * rBore).ToString("0.###") +
                  "   = " + clr.ToString("0.###") + " " + fitName +
                  (xt > 0.0 ? " + 2 x " + xt.ToString("0.###") + " Tol" : ""));
    sb.AppendLine("prep recess         " + prepTxt);
    sb.AppendLine("prep reach          " + (pLen + zShift).ToString("0.###") + " below Pln" +
                  "   (tip is at " + (shankLen + zShift).ToString("0.###") + ")");
    sb.AppendLine("--");
    sb.AppendLine("nut side            wire Spec into ThreadTap - it inherits size, hand, tol and seg");
    if (log.Count > 0) { sb.AppendLine("--"); foreach (string s2 in log) sb.AppendLine("! " + s2); }

    // Spec is readable AND machine-read: ThreadTap takes the first token as the
    // size and the key=values as its own settings, so one wire carries the lot.
    string spec = canon + " hand=" + (thr == 0 ? "none" : thr == 2 ? "L" : "R") +
                  " tol=" + xt.ToString("0.###", IC) + " seg=" + nSeg;

    Screw = screw;
    Prep  = prep;
    Spec  = spec;
    Info  = sb.ToString();
    }
}
