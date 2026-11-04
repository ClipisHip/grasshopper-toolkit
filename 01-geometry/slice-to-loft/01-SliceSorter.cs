// -----------------------------------------------------------------------------
// slice-to-loft · 01 Slice Sorter                           grasshopper-toolkit
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
//  01 · SLICE SORTER   —   levels + inner/outer + cross-level region chaining
//  Grasshopper C# Script component (Rhino 8)
// ===========================================================================
//
//  PARAMETER SET-UP
//
//  NOTE  The type hints listed below are OPTIONAL.  Set them or leave them at
//        the default -- every input arrives as "object" and the script
//        normalises it.  What DOES matter is the ACCESS mode in the right-hand
//        column (right-click the input -> Item / List / Tree access).
//
//   INPUTS       type hint   access    meaning
//   ---------------------------------------------------------------------
//    C           Curve       LIST      all section curves (closed, planar)
//    PL          Plane       LIST      the cut planes.  Optional — if empty
//                                      they are rebuilt from P / H / N.
//    P           Plane       Item      base plane (default = World XY)
//    H           double      Item      total height spanned by the cuts
//    N           int         Item      number of cuts (planes), N >= 1,
//                                      spread over 0..H inclusive
//    MO          double      Item      minimum OVERLAP, 0..1, for a curve to
//                                      continue an existing branch.  0 -> 0.15
//    Tol         double      Item      tolerance (0 = document tolerance)
//
//   OUTPUTS
//    Crv    tree {io ; group}   curves, sorted bottom -> top
//                               io = 0 -> OUTER      io = 1 -> INNER
//    Lev    tree {io ; group}   level index of each curve   (parallel to Crv)
//    Dep    tree {io ; group}   containment depth 0,1,2...  (io = depth % 2)
//    Pln    list               the level planes actually used, bottom -> top
//    Info   list               report + warnings
//    Par    tree {io ; group}   ONE item per branch: the branch its region
//                               sits inside, or "-" for a top level region
//
//  ---------------------------------------------------------------------------
//  HOW A BRANCH IS DECIDED
//
//  Inner and outer is a fact about ONE level: a curve is inner when an odd
//  number of curves at its own level contain it.  Nothing about another level
//  is allowed to influence that.
//
//  A curve continues an existing branch only when BOTH hold:
//
//    1. LINEAGE.  It sits inside the same region the branch's last curve sits
//       inside.  A hole in boss A can never join a branch of holes in boss B,
//       because their parents are different branches.  Top level outlines
//       match only other top level outlines.
//
//    2. OVERLAP.  Projected down the plane normal, the two regions actually
//       cover each other by at least MO (intersection over union, measured on
//       a sample grid).  A small boss and the main outline are both centred on
//       the axis, so they sit almost on top of each other by centroid — but
//       they barely overlap in area, so they cannot be confused.
//
//  Centroid proximity, which the earlier version used, satisfies neither.  On
//  a concentric part every centroid lands near the axis and the pairing came
//  out essentially arbitrary.
// ===========================================================================

  private void RunScript(
		List<object> C,
		List<object> PL,
		object P,
		object H,
		object N,
		object MO,
		object Tol,
		ref object Crv,
		ref object Lev,
		ref object Dep,
		ref object Pln,
		ref object Info,
		ref object Par)
  {
    Solve(AsCurves(C), AsPlanes(PL), AsPlane(P, Plane.WorldXY), AsDouble(H, 0.0),
          AsInt(N, 0), AsDouble(MO, 0.0), AsDouble(Tol, 0.0),
          ref Crv, ref Lev, ref Dep, ref Pln, ref Info, ref Par);
  }

  private void Solve(List<Curve> C, List<Plane> PL, Plane P, double H, int N, double MO, double Tol, ref object Crv, ref object Lev, ref object Dep, ref object Pln, ref object Info, ref object Par)
  {
    List<string> log = new List<string>();
    double tol = (Tol > 0.0) ? Tol : DocTol();
    double minOv = (MO > 0.0) ? MO : 0.15;

    if (C == null || C.Count == 0)
    {
      string w = AccessCheck("|C||PL|", "");
      log.Add(w != null ? ("*** WRONG ACCESS MODE: " + w) : "no curves supplied");
      Info = log;
      return;
    }

    // ---- 1 · the level planes --------------------------------------------
    Plane bas = P.IsValid ? P : Plane.WorldXY;
    List<Plane> planes = new List<Plane>();

    if (PL != null && PL.Count > 0)
    {
      for (int i = 0; i < PL.Count; i++)
        if (PL[i].IsValid) planes.Add(PL[i]);
    }
    else
    {
      int n = (N > 0) ? N : 1;
      if (n == 1) planes.Add(bas);
      else
      {
        double step = H / (double) (n - 1);
        for (int i = 0; i < n; i++)
        {
          Plane q = new Plane(bas);
          q.Origin = bas.Origin + bas.ZAxis * (step * i);
          planes.Add(q);
        }
      }
    }
    if (planes.Count == 0) { Info = new List<string>() { "no valid planes" }; return; }

    Vector3d nrm = bas.ZAxis;
    nrm.Unitize();
    Point3d org = bas.Origin;

    double[] pElev = new double[planes.Count];
    for (int i = 0; i < planes.Count; i++) pElev[i] = Elev(planes[i].Origin, org, nrm);
    int[] pOrder = new int[planes.Count];
    for (int i = 0; i < planes.Count; i++) pOrder[i] = i;
    Array.Sort(pOrder, delegate(int a, int b) { return pElev[a].CompareTo(pElev[b]); });
    List<Plane> sortedPlanes = new List<Plane>();
    double[] sortedElev = new double[planes.Count];
    for (int i = 0; i < planes.Count; i++)
    {
      sortedPlanes.Add(planes[pOrder[i]]);
      sortedElev[i] = pElev[pOrder[i]];
    }
    planes = sortedPlanes;
    pElev = sortedElev;
    int np = planes.Count;

    log.Add("received " + C.Count + " curve(s); " +
            ((PL != null && PL.Count > 0)
              ? ("using the " + np + " plane(s) wired into PL")
              : ("built " + np + " plane(s) from H=" + H + ", N=" + N)));

    // ---- 2 · park every curve on its nearest level -----------------------
    List<List<Curve>> byLevel = new List<List<Curve>>();
    for (int i = 0; i < np; i++) byLevel.Add(new List<Curve>());

    double maxDev = 0.0;
    int skipped = 0;

    for (int i = 0; i < C.Count; i++)
    {
      Curve c = C[i];
      if (c == null || !c.IsValid) { skipped++; log.Add("curve " + i + " : invalid — skipped"); continue; }

      Curve d = c.DuplicateCurve();
      if (!d.IsClosed && !d.MakeClosed(tol))
      {
        skipped++;
        double gap = d.PointAtStart.DistanceTo(d.PointAtEnd);
        log.Add("curve " + i + " : open, ends " + Math.Round(gap, 6) +
                " apart (tolerance is " + tol + ") — skipped.  Join it upstream, or raise Tol above that gap.");
        continue;
      }

      Plane cp;
      if (!d.TryGetPlane(out cp, Math.Max(tol * 10.0, 1e-6)))
      { skipped++; log.Add("curve " + i + " : not planar — skipped"); continue; }

      double e = Elev(d.PointAtStart, org, nrm);
      int best = 0; double bestD = double.MaxValue;
      for (int k = 0; k < np; k++)
      {
        double dd = Math.Abs(e - pElev[k]);
        if (dd < bestD) { bestD = dd; best = k; }
      }
      if (bestD > maxDev) maxDev = bestD;
      byLevel[best].Add(d);
    }

    if (maxDev > tol)
      log.Add("largest curve-to-plane deviation was " + Math.Round(maxDev, 6) +
              " (> tolerance " + tol + ") — curves were still snapped to the nearest plane");

    // ---- 3 · per level: containment depth AND immediate parent -----------
    double cTol = Math.Max(tol, 1e-12);

    List<int[]> depByLevel = new List<int[]>();
    List<int[]> parByLevel = new List<int[]>();
    List<double[]> areaByLevel = new List<double[]>();

    for (int L = 0; L < np; L++)
    {
      List<Curve> lc = byLevel[L];
      int cnt = lc.Count;
      double[] ar = new double[cnt];
      for (int i = 0; i < cnt; i++) ar[i] = CurveArea(lc[i], bas);

      int[] dep = new int[cnt];
      int[] par = new int[cnt];
      for (int i = 0; i < cnt; i++) { dep[i] = 0; par[i] = -1; }

      double[] parArea = new double[cnt];
      for (int i = 0; i < cnt; i++) parArea[i] = double.MaxValue;

      for (int i = 0; i < cnt; i++)
      {
        for (int j = 0; j < cnt; j++)
        {
          if (i == j) continue;
          if (!IsInside(lc[i], lc[j], planes[L], cTol, ar[i], ar[j])) continue;
          dep[i] = dep[i] + 1;
          if (ar[j] < parArea[i]) { parArea[i] = ar[j]; par[i] = j; }   // smallest container = immediate parent
        }
      }

      depByLevel.Add(dep);
      parByLevel.Add(par);
      areaByLevel.Add(ar);

      int nOut = 0, nIn = 0;
      for (int i = 0; i < cnt; i++) { if ((dep[i] % 2) == 0) nOut++; else nIn++; }
      log.Add("level " + L + " @ " + Math.Round(pElev[L], 4) + " : " + cnt +
              " curve(s) -> " + nOut + " outer, " + nIn + " inner");
    }

    // ---- 4 · chain the regions from level to level -----------------------
    List<List<Grp>> groupsByIo = new List<List<Grp>>();
    groupsByIo.Add(new List<Grp>());   // io 0 : outer
    groupsByIo.Add(new List<Grp>());   // io 1 : inner

    for (int L = 0; L < np; L++)
    {
      List<Curve> lc = byLevel[L];
      int cnt = lc.Count;
      if (cnt == 0) continue;

      int[] dep = depByLevel[L];
      int[] par = parByLevel[L];
      double[] ar = areaByLevel[L];

      int[] gAssign = new int[cnt];
      for (int i = 0; i < cnt; i++) gAssign[i] = -1;

      int maxD = 0;
      for (int i = 0; i < cnt; i++) if (dep[i] > maxD) maxD = dep[i];

      // shallowest first, so a parent always has its branch before its children
      for (int d = 0; d <= maxD; d++)
      {
        int io = d % 2;

        List<int> items = new List<int>();
        for (int i = 0; i < cnt; i++) if (dep[i] == d) items.Add(i);
        if (items.Count == 0) continue;

        items.Sort(delegate(int x, int y) { return ar[y].CompareTo(ar[x]); });   // biggest first

        string[] pk = new string[items.Count];
        for (int k = 0; k < items.Count; k++)
        {
          int pi = par[items[k]];
          pk[k] = (pi < 0) ? "" : Key(dep[pi] % 2, gAssign[pi]);
        }

        List<Grp> gs = groupsByIo[io];

        // score every lineage-compatible pair by real area overlap
        List<int> pItem = new List<int>();
        List<int> pGrp = new List<int>();
        List<double> pOv = new List<double>();

        for (int k = 0; k < items.Count; k++)
        {
          for (int g = 0; g < gs.Count; g++)
          {
            if (gs[g].ParentKey != pk[k]) continue;                    // lineage gate
            Reg last = gs[g].Members[gs[g].Members.Count - 1];
            if (last.Level == L) continue;
            double ov = Overlap(lc[items[k]], last.Crv, bas, cTol);
            if (ov < minOv) continue;                                  // overlap gate
            pItem.Add(k); pGrp.Add(g); pOv.Add(ov);
          }
        }

        int[] ord = new int[pOv.Count];
        for (int t = 0; t < ord.Length; t++) ord[t] = t;
        Array.Sort(ord, delegate(int x, int y) { return pOv[y].CompareTo(pOv[x]); });   // best overlap first

        bool[] usedItem = new bool[items.Count];
        bool[] usedGrp = new bool[gs.Count];

        for (int t = 0; t < ord.Length; t++)
        {
          int q = ord[t];
          int k = pItem[q], g = pGrp[q];
          if (usedItem[k] || usedGrp[g]) continue;
          int ci = items[k];
          gs[g].Members.Add(MakeReg(lc[ci], L, dep[ci], ar[ci]));
          gAssign[ci] = g;
          usedItem[k] = true;
          usedGrp[g] = true;
        }

        for (int k = 0; k < items.Count; k++)
        {
          if (usedItem[k]) continue;
          int ci = items[k];
          Grp fresh = new Grp();
          fresh.Io = io;
          fresh.ParentKey = pk[k];
          fresh.Members = new List<Reg>();
          fresh.Members.Add(MakeReg(lc[ci], L, dep[ci], ar[ci]));
          gs.Add(fresh);
          gAssign[ci] = gs.Count - 1;

          if (L > 0)
            log.Add("level " + L + " : a new " + (io == 0 ? "OUTER" : "INNER") +
                    " region starts branch {" + io + ";" + (gs.Count - 1) + "}" +
                    (pk[k] == "" ? "" : (" inside " + PathOf(pk[k]))) +
                    " — nothing below it overlapped by " + minOv);
        }
      }
    }

    // ---- 5 · trees --------------------------------------------------------
    DataTree<Curve>  tCrv = new DataTree<Curve>();
    DataTree<int>    tLev = new DataTree<int>();
    DataTree<int>    tDep = new DataTree<int>();
    DataTree<string> tPar = new DataTree<string>();

    for (int io = 0; io < 2; io++)
    {
      List<Grp> gs = groupsByIo[io];
      for (int g = 0; g < gs.Count; g++)
      {
        GH_Path path = new GH_Path(io, g);
        List<Reg> mem = gs[g].Members;
        mem.Sort(delegate(Reg a, Reg b) { return a.Level.CompareTo(b.Level); });
        for (int k = 0; k < mem.Count; k++)
        {
          tCrv.Add(mem[k].Crv, path);
          tLev.Add(mem[k].Level, path);
          tDep.Add(mem[k].Depth, path);
        }
        tPar.Add(gs[g].ParentKey == "" ? "-" : PathOf(gs[g].ParentKey), path);

        // compare against the levels that actually hold curves, not the plane
        // count — an empty level would otherwise flag every branch
        System.Text.StringBuilder miss = new System.Text.StringBuilder();
        for (int L = 0; L < np; L++)
        {
          if (byLevel[L].Count == 0) continue;
          bool here = false;
          for (int k = 0; k < mem.Count; k++) if (mem[k].Level == L) { here = true; break; }
          if (!here) { if (miss.Length > 0) miss.Append(", "); miss.Append(L.ToString()); }
        }
        if (miss.Length > 0)
          log.Add("branch {" + io + ";" + g + "} has no curve at level(s) " + miss.ToString() +
                  " — that loft will skip them");
      }
    }

    log.Insert(0, "levels: " + np +
               "   outer branches: " + groupsByIo[0].Count +
               "   inner branches: " + groupsByIo[1].Count +
               "   curves used: " + (C.Count - skipped) + "/" + C.Count +
               "   min overlap: " + minOv);

    string accWarn = AccessCheck("|C||PL|", "");
    if (accWarn != null) log.Insert(0, "*** WRONG ACCESS MODE: " + accWarn);

    Crv  = tCrv;
    Lev  = tLev;
    Dep  = tDep;
    Pln  = planes;
    Par  = tPar;
    Info = log;
  }

  // <Custom additional code>

  private class Reg
  {
    public Curve  Crv;
    public int    Level;
    public int    Depth;
    public double Area;
  }

  private class Grp
  {
    public int        Io;
    public string     ParentKey;    // "" for a top level region, else "io:group"
    public List<Reg>  Members;
  }

  private string Key(int io, int g) { return io.ToString() + ":" + g.ToString(); }

  private string PathOf(string key)
  {
    string[] bits = key.Split(':');
    if (bits.Length != 2) return key;
    return "{" + bits[0] + ";" + bits[1] + "}";
  }

  private Reg MakeReg(Curve c, int level, int depth, double area)
  {
    Reg r = new Reg();
    r.Crv = c;
    r.Level = level;
    r.Depth = depth;
    r.Area = area;
    return r;
  }

  private double DocTol()
  {
    try { if (RhinoDocument != null) return RhinoDocument.ModelAbsoluteTolerance; }
    catch { }
    return 0.001;
  }

  private double Elev(Point3d pt, Point3d org, Vector3d n)
  {
    return (pt - org) * n;
  }

  // Always a real area, never a length: the old fallback returned a bounding
  // box diagonal when AreaMassProperties failed, which made the "a hole is
  // smaller than its outline" test compare an area against a length.
  private double CurveArea(Curve c, Plane bas)
  {
    AreaMassProperties amp = null;
    try { amp = AreaMassProperties.Compute(c); }
    catch { amp = null; }
    if (amp != null)
    {
      double a = Math.Abs(amp.Area);
      if (a > 0.0) return a;
    }

    // shoelace over samples, taken in base plane coordinates
    double len = c.GetLength();
    if (len <= 0.0) return 0.0;
    const int K = 96;
    double sum = 0.0;
    double pu = 0.0, pv = 0.0;
    for (int i = 0; i <= K; i++)
    {
      double t;
      if (!c.LengthParameter(len * (double) i / (double) K, out t))
        t = c.Domain.ParameterAt((double) i / (double) K);
      double u, v;
      bas.ClosestParameter(c.PointAt(t), out u, out v);
      if (i > 0) sum += (pu * v - u * pv);
      pu = u; pv = v;
    }
    return Math.Abs(sum) * 0.5;
  }

  // Is closed curve a inside closed curve b?  Sampled, so it does not care how
  // close the two run to each other — unlike PlanarClosedCurveRelationship,
  // which reports MutualIntersection for curves that merely come near.
  private bool IsInside(Curve a, Curve b, Plane pl, double tol, double areaA, double areaB)
  {
    if (a == null || b == null) return false;
    if (areaA >= areaB) return false;          // a hole is always smaller than what holds it

    double len = a.GetLength();
    if (len <= tol) return false;

    int inC = 0, outC = 0;
    const int K = 11;
    for (int i = 0; i < K; i++)
    {
      double s = ((double) i + 0.5) / (double) K;    // offset, so we skip the seam
      double t;
      if (!a.LengthParameter(s * len, out t)) continue;
      PointContainment pc = PointContainment.Unset;
      try { pc = b.Contains(a.PointAt(t), pl, tol); }
      catch { pc = PointContainment.Unset; }
      if (pc == PointContainment.Inside) inC++;
      else if (pc == PointContainment.Outside) outC++;
    }

    if (inC == 0 && outC == 0)
    {
      try { return Curve.PlanarClosedCurveRelationship(a, b, pl, tol) == RegionContainment.AInsideB; }
      catch { return false; }
    }
    return inC > outC;
  }

  // Intersection over union of the two regions, seen straight down the plane
  // normal, measured on a sample grid.  1 = the same region, 0 = no shared
  // area.  This is what tells the main outline apart from a small boss sitting
  // on the same axis, which a centroid distance cannot do.
  private double Overlap(Curve a, Curve b, Plane pl, double tol)
  {
    if (a == null || b == null) return 0.0;

    BoundingBox ba = a.GetBoundingBox(pl);
    BoundingBox bb = b.GetBoundingBox(pl);
    double u0 = Math.Min(ba.Min.X, bb.Min.X), u1 = Math.Max(ba.Max.X, bb.Max.X);
    double v0 = Math.Min(ba.Min.Y, bb.Min.Y), v1 = Math.Max(ba.Max.Y, bb.Max.Y);
    if (u1 - u0 <= tol || v1 - v0 <= tol) return 0.0;

    int[] tries = new int[] { 20, 44 };          // refine once if the first grid is too coarse
    for (int pass = 0; pass < tries.Length; pass++)
    {
      int K = tries[pass];
      int inA = 0, inB = 0, both = 0;

      for (int i = 0; i < K; i++)
      {
        double u = u0 + (u1 - u0) * ((double) i + 0.5) / (double) K;
        for (int j = 0; j < K; j++)
        {
          double v = v0 + (v1 - v0) * ((double) j + 0.5) / (double) K;
          Point3d p = pl.PointAt(u, v);

          bool ia = false, ib = false;
          try { ia = (a.Contains(p, pl, tol) == PointContainment.Inside); } catch { }
          try { ib = (b.Contains(p, pl, tol) == PointContainment.Inside); } catch { }
          if (ia) inA++;
          if (ib) inB++;
          if (ia && ib) both++;
        }
      }

      if (inA == 0 || inB == 0) continue;        // region too thin for this grid — refine
      int un = inA + inB - both;
      if (un <= 0) return 0.0;
      return (double) both / (double) un;
    }
    return 0.0;
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

  // </Custom additional code>

}
