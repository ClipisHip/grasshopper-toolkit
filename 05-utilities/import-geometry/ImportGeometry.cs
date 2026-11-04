// -----------------------------------------------------------------------------
// import-geometry · Import Geometry                         grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
// Grasshopper Script Instance
using System;

using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.Geometry;
using Rhino.DocObjects;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;


//A Class may have Atributes, Methods and other classes.
//Class (Type: Script_Instance) definition. Inherits from GH_ScriptInstance

public class Script_Instance : GH_ScriptInstance
{

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
//Attributes Definition:
    private List<GeometryBase> geometries = new List<GeometryBase>();
//Method definition:
    private void RunScript(
		string filepath,
		bool import,
		bool reset,
		ref object geometry)
    {
        if(reset){
            ClearGeometries();
        }else if(import){
            RhinoDoc.ActiveDoc.Objects.UnselectAll();
            string cmd = "!_-Import "+"\""+ filepath +"\"" + " _Enter";
            Rhino.RhinoApp.RunScript(cmd, false);
            var selectedObjs = RhinoDoc.ActiveDoc.Objects.GetSelectedObjects(false,false).ToList();
            ClearGeometries();
            // Loop
            foreach(RhinoObject selectedObj in selectedObjs){
                var geo = selectedObj.Geometry;
                geometries.Add(geo);
                RhinoDoc.ActiveDoc.Objects.Delete(selectedObj, true);
            }
            
            
        } 
        geometry = geometries;
    } 
    private void ClearGeometries(){
        geometries.Clear();
    }
}
