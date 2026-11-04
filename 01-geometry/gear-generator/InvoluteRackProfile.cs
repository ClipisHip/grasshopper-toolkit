// -----------------------------------------------------------------------------
// gear-generator · Involute Rack                            grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
// ===========================================================================
//  INVOLUTE RACK PROFILE  -  Grasshopper C# component (RhinoCommon)
//  Mating rack for the gear made by InvoluteGearProfile.cs
//  ---------------------------------------------------------------------
//  Component INPUTS  (all "Item" access):
//     Module      double   must match the gear
//     Pressure    double   pressure angle [deg], must match the gear
//     Backlash    double   backlash [%] taken out of the RACK teeth
//     RackTeeth   int      how many teeth to draw
//     GearTeeth   int      teeth on the mating gear (sets the axis height)
//  Component OUTPUTS:
//     Rack        closed planar Curve
//     GearPlane   Plane - feed straight into the gear script's Pln input
//     Info        report + warnings
//
//  Layout: the rack DATUM line lies on the base plane's X axis, teeth point
//  +Y, and one tooth is centred at x = 0.  The mating gear's axis sits at
//  (0, m*z/2 + x*m).  GearPlane is already flipped 180 deg so the gear comes
//  out correctly phased - a tooth space faces the rack tooth at x = 0.
//
//  Backlash adds up: total = gear Backlash + rack Backlash.  Put it all on
//  one part, or split it - the pair is conjugate either way.
// ===========================================================================
// Grasshopper Script Instance
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
		double Module,
		double Pressure,
		double Backlash,
		int RackTeeth,
		int GearTeeth,
		ref object Rack,
		ref object GearPlane,
		ref object Info)
    {
        List<string> log = new List<string>();

    double m  = Module > 1e-9 ? Module : 1.5;
    double pa = Math.Min(45.0, Math.Max(5.0, Pressure > 1e-9 ? Pressure : 20.0));
    double a  = RhinoMath.ToRadians(pa);
    double bl = Math.Max(0.0, Backlash);
    int    N  = RackTeeth > 0 ? RackTeeth : 12;
    int    zg = GearTeeth > 2 ? GearTeeth : 16;

    // --- keep these identical to the gear script -----------------------------
    double xs = 0.12;              // profile shift on the MATING GEAR
    double ha = 1.00;             // addendum coefficient ha*
    double hf = 1.25;             // dedendum coefficient hf*
    double rc = 0.38;             // root fillet radius = rc * module
    double tr = 0.00;             // tip round radius  = tr * module (0 = sharp)
    double bh = 0.0;              // body depth below the root line (0 -> 2*m)
    Plane  pl = Plane.WorldXY;    // base plane

    double sa = Math.Sin(a), ca = Math.Cos(a), ta = Math.Tan(a);

    // ---------- profile dimensions (about the datum line, y = 0) -------------
    double p     = Math.PI * m;                        // linear pitch
    double sBl   = 0.5 * Math.PI * m * bl / 100.0;     // removed for backlash
    double tHalf = 0.25 * Math.PI * m - 0.5 * sBl;     // half thickness at datum
    if (tHalf <= 1e-9) { Info = "Backlash leaves no rack tooth."; return; }

    double har  = ha * m;                              // tip line above datum
    double hfr  = hf * m;                              // root line below datum
    double rho  = rc * m;
    double tipR = tr * m;
    if (bh <= 1e-9) bh = 2.0 * m;

    // root fillet: circular, tangent to the flank and to the root line.
    // Two separate limits apply - it must fit the depth, AND the two fillets
    // in one tooth space must not collide before they reach the root line.
    //   cxr = tHalf + hfr*tan(a) + rho*K,   K = cos(a) - tan(a)*(1 - sin(a))
    // so the space limit is rho < (p/2 - tHalf - hfr*tan(a)) / K.
    double K    = ca - ta * (1.0 - sa);
    double room = 0.5 * p - tHalf - hfr * ta;
    if (K <= 1e-9 || room <= 1e-9)
    { Info = "Rack tooth spaces close up before reaching the root line - reduce Dedendum or the pressure angle."; return; }

    double rhoMax = hfr / (1.0 - sa) * 0.999;          // fits the depth
    double rhoFit = room / K * 0.999;                  // fits between teeth
    if (rhoFit < rhoMax) rhoMax = rhoFit;
    if (rho > rhoMax)
    { rho = rhoMax; log.Add("Root fillet clamped to " + (rho / m).ToString("0.###") + "*m (largest that fits; " +
                            (rhoFit / m).ToString("0.###") + "*m is the tooth-space limit at " + pa.ToString("0.##") + " deg)."); }
    double rhoStd = (hf - ha) * m / (1.0 - sa);
    if (rho > rhoStd * 1.01)
      log.Add("Root fillet " + (rho / m).ToString("0.###") + "*m exceeds the clearance fillet " +
              (rhoStd / m).ToString("0.###") + "*m - the gear tip may not clear this root.");

    double d   = hfr - rho * (1.0 - sa);   // depth below datum of the flank tangency
    double fx  = tHalf + d * ta;           // x offset of that tangency
    double cxr = fx + rho * ca;            // x offset of the root-line tangency
    if (cxr >= 0.5 * p)
    { Info = "Root fillets overlap - reduce the root fillet or the backlash."; return; }

    // optional tip round
    double hTip, tfx, tw;
    if (tipR > 1e-12)
    {
      hTip = har - tipR * (1.0 - sa);
      tfx  = tHalf - hTip * ta;
      tw   = tfx - tipR * ca;
    }
    else { hTip = har; tfx = tHalf - har * ta; tw = tfx; }
    if (tw <= 1e-9) { Info = "Rack teeth come to a point - reduce the tip round or the backlash."; return; }

    // ---------- walk the toothed edge, left to right -------------------------
    int    kLo = -(N / 2);
    double xL  = kLo * p - 0.5 * p;
    double xR  = (kLo + N - 1) * p + 0.5 * p;

    List<Curve> segs = new List<Curve>();
    Point3d cur = new Point3d(xL, -hfr, 0.0);

    for (int k = 0; k < N; k++)
    {
      double xc = (kLo + k) * p;

      Point3d p1 = new Point3d(xc - cxr, -hfr, 0.0);   // root line
      segs.Add(new LineCurve(cur, p1));

      Point3d p2 = new Point3d(xc - fx, -d, 0.0);      // left root fillet
      segs.Add(new ArcCurve(new Arc(p1, new Vector3d(1.0, 0.0, 0.0), p2)));

      Point3d p3 = new Point3d(xc - tfx, hTip, 0.0);   // left flank
      segs.Add(new LineCurve(p2, p3));

      Point3d p4 = new Point3d(xc - tw, har, 0.0);
      Point3d p5 = new Point3d(xc + tw, har, 0.0);
      if (tipR > 1e-12)                                 // left tip round
        segs.Add(new ArcCurve(new Arc(p3, new Vector3d(sa, ca, 0.0), p4)));
      segs.Add(new LineCurve(p4, p5));                  // tip flat

      Point3d p6 = new Point3d(xc + tfx, hTip, 0.0);
      if (tipR > 1e-12)                                 // right tip round
        segs.Add(new ArcCurve(new Arc(p5, new Vector3d(1.0, 0.0, 0.0), p6)));

      Point3d p7 = new Point3d(xc + fx, -d, 0.0);       // right flank
      segs.Add(new LineCurve(p6, p7));

      Point3d p8 = new Point3d(xc + cxr, -hfr, 0.0);    // right root fillet
      segs.Add(new ArcCurve(new Arc(p7, new Vector3d(sa, -ca, 0.0), p8)));

      cur = p8;
    }
    segs.Add(new LineCurve(cur, new Point3d(xR, -hfr, 0.0)));

    // ---------- close the blank ---------------------------------------------
    double yBack = -hfr - bh;
    segs.Add(new LineCurve(new Point3d(xR, -hfr,  0.0), new Point3d(xR, yBack, 0.0)));
    segs.Add(new LineCurve(new Point3d(xR, yBack, 0.0), new Point3d(xL, yBack, 0.0)));
    segs.Add(new LineCurve(new Point3d(xL, yBack, 0.0), new Point3d(xL, -hfr,  0.0)));

    double jt = m * 1e-4;
    Curve[] joined = Curve.JoinCurves(segs, jt);
    if (joined.Length != 1) { Info = "Could not join the rack (" + joined.Length + " pieces)."; return; }
    Curve rackCrv = joined[0];
    if (!rackCrv.IsClosed) rackCrv.MakeClosed(jt);

    // ---------- where the gear goes -----------------------------------------
    double rp = 0.5 * m * zg;
    double H  = rp + xs * m;      // gear axis height above the datum line
    // flipped 180 deg so the gear's tooth space faces the rack tooth at x = 0
    Plane gp = new Plane(new Point3d(0.0, H, 0.0),
                         new Vector3d(-1.0, 0.0, 0.0),
                         new Vector3d( 0.0, -1.0, 0.0));

    Transform xf = Transform.PlaneToPlane(Plane.WorldXY, pl);
    rackCrv.Transform(xf);
    gp.Transform(xf);

    // ---------- report -------------------------------------------------------
    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.AppendLine("module m            " + m.ToString("0.####"));
    sb.AppendLine("pressure angle      " + pa.ToString("0.##") + " deg");
    sb.AppendLine("linear pitch        " + p.ToString("0.####"));
    sb.AppendLine("rack teeth          " + N + "   (length " + (xR - xL).ToString("0.###") + ")");
    sb.AppendLine("backlash            " + bl.ToString("0.##") + " %  (" + sBl.ToString("0.####") + " per tooth)");
    sb.AppendLine("tooth thk @ datum   " + (2.0 * tHalf).ToString("0.####"));
    sb.AppendLine("--");
    sb.AppendLine("tip line            +" + har.ToString("0.####") + " from datum");
    sb.AppendLine("root line           -" + hfr.ToString("0.####") + " from datum");
    sb.AppendLine("whole depth         " + (har + hfr).ToString("0.####"));
    sb.AppendLine("root fillet r       " + rho.ToString("0.####") + "   (" + (rho / m).ToString("0.###") + "*m)");
    sb.AppendLine("--");
    sb.AppendLine("GEAR AXIS at (0, " + H.ToString("0.####") + ") from the datum line");
    sb.AppendLine("  mating gear z     " + zg + " (pitch dia " + (2.0 * rp).ToString("0.####") + ")");
    sb.AppendLine("  roll: rack +1.0 along X  =  gear +" + (1.0 / rp).ToString("0.######") + " rad CCW");
    sb.AppendLine("        one full turn  =  " + (2.0 * Math.PI * rp).ToString("0.####") + " of travel");
    if (log.Count > 0) { sb.AppendLine("--"); foreach (string s in log) sb.AppendLine("! " + s); }

    Rack      = rackCrv;
    GearPlane = gp;
    Info      = sb.ToString();

    }
}
