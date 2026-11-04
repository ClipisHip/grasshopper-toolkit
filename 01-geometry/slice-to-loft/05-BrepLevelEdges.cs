// -----------------------------------------------------------------------------
// slice-to-loft · 05 Brep Level Edges                       grasshopper-toolkit
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
//  05 · BREP LEVEL EDGES   —   real edges parallel to a reference plane,
//                              clustered into the levels the solid actually has
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
//    B           Brep        LIST      the solid(s).  Surfaces and extrusions
//                                      are accepted too.
//    P           Plane       Item      reference plane.  Levels are measured
//                                      along its Z axis.
//                                        XY -> horizontal edges, levels in Z
//                                        YZ -> vertical edges,  levels in X
//                                        XZ -> vertical edges,  levels in Y
//    AT          double      Item      angle tolerance in DEGREES.  An edge is
//                                      kept when EVERY tangent along it sits
//                                      within AT of the reference plane.
//                                      0 -> 1.0 deg.  Raise it to catch draft:
//                                      AT = 5 against the YZ plane keeps
//                                      vertical edges drafted up to 5 deg.
//    LT          double      Item      level tolerance.  Edges whose height
//                                      differs by less than this land on the
//                                      same level.  0 -> 10 x Tol.
//    Jn          bool        Item      join each level's edges into closed
//                                      curves, ready for component 01
//    Tol         double      Item      tolerance (0 = document tolerance)
//
//   OUTPUTS
//    E      tree {level}   the edges that qualified, bottom -> top
//    J      tree {level}   those edges joined  (only when Jn is true)
//    H      list           the level heights, signed, measured from P's origin
//                          along P's Z axis.  These are the real heights of
//                          the solid, not a division you guessed.
//    Pl     list           a copy of P sitting at each height — wire this
//                          straight into component 01's PL input
//    Ang    tree {level}   each edge's measured angle to P, in degrees
//    Nc     list           edge count per level
//    Info   list           report
//    Ei     tree {level}   the index of each edge in ITS OWN brep's edge list —
//                          the same order Deconstruct Brep gives you, so this
//                          drops straight into a List Item on that Edges output
//    Bi     tree {level}   which brep in B each edge came from.  With a single
//                          brep this is all zeros; with several it is what
//                          makes Ei unambiguous, since edge 7 exists in each
//                          of them.
//
//  Ei and Bi are parallel to E: same paths, same order, one value per edge.
//  They are appended AFTER Info in the parameter list so that adding them does
//  not shift any output you have already wired.
//
//  Why the tangent test rather than a height spread: a circle tilted by 5 deg
//  only rises and falls by a little over its whole circumference, so a spread
//  test would wave it through as flat.  Every tangent of a genuinely level
//  curve is perpendicular to P's normal, whatever shape the curve is, so that
//  is what gets measured — and for a straight edge it is exactly the angle
//  between the edge and the plane.
// ===========================================================================

  private void RunScript(
		List<object> B,
		object P,
		object AT,
		object LT,
		object Jn,
		object Tol,
		ref object E,
		ref object J,
		ref object H,
		ref object Pl,
		ref object Ang,
		ref object Nc,
		ref object Info,
		ref object Ei,
		ref object Bi)
  {
    List<string> log = new List<string>();

    double tol = AsDouble(Tol, 0.0);
    if (tol <= 0.0) tol = DocTol();
    double at = AsDouble(AT, 0.0);
    if (at <= 0.0) at = 1.0;
    double lt = AsDouble(LT, 0.0);
    if (lt <= 0.0) lt = tol * 10.0;
    bool join = AsBool(Jn, false);

    Plane bas = AsPlane(P, Plane.WorldXY);
    Vector3d nrm = bas.ZAxis;
    nrm.Unitize();

    List<Brep> breps = AsBreps(B);
    if (breps.Count == 0)
    {
      string w0 = AccessCheck("|B|", "");
      log.Add(w0 != null ? ("*** WRONG ACCESS MODE: " + w0) : "no brep supplied");
      Info = log;
      return;
    }

    // ---- 1 · measure every edge against the plane ------------------------
    List<Curve> keep = new List<Curve>();
    List<double> keepH = new List<double>();
    List<double> keepA = new List<double>();
    List<int> keepEi = new List<int>();     // index in its brep's edge list
    List<int> keepBi = new List<int>();     // which brep it came from
    int seen = 0, tooShort = 0;

    for (int bi = 0; bi < breps.Count; bi++)
    {
      Brep br = breps[bi];
      for (int ei = 0; ei < br.Edges.Count; ei++)
      {
        Curve ec = null;
        try { ec = br.Edges[ei].DuplicateCurve(); }
        catch { ec = null; }
        if (ec == null || !ec.IsValid) continue;

        double len = ec.GetLength();
        if (len <= tol) { tooShort++; continue; }
        seen++;

        int K = 24;
        double worst = 0.0;
        double lo = double.MaxValue, hi = -double.MaxValue;

        for (int s = 0; s <= K; s++)
        {
          double t;
          if (!ec.LengthParameter(len * (double) s / (double) K, out t))
            t = ec.Domain.ParameterAt((double) s / (double) K);

          double e = (ec.PointAt(t) - bas.Origin) * nrm;
          if (e < lo) lo = e;
          if (e > hi) hi = e;

          Vector3d tg = ec.TangentAt(t);
          if (!tg.Unitize()) continue;
          double d = Math.Abs(tg * nrm);
          if (d > 1.0) d = 1.0;
          double a = Math.Asin(d) * 180.0 / Math.PI;
          if (a > worst) worst = a;
        }

        if (worst > at) continue;

        keep.Add(ec);
        keepH.Add((lo + hi) * 0.5);
        keepA.Add(worst);
        keepEi.Add(ei);
        keepBi.Add(bi);
      }
    }

    if (keep.Count == 0)
    {
      log.Add("looked at " + seen + " edge(s), none within " + at +
              " deg of the reference plane — raise AT, or check the plane orientation");
      Info = log;
      return;
    }

    // ---- 2 · cluster the heights into levels -----------------------------
    int[] ord = new int[keep.Count];
    for (int i = 0; i < keep.Count; i++) ord[i] = i;
    Array.Sort(ord, delegate(int x, int y) { return keepH[x].CompareTo(keepH[y]); });

    List<List<int>> levels = new List<List<int>>();
    List<int> cur = new List<int>();
    cur.Add(ord[0]);
    for (int i = 1; i < ord.Length; i++)
    {
      if (keepH[ord[i]] - keepH[ord[i - 1]] > lt)
      {
        levels.Add(cur);
        cur = new List<int>();
      }
      cur.Add(ord[i]);
    }
    levels.Add(cur);

    // ---- 3 · outputs -----------------------------------------------------
    DataTree<Curve>  tE   = new DataTree<Curve>();
    DataTree<Curve>  tJ   = new DataTree<Curve>();
    DataTree<double> tA   = new DataTree<double>();
    DataTree<int>    tEi  = new DataTree<int>();
    DataTree<int>    tBi  = new DataTree<int>();
    List<double>     hOut = new List<double>();
    List<Plane>      pOut = new List<Plane>();
    List<int>        nOut = new List<int>();

    for (int L = 0; L < levels.Count; L++)
    {
      GH_Path path = new GH_Path(L);
      List<int> mem = levels[L];

      double sum = 0.0;
      List<Curve> lvl = new List<Curve>();
      for (int k = 0; k < mem.Count; k++)
      {
        int idx = mem[k];
        sum += keepH[idx];
        lvl.Add(keep[idx]);
        tE.Add(keep[idx], path);
        tA.Add(keepA[idx], path);
        tEi.Add(keepEi[idx], path);
        tBi.Add(keepBi[idx], path);
      }
      double h = sum / (double) mem.Count;

      hOut.Add(h);
      nOut.Add(mem.Count);

      Plane q = new Plane(bas);
      q.Origin = bas.Origin + nrm * h;
      pOut.Add(q);

      if (join)
      {
        Curve[] jc = null;
        try { jc = Curve.JoinCurves(lvl, Math.Max(tol * 2.0, lt)); }
        catch { jc = null; }
        int closed = 0;
        if (jc != null)
        {
          for (int k = 0; k < jc.Length; k++)
          {
            if (jc[k] == null) continue;
            if (!jc[k].IsClosed) jc[k].MakeClosed(Math.Max(tol * 2.0, lt));
            if (jc[k].IsClosed) closed++;
            tJ.Add(jc[k], path);
          }
        }
        int made = (jc == null) ? 0 : jc.Length;
        log.Add("level " + L + " @ " + Math.Round(h, 4) + " : " + mem.Count +
                " edge(s) -> " + made + " joined curve(s), " + closed + " closed");
      }
      else
      {
        log.Add("level " + L + " @ " + Math.Round(h, 4) + " : " + mem.Count + " edge(s)");
      }
    }

    log.Insert(0, "looked at " + seen + " edge(s), kept " + keep.Count +
               " within " + at + " deg, in " + levels.Count + " level(s)" +
               (tooShort > 0 ? ("   (" + tooShort + " edge(s) shorter than tolerance were ignored)") : ""));

    string accWarn = AccessCheck("|B|", "");
    if (accWarn != null) log.Insert(0, "*** WRONG ACCESS MODE: " + accWarn);

    E    = tE;
    J    = tJ;
    H    = hOut;
    Pl   = pOut;
    Ang  = tA;
    Nc   = nOut;
    Ei   = tEi;
    Bi   = tBi;
    Info = log;
  }

  // <Custom additional code>

  private double DocTol()
  {
    try { if (RhinoDocument != null) return RhinoDocument.ModelAbsoluteTolerance; }
    catch { }
    return 0.001;
  }

  private List<Brep> AsBreps(object o)
  {
    List<Brep> res = new List<Brep>();
    List<object> flat = Flatten(o);
    for (int i = 0; i < flat.Count; i++)
    {
      object x = flat[i];
      if (x == null) continue;

      Brep b = x as Brep;

      if (b == null)
      {
        Extrusion ex = x as Extrusion;
        if (ex != null) b = ex.ToBrep();
      }
      if (b == null)
      {
        Surface sf = x as Surface;
        if (sf != null) b = sf.ToBrep();
      }
      if (b == null)
      {
        IGH_Goo g = x as IGH_Goo;
        if (g != null)
        {
          Brep q = null;
          try { if (g.CastTo<Brep>(out q)) b = q; }
          catch { }
        }
      }
      if (b != null && b.IsValid) res.Add(b);
    }
    return res;
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
