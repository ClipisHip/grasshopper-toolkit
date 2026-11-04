// -----------------------------------------------------------------------------
// screws-nuts-springs · Nut Generator                       grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
// ===========================================================================
//  NUT GENERATOR  -  Grasshopper C# component (RhinoCommon)
//  A standard nut - hex, thin / jam, nylon-insert lock, square - and the
//  pocket that captures it.  Same size strings, same Spec, same frame as
//  ScrewGenerator and ThreadTap, so a screw, its hole and its nut all land on
//  one plane.
//  ---------------------------------------------------------------------
//  INPUTS (Item access):
//     Spec    string  ScrewGenerator's Spec output, or a bare size by hand:
//                     "M5", "M8x1", "1/4-20", "#8-32".  It carries hand, tol and
//                     seg across, exactly as it does into ThreadTap.
//     Type    string  "hex" | "thin" | "nylock" | "nylock-thin" | "square"
//                     | "square-thin"                    - see the table below
//     Entry   string  how the pocket is made:
//                       "top"   drop the nut in from the face
//                       "side"  slide it in from the side, along +Pln.X
//                       "none"  no pocket - the nut sits ON the face, and Prep
//                               is only the bolt bore through the part
//     Tol     double  clearance override, model units.  LEAVE AT 0 to use what
//                     Spec brought.  It opens the pocket on every flat and the
//                     nut's thread on the radius - both are your printer's error.
//     Opts    string  key=value, space separated, all optional:
//                       sink=<n>     nut top this far below Pln (0 = flush)
//                       slot=<n>     side slot length from the bolt axis
//                                    (default 2 x across-flats).  Make it reach
//                                    the edge of your part.  slot=0 with side
//                                    entry EMBEDS the nut instead: a roofed
//                                    pocket with no way in, for pausing the
//                                    print and dropping the nut in.
//                       bore=<n>     how far the bolt bore reaches below Pln
//                                    (default: 2 d past the nut).  Your plate
//                                    thickness, for a clean through hole.
//                       af=<n>       YOUR nut's across-flats, measured.  Wins
//                                    over the table.  See the ISO/DIN note below.
//                       h=<n>        your nut's height, measured.
//                       chamfer=<n>  bore countersink, in pitches (default 0.5)
//     Pln     Plane   the part face the pocket opens on
//  OUTPUTS:
//     Nut     Brep    the nut, threaded - a plain bore if Spec says hand=none
//     Prep    Brep    pocket + bolt bore.  Boolean-DIFFERENCE it out of the part.
//     Info    string  report + warnings
// ===========================================================================


