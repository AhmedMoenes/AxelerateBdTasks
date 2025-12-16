using Task4.Models;

namespace Task4.Utils;

public static class WallUtils
{
    /// <summary>
    /// Extracts all necessary data from a wall for framing
    /// </summary>
    public static WallData ExtractWallData(Wall wall, Document document)
    {
        var locationCurve = wall.Location as LocationCurve;
        if (locationCurve == null)
            return null;

        var curve = locationCurve.Curve;
        var levelId = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId();
        var baseLevel = document.GetElement(levelId) as Level;

        if (baseLevel == null)
            return null;

        var direction = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
        var normal = direction.CrossProduct(XYZ.BasisZ).Normalize();

        return new WallData
        {
            Curve = curve,
            Length = curve.Length,
            Width = wall.Width,
            Height = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble(),
            BaseElevation = baseLevel.Elevation,
            Direction = direction,
            Normal = normal
        };
    }
}