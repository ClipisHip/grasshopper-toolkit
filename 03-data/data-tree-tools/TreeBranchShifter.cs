// -----------------------------------------------------------------------------
// data-tree-tools · Tree Branches Shifter                   grasshopper-toolkit
// Rhino 8 · Grasshopper C# Script component · no plugins
// Daniel Andújar · andujar.design · MIT licence
// Written with AI assistance; I designed the logic and tested it.
// -----------------------------------------------------------------------------
using Grasshopper;
using Grasshopper.Kernel.Data;
DataTree<object> treeOut = new DataTree<object>();
int pCount = T.BranchCount;
if (pCount == 0) { A = treeOut; return; }

for (int i = 0; i < pCount; i++) {
    // Cyclic shift formula: shifts forward by 1 (branch i goes to (i + 1) % pCount)
    // Change + 1 to - 1 or link a slider for custom offset shifts
    int targetIdx = (i + 1) % pCount;
    GH_Path path = new GH_Path(targetIdx);
    var branch = T.Branch(i);
    treeOut.AddRange(branch, path);
}
A = treeOut;