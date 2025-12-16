using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using Task3.Utils;

namespace Task3.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    public override void Execute()
    {
        try
        {
            var floorType = GetFloorType();
            if (floorType == null)
            {
                TaskDialog.Show("Error", "No floor type found in the project");
                return;
            }

            var allRooms = CollectRooms();
            if (!allRooms.Any())
            {
                TaskDialog.Show("Error", "No rooms found in the project");
                return;
            }

            var allDoors = CollectDoors();

            CreateContinuousFloors(floorType, allRooms, allDoors);
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Error", $"Operation failed: {ex.Message}");
        }
    }

    private FloorType GetFloorType()
    {
        return new FilteredElementCollector(Document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault();
    }

    private List<Room> CollectRooms()
    {
        return new FilteredElementCollector(Document)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .OfClass(typeof(SpatialElement))
            .Cast<Room>()
            .ToList();
    }

    private List<FamilyInstance> CollectDoors()
    {
        return new FilteredElementCollector(Document)
            .OfCategory(BuiltInCategory.OST_Doors)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .ToList();
    }

    private void CreateContinuousFloors(
        FloorType floorType,
        List<Room> rooms,
        List<FamilyInstance> allDoors)
    {
        using var transaction = new Transaction(Document, "Create Continuous Room Floors");
        transaction.Start();
        try
        {
            var floorsCreated = 0;

            foreach (var room in rooms)
            {
                var level = GetRoomLevel(room);
                if (level == null)
                    continue;

                if (CreateContinuousRoomFloor(room, allDoors, floorType, level))
                {
                    floorsCreated++;
                }
            }

            transaction.Commit();

            TaskDialog.Show("Success", $"{floorsCreated} continuous floors created.");
        }
        catch (Exception ex)
        {
            transaction.RollBack();
            TaskDialog.Show("Error", $"Floor Creation Failed");
        }

    }

    private Level GetRoomLevel(Room room)
    {
        if (room.LevelId == ElementId.InvalidElementId)
            return null;

        return Document.GetElement(room.LevelId) as Level;
    }

    private bool CreateContinuousRoomFloor(
        Room room,
        List<FamilyInstance> allDoors,
        FloorType floorType,
        Level level)
    {
        var boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
        if (boundaries == null || boundaries.Count == 0)
            return false;

        // Get room center for orientation
        var roomCenter = RoomUtils.CalculateRoomCentroid(room);

        // Get doors in this room
        var doorsInRoom = allDoors
            .Where(door => door.FromRoom?.Id == room.Id || door.ToRoom?.Id == room.Id)
            .ToList();

        // Step 1: Get all room segment points
        var allPoints = GeometryUtils.GetBoundaryPoints(boundaries[0]);

        // Step 2: Add door extension points
        foreach (var door in doorsInRoom)
        {
            var doorPoints = RoomUtils.GetDoorExtensionPoints(door, roomCenter, Document);
            if (doorPoints != null)
            {
                allPoints.AddRange(doorPoints);
            }
        }

        // Step 3: Get exterior boundary from all points
        var exteriorBoundary = GeometryUtils.ComputeConvexHull(allPoints);

        if (exteriorBoundary == null || exteriorBoundary.Count < 3)
            return false;

        // Step 4: Create curves from points
        var curves = new List<Curve>();
        for (int i = 0; i < exteriorBoundary.Count; i++)
        {
            var start = exteriorBoundary[i];
            var end = exteriorBoundary[(i + 1) % exteriorBoundary.Count];

            if (start.DistanceTo(end) > 0.001)
            {
                curves.Add(Line.CreateBound(start, end));
            }
        }

        try
        {
            var loop = CurveLoop.Create(curves);
            Floor.Create(Document, new List<CurveLoop> { loop }, floorType.Id, level.Id);
            return true;
        }
        catch
        {
            return false;
        }
    }
}