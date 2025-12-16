using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace Task1.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    public override void Execute()
    {
        try
        {
            var floorType = GetFirstFloorType();
            if (floorType == null)
            {
                TaskDialog.Show("Error", "No floor types found in the project");
                return;
            }

            var level = GetFirstLevel();
            if (level == null)
            {
                TaskDialog.Show("Error", "No levels found in the project");
                return;
            }

            var lines = GetFloorLines();

            var curveLoop = CreateValidCurveLoop(lines);
            if (curveLoop == null)
            {
                TaskDialog.Show("Error", "Unable to create a valid curve loop from the given lines");
                return;
            }

            CreateFloor(curveLoop, floorType.Id, level.Id);
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Error", $"Floor creation failed: {ex.Message}");
        }
    }

    private FloorType GetFirstFloorType()
    {
        return new FilteredElementCollector(Document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault();
    }

    private Level GetFirstLevel()
    {
        return new FilteredElementCollector(Document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault();
    }

    private List<Line> GetFloorLines()
    {
        return new List<Line>
        {
            Line.CreateBound(new XYZ(0, 0, 0), new XYZ(79, 0, 0)),
            Line.CreateBound(new XYZ(44, 25, 0), new XYZ(13, 25, 0)),
            Line.CreateBound(new XYZ(13, 40, 0), new XYZ(-8, 40, 0)),
            Line.CreateBound(new XYZ(55, 34, 0), new XYZ(55, 10, 0)),
            Line.CreateBound(new XYZ(79, 34, 0), new XYZ(55, 34, 0)),
            Line.CreateBound(new XYZ(0, 20, 0), new XYZ(0, 0, 0)),
            Line.CreateBound(new XYZ(55, 10, 0), new XYZ(44, 12, 0)),
            Line.CreateBound(new XYZ(-8, 40, 0), new XYZ(-8, 20, 0)),
            Line.CreateBound(new XYZ(79, 0, 0), new XYZ(79, 34, 0)),
            Line.CreateBound(new XYZ(44, 12, 0), new XYZ(44, 25, 0)),
            Line.CreateBound(new XYZ(-8, 20, 0), new XYZ(0, 20, 0)),
            Line.CreateBound(new XYZ(13, 25, 0), new XYZ(13, 40, 0))
        };
    }

    private CurveLoop CreateValidCurveLoop(List<Line> lines)
    {
        if (TryCreateCurveLoop(lines, out var curveLoop))
        {
            return curveLoop;
        }

        var arrangedLines = ArrangeLinesIntoValidCurveLoop(lines);
        if (arrangedLines != null && TryCreateCurveLoop(arrangedLines, out curveLoop))
        {
            return curveLoop;
        }

        return null;
    }

    private bool TryCreateCurveLoop(List<Line> lines, out CurveLoop curveLoop)
    {
        curveLoop = null;

        if (lines == null || lines.Count == 0)
            return false;

        if (!IsValidLoop(lines))
            return false;

        try
        {
            curveLoop = CurveLoop.Create(lines.Cast<Curve>().ToList());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidLoop(List<Line> lines)
    {
        for (int i = 0; i < lines.Count - 1; i++)
        {
            if (!lines[i].GetEndPoint(1).IsAlmostEqualTo(lines[i + 1].GetEndPoint(0)))
            {
                return false;
            }
        }

        return lines.Last().GetEndPoint(1).IsAlmostEqualTo(lines.First().GetEndPoint(0));
    }

    private List<Line> ArrangeLinesIntoValidCurveLoop(List<Line> lines)
    {
        var remaining = new List<Line>(lines);
        var arranged = new List<Line>();

        var current = remaining[0];
        arranged.Add(current);
        remaining.RemoveAt(0);

        while (remaining.Count > 0)
        {
            var lastPoint = arranged.Last().GetEndPoint(1);
            bool found = false;

            for (int i = 0; i < remaining.Count; i++)
            {
                var line = remaining[i];
                var startPoint = line.GetEndPoint(0);
                var endPoint = line.GetEndPoint(1);

                if (lastPoint.IsAlmostEqualTo(startPoint))
                {
                    arranged.Add(line);
                    remaining.RemoveAt(i);
                    found = true;
                    break;
                }

                else if (lastPoint.IsAlmostEqualTo(endPoint))
                {
                    var reversedLine = Line.CreateBound(endPoint, startPoint);
                    arranged.Add(reversedLine);
                    remaining.RemoveAt(i);
                    found = true;
                    break;
                }
            }

            if (!found)
                return null;
        }

        if (!arranged.Last().GetEndPoint(1).IsAlmostEqualTo(arranged.First().GetEndPoint(0)))
            return null;

        return arranged;
    }

    private void CreateFloor(CurveLoop curveLoop, ElementId floorTypeId, ElementId levelId)
    {
        using var transaction = new Transaction(Document, "Create Floor");

        transaction.Start();
        try
        {
            Floor.Create(Document, new List<CurveLoop> { curveLoop }, floorTypeId, levelId);
            TaskDialog.Show("Success", "Floor created successfully");
            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.RollBack();
            TaskDialog.Show("Error", $"Floor creation failed: {ex.Message}");
        }
    }
}
