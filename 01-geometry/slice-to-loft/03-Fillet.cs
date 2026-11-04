// -----------------------------------------------------------------------------
// slice-to-loft · 03 Kink Filleter                          grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
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

    // ===========================================================================
//  03 · KINK FILLETER   —   multi-pass radii, feasibility aware
//  Grasshopper C# Script component (Rhino 8)
// ===========================================================================
//
//  PARAMETER SET-UP
//
//  NOTE  The type hints listed below are OPTIONAL.  Set them or leave them at
//        the default -- every input arrives as "object" and the script
//        normalises it.  What DOES matter is the ACCESS mode in the right-hand
//        column (right-click the input -> Item / List / Tree access).  Get
//        that wrong and nothing works: with Item access on a tree input,
//        Grasshopper runs the script once per curve instead of once per tree.
//
//   INPUTS       type hint   access    meaning
//   ---------------------------------------------------------------------
//    C           Curve       TREE      output "S" of component 02
//    R           double      LIST      the radii, tried in the order given.
//                                      Pass 1 fillets every corner it can
//                                      with R[0], pass 2 picks up what is
//                                      left with R[1], and so on.
//    Max         bool        Item      after all the radii are spent, fillet
//                                      whatever is still sharp with the
//                                      largest radius that eats at most HALF
//                                      of each neighbouring segment.
//                                      Never touches an already filleted
//                                      corner.
//    Ang         double      Item      corners flatter than this (DEGREES of
//                                      deflection) are treated as already
//                                      smooth and are never counted as
//                                      missing.  0 -> 0.5 deg
//    Tol         double      Item      tolerance (0 = document tolerance)
//    Sel         int         Item      WHICH corners to round:
//                                        0 (or empty, or "both") -> both
//                                        1 (or "convex")  -> convex only
//                                        2 (or "concave") -> concave only
//                                      Corners of the kind you did not pick
//                                      are left sharp on purpose and are NOT
//                                      counted in Rem, so Rem still means
//                                      "corners I asked for and did not get".
//                                      Sel is the LAST input, so adding it
//                                      does not shift anything already wired.
//
//   EXTRA RADII WITHOUT REWIRING
//    You can also hit the component's "+" and add further inputs named
//    R1, R2, R3 ... (double, Item).  They are picked up automatically, in
//    numeric order, and appended after whatever came in on R.  Nothing else
//    needs editing.
//
//   OUTPUTS
//    F      tree   filleted curves, identical paths to the input
//    Np     tree   how many points (segment junctions) each result carries
//    Rem    int    TOTAL corners still unfilleted — 0 means you are done
//    Kp     tree {..; i}   the corner points still unfilleted, to look at
//    Ref    tree   per branch, the curve with the MOST points (the datum
//                  that component 04 matches everything else to)
//    Rn     tree   that reference point count
//    Info   list   per-curve report
//    Cvx    tree {..; i}   the CONVEX corner points of each input curve
//    Ccv    tree {..; i}   the CONCAVE corner points
//
//  Cvx and Ccv are appended AFTER Info so adding them does not shift any
//  output already wired.  Convex and concave are judged against the region
//  each curve encloses, taken from the curve's own plane and orientation, so
//  the answer does not depend on how that plane happened to be fitted.  Note
//  that for a hole this means convex-of-the-hole, which is concave in the
//  material — flip the two when you are working on the inner branches, and
//  remember that Sel reads the same way: on a hole branch, Sel = 1 rounds the
//  corners that bulge into the hole.
//
//  How a pass decides
//    A segment must be able to take the fillets BOTH of its still-sharp ends
//    need at that radius, or it takes neither.  A segment too short to hold
//    two fillets therefore keeps both of its corners sharp and they wait for
//    a smaller radius, instead of one of them being rounded arbitrarily and
//    leaving the segment square at one end and round at the other.
//
//    That makes each pass deterministic — there is no ordering to depend on.
//    Corners left over cascade to the next radius, and the Max sweep catches
//    whatever is still sharp, capping each corner at half of what remains of
//    each neighbour so two corners sharing a segment take half each.  Watch
//    the Info line: it counts any segment that still ended up rounded at one
//    end only.
// ===========================================================================

  private void RunScript(
		DataTree<object> C,
		List<object> R,
		object Max,
		object Ang,
		object Tol,
		object Sel,
		List<object> R1,
		ref object F,
		ref object Np,
		ref object Rem,
		ref object Kp,
		ref object Ref,
		ref object Rn,
		ref object Info,
		ref object Cvx,
		ref object Ccv)
  {
    List<GH_Path> paths;
    List<List<Curve>> branches;
    if (!ReadTreeParam(C, "C", out paths, out branches))
    {
      Info = NoInput("C", C, "|R|", "|C|");
      Rem = 0;
      return;
    }
    Solve(paths, branches, AsDoublesParam(R, "R"), AsBool(Max, false), AsDouble(Ang, 0.0), AsDouble(Tol, 0.0), AsSel(Sel),
          ref F, ref Np, ref Rem, ref Kp, ref Ref, ref Rn, ref Info, ref Cvx, ref Ccv);
  }

  private void Solve(List<GH_Path> paths, List<List<Curve>> branches, List<double> R, bool Max, double Ang, double Tol, int Sel, ref object F, ref object Np, ref object Rem, ref object Kp, ref object Ref, ref object Rn, ref object Info, ref object Cvx, ref object Ccv)
  {
    List<string> log = new List<string>();
    double tol = (Tol > 0.0) ? Tol : DocTol();
    double angMin = ((Ang > 0.0) ? Ang : 0.5) * Math.PI / 180.0;

    List<double> radii = new List<double>();
    if (R != null)
      for (int i = 0; i < R.Count; i++)
        if (R[i] > 0.0) radii.Add(R[i]);
    List<double> extra = ExtraRadii();
    for (int i = 0; i < extra.Count; i++) radii.Add(extra[i]);
    if (extra.Count > 0) log.Add("picked up " + extra.Count + " extra radius input(s): " + Join(extra));

    DataTree<Curve>   tF   = new DataTree<Curve>();
    DataTree<int>     tNp  = new DataTree<int>();
    DataTree<Point3d> tKp  = new DataTree<Point3d>();
    DataTree<Curve>   tRef = new DataTree<Curve>();
    DataTree<int>     tRn  = new DataTree<int>();
    DataTree<Point3d> tCvx = new DataTree<Point3d>();
    DataTree<Point3d> tCcv = new DataTree<Point3d>();


    int totalRemaining = 0, totalCvx = 0, totalCcv = 0, totalOneSided = 0, totalSkipped = 0;
    int totalFailBudget = 0, totalFailGeom = 0;

    for (int bi = 0; bi < branches.Count; bi++)
    {
      GH_Path path = paths[bi];
      List<Curve> branch = branches[bi];

      Curve bestRef = null;
      int bestN = -1;

      for (int ci = 0; ci < branch.Count; ci++)
      {
        Curve src = branch[ci];
        if (src == null || !src.IsValid)
        {
          tF.Add(null, path); tNp.Add(0, path);
          log.Add(path.ToString() + "[" + ci + "] : invalid curve");
          continue;
        }

        FRes fr = FilletCurve(src, radii, Max, angMin, tol, Sel);
        Curve res = (fr.Curve == null) ? src.DuplicateCurve() : fr.Curve;

        int npts = PointCount(res, tol);
        tF.Add(res, path);
        tNp.Add(npts, path);

        GH_Path kp = path.AppendElement(ci);
        for (int i = 0; i < fr.RemPts.Count; i++)  tKp.Add(fr.RemPts[i], kp);
        for (int i = 0; i < fr.Convex.Count; i++)  tCvx.Add(fr.Convex[i], kp);
        for (int i = 0; i < fr.Concave.Count; i++) tCcv.Add(fr.Concave[i], kp);

        totalRemaining += fr.Remaining;
        totalCvx += fr.Convex.Count;
        totalCcv += fr.Concave.Count;
        totalOneSided += fr.OneSided;
        totalSkipped += fr.Skipped;
        totalFailBudget += fr.FailBudget;
        totalFailGeom += fr.FailGeom;

        if (fr.Remaining > 0)
          log.Add(path.ToString() + "[" + ci + "] : " + fr.Applied + " filleted, " +
                  fr.Remaining + " still sharp (" + fr.RemConvex + " convex, " +
                  fr.RemConcave + " concave)");

        if (npts > bestN) { bestN = npts; bestRef = res; }
      }

      if (bestRef != null)
      {
        tRef.Add(bestRef, path);
        tRn.Add(bestN, path);
      }
    }

    if (Max && totalRemaining > 0)
      log.Insert(0, "Max was ON and " + totalRemaining + " corner(s) still came out sharp: " +
                 totalFailBudget + " had no room left on a neighbouring segment, " +
                 totalFailGeom + " produced no valid tangent arc at any radius");
    log.Insert(0, "filleting: " + SelName(Sel) +
               (totalSkipped > 0
                 ? ("   |   " + totalSkipped + " corner(s) left sharp by that choice, not counted as remaining")
                 : ""));
    log.Insert(0, "corners: " + totalCvx + " convex, " + totalCcv + " concave" +
               (totalOneSided > 0
                 ? ("   |   " + totalOneSided + " segment(s) ended rounded at one end only — add a smaller radius, or switch Max on")
                 : "   |   no segment ended rounded at one end only"));
    log.Insert(0, "radii used: " + (radii.Count == 0 ? "(none)" : Join(radii)) +
               (Max ? "  + max-fit pass" : "") +
               "   |   corners still sharp: " + totalRemaining);

    F    = tF;
    Np   = tNp;
    Rem  = totalRemaining;
    Kp   = tKp;
    Ref  = tRef;
    Rn   = tRn;
    Cvx  = tCvx;
    Ccv  = tCcv;
    string accWarn = AccessCheck("|R|", "|C|");
    if (accWarn != null) log.Insert(0, "*** WRONG ACCESS MODE: " + accWarn);
    Info = log;
  }

  // <Custom additional code>

  private double DocTol()
  {
    try { if (RhinoDocument != null) return RhinoDocument.ModelAbsoluteTolerance; }
    catch { }
    return 0.001;
  }

  private int AsSel(object o)
  {
    if (o == null) return 0;
    string t = null;
    try { t = o.ToString(); }
    catch { t = null; }
    if (t != null)
    {
      string k = t.Trim().ToLowerInvariant();
      if (k.StartsWith("convex") || k == "cvx" || k == "out") return 1;
      if (k.StartsWith("concave") || k == "ccv" || k == "in") return 2;
      if (k.StartsWith("both") || k == "all") return 0;
    }
    int v = AsInt(o, 0);
    if (v == 1) return 1;
    if (v == 2) return 2;
    return 0;
  }

  private string SelName(int sel)
  {
    if (sel == 1) return "CONVEX corners only";
    if (sel == 2) return "CONCAVE corners only";
    return "both convex and concave corners";
  }

  private string Join(List<double> v)
  {
    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    for (int i = 0; i < v.Count; i++)
    {
      if (i > 0) sb.Append(", ");
      sb.Append(Math.Round(v[i], 6).ToString());
    }
    return sb.ToString();
  }

  // Reads any extra inputs called R1, R2, R3 ... straight off the component,
  // so the "+" button just works without editing this signature.
  private List<double> ExtraRadii()
  {
    List<double> res = new List<double>();
    try
    {
      if (Component == null) return res;
      List<IGH_Param> ps = new List<IGH_Param>();
      for (int i = 0; i < Component.Params.Input.Count; i++)
      {
        IGH_Param p = Component.Params.Input[i];
        if (p == null || p.NickName == null) continue;
        if (!System.Text.RegularExpressions.Regex.IsMatch(p.NickName, "^[Rr][0-9]+$")) continue;
        ps.Add(p);
      }
      ps.Sort(delegate(IGH_Param a, IGH_Param b)
      {
        int ia = int.Parse(a.NickName.Substring(1));
        int ib = int.Parse(b.NickName.Substring(1));
        return ia.CompareTo(ib);
      });

      for (int i = 0; i < ps.Count; i++)
      {
        IGH_Structure vd = ps[i].VolatileData;
        if (vd == null || vd.DataCount == 0) continue;
        for (int b = 0; b < vd.PathCount; b++)
        {
          System.Collections.IList br = vd.get_Branch(vd.Paths[b]);
          if (br == null) continue;
          for (int k = 0; k < br.Count; k++)
          {
            double val = AsDouble(br[k], 0.0);
            if (val > 0.0) res.Add(val);
          }
        }
      }
    }
    catch { }
    return res;
  }

  // -------------------------------------------------------------------------

  private class FRes
  {
    public Curve Curve;
    public int Applied;
    public int Remaining;
    public int RemConvex;
    public int RemConcave;
    public int OneSided;
    public int Skipped;
    public int FailBudget;    // no room left on a neighbouring segment
    public int FailGeom;      // no radius produced a valid tangent arc
    public List<Point3d> SkipPts = new List<Point3d>();
    public List<Point3d> RemPts  = new List<Point3d>();
    public List<Point3d> Convex  = new List<Point3d>();
    public List<Point3d> Concave = new List<Point3d>();
  }

  private FRes FilletCurve(Curve crv, List<double> radii, bool maxFill, double angMin, double tol, int sel)
  {
    FRes res = new FRes();
    res.Curve = crv;

    List<Curve> segs = Explode(crv, tol);
    int n = segs.Count;
    if (n == 0) return res;
    bool closed = crv.IsClosed;
    if (n == 1 && closed) { res.Curve = crv.DuplicateCurve(); return res; }

    double[] len = new double[n];
    for (int i = 0; i < n; i++) len[i] = segs[i].GetLength();

    bool[] live = new bool[n];     // corner i is a real kink
    bool[] pick = new bool[n];     // and is one of the kinds we were asked to round
    bool[] has  = new bool[n];     // and has been rounded
    bool[] cvx  = new bool[n];     // convex, as seen from the region this curve encloses
    Arc[]  arcs = new Arc[n];
    double[] ppE = new double[n];
    double[] pnS = new double[n];
    double[] consStart = new double[n];
    double[] consEnd   = new double[n];

    // which way is "out"?  Taking the normal from the curve's own fitted plane
    // and the orientation measured against that same plane makes the answer
    // invariant: flip the plane and both the turn sign and the orientation flip
    // with it, so convex stays convex.
    Plane cpl;
    bool havePlane = crv.TryGetPlane(out cpl, Math.Max(tol * 10.0, 1e-6));
    Vector3d nn = havePlane ? cpl.ZAxis : new Vector3d(0.0, 0.0, 1.0);
    nn.Unitize();
    int ori = 0;
    if (closed && havePlane)
    {
      CurveOrientation co = crv.ClosedCurveOrientation(cpl);
      if (co == CurveOrientation.CounterClockwise) ori = 1;
      else if (co == CurveOrientation.Clockwise) ori = -1;
    }
    if (ori == 0) ori = 1;

    int first = closed ? 0 : 1;
    for (int i = first; i < n; i++)
    {
      int p = (i - 1 + n) % n;
      Vector3d va = segs[p].TangentAt(segs[p].Domain.Max);
      Vector3d vb = segs[i].TangentAt(segs[i].Domain.Min);
      if (!va.Unitize() || !vb.Unitize()) continue;
      double defl = Vector3d.VectorAngle(va, vb);        // 0 = perfectly smooth
      if (defl < angMin) continue;
      if (defl > Math.PI - 1e-9) continue;               // full cusp, cannot round
      live[i] = true;

      double turn = (Vector3d.CrossProduct(va, vb) * nn) * (double) ori;
      cvx[i] = (turn > 0.0);
      if (cvx[i]) res.Convex.Add(segs[i].PointAtStart);
      else res.Concave.Add(segs[i].PointAtStart);

      // sel: 0 = both, 1 = convex only, 2 = concave only
      pick[i] = (sel == 0) || (sel == 1 && cvx[i]) || (sel == 2 && !cvx[i]);
      if (!pick[i]) { res.Skipped++; res.SkipPts.Add(segs[i].PointAtStart); }
    }

    double eps = Math.Max(tol * 0.5, 1e-9);

    // ---- one pass per radius --------------------------------------------
    for (int ri = 0; ri < radii.Count; ri++)
    {
      double R = radii[ri];

      Arc[] cA = new Arc[n];
      double[] cPa = new double[n], cPb = new double[n], cLa = new double[n], cLb = new double[n];
      bool[] cOk = new bool[n];

      for (int i = 0; i < n; i++)
      {
        if (!pick[i] || has[i]) continue;
        int p = (i - 1 + n) % n;
        Arc a; double pa, pb, la, lb;
        if (!TryCornerFillet(segs[p], segs[i], R, tol, out a, out pa, out pb, out la, out lb)) continue;
        cOk[i] = true; cA[i] = a; cPa[i] = pa; cPb[i] = pb; cLa[i] = la; cLb[i] = lb;
      }

      // A segment must be able to take what BOTH of its still-sharp ends need
      // at this radius, or it takes neither.  This is the rule that stops a
      // short segment coming out rounded at one end and square at the other:
      // if it cannot hold two fillets, both of its corners wait for a smaller
      // radius instead of one of them being served arbitrarily.
      bool[] fits = new bool[n];
      for (int i = 0; i < n; i++)
      {
        int nx = (i + 1) % n;
        bool wantS = pick[i]  && !has[i];
        bool wantE = pick[nx] && !has[nx];

        if (wantS && !cOk[i])  { fits[i] = false; continue; }
        if (wantE && !cOk[nx]) { fits[i] = false; continue; }

        double need = 0.0;
        if (wantS) need += cLb[i];     // corner i eats into the START of segment i
        if (wantE) need += cLa[nx];    // corner nx eats into the END of segment i

        double avail = len[i] - consStart[i] - consEnd[i];
        fits[i] = (need <= avail - eps);
      }

      for (int i = 0; i < n; i++)
      {
        if (!pick[i] || has[i] || !cOk[i]) continue;
        int p = (i - 1 + n) % n;
        if (!fits[p] || !fits[i]) continue;

        has[i] = true;
        arcs[i] = cA[i];
        ppE[i] = cPa[i];
        pnS[i] = cPb[i];
        consEnd[p]   = cLa[i];
        consStart[i] = cLb[i];
        res.Applied++;
      }
    }

    // ---- the max-fit sweep ------------------------------------------------
    // Every remaining corner is served here, capped at half of what is left of
    // each neighbour, so two corners sharing a segment take half each and it
    // still comes out rounded at both ends.
    if (maxFill)
    {
      for (int i = 0; i < n; i++)
      {
        if (!pick[i] || has[i]) continue;
        int p = (i - 1 + n) % n;

        double budP = Math.Min(len[p] * 0.5, len[p] - consStart[p] - eps);
        double budN = Math.Min(len[i] * 0.5, len[i] - consEnd[i]   - eps);
        double lim = Math.Min(budP, budN);
        if (lim <= tol) { res.FailBudget++; continue; }

        Vector3d va = -segs[p].TangentAt(segs[p].Domain.Max);
        Vector3d vb =  segs[i].TangentAt(segs[i].Domain.Min);
        if (!va.Unitize() || !vb.Unitize()) { res.FailGeom++; continue; }
        double theta = Vector3d.VectorAngle(va, vb);
        if (theta <= 1e-6 || theta >= Math.PI - 1e-9) { res.FailGeom++; continue; }

        // the seed is built so the tangent run lands exactly on lim, so the
        // budget test has to carry a little slack or the seed fails itself on
        // nothing but rounding
        double slack = 1e-9 * Math.Max(1.0, lim);
        double seed = lim * Math.Tan(theta * 0.5);
        if (seed <= tol) { res.FailBudget++; continue; }

        Arc bA = Arc.Unset;
        double bPa = 0, bPb = 0, bLa = 0, bLb = 0;
        bool found = false;
        double lo = 0.0, hi = seed;
        Arc tA; double tPa, tPb, tLa, tLb;

        // grow while it still fits, KEEPING the largest that did.  The old
        // version moved lo up without recording the arc, so a seed that fitted
        // exactly was consumed by the growth step and then thrown away when no
        // larger radius could fit — which is how corners survived Max.
        int grow = 0;
        while (grow++ < 12 &&
               TryCornerFillet(segs[p], segs[i], hi, tol, out tA, out tPa, out tPb, out tLa, out tLb) &&
               tLa <= budP + slack && tLb <= budN + slack)
        {
          bA = tA; bPa = tPa; bPb = tPb; bLa = tLa; bLb = tLb;
          found = true;
          lo = hi;
          hi *= 2.0;
        }

        for (int it = 0; it < 40; it++)
        {
          double mid = 0.5 * (lo + hi);
          if (mid <= tol) break;
          if (mid <= lo * (1.0 + 1e-12)) break;
          if (TryCornerFillet(segs[p], segs[i], mid, tol, out tA, out tPa, out tPb, out tLa, out tLb) &&
              tLa <= budP + slack && tLb <= budN + slack)
          {
            lo = mid;
            bA = tA; bPa = tPa; bPb = tPb; bLa = tLa; bLb = tLb;
            found = true;
          }
          else hi = mid;
        }

        if (!found) { res.FailGeom++; continue; }

        has[i] = true;
        arcs[i] = bA;
        ppE[i] = bPa;
        pnS[i] = bPb;
        consEnd[p]   = bLa;
        consStart[i] = bLb;
        res.Applied++;
      }
    }

    // ---- what is left ----------------------------------------------------
    for (int i = 0; i < n; i++)
    {
      if (!pick[i] || has[i]) continue;
      res.Remaining++;
      if (cvx[i]) res.RemConvex++; else res.RemConcave++;
      res.RemPts.Add(segs[i].PointAtStart);
    }

    // a segment rounded at one end while its other end is still a live corner
    for (int i = 0; i < n; i++)
    {
      int nx = (i + 1) % n;
      bool a = pick[i]  && has[i];
      bool b = pick[nx] && has[nx];
      bool sa = pick[i]  && !has[i];
      bool sb = pick[nx] && !has[nx];
      if ((a && sb) || (b && sa)) res.OneSided++;
    }

    // ---- reassemble ------------------------------------------------------
    double[] pS = new double[n];
    double[] pE = new double[n];
    for (int i = 0; i < n; i++)
    {
      pS[i] = segs[i].Domain.Min;
      pE[i] = segs[i].Domain.Max;
    }
    for (int i = 0; i < n; i++)
    {
      if (!has[i]) continue;
      int p = (i - 1 + n) % n;
      pE[p] = ppE[i];
      pS[i] = pnS[i];
    }

    PolyCurve pc = new PolyCurve();
    for (int i = 0; i < n; i++)
    {
      if (has[i])
      {
        ArcCurve ac = new ArcCurve(arcs[i]);
        if (ac.IsValid && ac.GetLength() > tol * 0.25) pc.Append(ac);
      }
      if (pE[i] - pS[i] > RhinoMath.SqrtEpsilon)
      {
        Curve piece = segs[i].Trim(pS[i], pE[i]);
        if (piece != null && piece.IsValid && piece.GetLength() > tol * 0.5) pc.Append(piece);
      }
    }
    pc.RemoveNesting();

    Curve outCrv = pc;
    if (closed && !outCrv.IsClosed)
    {
      PolyCurve pc2 = pc.DuplicatePolyCurve();
      if (pc2.MakeClosed(tol)) outCrv = pc2;
    }
    res.Curve = outCrv.IsValid ? outCrv : crv.DuplicateCurve();
    return res;
  }

  // -------------------------------------------------------------------------

  private bool TryCornerFillet(Curve a, Curve b, double R, double tol,
                               out Arc arc, out double pa, out double pb, out double la, out double lb)
  {
    arc = Arc.Unset;
    pa = 0.0; pb = 0.0; la = 0.0; lb = 0.0;
    if (a == null || b == null) return false;
    if (R <= tol * 0.5) return false;

    Point3d corner = a.PointAtEnd;
    Vector3d va = -a.TangentAt(a.Domain.Max);
    Vector3d vb =  b.TangentAt(b.Domain.Min);
    if (!va.Unitize() || !vb.Unitize()) return false;
    double theta = Vector3d.VectorAngle(va, vb);
    if (theta <= 1e-6 || theta >= Math.PI - 1e-9) return false;

    if (a.IsLinear(tol) && b.IsLinear(tol))
    {
      double t = R / Math.Tan(theta * 0.5);
      if (t <= 0.0 || double.IsNaN(t) || double.IsInfinity(t)) return false;
      Point3d pA = corner + va * t;
      Point3d pB = corner + vb * t;
      Vector3d dir = -va;
      Arc f = new Arc(pA, dir, pB);
      if (!f.IsValid) return false;
      if (!a.ClosestPoint(pA, out pa)) return false;
      if (!b.ClosestPoint(pB, out pb)) return false;
      arc = f;
      la = t; lb = t;
      return true;
    }

    // Seed a little way in from the shared corner.  Seeding exactly AT it
    // leaves which side the fillet belongs on ambiguous, and that is where
    // corners with an arc on one side were failing.
    double inA = Math.Min(a.GetLength() * 0.5, Math.Max(R, tol * 10.0));
    double inB = Math.Min(b.GetLength() * 0.5, Math.Max(R, tol * 10.0));
    double sA, sB;
    if (!a.LengthParameter(Math.Max(a.GetLength() - inA, 0.0), out sA)) sA = a.Domain.Max;
    if (!b.LengthParameter(inB, out sB)) sB = b.Domain.Min;

    Arc g;
    try { g = Curve.CreateFillet(a, b, R, sA, sB); }
    catch { return false; }
    if (!g.IsValid || g.Radius <= 0.0)
    {
      try { g = Curve.CreateFillet(a, b, R, a.Domain.Max, b.Domain.Min); }
      catch { return false; }
    }
    if (!g.IsValid || g.Radius <= 0.0) return false;

    double u;
    if (!a.ClosestPoint(g.StartPoint, out u)) return false;
    double dS = a.PointAt(u).DistanceTo(g.StartPoint);
    if (!a.ClosestPoint(g.EndPoint, out u)) return false;
    double dE = a.PointAt(u).DistanceTo(g.EndPoint);

    Point3d pA2 = (dS <= dE) ? g.StartPoint : g.EndPoint;
    Point3d pB2 = (dS <= dE) ? g.EndPoint   : g.StartPoint;

    if (!a.ClosestPoint(pA2, out pa)) return false;
    if (!b.ClosestPoint(pB2, out pb)) return false;

    double snap = Math.Max(tol * 10.0, 1e-6);
    if (a.PointAt(pa).DistanceTo(pA2) > snap) return false;
    if (b.PointAt(pb).DistanceTo(pB2) > snap) return false;

    Vector3d tanA = a.TangentAt(pa);
    Arc f2 = new Arc(a.PointAt(pa), tanA, b.PointAt(pb));
    if (!f2.IsValid) return false;
    arc = f2;

    la = Math.Abs(a.GetLength(new Interval(pa, a.Domain.Max)));
    lb = Math.Abs(b.GetLength(new Interval(b.Domain.Min, pb)));
    if (la <= 0.0 || lb <= 0.0) return false;
    return true;
  }

  private int CountSegments(Curve c)
  {
    if (c == null) return 0;
    PolyCurve pc = c as PolyCurve;
    if (pc != null) return pc.SegmentCount;
    return 1;
  }

  // Fully recursive.  PolyCurve.Explode() hands back whatever each segment was
  // STORED as, so a polyline sitting inside a polycurve comes back as one item
  // even though it carries a dozen points — which made every count wrong and
  // made the segment output look like a list of curves rather than segments.
  private List<Curve> Explode(Curve c, double tol)
  {
    List<Curve> res = new List<Curve>();
    ExplodeInto(c, tol, res, 0);
    if (res.Count == 0 && c != null && c.IsValid) res.Add(c.DuplicateCurve());
    return res;
  }

  private void ExplodeInto(Curve c, double tol, List<Curve> res, int depth)
  {
    if (c == null || !c.IsValid) return;
    if (depth > 8) { res.Add(c.DuplicateCurve()); return; }

    PolyCurve pc = c as PolyCurve;
    if (pc != null)
    {
      PolyCurve dup = pc.DuplicatePolyCurve();
      dup.RemoveNesting();
      Curve[] sg = dup.Explode();
      if (sg != null && sg.Length > 0)
      {
        for (int i = 0; i < sg.Length; i++) ExplodeInto(sg[i], tol, res, depth + 1);
        return;
      }
      res.Add(c.DuplicateCurve());
      return;
    }

    // catches PolylineCurve AND a degree 1 nurbs curve, which is the same thing
    // wearing a different type
    Polyline pl;
    if (c.TryGetPolyline(out pl) && pl.Count > 2)
    {
      for (int i = 0; i < pl.Count - 1; i++)
      {
        if (pl[i].DistanceTo(pl[i + 1]) <= RhinoMath.ZeroTolerance) continue;
        res.Add(new LineCurve(pl[i], pl[i + 1]));
      }
      return;
    }

    // a single span that may still carry kinks inside it
    List<double> ts = new List<double>();
    double t0 = c.Domain.Min, t1 = c.Domain.Max, t;
    int guard = 0;
    while (guard++ < 1000 && c.GetNextDiscontinuity(Continuity.C1_locus_continuous, t0, t1, out t))
    {
      if (t <= t0 + RhinoMath.ZeroTolerance) break;
      ts.Add(t);
      t0 = t;
    }
    if (ts.Count > 0)
    {
      Curve[] pieces = c.Split(ts);
      if (pieces != null && pieces.Length > 1)
      {
        for (int i = 0; i < pieces.Length; i++) ExplodeInto(pieces[i], tol, res, depth + 1);
        return;
      }
    }

    res.Add(c.DuplicateCurve());
  }

  // The number that decides whether a loft is clean: how many points describe
  // the curve.  For a closed curve that is the number of segment junctions.
  private int PointCount(Curve c, double tol)
  {
    if (c == null) return 0;
    int n = Explode(c, tol).Count;
    if (n == 0) return 0;
    return c.IsClosed ? n : (n + 1);
  }

  // Warns when an input's ACCESS mode is wrong.  This is the single most common
  // reason a component silently "does nothing": editing the RunScript signature
  // resets access to Item, and on Item access Grasshopper runs the script once
  // per item instead of once for the whole set, so the script only ever sees a
  // single curve and has nothing to sort, compare or branch.
  private string AccessCheck(string wantList, string wantTree)
  {
    try
    {
      if (Component == null) return null;
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      for (int i = 0; i < Component.Params.Input.Count; i++)
      {
        IGH_Param p = Component.Params.Input[i];
        if (p == null || p.NickName == null) continue;
        string tag = "|" + p.NickName + "|";
        GH_ParamAccess want;
        if (wantList.Contains(tag)) want = GH_ParamAccess.list;
        else if (wantTree.Contains(tag)) want = GH_ParamAccess.tree;
        else continue;
        if (p.Access == want) continue;
        sb.Append("input '" + p.NickName + "' is on " + p.Access.ToString() +
                  " access but needs " + want.ToString().ToUpper() +
                  " (right-click the input to change it).  ");
      }
      return (sb.Length == 0) ? null : sb.ToString();
    }
    catch { return null; }
  }

  // ---- input normalisers --------------------------------------------------
  // These let every input arrive as plain "object", which is what Grasshopper
  // hands a tree-access parameter.  Type hints no longer matter anywhere —
  // only the ACCESS mode (Item / List / Tree) has to be right.

  private Curve ToCurve(object o)
  {
    if (o == null) return null;
    Curve c = o as Curve;
    if (c != null) return c;
    GH_Curve gc = o as GH_Curve;
    if (gc != null) return gc.Value;
    IGH_Goo g = o as IGH_Goo;
    if (g != null)
    {
      Curve q = null;
      try { if (g.CastTo<Curve>(out q) && q != null) return q; }
      catch { }
    }
    return null;
  }

  private double AsDouble(object o, double dflt)
  {
    if (o == null) return dflt;
    if (o is double) return (double) o;
    if (o is int)    return (double) (int) o;
    if (o is float)  return (double) (float) o;
    IGH_Goo g = o as IGH_Goo;
    if (g != null)
    {
      double d;
      try { if (g.CastTo<double>(out d)) return d; }
      catch { }
      int i2;
      try { if (g.CastTo<int>(out i2)) return (double) i2; }
      catch { }
    }
    double pv;
    if (double.TryParse(o.ToString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out pv)) return pv;
    return dflt;
  }

  private int AsInt(object o, int dflt)
  {
    if (o == null) return dflt;
    if (o is int)    return (int) o;
    if (o is double) return (int) Math.Round((double) o);
    IGH_Goo g = o as IGH_Goo;
    if (g != null)
    {
      int i2;
      try { if (g.CastTo<int>(out i2)) return i2; }
      catch { }
      double d;
      try { if (g.CastTo<double>(out d)) return (int) Math.Round(d); }
      catch { }
    }
    int pv;
    if (int.TryParse(o.ToString(), out pv)) return pv;
    return dflt;
  }

  private bool AsBool(object o, bool dflt)
  {
    if (o == null) return dflt;
    if (o is bool) return (bool) o;
    IGH_Goo g = o as IGH_Goo;
    if (g != null)
    {
      bool b;
      try { if (g.CastTo<bool>(out b)) return b; }
      catch { }
    }
    bool pb;
    if (bool.TryParse(o.ToString(), out pb)) return pb;
    return dflt;
  }

  private Plane AsPlane(object o, Plane dflt)
  {
    if (o == null) return dflt;
    if (o is Plane) return (Plane) o;
    IGH_Goo g = o as IGH_Goo;
    if (g != null)
    {
      Plane p;
      try { if (g.CastTo<Plane>(out p) && p.IsValid) return p; }
      catch { }
    }
    return dflt;
  }

  // ---- collection readers -------------------------------------------------

  private List<object> Flatten(object src)
  {
    List<object> res = new List<object>();
    if (src == null) return res;

    IGH_Structure st = src as IGH_Structure;
    if (st != null)
    {
      for (int i = 0; i < st.PathCount; i++)
      {
        System.Collections.IList br = st.get_Branch(st.Paths[i]);
        if (br == null) continue;
        for (int k = 0; k < br.Count; k++) res.Add(br[k]);
      }
      return res;
    }

    System.Collections.IEnumerable en = src as System.Collections.IEnumerable;
    if (en != null && !(src is string))
    {
      foreach (object o in en) res.Add(o);
      return res;
    }

    res.Add(src);
    return res;
  }

  private bool ReadTree(object src, out List<GH_Path> paths, out List<List<Curve>> branches)
  {
    paths = new List<GH_Path>();
    branches = new List<List<Curve>>();
    if (src == null) return false;

    GH_Structure<GH_Curve> sc = src as GH_Structure<GH_Curve>;
    if (sc != null)
    {
      for (int i = 0; i < sc.Branches.Count; i++)
      {
        List<Curve> cs = new List<Curve>();
        for (int k = 0; k < sc.Branches[i].Count; k++) cs.Add(ToCurve(sc.Branches[i][k]));
        paths.Add(sc.Paths[i]);
        branches.Add(cs);
      }
      return paths.Count > 0;
    }

    GH_Structure<IGH_Goo> sg = src as GH_Structure<IGH_Goo>;
    if (sg != null)
    {
      for (int i = 0; i < sg.Branches.Count; i++)
      {
        List<Curve> cs = new List<Curve>();
        for (int k = 0; k < sg.Branches[i].Count; k++) cs.Add(ToCurve(sg.Branches[i][k]));
        paths.Add(sg.Paths[i]);
        branches.Add(cs);
      }
      return paths.Count > 0;
    }

    IGH_Structure st = src as IGH_Structure;
    if (st != null)
    {
      for (int i = 0; i < st.PathCount; i++)
      {
        GH_Path p = st.Paths[i];
        System.Collections.IList br = st.get_Branch(p);
        List<Curve> cs = new List<Curve>();
        if (br != null)
          for (int k = 0; k < br.Count; k++) cs.Add(ToCurve(br[k]));
        paths.Add(p);
        branches.Add(cs);
      }
      return paths.Count > 0;
    }

    // a flat list, or one lone curve — treat it as a single branch
    List<object> flat = Flatten(src);
    List<Curve> one = new List<Curve>();
    for (int i = 0; i < flat.Count; i++)
    {
      Curve cc = ToCurve(flat[i]);
      if (cc != null) one.Add(cc);
    }
    if (one.Count == 0) return false;
    paths.Add(new GH_Path(0));
    branches.Add(one);
    return true;
  }

  private List<Curve> AsCurves(object o)
  {
    List<Curve> res = new List<Curve>();
    List<object> flat = Flatten(o);
    for (int i = 0; i < flat.Count; i++)
    {
      Curve c = ToCurve(flat[i]);
      if (c != null) res.Add(c);
    }
    return res;
  }

  private List<Plane> AsPlanes(object o)
  {
    List<Plane> res = new List<Plane>();
    List<object> flat = Flatten(o);
    for (int i = 0; i < flat.Count; i++)
    {
      Plane p = AsPlane(flat[i], Plane.Unset);
      if (p.IsValid) res.Add(p);
    }
    return res;
  }

  private List<double> AsDoubles(object o)
  {
    List<double> res = new List<double>();
    List<object> flat = Flatten(o);
    for (int i = 0; i < flat.Count; i++) res.Add(AsDouble(flat[i], 0.0));
    return res;
  }

  // ---- input diagnostics --------------------------------------------------

  private string TypeName(object o)
  {
    if (o == null) return "null";
    try { return o.GetType().ToString(); }
    catch { return "?"; }
  }

  // The component's own volatile data for an input.  This is always a full
  // tree, whatever the access mode or type hint, so it is the reliable way in
  // when the object handed to RunScript turns out to be something this script
  // does not recognise.
  private IGH_Structure ParamData(string nick)
  {
    try
    {
      if (Component == null) return null;
      for (int i = 0; i < Component.Params.Input.Count; i++)
      {
        IGH_Param p = Component.Params.Input[i];
        if (p != null && p.NickName == nick) return p.VolatileData;
      }
    }
    catch { }
    return null;
  }

  private string ParamReport(string nick)
  {
    try
    {
      if (Component == null) return "(component not reachable)";
      for (int i = 0; i < Component.Params.Input.Count; i++)
      {
        IGH_Param p = Component.Params.Input[i];
        if (p == null || p.NickName != nick) continue;
        IGH_Structure vd = p.VolatileData;
        if (vd == null) return "access=" + p.Access.ToString() + ", no volatile data";
        string first = "-";
        for (int b = 0; b < vd.PathCount && first == "-"; b++)
        {
          System.Collections.IList br = vd.get_Branch(vd.Paths[b]);
          if (br != null && br.Count > 0 && br[0] != null) first = TypeName(br[0]);
        }
        return vd.DataCount + " item(s) in " + vd.PathCount + " branch(es), access=" +
               p.Access.ToString() + ", first item is " + first;
      }
      System.Text.StringBuilder names = new System.Text.StringBuilder();
      for (int i = 0; i < Component.Params.Input.Count; i++)
      {
        if (names.Length > 0) names.Append(", ");
        names.Append(Component.Params.Input[i].NickName);
      }
      return "there is no input named '" + nick + "' — this component's inputs are: " + names.ToString();
    }
    catch (Exception ex) { return "(" + ex.Message + ")"; }
  }

  // Reads a curve tree, falling back to the component's volatile data if the
  // object passed into RunScript is not a shape this script knows how to walk.
  private bool ReadTreeParam(object src, string nick, out List<GH_Path> paths, out List<List<Curve>> branches)
  {
    if (ReadTree(src, out paths, out branches)) return true;
    IGH_Structure vd = ParamData(nick);
    if (vd != null && ReadTree(vd, out paths, out branches)) return true;
    return false;
  }

  private List<Curve> AsCurvesParam(object src, string nick)
  {
    List<Curve> res = AsCurves(src);
    if (res.Count > 0) return res;
    IGH_Structure vd = ParamData(nick);
    if (vd != null) res = AsCurves(vd);
    return res;
  }

  private List<Plane> AsPlanesParam(object src, string nick)
  {
    List<Plane> res = AsPlanes(src);
    if (res.Count > 0) return res;
    IGH_Structure vd = ParamData(nick);
    if (vd != null) res = AsPlanes(vd);
    return res;
  }

  private List<double> AsDoublesParam(object src, string nick)
  {
    List<double> res = AsDoubles(src);
    if (res.Count > 0) return res;
    IGH_Structure vd = ParamData(nick);
    if (vd != null) res = AsDoubles(vd);
    return res;
  }

  // What to report when an input arrives empty.  Says which access mode is
  // wrong, what .NET type actually turned up, and what the component itself
  // is holding for that input — which separates "nothing is wired" from
  // "wired fine, but the script could not read it".
  private List<string> NoInput(string nick, object got, string wantList, string wantTree)
  {
    List<string> log = new List<string>();
    string w = AccessCheck(wantList, wantTree);
    if (w != null) log.Add("*** WRONG ACCESS MODE: " + w);
    log.Add("nothing usable reached input '" + nick + "'");
    log.Add("   RunScript was handed: " + TypeName(got));
    log.Add("   the component holds:  " + ParamReport(nick));
    return log;
  }

  // </Custom additional code>

}
