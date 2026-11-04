// -----------------------------------------------------------------------------
// slice-to-loft · 04 Loft Aligner                           grasshopper-toolkit
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
//  04 · LOFT ALIGNER   —   flip, seam, and matching point counts
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
//    C           Curve       TREE      output "F" of component 03
//    Ref         Curve       TREE      output "Ref" of component 03.
//                                      Optional — if a branch has none, the
//                                      richest curve in that branch is used.
//    P           Plane       Item      the cut plane.  Correspondence runs
//                                      along P's Z axis (XY -> straight up).
//    Ev          bool        Item      TRUE  = spread the added points evenly
//                                              between the real kinks
//                                      FALSE = follow the reference curve's
//                                              own point distribution
//    Tol         double      Item      tolerance (0 = document tolerance)
//
//   OUTPUTS
//    A      tree   aligned curves — same direction, same seam, same point
//                  count inside each branch.  Loft this.
//    Pt     tree {io; group; i}   the ordered points of curve i
//    Np     tree   final point count per curve (should be constant per branch)
//    Rail   tree {io; group; k}   a polyline through point k of every level.
//                  Look at these: if none of them cross or spiral, the loft
//                  will not twist.
//    Info   list   report
//
//  Nothing here changes the shape of a curve.  Extra points are inserted by
//  splitting the curve at a parameter, which is geometrically a no-op —
//  a line split in two is still the same line, an arc split in two is still
//  the same arc.
// ===========================================================================

  private void RunScript(
		DataTree<object> C,
		DataTree<object> Ref,
		object P,
		object Ev,
		object Tol,
		ref object A,
		ref object Pt,
		ref object Np,
		ref object Rail,
		ref object Info)
  {
    List<GH_Path> paths;
    List<List<Curve>> branches;
    if (!ReadTreeParam(C, "C", out paths, out branches))
    {
      Info = NoInput("C", C, "", "|C||Ref|");
      return;
    }
    List<GH_Path> rPaths;
    List<List<Curve>> rBranches;
    ReadTreeParam(Ref, "Ref", out rPaths, out rBranches);
    Solve(paths, branches, rPaths, rBranches, AsPlane(P, Plane.WorldXY), AsBool(Ev, false), AsDouble(Tol, 0.0),
          ref A, ref Pt, ref Np, ref Rail, ref Info);
  }

  private void Solve(List<GH_Path> paths, List<List<Curve>> branches, List<GH_Path> rPaths, List<List<Curve>> rBranches, Plane P, bool Ev, double Tol, ref object A, ref object Pt, ref object Np, ref object Rail, ref object Info)
  {
    List<string> log = new List<string>();
    double tol = (Tol > 0.0) ? Tol : DocTol();

    DataTree<Curve>   tA    = new DataTree<Curve>();
    DataTree<Point3d> tPt   = new DataTree<Point3d>();
    DataTree<int>     tNp   = new DataTree<int>();
    DataTree<Curve>   tRail = new DataTree<Curve>();


    Plane bas = P.IsValid ? P : Plane.WorldXY;
    Vector3d nrm = bas.ZAxis;
    nrm.Unitize();

    for (int bi = 0; bi < branches.Count; bi++)
    {
      GH_Path path = paths[bi];
      List<Curve> branch = branches[bi];

      List<Curve> src = new List<Curve>();
      for (int i = 0; i < branch.Count; i++)
      {
        Curve c = branch[i];
        if (c != null && c.IsValid && c.IsClosed) src.Add(c.DuplicateCurve());
        else log.Add(path.ToString() + "[" + i + "] : missing / open curve — dropped");
      }
      if (src.Count == 0) continue;

      // ---- the datum ------------------------------------------------------
      Curve seed = FindRef(rPaths, rBranches, path);
      if (seed == null)
      {
        int bn = -1;
        for (int i = 0; i < src.Count; i++)
        {
          int k = PointCount(src[i], tol);
          if (k > bn) { bn = k; seed = src[i]; }
        }
      }
      if (seed == null) continue;

      CurveOrientation seedOri = seed.ClosedCurveOrientation(bas);
      Point3d seedSeam = seed.PointAtStart;
      double seedElev = (seedSeam - bas.Origin) * nrm;

      // ---- phase 1 · direction + seam -------------------------------------
      List<Curve> fixedC = new List<Curve>();
      for (int i = 0; i < src.Count; i++)
      {
        Curve c = src[i];

        CurveOrientation ori = c.ClosedCurveOrientation(bas);
        if (ori != CurveOrientation.Undefined && seedOri != CurveOrientation.Undefined && ori != seedOri)
          c.Reverse();

        double elev = (c.PointAtStart - bas.Origin) * nrm;
        Point3d target = seedSeam + nrm * (elev - seedElev);

        double ts;
        if (c.ClosestPoint(target, out ts))
        {
          Curve rs = Reseam(c, ts, tol);
          if (rs != null && rs.IsValid) c = rs;
        }
        fixedC.Add(c);
      }

      // ---- phase 2 · how many points does this branch need? ---------------
      int M = 0;
      int refIdx = 0;
      for (int i = 0; i < fixedC.Count; i++)
      {
        int k = Explode(fixedC[i], tol).Count;
        if (k > M) { M = k; refIdx = i; }
      }
      if (M < 2)
      {
        for (int i = 0; i < fixedC.Count; i++) { tA.Add(fixedC[i], path); tNp.Add(M, path); }
        log.Add(path.ToString() + " : only " + M + " point(s) — nothing to match");
        continue;
      }

      Curve eff = fixedC[refIdx];
      List<Curve> effSegs = Explode(eff, tol);
      List<Point3d> effPts = new List<Point3d>();
      for (int i = 0; i < effSegs.Count; i++) effPts.Add(effSegs[i].PointAtStart);
      double effElev = (eff.PointAtStart - bas.Origin) * nrm;

      // ---- phase 3 · match every curve to it ------------------------------
      List<List<Point3d>> allPts = new List<List<Point3d>>();

      for (int i = 0; i < fixedC.Count; i++)
      {
        Curve c = fixedC[i];
        double total = c.GetLength();
        if (total <= tol) { log.Add(path.ToString() + "[" + i + "] : degenerate"); continue; }

        // this curve's own kinks, as normalised arc length
        List<Curve> mySegs = Explode(c, tol);
        List<double> e = new List<double>();
        double acc = 0.0;
        for (int k = 0; k < mySegs.Count; k++)
        {
          e.Add(Clamp01(acc / total));
          acc += mySegs[k].GetLength();
        }
        int m = e.Count;

        // where the datum's points land on this curve, straight along P.Z
        double elev = (c.PointAtStart - bas.Origin) * nrm;
        double shift = elev - effElev;
        double minGap = Math.Max(tol / total, 1e-7);

        List<double> r = new List<double>();
        for (int k = 0; k < M; k++)
        {
          Point3d target = effPts[k] + nrm * shift;
          double t;
          if (!c.ClosestPoint(target, out t)) { r.Add(Clamp01((double) k / (double) M)); continue; }
          double at = Math.Abs(c.GetLength(new Interval(c.Domain.Min, t)));
          r.Add(Clamp01(at / total));
        }
        r[0] = 0.0;
        for (int k = 1; k < M; k++)
          if (r[k] <= r[k - 1] + minGap) r[k] = r[k - 1] + minGap;
        for (int k = M - 1; k >= 1; k--)
          if (r[k] >= 1.0 - minGap) r[k] = 1.0 - minGap * (double) (M - k);

        if (m > M)
        {
          log.Add(path.ToString() + "[" + i + "] : has " + m + " kinks but the datum only " + M +
                  " — points cannot be removed, this curve keeps its own count");
        }

        // monotone slot assignment for the real kinks
        int mm = Math.Min(m, M);
        int[] slot = new int[mm];
        slot[0] = 0;
        int prev = 0;
        for (int j = 1; j < mm; j++)
        {
          int lo = prev + 1;
          int hi = M - (mm - j);
          if (hi < lo) hi = lo;
          int bestI = lo;
          double bestD = double.MaxValue;
          for (int k = lo; k <= hi && k < M; k++)
          {
            double d = Math.Abs(r[k] - e[j]);
            if (d < bestD) { bestD = d; bestI = k; }
          }
          slot[j] = bestI;
          prev = bestI;
        }

        double[] u = new double[M];
        for (int k = 0; k < M; k++) u[k] = -1.0;
        for (int j = 0; j < mm; j++) u[slot[j]] = e[j];

        for (int j = 0; j < mm; j++)
        {
          int a0 = slot[j];
          int b0 = (j + 1 < mm) ? slot[j + 1] : M;
          double ea = e[j];
          double eb = (j + 1 < mm) ? e[j + 1] : 1.0;
          int free = b0 - a0 - 1;
          if (free <= 0) continue;

          bool useRef = !Ev;
          if (useRef)
          {
            double last = ea;
            for (int q = 0; q < free; q++)
            {
              double v = r[a0 + 1 + q];
              if (v <= last + minGap || v >= eb - minGap * (double) (free - q)) { useRef = false; break; }
              last = v;
            }
          }

          for (int q = 0; q < free; q++)
          {
            int idx = a0 + 1 + q;
            if (useRef) u[idx] = r[idx];
            else u[idx] = ea + (eb - ea) * ((double) (q + 1) / (double) (free + 1));
          }
        }

        // safety net: strictly increasing
        for (int k = 1; k < M; k++)
          if (u[k] <= u[k - 1]) u[k] = Math.Min(u[k - 1] + minGap, 1.0 - minGap * (double) (M - k));

        // normalised arc length -> curve parameters
        List<double> tp = new List<double>();
        for (int k = 0; k < M; k++)
        {
          double t;
          if (k == 0) t = c.Domain.Min;
          else if (!c.LengthParameter(u[k] * total, out t)) t = c.Domain.ParameterAt(u[k]);
          tp.Add(t);
        }
        tp.Add(c.Domain.Max);

        PolyCurve pc = new PolyCurve();
        List<Point3d> pts = new List<Point3d>();
        for (int k = 0; k < M; k++)
        {
          pts.Add(c.PointAt(tp[k]));
          if (tp[k + 1] - tp[k] <= RhinoMath.SqrtEpsilon) continue;
          Curve piece = c.Trim(tp[k], tp[k + 1]);
          if (piece != null && piece.IsValid) pc.Append(piece);
        }
        pc.RemoveNesting();

        Curve res = pc;
        if (!res.IsValid) res = c;
        else if (!res.IsClosed)
        {
          PolyCurve pc2 = pc.DuplicatePolyCurve();
          if (pc2.MakeClosed(tol)) res = pc2;
        }

        tA.Add(res, path);
        tNp.Add(PointCount(res, tol), path);
        GH_Path pp = path.AppendElement(i);
        for (int k = 0; k < pts.Count; k++) tPt.Add(pts[k], pp);
        allPts.Add(pts);

        if (PointCount(res, tol) != M)
          log.Add(path.ToString() + "[" + i + "] : ended with " + PointCount(res, tol) +
                  " points instead of " + M + " (two kinks closer than tolerance?)");
      }

      // ---- rails, for eyeballing the twist --------------------------------
      if (allPts.Count > 1)
      {
        for (int k = 0; k < M; k++)
        {
          List<Point3d> col = new List<Point3d>();
          for (int i = 0; i < allPts.Count; i++)
            if (k < allPts[i].Count) col.Add(allPts[i][k]);
          if (col.Count > 1) tRail.Add(new PolylineCurve(col), path.AppendElement(k));
        }
      }

      log.Add(path.ToString() + " : " + fixedC.Count + " curves aligned to " + M + " points");
    }

    A    = tA;
    Pt   = tPt;
    Np   = tNp;
    Rail = tRail;
    string accWarn = AccessCheck("", "|C||Ref|");
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

  private double Clamp01(double v)
  {
    if (v < 0.0) return 0.0;
    if (v > 1.0) return 1.0;
    return v;
  }

  private Curve FindRef(List<GH_Path> rPaths, List<List<Curve>> rBranches, GH_Path path)
  {
    if (rPaths == null || rPaths.Count == 0) return null;
    for (int i = 0; i < rPaths.Count; i++)
    {
      if (!rPaths[i].IsCoincident(path)) continue;
      List<Curve> br = rBranches[i];
      for (int k = 0; k < br.Count; k++)
        if (br[k] != null && br[k].IsValid)
          return br[k].DuplicateCurve();
    }
    return null;
  }

  private Curve Reseam(Curve c, double t, double tol)
  {
    if (c == null || !c.IsClosed) return c;
    if (Math.Abs(t - c.Domain.Min) <= RhinoMath.SqrtEpsilon) return c;
    if (Math.Abs(t - c.Domain.Max) <= RhinoMath.SqrtEpsilon) return c;

    Curve[] parts = null;
    try { parts = c.Split(t); } catch { parts = null; }

    if (parts != null && parts.Length == 2 && parts[0] != null && parts[1] != null)
    {
      PolyCurve pc = new PolyCurve();
      pc.Append(parts[1]);
      pc.Append(parts[0]);
      pc.RemoveNesting();
      if (pc.IsValid)
      {
        if (!pc.IsClosed) pc.MakeClosed(tol);
        return pc;
      }
    }
    if (parts != null && parts.Length == 1 && parts[0] != null && parts[0].IsValid)
    {
      Curve one = parts[0];
      if (!one.IsClosed) one.MakeClosed(tol);
      return one;
    }

    Curve d = c.DuplicateCurve();
    if (d.ChangeClosedCurveSeam(t)) return d;
    return c;
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
