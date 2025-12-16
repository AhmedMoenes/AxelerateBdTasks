using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Nice3point.Revit.Toolkit.External;
using Task2.Filters;
using Task2.Models;

namespace Task2.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    private const double CORNER_OFFSET = 1.0; // clearance between WC and wall end
    private const double ROOM_CHECK_DISTANCE = 0.3; // Test point offset for room detection
    private const string FIXTURE_FAMILY_NAME = "ADA";

    public override void Execute()
    {
        try
        {
            // Let user pick a wall
            var wall = RequestWallSelection();
            if (wall == null)
            {
                TaskDialog.Show("Error", "Invalid wall selection");
                return;
            }

            // Get the level from the selected wall
            var level = GetWallLevel(wall);
            if (level == null)
            {
                TaskDialog.Show("Error", "Could not determine wall level");
                return;
            }

            // Find the room(s) that this wall belongs to
            var roomsForWall = FindBathroomsContainingWall(wall);
            if (!roomsForWall.Any())
            {
                TaskDialog.Show("Error", "Selected wall does not belong to any bathroom");
                return;
            }

            // Filter only bathrooms with doors
            var bathroomsWithDoors = FilterBathroomsWithDoors(roomsForWall);
            if (!bathroomsWithDoors.Any())
            {
                TaskDialog.Show("Error", "No bathrooms with doors found for this wall");
                return;
            }

            // Find WC placement points for each bathroom
            var placementInfos = DetermineFixturePlacement(wall, bathroomsWithDoors);
            if (!placementInfos.Any())
            {
                TaskDialog.Show("Error", "No valid placement points found on the selected wall");
                return;
            }

            // Get WC family symbol
            var wcSymbol = GetWCFamilySymbol();
            if (wcSymbol == null)
            {
                TaskDialog.Show("Error", "WC family not found");
                return;
            }

            // Place WC instances
            InstallFixtureInstances(wall, wcSymbol, level, placementInfos);

            TaskDialog.Show("Success", "WC fixtures placed successfully");
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Error", $"Operation failed: {ex.Message}");
        }
    }

    private Wall RequestWallSelection()
    {
        try
        {
            var wallFilter = new WallSelectionFilter();
            var reference = UiDocument.Selection.PickObject(
                ObjectType.Element,
                wallFilter,
                "Select a wall");
            return Document.GetElement(reference) as Wall;
        }
        catch
        {
            return null;
        }
    }

    private Level GetWallLevel(Wall wall)
    {
        // Get the level parameter from the wall
        var levelId = wall.LevelId;
        if (levelId == ElementId.InvalidElementId)
            return null;

        return Document.GetElement(levelId) as Level;
    }

    private List<Room> FindBathroomsContainingWall(Wall wall)
    {
        var rooms = new List<Room>();

        List<Room> bathroomRooms = new FilteredElementCollector(Document)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .OfClass(typeof(SpatialElement))
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(r => r.Name.Contains("Bathroom"))
            .ToList();

        // Check which rooms have this wall as a boundary
        foreach (var room in bathroomRooms)
        {
            var boundaryWallIds = ExtractBoundaryWallIds(room);
            if (boundaryWallIds.Contains(wall.Id))
            {
                rooms.Add(room);
            }
        }

        return rooms;
    }

    private List<(Room room, XYZ doorLocation)> FilterBathroomsWithDoors(List<Room> rooms)
    {
        var bathroomsWithDoors = new List<(Room, XYZ)>();

        foreach (var room in rooms)
        {
            var doorLocation = LocateDoorInBathroom(room);
            if (doorLocation != null)
            {
                bathroomsWithDoors.Add((room, doorLocation));
            }
        }

        return bathroomsWithDoors;
    }

    private XYZ? LocateDoorInBathroom(Room room)
    {
        var boundaryWallIds = ExtractBoundaryWallIds(room);

        var door = new FilteredElementCollector(Document)
            .OfCategory(BuiltInCategory.OST_Doors)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .FirstOrDefault(d => boundaryWallIds.Contains(d.Host?.Id));

        return door?.GetTransform().Origin;
    }

    private List<ElementId> ExtractBoundaryWallIds(Room room)
    {
        var wallIds = new List<ElementId>();
        var boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions());

        foreach (var segmentList in boundaries)
        {
            foreach (var segment in segmentList)
            {
                var element = Document.GetElement(segment.ElementId);
                if (element is Wall)
                {
                    wallIds.Add(element.Id);
                }
            }
        }

        return wallIds;
    }

    private List<FixturePosition> DetermineFixturePlacement(
        Wall wall,
        List<(Room room, XYZ doorLocation)> bathrooms)
    {
        var placementInfos = new List<FixturePosition>();

        foreach (var (room, doorLocation) in bathrooms)
        {
            var placement = CalculateOptimalPosition(wall, room, doorLocation);
            if (placement != null)
            {
                placementInfos.Add(placement);
            }
        }

        return placementInfos;
    }

    private FixturePosition CalculateOptimalPosition(
        Wall wall,
        Room room,
        XYZ doorLocation)
    {
        var boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions());

        foreach (var segmentList in boundaries)
        {
            foreach (var segment in segmentList)
            {
                // Only process segments that belong to the selected wall
                if (segment.ElementId != wall.Id)
                    continue;

                var curve = segment.GetCurve();
                var segmentStart = curve.GetEndPoint(0);
                var segmentEnd = curve.GetEndPoint(1);

                // Calculate wall normal pointing into the room
                var wallNormal = DetermineRoomFacingDirection(room, curve);
                if (wallNormal == null)
                    continue;

                // Find the farthest corner from the door
                var placementPoint = ComputeOptimalPlacementPoint(
                    segmentStart,
                    segmentEnd,
                    doorLocation,
                    curve.Length);

                return new FixturePosition
                {
                    Point = placementPoint,
                    Normal = wallNormal,
                    DoorLocation = doorLocation
                };
            }
        }

        return null;
    }

    private XYZ? DetermineRoomFacingDirection(Room room, Curve wallCurve)
    {
        var start = wallCurve.GetEndPoint(0);
        var end = wallCurve.GetEndPoint(1);
        var direction = (end - start).Normalize();
        var normal = direction.CrossProduct(XYZ.BasisZ).Normalize();

        var midpoint = (start + end) / 2;
        var inwardTest = midpoint - normal * ROOM_CHECK_DISTANCE;
        var outwardTest = midpoint + normal * ROOM_CHECK_DISTANCE;

        if (room.IsPointInRoom(inwardTest))
            return normal;
        else if (room.IsPointInRoom(outwardTest))
            return -normal;

        return null;
    }

    private XYZ ComputeOptimalPlacementPoint(
        XYZ segmentStart,
        XYZ segmentEnd,
        XYZ doorLocation,
        double segmentLength)
    {
        // Choose the corner farthest from the door
        var distToStart = doorLocation.DistanceTo(segmentStart);
        var distToEnd = doorLocation.DistanceTo(segmentEnd);
        var farthestCorner = distToStart > distToEnd ? segmentStart : segmentEnd;

        // If segment is too short, place at midpoint
        if (segmentLength < CORNER_OFFSET)
        {
            return (segmentStart + segmentEnd) / 2;
        }

        // Offset from the corner toward the center of the wall
        var direction = (segmentEnd - segmentStart).Normalize();
        if (farthestCorner.IsAlmostEqualTo(segmentEnd))
        {
            direction = -direction;
        }

        return farthestCorner + direction * CORNER_OFFSET;
    }

    private FamilySymbol GetWCFamilySymbol()
    {
        return new FilteredElementCollector(Document)
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(f => f.Name == FIXTURE_FAMILY_NAME);
    }

    private void InstallFixtureInstances(
        Wall wall,
        FamilySymbol wcSymbol,
        Level level,
        List<FixturePosition> placements)
    {
        using (var transaction = new Transaction(Document, "Install Bathroom Fixtures"))
        {
            transaction.Start();

            // Activate symbol if needed
            if (!wcSymbol.IsActive)
            {
                wcSymbol.Activate();
                Document.Regenerate();
            }

            foreach (var placement in placements)
            {
                var instance = Document.Create.NewFamilyInstance(
                    location: placement.Point,
                    symbol: wcSymbol,
                    host: wall,
                    level: level,
                    structuralType: StructuralType.NonStructural
                );

                // Orient WC to face the door
                SetFixtureOrientation(instance, placement);
            }

            transaction.Commit();
        }
    }

    private void SetFixtureOrientation(FamilyInstance instance, FixturePosition placement)
    {
        // Calculate direction from WC to door (in wall plane)
        var doorVector = (placement.DoorLocation - placement.Point).Normalize();
        var projectedDirection = doorVector -
                                 (doorVector.DotProduct(XYZ.BasisZ) * XYZ.BasisZ);
        projectedDirection = projectedDirection.Normalize();

        // Flip if needed so WC faces the door
        var alignment = projectedDirection.DotProduct(placement.Normal);
        if (alignment > 0)
        {
            instance.flipFacing();
        }
    }
}