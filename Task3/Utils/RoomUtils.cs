using Autodesk.Revit.DB.Architecture;

namespace Task3.Utils;

public static class RoomUtils
{
    /// <summary>
    /// Calculates the centroid of a room based on its boundary segments
    /// </summary>
    public static XYZ? CalculateRoomCentroid(Room room)
    {
        var boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
        if (boundaries == null || boundaries.Count == 0)
            return null;

        var boundaryPoints = GeometryUtils.GetBoundaryPoints(boundaries[0]);
        if (boundaryPoints.Count < 3)
            return null;

        return GeometryUtils.CalculatePolygonCentroid(boundaryPoints);
    }

    /// <summary>
    /// Gets door extension points (2 points at door location + 2 points extended into room)
    /// </summary>
    public static List<XYZ> GetDoorExtensionPoints(FamilyInstance door, XYZ roomCenter, Document document)
    {
        if (!(door.Host is Wall wall))
            return null;

        var doorLocation = door.Location as LocationPoint;
        if (doorLocation == null)
            return null;

        var doorCenter = doorLocation.Point;
        var wallWidth = wall.Width;
        var extensionDepth = wallWidth / 2;

        // Get door width
        var doorWidth = GetDoorWidth(door, document);
        var halfDoorWidth = doorWidth / 2;

        // Get door facing direction
        var facing = door.FacingOrientation.Normalize();
        var toRoom = (roomCenter - doorCenter).Normalize();

        // Normal points into the room
        var normal = facing.DotProduct(toRoom) > 0 ? facing : -facing;

        // Right vector (perpendicular to normal in XY plane)
        var right = new XYZ(normal.Y, -normal.X, 0).Normalize();

        // Calculate 4 points:
        // 1. Door left at wall (on room segment)
        var doorLeft = doorCenter - right * halfDoorWidth;

        // 2. Door right at wall (on room segment)
        var doorRight = doorCenter + right * halfDoorWidth;

        // 3. Extended left (half wall thickness into room)
        var extendedLeft = doorLeft + normal * extensionDepth;

        // 4. Extended right (half wall thickness into room)
        var extendedRight = doorRight + normal * extensionDepth;

        return new List<XYZ> { doorLeft, doorRight, extendedLeft, extendedRight };
    }

    /// <summary>
    /// Gets door width from parameters
    /// </summary>
    private static double GetDoorWidth(FamilyInstance door, Document document)
    {
        var doorType = document.GetElement(door.GetTypeId());
        var widthParam = doorType?.LookupParameter("Width");

        if (widthParam != null && widthParam.HasValue)
            return widthParam.AsDouble();

        return 3.0;
    }

}