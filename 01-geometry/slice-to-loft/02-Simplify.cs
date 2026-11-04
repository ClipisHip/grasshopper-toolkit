// -----------------------------------------------------------------------------
// slice-to-loft · 02 Segment Simplifier                     grasshopper-toolkit
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
//  02 · SEGMENT SIMPLIFIER   —   merge collinear lines and co-circular arcs
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
//    C           Curve       TREE      output "Crv" of component 01
//    Ang         double      Item      angle tolerance in DEGREES for
//                                      "same direction" / "same plane".
//                                      0 -> 0.5 deg
//    RTol        double      Item      relative radius tolerance for merging
//                                      two arcs.  0 -> 0.001  (= 0.1 %)
//    Pre         bool        Item      run RhinoCommon's own Curve.Simplify
//                                      as a pre-pass (lines/arcs recognition)
//    Tol         double      Item      tolerance (0 = document tolerance)
//
//   OUTPUTS
//    S      tree   simplified curves, identical paths to the input
//    Nb     tree   POINT count BEFORE, per curve
//    Na     tree   POINT count AFTER,  per curve   <- the number to drive down
//    Seg    tree {..; i}   the exploded segments of each result
//    Info   list   report
//    Pt     tree {..; i}   the actual points of each result, in order
//    Kind   tree {..; i}   what each segment is: 0 = line, 1 = arc,
//                          2 = freeform.  A 2 is the thing blocking further
//                          reduction on that curve — it cannot be merged away
//                          without changing the shape.
//
//  Pt and Kind are appended AFTER Info so adding them does not shift any
//  output already wired.
//
//  POINT COUNT is what matters for the loft, not segment count, and for a
//  closed curve the two are the same number: n segments meet at n junctions.
//  They only diverge when a segment is not what it claims to be — see below.
//
//  Merge rules
//    line + line : unit directions within Ang AND the far end sits on the
//                  first line within Tol  ->  one line
//    arc  + arc  : same centre (Tol), same radius (RTol), same plane normal
//                  (Ang), joint point verified to sit on the merged arc
//                  ->  one arc.  A pair that would sweep a full turn is
//                  rebuilt as a circle instead.
//  The scan wraps around the seam, so a corner-free seam gets absorbed too.
//
//  A note on exploding.  PolyCurve.Explode() returns each segment as it was
//  STORED, so a polyline nested inside a polycurve comes back as a single item
//  carrying a dozen points.  Counting those as one segment undercounts the
//  points badly and leaves the merge with nothing to work on.  Exploding is now
//  recursive and also splits any degree 1 nurbs curve, which is a polyline
//  wearing a different type, so every item really is one line or one arc.
// ===========================================================================

  private void RunScript(
		DataTree<object> C,
		object Ang,
		object RTol,
		object Pre,
		object Tol,
		ref object S,
		ref object Nb,
		ref object Na,
		ref object Seg,
		ref object Info,
		ref object Pt,
		ref object Kind)
  {
    List<GH_Path> paths;
    List<List<Curve>> branches;
    if (!ReadTreeParam(C, "C", out paths, out branches))
    {
      Info = NoInput("C", C, "", "|C|");
      return;
    }
    Solve(paths, branches, AsDouble(Ang, 0.0), AsDouble(RTol, 0.0), AsBool(Pre, false), AsDouble(Tol, 0.0),
          ref S, ref Nb, ref Na, ref Seg, ref Info, ref Pt, ref Kind);
  }

  private void Solve(List<GH_Path> paths, List<List<Curve>> branches, double Ang, double RTol, bool Pre, double Tol, ref object S, ref object Nb, ref object Na, ref object Seg, ref object Info, ref object Pt, ref object Kind)
  {
    List<string> log = new List<string>();
    double tol   = (Tol  > 0.0) ? Tol  : DocTol();
    double ang   = ((Ang > 0.0) ? Ang  : 0.5) * Math.PI / 180.0;
    double rtol  = (RTol > 0.0) ? RTol : 0.001;

    DataTree<Curve>   tOut  = new DataTree<Curve>();
    DataTree<int>     tNb   = new DataTree<int>();
    DataTree<int>     tNa   = new DataTree<int>();
    DataTree<Curve>   tSeg  = new DataTree<Curve>();
    DataTree<Point3d> tPt   = new DataTree<Point3d>();
    DataTree<int>     tKind = new DataTree<int>();

    int kLine = 0, kArc = 0, kFree = 0, freeCurves = 0;
    int totalBefore = 0, totalAfter = 0;

    for (int bi = 0; bi < branches.Count; bi++)
    {
      GH_Path path = paths[bi];
      List<Curve> branch = branches[bi];

      for (int ci = 0; ci < branch.Count; ci++)
      {
        Curve src = branch[ci];
        if (src == null || !src.IsValid)
        {
          log.Add(path.ToString() + "[" + ci + "] : invalid curve — passed through as null");
          tOut.Add(null, path); tNb.Add(0, path); tNa.Add(0, path);
          continue;
        }

        Curve work = src.DuplicateCurve();
        bool closed = work.IsClosed;
        int before = PointCount(work, tol);

        if (Pre)
        {
          Curve simp = work.Simplify(CurveSimplifyOptions.All, tol, ang);
          if (simp != null) work = simp;
        }

        // a plain circle needs no work at all
        Circle circ;
        if (work.TryGetCircle(out circ, tol))
        {
          Curve cc = new ArcCurve(circ);
          GH_Path cp0 = path.AppendElement(ci);
          tOut.Add(cc, path);
          tNb.Add(before, path); tNa.Add(1, path);
          tSeg.Add(cc, cp0);
          tPt.Add(cc.PointAtStart, cp0);
          tKind.Add(1, cp0);
          kArc++;
          totalBefore += before; totalAfter += 1;
          continue;
        }

        List<Curve> raw = Explode(work, tol);

        List<Seg2> segs = new List<Seg2>();
        for (int i = 0; i < raw.Count; i++)
        {
          Seg2 s2 = Classify(raw[i], tol);
          if (s2 != null) segs.Add(s2);
        }

        MergeLoop(segs, closed, tol, ang, rtol);

        // two co-circular arcs left over that between them close the circle
        if (segs.Count == 2 && segs[0].Kind == 1 && segs[1].Kind == 1 && closed)
        {
          if (segs[0].Ar.Center.DistanceTo(segs[1].Ar.Center) <= tol &&
              Math.Abs(segs[0].Ar.Radius - segs[1].Ar.Radius) <= Math.Max(tol, rtol * segs[0].Ar.Radius) &&
              Vector3d.VectorAngle(segs[0].Ar.Plane.ZAxis, segs[1].Ar.Plane.ZAxis) <= ang)
          {
            Circle full = new Circle(segs[0].Ar.Plane, segs[0].Ar.Radius);
            Curve cc = new ArcCurve(full);
            GH_Path cp = path.AppendElement(ci);
            tOut.Add(cc, path);
            tNb.Add(before, path); tNa.Add(1, path);
            tSeg.Add(cc, cp);
            tPt.Add(cc.PointAtStart, cp);
            tKind.Add(1, cp);
            kArc++;
            totalBefore += before; totalAfter += 1;
            continue;
          }
        }

        PolyCurve pc = new PolyCurve();
        for (int i = 0; i < segs.Count; i++)
        {
          Curve piece = segs[i].ToCurve();
          if (piece != null && piece.IsValid && piece.GetLength() > tol * 0.5) pc.Append(piece);
        }
        pc.RemoveNesting();

        Curve result = pc;
        if (closed && !result.IsClosed)
        {
          PolyCurve pc2 = pc.DuplicatePolyCurve();
          if (pc2.MakeClosed(tol)) result = pc2;
          else log.Add(path.ToString() + "[" + ci + "] : could not re-close after simplifying");
        }

        int after = PointCount(result, tol);
        totalBefore += before; totalAfter += after;

        tOut.Add(result, path);
        tNb.Add(before, path);
        tNa.Add(after, path);

        List<Curve> fin = Explode(result, tol);
        GH_Path sp = path.AppendElement(ci);
        bool anyFree = false;
        for (int i = 0; i < fin.Count; i++)
        {
          tSeg.Add(fin[i], sp);
          tPt.Add(fin[i].PointAtStart, sp);

          Seg2 cl = Classify(fin[i], tol);
          int kind = (cl == null) ? 2 : cl.Kind;
          tKind.Add(kind, sp);
          if (kind == 0) kLine++;
          else if (kind == 1) kArc++;
          else { kFree++; anyFree = true; }
        }
        if (!result.IsClosed && fin.Count > 0)
          tPt.Add(fin[fin.Count - 1].PointAtEnd, sp);
        if (anyFree) freeCurves++;
      }
    }

    log.Insert(0, "segments now: " + kLine + " line, " + kArc + " arc, " + kFree + " freeform" +
               (freeCurves > 0
                 ? ("   |   " + freeCurves + " curve(s) still hold a freeform segment — those cannot be reduced further without changing the shape")
                 : ""));
    log.Insert(0, "POINTS " + totalBefore + " -> " + totalAfter +
               "   (" + (totalBefore - totalAfter) + " removed, " +
               ((totalBefore > 0) ? Math.Round(100.0 * (totalBefore - totalAfter) / (double) totalBefore, 1) : 0.0) + "%)");

    string accWarn = AccessCheck("", "|C|");
    if (accWarn != null) log.Insert(0, "*** WRONG ACCESS MODE: " + accWarn);

    S    = tOut;
    Nb   = tNb;
    Na   = tNa;
    Seg  = tSeg;
    Pt   = tPt;
    Kind = tKind;
    Info = log;
  }

  // <Custom additional code>

  private class Seg2
  {
    public int   Kind;    // 0 = line, 1 = arc, 2 = anything else
    public Line  Ln;
    public Arc   Ar;
    public Curve Cv;

    public Point3d Start
    {
      get
      {
        if (Kind == 0) return Ln.From;
        if (Kind == 1) return Ar.StartPoint;
        return Cv.PointAtStart;
      }
    }

    public Point3d End
    {
      get
      {
        if (Kind == 0) return Ln.To;
        if (Kind == 1) return Ar.EndPoint;
        return Cv.PointAtEnd;
      }
    }

    public Curve ToCurve()
    {
      if (Kind == 0) return new LineCurve(Ln);
      if (Kind == 1) return new ArcCurve(Ar);
      return Cv.DuplicateCurve();
    }
  }

  private double DocTol()
  {
    try { if (RhinoDocument != null) return RhinoDocument.ModelAbsoluteTolerance; }
    catch { }
    return 0.001;
  }

  private Seg2 Classify(Curve c, double tol)
  {
    if (c == null || !c.IsValid) return null;
    Seg2 s = new Seg2();
    Arc ar;

    if (c.IsLinear(tol))
    {
      s.Kind = 0;
      s.Ln = new Line(c.PointAtStart, c.PointAtEnd);
      return s;
    }
    if (c.TryGetArc(out ar, tol))
    {
      s.Kind = 1;
      s.Ar = ar;
      return s;
    }
    s.Kind = 2;
    s.Cv = c.DuplicateCurve();
    return s;
  }

  private void MergeLoop(List<Seg2> list, bool closed, double tol, double ang, double rtol)
  {
    bool changed = true;
    int guard = 0;
    while (changed && list.Count > 1 && guard < 10000)
    {
      guard++;
      changed = false;
      int last = closed ? list.Count : (list.Count - 1);
      for (int i = 0; i < last; i++)
      {
        int j = (i + 1) % list.Count;
        if (j == i) break;
        Seg2 m = TryMerge(list[i], list[j], tol, ang, rtol);
        if (m == null) continue;
        list[i] = m;
        list.RemoveAt(j);
        changed = true;
        break;                     // indices moved — rescan
      }
    }
  }

  private Seg2 TryMerge(Seg2 a, Seg2 b, double tol, double ang, double rtol)
  {
    if (a == null || b == null) return null;
    if (a.End.DistanceTo(b.Start) > tol) return null;

    // ---- line + line ----
    if (a.Kind == 0 && b.Kind == 0)
    {
      Vector3d da = a.Ln.Direction; if (!da.Unitize()) return null;
      Vector3d db = b.Ln.Direction; if (!db.Unitize()) return null;
      if (Vector3d.VectorAngle(da, db) > ang) return null;

      Line inf = new Line(a.Ln.From, a.Ln.From + da);
      double t = inf.ClosestParameter(b.Ln.To);
      if (inf.PointAt(t).DistanceTo(b.Ln.To) > tol) return null;

      Seg2 s = new Seg2();
      s.Kind = 0;
      s.Ln = new Line(a.Ln.From, b.Ln.To);
      if (s.Ln.Length <= tol) return null;
      return s;
    }

    // ---- arc + arc ----
    if (a.Kind == 1 && b.Kind == 1)
    {
      if (a.Ar.Center.DistanceTo(b.Ar.Center) > tol) return null;

      double r0 = a.Ar.Radius, r1 = b.Ar.Radius;
      if (Math.Abs(r0 - r1) > Math.Max(tol, rtol * Math.Max(r0, r1))) return null;
      if (Vector3d.VectorAngle(a.Ar.Plane.ZAxis, b.Ar.Plane.ZAxis) > ang) return null;

      double sweep = a.Ar.Angle + b.Ar.Angle;
      if (sweep >= 2.0 * Math.PI - 1e-9) return null;      // would close the circle

      ArcCurve ac = new ArcCurve(a.Ar);
      Vector3d ta = ac.TangentAtStart;

      Arc merged = new Arc(a.Ar.StartPoint, ta, b.Ar.EndPoint);
      if (!merged.IsValid) return null;
      if (Math.Abs(merged.Radius - r0) > Math.Max(tol, rtol * r0)) return null;
      if (merged.Center.DistanceTo(a.Ar.Center) > tol) return null;

      // the old joint must still sit on the merged arc
      ArcCurve mc = new ArcCurve(merged);
      double tt;
      if (!mc.ClosestPoint(a.End, out tt)) return null;
      if (mc.PointAt(tt).DistanceTo(a.End) > tol) return null;

      Seg2 s = new Seg2();
      s.Kind = 1;
      s.Ar = merged;
      return s;
    }

    return null;
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
