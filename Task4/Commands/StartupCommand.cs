using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Nice3point.Revit.Toolkit.External;
using Task4.Filters;
using Task4.Models;
using Task4.Utils;
using WallUtils = Task4.Utils.WallUtils;

namespace Task4.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    private const double STUD_SPACING = 2.0; // feet
    private const double STUD_OFFSET = 0.20; // feet

    public override void Execute()
    {
        try
        {
            var wall = SelectWall();
            if (wall == null)
            {
                TaskDialog.Show("Error", "Please select a valid wall");
                return;
            }

            CreateWallFraming(wall);

            TaskDialog.Show("Success", "Wall framing created successfully");
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Error", $"Operation failed: {ex.Message}");
        }
    }

    private Wall SelectWall()
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

    private void CreateWallFraming(Wall wall)
    {
        var wallData = WallUtils.ExtractWallData(wall, Document);
        if (wallData == null)
            return;

        // Collect openings first (needed for stud cutting)
        var openings = CollectOpenings(wall);

        using (var transaction = new Transaction(Document, "Create Wall Framing"))
        {
            transaction.Start();

            // Create vertical studs (with cuts around openings)
            CreateVerticalStuds(wallData, openings);

            // Create horizontal studs (top and bottom)
            CreateHorizontalStuds(wallData);

            // Create corner studs (with cuts around openings)
            CreateCornerStuds(wallData, openings);

            // Create opening framing (doors and windows)
            CreateOpeningFraming(openings, wallData);

            transaction.Commit();
        }
    }

    private void CreateVerticalStuds(WallData wallData, List<FamilyInstance> openings)
    {
        var studSpacing = UnitUtils.ConvertToInternalUnits(STUD_SPACING, UnitTypeId.Feet);
        var numberOfStuds = (int)(wallData.Length / studSpacing);

        for (int i = 1; i <= numberOfStuds; i++)
        {
            var distanceOnCurve = i * studSpacing;

            // Skip if too close to wall end
            if (Math.Abs(distanceOnCurve - wallData.Length) < 0.01)
                continue;

            var studPoint = wallData.Curve.Evaluate(distanceOnCurve / wallData.Length, true);

            FramingUtils.CreateVerticalStudWithCuts(
                Document,
                studPoint,
                wallData,
                openings,
                STUD_OFFSET);
        }
    }

    private void CreateHorizontalStuds(WallData wallData)
    {
        // Bottom stud
        FramingUtils.CreateHorizontalStud(
            Document,
            wallData.Curve,
            wallData.BaseElevation,
            wallData.Normal,
            wallData.Width,
            STUD_OFFSET);

        // Top stud
        FramingUtils.CreateHorizontalStud(
            Document,
            wallData.Curve,
            wallData.BaseElevation + wallData.Height,
            wallData.Normal,
            wallData.Width,
            STUD_OFFSET);
    }

    private void CreateCornerStuds(WallData wallData, List<FamilyInstance> openings)
    {
        // Start corner
        FramingUtils.CreateVerticalStudWithCuts(
            Document,
            wallData.Curve.GetEndPoint(0),
            wallData,
            openings,
            STUD_OFFSET);

        // End corner
        FramingUtils.CreateVerticalStudWithCuts(
            Document,
            wallData.Curve.GetEndPoint(1),
            wallData,
            openings,
            STUD_OFFSET);
    }

    private List<FamilyInstance> CollectOpenings(Wall wall)
    {
        var doors = new FilteredElementCollector(Document)
            .OfClass(typeof(FamilyInstance))
            .OfCategory(BuiltInCategory.OST_Doors)
            .Cast<FamilyInstance>();

        var windows = new FilteredElementCollector(Document)
            .OfClass(typeof(FamilyInstance))
            .OfCategory(BuiltInCategory.OST_Windows)
            .Cast<FamilyInstance>();

        return doors.Concat(windows)
            .Where(opening => opening.Host?.Id == wall.Id)
            .ToList();
    }

    private void CreateOpeningFraming(List<FamilyInstance> openings, WallData wallData)
    {
        foreach (var opening in openings)
        {
            FramingUtils.CreateOpeningFrame(
                Document,
                opening,
                wallData.Normal,
                wallData.Width,
                STUD_OFFSET);
        }
    }
}