// ===========================================================================
//  THE TYPES, AND WHICH STANDARD EACH ONE IS
//
//     Type          metric                 inch
//     hex           ISO 4032               ASME B18.2.2 hex nut, and the hex
//                                          machine screw nut below 1/4
//     thin          ISO 4035               ASME B18.2.2 jam nut, 1/4 and up
//     nylock        ISO 7040               IFI-100/107 NE / NM
//     nylock-thin   DIN 985                IFI-100/107 NTE / NTM
//     square        DIN 557                ASME B18.2.2 square nut, and the
//                                          square machine screw nut below 1/4
//     square-thin   DIN 562                - (no inch standard)
//
//  ISO vs DIN - THE ONE THAT WILL CATCH YOU.  In 1982 ISO changed the across-
//  flats of four sizes and DIN 934 kept the old ones:
//
//           ISO 4032   DIN 934
//     M10      16         17
//     M12      18         19
//     M14      21         22
//
//  Most nuts sold in Europe, and a great many online, are still DIN.  A pocket
//  cut for a 16 mm nut will not take a 17.  Put calipers on the nut you have
//  and give the number to af= in Opts.  Info reminds you on those sizes.
//
//  HOW IT IS PLACED
//     Pln is the face the pocket opens on; +Z points out of the part, the nut
//     goes in along -Z, the same as every other component here.
//       top    nut top flush with Pln (or sink= below it), pocket open upward
//       side   nut buried at sink= below Pln, slot runs out along +Pln.X -
//              rotate Pln to aim it at the edge it should break out of
//       none   the nut sits ON the face, outside the part, bearing on it;
//              Prep is the bolt bore going in
//     The hex has a corner on +X, so its across-flats is the width of the
//     side slot and it slides straight in.
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
		string Type,
		string Entry,
		double Tol,
		string Opts,
		Plane Pln,
		ref object Nut,
		ref object Prep,
		ref object Info)
    {
        List<string> log = new List<string>();
    System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;
    System.Globalization.NumberStyles NS = System.Globalization.NumberStyles.Float;

    double tol = 0.001;
    if (Rhino.RhinoDoc.ActiveDoc != null) tol = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

    // =======================================================================
    //  THE DATABASE.  Every number is a MAXIMUM off the published table, so a
    //  pocket built from it clears any conforming nut.  "-" = that standard
    //  does not make that size.
    //
    //  metric   d  P  clN | hex s H | thin s H | nylock s h m | nylock-thin s h m
    //           | square s H | square-thin s H
    //     hex ISO 4032, thin ISO 4035, nylock ISO 7040:1997 table 1, nylock-thin
    //     DIN 985, square DIN 557, square-thin DIN 562.  clN = ISO 273 medium.
    //     For the lock nuts h is the total height and m the height of the hex
    //     flats - above m is the crimped collar over the nylon.
    // =======================================================================
    string[] MET = new string[]{
    // name   d     P     clN  | hex       | thin      | nylock           | nylock-thin      | square    | sq-thin
      "M1.6   1.6  0.35   1.8    3.2  1.3    3.2  1.0    -    -     -       -    -     -       -    -     -    -",
      "M2     2.0  0.40   2.4    4.0  1.6    4.0  1.2    -    -     -       -    -     -       -    -     -    -",
      "M2.5   2.5  0.45   2.9    5.0  2.0    5.0  1.6    -    -     -       -    -     -       -    -     -    -",
      "M3     3.0  0.50   3.4    5.5  2.4    5.5  1.8    5.5  4.5   2.15    5.5  4.0   2.4     -    -     5.5  1.8",
      "M3.5   3.5  0.60   3.9    6.0  2.8    6.0  2.0    -    -     -       -    -     -       -    -     -    -",
      "M4     4.0  0.70   4.5    7.0  3.2    7.0  2.2    7.0  6.0   2.9     7.0  5.0   2.9     -    -     7.0  2.2",
      "M5     5.0  0.80   5.5    8.0  4.7    8.0  2.7    8.0  6.8   4.4     8.0  5.0   3.2     8.0  4.0   8.0  2.7",
      "M6     6.0  1.00   6.6   10.0  5.2   10.0  3.2   10.0  8.0   4.9    10.0  6.0   4.0    10.0  5.0  10.0  3.2",
      "M8     8.0  1.25   9.0   13.0  6.8   13.0  4.0   13.0  9.5   6.44   13.0  8.0   5.5    13.0  6.5  13.0  4.0",
      "M10   10.0  1.50  11.0   16.0  8.4   16.0  5.0   16.0 11.9   8.04   17.0 10.0   6.5    17.0  8.0  17.0  5.0",
      "M12   12.0  1.75  13.5   18.0 10.8   18.0  6.0   18.0 14.9  10.37   19.0 12.0   8.0    19.0 10.0   -    -",
      "M14   14.0  2.00  15.5   21.0 12.8   21.0  7.0   21.0 17.0  12.1    22.0 14.0   9.5     -    -     -    -",
      "M16   16.0  2.00  17.5   24.0 14.8   24.0  8.0   24.0 19.1  14.1    24.0 16.0  10.5    24.0 13.0   -    -",
      "M20   20.0  2.50  22.0   30.0 18.0   30.0 10.0   30.0 22.8  16.9    30.0 20.0  14.0     -    -     -    -",
      "M24   24.0  3.00  26.0   36.0 21.5   36.0 12.0   36.0 27.1  20.2     -    -     -       -    -     -    -"
    };

    // =======================================================================
    //  inch     d  UNC UNF clN | the same six types
    //     hex: ASME B18.2.2-2010 table 4 from 1/4, table 1-1 (hex machine screw
    //     nut) below.  thin: table 4 jam nut.  square: table 2 from 1/4, table
    //     1-1 (square machine screw nut) below.  nylock / nylock-thin: IFI-100/
    //     107 NE, NM / NTE, NTM - light hex series, which is why 7/16 and up are
    //     a flat smaller than the plain hex nut.  clN = free-fit drill.
    // =======================================================================
    string[] INC = new string[]{
    // name   d       UNC UNF  clN     | hex         | thin        | nylock              | nylock-thin         | square      | sq-thin
      "#0    0.0600    -  80  0.0730   0.156 0.050    -     -       -     -     -        -     -     -        0.156 0.050   -  -",
      "#1    0.0730   64  72  0.0860   0.156 0.050    -     -       -     -     -        -     -     -        0.156 0.050   -  -",
      "#2    0.0860   56  64  0.0990   0.188 0.066    -     -       -     -     -        -     -     -        0.188 0.066   -  -",
      "#3    0.0990   48  56  0.1130   0.188 0.066    -     -       -     -     -        -     -     -        0.188 0.066   -  -",
      "#4    0.1120   40  48  0.1285   0.250 0.098    -     -       0.251 0.153 0.081    0.251 0.124 0.075    0.250 0.098   -  -",
      "#5    0.1250   40  44  0.1405   0.312 0.114    -     -       0.251 0.153 0.081    -     -     -        0.312 0.114   -  -",
      "#6    0.1380   32  40  0.1495   0.312 0.114    -     -       0.313 0.188 0.103    0.313 0.140 0.090    0.312 0.114   -  -",
      "#8    0.1640   32  36  0.1770   0.344 0.130    -     -       0.345 0.239 0.140    0.345 0.187 0.110    0.344 0.130   -  -",
      "#10   0.1900   24  32  0.2010   0.375 0.130    -     -       0.376 0.249 0.140    0.376 0.187 0.110    0.375 0.130   -  -",
      "#12   0.2160   24  28  0.2280   0.438 0.161    -     -       -     -     -        -     -     -        0.438 0.161   -  -",
      "1/4   0.2500   20  28  0.2660   0.438 0.226   0.438 0.163   0.439 0.328 0.225    0.439 0.218 0.125    0.438 0.235   -  -",
      "5/16  0.3125   18  24  0.3320   0.500 0.273   0.500 0.195   0.502 0.359 0.250    0.502 0.265 0.158    0.562 0.283   -  -",
      "3/8   0.3750   16  24  0.3970   0.563 0.337   0.563 0.227   0.564 0.468 0.335    0.564 0.281 0.150    0.625 0.346   -  -",
      "7/16  0.4375   14  20  0.4687   0.688 0.385   0.688 0.260   0.627 0.468 0.324    0.627 0.328 0.225    0.750 0.394   -  -",
      "1/2   0.5000   13  20  0.5312   0.750 0.448   0.750 0.323   0.752 0.609 0.464    0.752 0.328 0.190    0.812 0.458   -  -",
      "5/8   0.6250   11  18  0.6562   0.938 0.559   0.938 0.387   0.940 0.765 0.593    0.940 0.406 0.265    1.000 0.569   -  -",
      "3/4   0.7500   10  16  0.7812   1.125 0.665   1.125 0.446   1.064 0.890 0.742    1.064 0.421 0.288    1.125 0.680   -  -",
      "1     1.0000    8  12  1.0312   1.500 0.887   1.500 0.575   1.440 1.078 0.825    -     -     -        1.500 0.903   -  -"
    };

    // =======================================================================
    //  SPEC.  First token is the size, the rest are key=value settings that
    //  ScrewGenerator wrote or you typed.  Identical to ThreadTap.
    // =======================================================================
    string sraw = (Spec == null) ? "" : Spec.Trim();
    if (sraw.Length == 0) sraw = "M5";
    string[] stok = sraw.Split(new char[]{' ', ',', ';', '\t', '\n', '\r'},
                               StringSplitOptions.RemoveEmptyEntries);
    int    thr     = 1;                             // 0 plain bore, 1 right, 2 left
    double specTol = -1.0;
    int    nSeg    = 96;
    string cls     = "6H";
    for (int i = 1; i < stok.Length; i++)
    {
      string t = stok[i].ToLowerInvariant();
      int eq = t.IndexOf('=');
      if (eq < 1 || eq == t.Length - 1) { log.Add("Spec: \"" + stok[i] + "\" is not key=value - ignored."); continue; }
      string ky = t.Substring(0, eq), vl = t.Substring(eq + 1);
      if (ky == "hand")
        thr = vl.StartsWith("l") ? 2 : (vl.StartsWith("n") || vl.StartsWith("p") || vl == "0") ? 0 : 1;
      else if (ky == "class") cls = vl.ToUpperInvariant();
      else
      {
        double vv;
        if (!double.TryParse(vl, NS, IC, out vv)) { log.Add("Spec: \"" + stok[i] + "\" has no number - ignored."); continue; }
        if      (ky == "tol") specTol = vv;
        else if (ky == "seg" || ky == "segments") nSeg = (int)vv;
        else log.Add("Spec: unknown key \"" + ky + "\" - ignored.");
      }
    }

    string raw = stok[0].ToUpperInvariant().Replace("UNC", "").Replace("UNF", "").Replace("UN", "").Trim();
    if (raw.Length == 0) raw = "M5";
    bool   metric  = raw.StartsWith("M") && raw.Length > 1 && (char.IsDigit(raw[1]) || raw[1] == '.');
    double wantDia = 0.0, wantP = 0.0;
    if (metric)
    {
      string[] mp = raw.Substring(1).Split(new char[]{'X', '*'}, StringSplitOptions.RemoveEmptyEntries);
      if (!double.TryParse(mp[0], NS, IC, out wantDia)) { Info = "Could not read the size \"" + stok[0] + "\"."; return; }
      if (mp.Length > 1) double.TryParse(mp[1], NS, IC, out wantP);
    }
    else
    {
      string[] ip = raw.Replace("\"", "").Split(new char[]{'-'}, StringSplitOptions.RemoveEmptyEntries);
      string dsg = ip[0].Trim();
      double tpi = 0.0;
      if (ip.Length > 1) double.TryParse(ip[1].Trim(), NS, IC, out tpi);
      bool hash = dsg.StartsWith("#");
      if (hash) dsg = dsg.Substring(1);
      if (dsg.Contains("/"))
      {
        string[] fr = dsg.Split('/');
        double nu, de;
        if (fr.Length == 2 && double.TryParse(fr[0], NS, IC, out nu) &&
            double.TryParse(fr[1], NS, IC, out de) && de > 0) wantDia = nu / de;
      }
      else
      {
        double v;
        if (!double.TryParse(dsg, NS, IC, out v)) { Info = "Could not read the size \"" + stok[0] + "\"."; return; }
        bool gauge = hash || (!dsg.Contains(".") && v >= 0.0 && v <= 12.0);
        if (gauge && !hash && v == 1.0 && tpi < 40.0) gauge = false;   // "1-8" is the one-inch bolt
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
    { Info = "\"" + stok[0] + "\" is not in the nut table.  Metric M1.6-M24, inch #0-1\"."; return; }

    string[] fld = tbl[row].Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
    string nm = fld[0];
    double d  = double.Parse(fld[1], IC);
    double P;
    if (metric) P = double.Parse(fld[2], IC);
    else
    {
      double t2;
      if (fld[2] != "-" && double.TryParse(fld[2], NS, IC, out t2)) P = 1.0 / t2;
      else { double.TryParse(fld[3], NS, IC, out t2); P = 1.0 / t2; }
    }
    if (wantP > 1e-9) P = wantP;
    double Pmm = metric ? P : P * 25.4;
    double clN = double.Parse(fld[metric ? 3 : 4], IC);
    string canon = metric
      ? nm + (Math.Abs(P - double.Parse(fld[2], IC)) > 1e-6 ? "x" + P.ToString("0.###", IC) : "")
      : nm + "-" + (1.0 / P).ToString("0.##", IC);

    // =======================================================================
    //  TYPE
    // =======================================================================
    string ts = (Type == null) ? "hex" : Type.Trim().ToLowerInvariant().Replace("_", "-").Replace(" ", "-");
    bool thinW = ts.Contains("thin") || ts.Contains("jam") || ts.Contains("low");
    int ty;                                         // 0 hex 1 thin 2 nylock 3 nylock-thin 4 square 5 square-thin
    if (ts.StartsWith("ny") || ts.Contains("lock") || ts.Contains("insert")) ty = thinW ? 3 : 2;
    else if (ts.StartsWith("sq") || ts.StartsWith("quad") || ts.StartsWith("4")) ty = thinW ? 5 : 4;
    else ty = thinW ? 1 : 0;
    if (ts.Length > 0 && !(ts.StartsWith("hex") || ts.StartsWith("thin") || ts.StartsWith("jam") ||
        ts.StartsWith("ny") || ts.Contains("lock") || ts.StartsWith("sq") || ts.StartsWith("quad")))
      log.Add("Type \"" + Type + "\" not recognised - using " +
              (ty == 0 ? "hex" : ty == 1 ? "thin" : "that") + ".");

    string[] tName = { "hex", "thin", "nylock", "nylock-thin", "square", "square-thin" };
    int b0 = metric ? 4 : 5;                        // first nut column
    int[] sI = { b0 + 0, b0 + 2, b0 + 4, b0 + 7, b0 + 10, b0 + 12 };
    int[] hI = { b0 + 1, b0 + 3, b0 + 5, b0 + 8, b0 + 11, b0 + 13 };
    int[] mI = { -1,     -1,     b0 + 6, b0 + 9, -1,      -1      };
    bool square = (ty >= 4);
    bool nylon  = (ty == 2 || ty == 3);

    if (fld[sI[ty]] == "-")
    {
      List<string> have = new List<string>();
      for (int k = 0; k < 6; k++) if (fld[sI[k]] != "-") have.Add(tName[k]);
      Info = "There is no standard " + tName[ty] + " nut in " + nm + ".\n" +
             "Available in " + nm + ":  " + string.Join(", ", have.ToArray()) +
             (ty == 5 && !metric ? "\n(No inch standard covers thin square nuts at all.)" : "");
      return;
    }

    string std;
    if (metric) std = new string[]{ "ISO 4032", "ISO 4035", "ISO 7040", "DIN 985", "DIN 557", "DIN 562" }[ty];
    else if (ty == 0) std = d < 0.25 ? "ASME B18.2.2 hex machine screw nut" : "ASME B18.2.2 hex nut";
    else if (ty == 1) std = "ASME B18.2.2 jam nut";
    else if (ty == 2) std = d < 0.25 ? "IFI-100/107 NM" : "IFI-100/107 NE";
    else if (ty == 3) std = d < 0.25 ? "IFI-100/107 NTM" : "IFI-100/107 NTE";
    else              std = d < 0.25 ? "ASME B18.2.2 square machine screw nut" : "ASME B18.2.2 square nut";

    double s  = double.Parse(fld[sI[ty]], IC);
    double H  = double.Parse(fld[hI[ty]], IC);
    double mw = (mI[ty] >= 0) ? double.Parse(fld[mI[ty]], IC) : H;

    // ---- units ----------------------------------------------------------
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
    d *= uf; P *= uf; clN *= uf; s *= uf; H *= uf; mw *= uf;
    double sTbl = s, hTbl = H;

    // =======================================================================
    //  OPTS and ENTRY
    // =======================================================================
    double oSink = 0.0, oSlot = 0.0, oBore = 0.0, oAf = 0.0, oH = 0.0, chamP = 0.5;
    bool   slotSet = false;
    string orw = (Opts == null) ? "" : Opts.Trim().ToLowerInvariant();
    if (orw.Length > 0)
    {
      foreach (string t in orw.Split(new char[]{' ', ',', ';', '\t', '\n', '\r'}, StringSplitOptions.RemoveEmptyEntries))
      {
        int eq = t.IndexOf('=');
        if (eq < 1 || eq == t.Length - 1) { log.Add("Opts: \"" + t + "\" is not key=value - ignored."); continue; }
        string ky = t.Substring(0, eq);
        double vv;
        if (!double.TryParse(t.Substring(eq + 1), NS, IC, out vv)) { log.Add("Opts: \"" + t + "\" has no number - ignored."); continue; }
        if      (ky == "sink")    oSink = vv;
        else if (ky == "slot")    { oSlot = vv; slotSet = true; }
        else if (ky == "bore")    oBore = vv;
        else if (ky == "af" || ky == "s") oAf = vv;
        else if (ky == "h" || ky == "height") oH = vv;
        else if (ky == "chamfer") chamP = vv;
        else if (ky == "seg" || ky == "segments" || ky == "prep" || ky == "preplen")
          log.Add("Opts: \"" + ky + "\" is a ScrewGenerator key. Did you wire ScrewOptions.ScrewOpts " +
                  "in here? That one goes to ScrewGenerator. NutGenerator takes sink, slot, bore, af, h and " +
                  "chamfer, and it gets seg from Spec.");
        else log.Add("Opts: unknown key \"" + ky + "\" - ignored. Valid: sink, slot, bore, af, h, chamfer.");
      }
    }
    if (oAf > 0.0) s = oAf;
    if (oH  > 0.0) { if (nylon) mw *= oH / H; H = oH; }
    if (chamP < 0.0) chamP = 0.0;

    string es = (Entry == null) ? "top" : Entry.Trim().ToLowerInvariant();
    int en = 0;                                     // 0 top 1 side 2 none
    if (es.StartsWith("si") || es.StartsWith("sl")) en = 1;
    else if (es.StartsWith("no") || es.StartsWith("ou") || es.StartsWith("su")) en = 2;

    // ---- clearance: Tol input wins, else whatever Spec carried ----------
    bool   posG = metric && cls.Contains("G");
    double EI   = posG ? (15.0 + 11.0 * Pmm) * 0.001 * mm : 0.0;
    double xt   = (Tol > 0.0) ? Tol : (specTol > 0.0 ? specTol : 0.0);
    bool   inherited = (Tol <= 0.0) && (specTol > 0.0);
    double c    = 0.5 * EI + xt;                    // thread opening, radial
    double g    = xt;                               // pocket opening, per flat

    if (nSeg <= 0) nSeg = 96;
    nSeg = Math.Max(32, Math.Min(720, nSeg));
    nSeg = 16 * (int)Math.Round(nSeg / 16.0);

    // =======================================================================
    //  THE NUT'S THREAD - the same construction as ThreadTap: one helix per
    //  profile sample, ruled surfaces between them, joined without a Boolean.
    //  Built with the nut on z in [0, H], z = 0 being the face side.
    // =======================================================================
    double Hf   = 0.8660254037844386 * P;
    double Rmaj = 0.5 * d + c;
    double Rmin = Rmaj - 0.625 * Hf;
    double D1   = d - 1.25 * Hf;

    // how much room the end faces leave around the bore
    double rw   = 0.475 * s;                        // chamfer circle on a hex face
    double rTop = 0.42 * s;                         // top of a nylock's crimped collar
    double capR = square ? 0.5 * s : (nylon ? Math.Min(rw, rTop) : rw);

    double lead = chamP * P;
    if (lead > 0.3 * H) lead = 0.3 * H;
    if (Rmaj + lead > 0.95 * capR)
    {
      double was = lead;
      lead = Math.Max(0.0, 0.95 * capR - Rmaj);
      if (was > 1e-9) log.Add("Bore chamfer cut back from " + was.ToString("0.###") + " to " +
                              lead.ToString("0.###") + " to leave the nut a face.");
    }
    if (Rmaj >= 0.95 * capR)
    { Info = "Tol " + xt.ToString("0.###") + " opens the thread wider than the nut's own face. " +
             "Nothing would be left to bear on.  Drop Tol, or use a bigger nut."; return; }

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
    if (thr == 0) for (int i = 0; i < nSeg; i++) rad[i] = Rmin;
    double[] flar = new double[nSeg];
    for (int i = 0; i < nSeg; i++) flar[i] = rad[i] + lead;

    List<double[]> zr0 = new List<double[]>(), zr1 = new List<double[]>();
    List<double>   zz0 = new List<double>(),   zz1 = new List<double>();
    if (lead > 1e-9)
    {
      zz0.Add(0.0);      zz1.Add(lead);     zr0.Add(flar); zr1.Add(rad);
      zz0.Add(lead);     zz1.Add(H - lead); zr0.Add(rad);  zr1.Add(rad);
      zz0.Add(H - lead); zz1.Add(H);        zr0.Add(rad);  zr1.Add(flar);
    }
    else { zz0.Add(0.0); zz1.Add(H); zr0.Add(rad); zr1.Add(rad); }

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
        double a = 2.0 * Math.PI * i / nSeg + angAcc;
        double r0 = zr0[z][i], r1 = zr1[z][i];
        rail[i] = NurbsCurve.CreateSpiral(new Point3d(0, 0, za), Vector3d.ZAxis,
                                          new Point3d(r0 * Math.Cos(a), r0 * Math.Sin(a), za),
                                          P, turns, r0, r1);
        if (rail[i] == null) { Info = "Helix failed - check Spec."; return; }
        if (z == 0)             rimT[i] = rail[i].PointAtStart;
        if (z == zz0.Count - 1) rimB[i] = rail[i].PointAtEnd;
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
    if (skin.Count == 0) { Info = "Could not build the thread.\n" + string.Join("\n", log.ToArray()); return; }
    rimT[nSeg] = rimT[0]; rimB[nSeg] = rimB[0];
    Curve loT = new Polyline(rimT).ToPolylineCurve();
    Curve loB = new Polyline(rimB).ToPolylineCurve();

    // =======================================================================
    //  THE NUT'S BODY.  A prism, and for the hex types the 30 deg chamfer that
    //  leaves the round bearing face - made by intersecting the prism with a
    //  revolved profile, which is a Boolean between two SIMPLE solids.  The
    //  thread never goes near a Boolean: the body's two end faces are thrown
    //  away and rebuilt as annuli around the thread's own rim, and it all joins.
    // =======================================================================
    double eAC = square ? s * Math.Sqrt(2.0) : 2.0 * s / Math.Sqrt(3.0);   // across corners
    List<Point3d> olP = new List<Point3d>();
    if (square)
    {
      double a2 = 0.5 * s;
      olP.Add(new Point3d( a2,  a2, 0)); olP.Add(new Point3d(-a2,  a2, 0));
      olP.Add(new Point3d(-a2, -a2, 0)); olP.Add(new Point3d( a2, -a2, 0));
    }
    else
    {
      double Rh = s / Math.Sqrt(3.0);
      for (int i = 0; i < 6; i++)
        olP.Add(new Point3d(Rh * Math.Cos(Math.PI / 3.0 * i), Rh * Math.Sin(Math.PI / 3.0 * i), 0));
    }
    olP.Add(olP[0]);

    double marg = 0.25 * H + 10.0 * tol;
    Curve pLo = new Polyline(olP).ToPolylineCurve(), pHi = pLo.DuplicateCurve();
    pLo.Translate(0, 0, square ? 0.0 : -marg);
    pHi.Translate(0, 0, square ? H   : H + marg);
    Brep prism = null;
    Brep[] pl = Brep.CreateFromLoft(new Curve[]{pLo, pHi}, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
    if (pl != null && pl.Length > 0) prism = pl[0].CapPlanarHoles(tol);
    if (prism == null) { Info = "Could not build the nut body."; return; }
    // Face it outward BEFORE any Boolean. An inward-oriented solid does not
    // intersect - it operates on the complement, and what comes back is the six
    // corner slivers the chamfer cuts off, which have no end faces at all.
    if (prism.SolidOrientation == BrepSolidOrientation.Inward) prism.Flip();

    Brep body = prism;
    if (!square)
    {
      double Rb = 0.5 * eAC + 0.05 * s;             // clear of the corners
      double dz = Math.Min((Rb - rw) * Math.Tan(RhinoMath.ToRadians(30.0)), 0.4 * H);
      List<Point3d> pf = new List<Point3d>();
      if (nylon && mw < H - 1e-9)
      {
        // crimped collar over the nylon on the FACE side, bearing face deep.
        // In a pocket the bolt comes from below, so that is the way round it sits.
        double zd = Math.Min(H - mw, H - dz - 0.1 * H);
        if (zd < 0.05 * H) zd = 0.05 * H;
        pf.Add(new Point3d(rTop, 0, 0.0));
        pf.Add(new Point3d(Rb,   0, zd));
      }
      else
      {
        pf.Add(new Point3d(rw, 0, 0.0));
        pf.Add(new Point3d(Rb, 0, dz));
      }
      pf.Add(new Point3d(Rb, 0, H - dz));
      pf.Add(new Point3d(rw, 0, H));
      RevSurface rv = RevSurface.Create(new Polyline(pf).ToPolylineCurve(),
                                        new Line(new Point3d(0, 0, -1), new Point3d(0, 0, 1)));
      Brep rb = (rv != null) ? Brep.CreateFromRevSurface(rv, true, true) : null;
      if (rb != null && rb.SolidOrientation == BrepSolidOrientation.Inward) rb.Flip();
      Brep[] ix = (rb != null) ? Brep.CreateBooleanIntersection(prism, rb, tol) : null;
      if (ix != null && ix.Length > 0)
      {
        body = ix[0]; double bv = -1.0;
        foreach (Brep bb in ix) { double v = bb.GetBoundingBox(true).Volume; if (v > bv) { bv = v; body = bb; } }
        if (body.SolidOrientation == BrepSolidOrientation.Inward) body.Flip();
      }
      else
      {
        log.Add("Chamfer failed - nut left square-edged.");
        Curve q0 = new Polyline(olP).ToPolylineCurve(), q1 = q0.DuplicateCurve();
        q1.Translate(0, 0, H);
        Brep[] ql = Brep.CreateFromLoft(new Curve[]{q0, q1}, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
        if (ql != null && ql.Length > 0) body = ql[0].CapPlanarHoles(tol);
      }
    }

    // swap the two end faces for annuli around the thread rim
    List<Brep> nf = new List<Brep>();
    Curve capTop = null, capBot = null;
    double zt = Math.Max(20.0 * tol, 0.002 * H);
    for (int pass = 0; pass < 2; pass++)
    {
      nf.Clear(); capTop = null; capBot = null;
      for (int i = 0; i < body.Faces.Count; i++)
      {
        BrepFace f = body.Faces[i];
        BoundingBox fb = f.GetBoundingBox(true);
        bool flat = (fb.Max.Z - fb.Min.Z) < zt;
        if (flat && Math.Abs(fb.Min.Z) < zt)     { capTop = f.OuterLoop.To3dCurve(); continue; }
        if (flat && Math.Abs(fb.Min.Z - H) < zt) { capBot = f.OuterLoop.To3dCurve(); continue; }
        nf.Add(f.DuplicateFace(false));
      }
      if (capTop != null && capBot != null) break;
      if (pass > 0) break;
      // Fall back to a plain prism rather than failing outright: a nut with
      // square corners is still a usable nut, and the pocket is unaffected.
      log.Add("The chamfered body gave no clean end faces - corners left square. " +
              "Raise the model tolerance a little if you want the chamfer.");
      Curve q0 = new Polyline(olP).ToPolylineCurve(), q1 = q0.DuplicateCurve();
      q1.Translate(0, 0, H);
      Brep[] ql = Brep.CreateFromLoft(new Curve[]{q0, q1}, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
      Brep qb = (ql != null && ql.Length > 0) ? ql[0].CapPlanarHoles(tol) : null;
      if (qb == null) break;
      if (qb.SolidOrientation == BrepSolidOrientation.Inward) qb.Flip();
      body = qb;
    }
    if (capTop == null || capBot == null)
    {
      Info = "Could not find the nut's end faces, even on a plain prism.\n" +
             string.Join("\n", log.ToArray());
      return;
    }
    Brep[] aT = Brep.CreatePlanarBreps(new Curve[]{capTop, loT}, tol);
    Brep[] aB = Brep.CreatePlanarBreps(new Curve[]{capBot, loB}, tol);
    if (aT != null && aT.Length > 0) nf.Add(aT[0]); else log.Add("Nut face failed on the face side.");
    if (aB != null && aB.Length > 0) nf.Add(aB[0]); else log.Add("Nut face failed on the bearing side.");
    nf.AddRange(skin);

    Brep nut = null;
    Brep[] nj = Brep.JoinBreps(nf, tol);
    if (nj != null && nj.Length > 0)
    {
      nut = nj[0]; double bv = -1.0;
      foreach (Brep bb in nj) { double v = bb.GetBoundingBox(true).Volume; if (v > bv) { bv = v; nut = bb; } }
      if (!nut.IsSolid) { Brep n2 = nut.CapPlanarHoles(tol); if (n2 != null) nut = n2; }
      if (!nut.IsSolid) log.Add("Nut is not watertight - raise seg= in Spec, or loosen the model tolerance.");
    }
    else { Info = "Could not join the nut.\n" + string.Join("\n", log.ToArray()); return; }

    // hand: ask CreateSpiral which way it winds rather than assume
    bool rhAPI = true;
    NurbsCurve probe = NurbsCurve.CreateSpiral(Point3d.Origin, Vector3d.ZAxis, new Point3d(1, 0, 0), 1.0, 0.25, 1.0, 1.0);
    if (probe != null)
    {
      Point3d pa = probe.PointAtStart, pb = probe.PointAt(probe.Domain.ParameterAt(0.3));
      double da = Math.Atan2(pb.Y, pb.X) - Math.Atan2(pa.Y, pa.X);
      while (da >  Math.PI) da -= 2.0 * Math.PI;
      while (da < -Math.PI) da += 2.0 * Math.PI;
      rhAPI = (da * (pb.Z - pa.Z)) > 0.0;
    }
    if (thr != 0 && ((thr != 2) != rhAPI)) nut.Transform(Transform.Mirror(Plane.WorldZX));
    if (nut.SolidOrientation == BrepSolidOrientation.Inward) nut.Flip();

    // =======================================================================
    //  PREP.  Pocket and bolt bore as ONE solid, built face by face and joined
    //  - the pocket's outline always encloses the bore, so the two meet on
    //  annular steps and no Boolean is needed.
    // =======================================================================
    double over = 0.5 * mm;
    double sink = (en == 2) ? 0.0 : Math.Max(0.0, oSink);
    double rb2  = 0.5 * clN + g;                    // bolt clearance bore
    double sP   = s + 2.0 * g;                      // pocket across flats
    double slot = (oSlot > 1e-9) ? oSlot : 2.0 * s;
    bool   embed = (en == 1 && slotSet && oSlot <= 1e-9);   // roofed pocket, no slot

    List<Point3d> ppt = new List<Point3d>();
    double pocketAC = 0.0;
    if (en != 2)
    {
      if (square)
      {
        double a2 = 0.5 * sP;
        if (en == 1 && !embed)
        {
          if (slot < a2) slot = a2;
          ppt.Add(new Point3d(slot, a2, 0)); ppt.Add(new Point3d(-a2, a2, 0));
          ppt.Add(new Point3d(-a2, -a2, 0)); ppt.Add(new Point3d(slot, -a2, 0));
        }
        else
        {
          ppt.Add(new Point3d( a2,  a2, 0)); ppt.Add(new Point3d(-a2,  a2, 0));
          ppt.Add(new Point3d(-a2, -a2, 0)); ppt.Add(new Point3d( a2, -a2, 0));
        }
        pocketAC = sP * Math.Sqrt(2.0);
      }
      else
      {
        double Rp = sP / Math.Sqrt(3.0), wy = 0.5 * sP;
        if (en == 1 && !embed)
        {
          if (slot < Rp) slot = Rp;                 // the +X corner must stay inside the slot
          ppt.Add(new Point3d(slot,      wy, 0)); ppt.Add(new Point3d( 0.5 * Rp,  wy, 0));
          ppt.Add(new Point3d(-0.5 * Rp, wy, 0)); ppt.Add(new Point3d(-Rp,        0, 0));
          ppt.Add(new Point3d(-0.5 * Rp, -wy, 0)); ppt.Add(new Point3d( 0.5 * Rp, -wy, 0));
          ppt.Add(new Point3d(slot,     -wy, 0));
        }
        else
          for (int i = 0; i < 6; i++)
            ppt.Add(new Point3d(Rp * Math.Cos(Math.PI / 3.0 * i), Rp * Math.Sin(Math.PI / 3.0 * i), 0));
        pocketAC = 2.0 * Rp;
      }
      ppt.Add(ppt[0]);
    }

    // z extents, local frame, +Z into the part
    double zp0, zp1, zb1;
    if (en == 2) { zp0 = 0.0; zp1 = 0.0; }
    else
    {
      zp1 = sink + H + g;
      zp0 = (en == 1 && sink > g) ? sink - g : -over;   // a buried side slot has a roof
    }
    double boreDef = ((en == 2) ? 0.0 : zp1) + 2.0 * d;
    zb1 = (oBore > 1e-9) ? oBore : boreDef;
    if (en != 2 && zb1 < zp1)
    {
      log.Add("bore=" + oBore.ToString("0.###") + " stops inside the pocket - taken to the pocket floor.");
      zb1 = zp1;
    }

    Brep prep = null;
    if (en == 2)
    {
      prep = new Cylinder(new Circle(new Plane(new Point3d(0, 0, -over), Vector3d.XAxis, Vector3d.YAxis), rb2),
                          zb1 + over).ToBrep(true, true);
    }
    else
    {
      List<Brep> pf = new List<Brep>();
      Curve o0 = new Polyline(ppt).ToPolylineCurve(), o1 = o0.DuplicateCurve();
      o0.Translate(0, 0, zp0); o1.Translate(0, 0, zp1);
      Brep[] wl = Brep.CreateFromLoft(new Curve[]{o0, o1}, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
      if (wl != null && wl.Length > 0) pf.Add(wl[0]);

      if (zp0 > -over + 1e-9)                       // bore above a buried slot
      {
        pf.Add(new Cylinder(new Circle(new Plane(new Point3d(0, 0, -over), Vector3d.XAxis, Vector3d.YAxis), rb2),
                            zp0 + over).ToBrep(false, false));
        Brep[] an = Brep.CreatePlanarBreps(new Curve[]{ o0,
          new ArcCurve(new Circle(new Plane(new Point3d(0, 0, zp0), Vector3d.XAxis, Vector3d.YAxis), rb2)) }, tol);
        if (an != null && an.Length > 0) pf.Add(an[0]);
      }
      if (zb1 > zp1 + 1e-9)                         // bore below the nut
      {
        pf.Add(new Cylinder(new Circle(new Plane(new Point3d(0, 0, zp1), Vector3d.XAxis, Vector3d.YAxis), rb2),
                            zb1 - zp1).ToBrep(false, false));
        Brep[] an = Brep.CreatePlanarBreps(new Curve[]{ o1,
          new ArcCurve(new Circle(new Plane(new Point3d(0, 0, zp1), Vector3d.XAxis, Vector3d.YAxis), rb2)) }, tol);
        if (an != null && an.Length > 0) pf.Add(an[0]);
      }
      Brep[] pj = Brep.JoinBreps(pf, tol);
      if (pj != null && pj.Length > 0)
      {
        prep = pj[0]; double bv = -1.0;
        foreach (Brep bb in pj) { double v = bb.GetBoundingBox(true).Volume; if (v > bv) { bv = v; prep = bb; } }
        Brep pc = prep.CapPlanarHoles(tol);
        if (pc != null) prep = pc;
      }
    }
    if (prep == null) log.Add("Prep failed.");
    else
    {
      if (!prep.IsSolid) log.Add("Prep is not watertight.");
      if (prep.SolidOrientation == BrepSolidOrientation.Inward) prep.Flip();
    }

    // =======================================================================
    //  PLACE.  In a pocket the nut sits at sink below Pln; with no pocket it
    //  sits ON the face, outside the part.  Then local +Z -> -Pln.Z by a
    //  proper rotation, so the thread keeps its hand.
    // =======================================================================
    nut.Transform(Transform.Translation(0, 0, (en == 2) ? -H : sink));
    Plane bp = Pln.IsValid ? Pln : Plane.WorldXY;
    Transform xf = Transform.PlaneToPlane(Plane.WorldXY, new Plane(bp.Origin, bp.XAxis, -bp.YAxis));
    nut.Transform(xf);
    if (prep != null) prep.Transform(xf);

    // =======================================================================
    //  REPORT
    // =======================================================================
    double depth = 0.5 * (d - D1);
    double flank = (depth > 1e-12) ? (depth - c) / depth : 0.0;
    string ut = (Rhino.RhinoDoc.ActiveDoc != null)
                ? " " + Rhino.RhinoDoc.ActiveDoc.ModelUnitSystem.ToString().ToLowerInvariant() : "";

    if (metric && oAf <= 0.0 && (ty == 0 || ty == 1 || ty == 2) && (nm == "M10" || nm == "M12" || nm == "M14"))
      log.Add("ISO / DIN: this " + nm + " is ISO at " + (sTbl / uf).ToString("0") + " across flats, but DIN 934 " +
              "and DIN 439 nuts are " + (nm == "M10" ? "17" : nm == "M12" ? "19" : "22") + ". Much of the " +
              "stock sold is still DIN. Measure yours - if it is the bigger one, put af=" +
              (nm == "M10" ? "17" : nm == "M12" ? "19" : "22") + " in Opts, or the nut will not go in.");
    if (metric && oAf <= 0.0 && (ty == 3 || ty == 4 || ty == 5) && (nm == "M10" || nm == "M12" || nm == "M14"))
      log.Add("ISO / DIN: " + tName[ty] + " is a DIN standard and uses the OLD across-flats, " +
              (sTbl / uf).ToString("0") + ". An ISO-pattern nut is a millimetre smaller and will rattle - " +
              "measure yours and set af= if it is.");
    if (slotSet && en != 1)
      log.Add("slot= only does something with Entry side - ignored on " +
              (en == 0 ? "top entry" : "Entry none") + ".");
    if (embed && sink <= g)
      log.Add("An embedded nut needs a roof - give sink= at least 3 layers of material above it, " +
              "or it is just a top pocket.");
    if (en == 1 && !embed)
      log.Add("The side slot runs " + slot.ToString("0.###") + " along +Pln.X from the bolt axis. It has " +
              "to break out of the part's edge - set slot= to reach it, and turn Pln to aim it.");
    if (en == 1 && !embed && sink <= g)
      log.Add("sink=0 on a side slot leaves it open at the face as well. For a buried slot give " +
              "sink= the depth of material you want over the nut.");
    if (thr != 0 && flank < 0.5)
      log.Add("IF YOU PRINT THIS NUT: only " + (100.0 * Math.Max(0.0, flank)).ToString("0") +
              "% of the flank is left after " + c.ToString("0.###") + " clearance. It will strip. " +
              "For a real steel nut in the pocket this does not matter.");
    if (en == 2 && oSink > 0.0) log.Add("sink= does nothing with Entry none - the nut sits on the face.");

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.AppendLine(canon + " nut   " + tName[ty] + "   (" + std + ")" +
                  (thr == 0 ? "   PLAIN BORE" : thr == 2 ? "   LEFT HAND" : "   right hand"));
    sb.AppendLine("--");
    sb.AppendLine("across flats  s     " + s.ToString("0.###") + ut +
                  (oAf > 0.0 ? "   <- af= override, table says " + sTbl.ToString("0.###") : ""));
    sb.AppendLine("across corners e    " + eAC.ToString("0.###"));
    sb.AppendLine("height        H     " + H.ToString("0.###") +
                  (oH > 0.0 ? "   <- h= override, table says " + hTbl.ToString("0.###") : "") +
                  (nylon ? "   (hex flats " + mw.ToString("0.###") + ", then the collar)" : ""));
    sb.AppendLine("thread        " + (thr == 0 ? "plain bore at the minor dia, " + (2.0 * Rmin).ToString("0.###")
                                               : "internal, major " + (2.0 * Rmaj).ToString("0.###") +
                                                 "  minor " + (2.0 * Rmin).ToString("0.###")));
    if (thr != 0)
      sb.AppendLine("flank left    " + (100.0 * Math.Max(0.0, flank)).ToString("0") +
                    "%  - only matters if you PRINT the nut");
    sb.AppendLine("bore chamfer  " + (lead > 0.0 ? chamP.ToString("0.##") + " x pitch = " + lead.ToString("0.###")
                                                 : "none"));
    sb.AppendLine("--");
    sb.AppendLine("clearance     " + g.ToString("0.###") + " per flat" + (inherited ? "   <- inherited from Spec" : ""));
    if (en == 2)
    {
      sb.AppendLine("POCKET        none - the nut sits on the face, outside the part, so Prep");
      sb.AppendLine("              is ONLY the bolt bore. For a pocket to capture the nut in,");
      sb.AppendLine("              set Entry to top (drop it in) or side (slide it in).");
      sb.AppendLine("bolt bore     " + (2.0 * rb2).ToString("0.###") + " dia, " + zb1.ToString("0.###") + " below Pln");
    }
    else
    {
      sb.AppendLine("POCKET        " + (en == 0 ? "top entry - drop it in from the face"
                                         : embed   ? "EMBEDDED - pause the print at the roof and drop it in"
                                                   : "side entry - slides in along +Pln.X"));
      sb.AppendLine("  across flats   " + sP.ToString("0.###") + "   corners " + pocketAC.ToString("0.###"));
      sb.AppendLine("  nut top        " + (sink > 0.0 ? sink.ToString("0.###") + " below Pln" : "flush with Pln"));
      sb.AppendLine("  pocket floor   " + zp1.ToString("0.###") + " below Pln");
      if (embed)
        sb.AppendLine("  pause at       " + zp0.ToString("0.###") + " below Pln - the roof starts there. " +
                      "Printing face-down, that is the height to stop at, measured from the bed.");
      if (en == 1 && !embed)
        sb.AppendLine("  slot           " + slot.ToString("0.###") + " long, " +
                      (sP).ToString("0.###") + " wide, " + (zp1 - Math.Max(zp0, 0.0)).ToString("0.###") + " tall" +
                      (zp0 > 0.0 ? ", roofed " + zp0.ToString("0.###") + " under the face" : ", open at the face"));
      sb.AppendLine("  bolt bore      " + (2.0 * rb2).ToString("0.###") + " dia (" + (metric ? "ISO 273 medium " : "free drill ") +
                    clN.ToString("0.###") + " + 2 x " + g.ToString("0.###") + "), reaches " + zb1.ToString("0.###") + " below Pln");
    }
    if (log.Count > 0) { sb.AppendLine("--"); foreach (string s2 in log) sb.AppendLine("! " + s2); }

    Nut  = nut;
    Prep = prep;
    Info = sb.ToString();
    }
}
