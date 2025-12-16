using Autodesk.Revit.UI.Selection;

namespace Task2.Filters;

public class WallSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element element)
    {
        return element is Wall;
    }

    public bool AllowReference(Reference reference, XYZ position)
    {
        return false;
    }
}

