using Autodesk.Revit.DB;
using Task4.Models;

namespace Task4.Utils;

public static class FramingUtils
{
    /// <summary>
    /// Creates a vertical stud with cuts around openings
    /// </summary>
    public static void CreateVerticalStudWithCuts(
        Document document,
        XYZ studPoint,
        WallData wallData,
        List<FamilyInstance> openings,
        double studOffsetFeet)
    {
        var offsetBasePoint = studPoint +
            wallData.Normal * wallData.Width * 0.5 +
            XYZ.BasisZ * wallData.BaseElevation;

        var topPoint = offsetBasePoint + XYZ.BasisZ * wallData.Height;
        var studLine = Line.CreateBound(offsetBasePoint, topPoint);

        // Check for intersections with openings
        var segments = CutStudAtOpenings(studLine, openings);

        var offsetDistance = UnitUtils.ConvertToInternalUnits(studOffsetFeet, UnitTypeId.Feet) * 0.5;

        // Create studs for each segment
        foreach (var segment in segments)
        {
            CreatePairedStuds(
                document,
                segment,
                wallData.Direction * offsetDistance,
                wallData.Normal);
        }
    }

    /// <summary>
    /// Cuts a stud line at openings and returns valid segments
    /// </summary>
    private static List<Line> CutStudAtOpenings(
        Line studLine,
        List<FamilyInstance> openings)
    {
        var segments = new List<Line>();
        var studStart = studLine.GetEndPoint(0);
        var studEnd = studLine.GetEndPoint(1);

        // Collect all cutting intervals (Z-ranges where openings exist)
        var cuttingIntervals = new List<(double min, double max)>();

        foreach (var opening in openings)
        {
            var boundingBox = opening.get_BoundingBox(null);
            if (boundingBox == null)
                continue;

            // Check if stud intersects with opening in XY plane
            var openingCenter = (boundingBox.Min + boundingBox.Max) / 2;
            var studXY = new XYZ(studStart.X, studStart.Y, 0);
            var openingXY = new XYZ(openingCenter.X, openingCenter.Y, 0);

            var distance = studXY.DistanceTo(openingXY);
            var openingWidth = Math.Abs(boundingBox.Max.X - boundingBox.Min.X);
            var openingDepth = Math.Abs(boundingBox.Max.Y - boundingBox.Min.Y);
            var openingRadius = Math.Max(openingWidth, openingDepth) / 2;

            // If stud is within opening's horizontal bounds, add cutting interval
            if (distance < openingRadius + 0.5) // 0.5 ft tolerance
            {
                cuttingIntervals.Add((boundingBox.Min.Z, boundingBox.Max.Z));
            }
        }

        // If no intersections, return the full stud
        if (cuttingIntervals.Count == 0)
        {
            segments.Add(studLine);
            return segments;
        }

        // Merge overlapping intervals
        var mergedIntervals = MergeIntervals(cuttingIntervals);

        // Create segments between cutting intervals
        var currentZ = studStart.Z;

        foreach (var interval in mergedIntervals.OrderBy(i => i.min))
        {
            // Add segment before the opening
            if (currentZ < interval.min - 0.1) // 0.1 ft minimum segment
            {
                var segmentStart = new XYZ(studStart.X, studStart.Y, currentZ);
                var segmentEnd = new XYZ(studStart.X, studStart.Y, interval.min);
                segments.Add(Line.CreateBound(segmentStart, segmentEnd));
            }

            // Move past the opening
            currentZ = interval.max;
        }

        // Add final segment after last opening
        if (currentZ < studEnd.Z - 0.1)
        {
            var segmentStart = new XYZ(studStart.X, studStart.Y, currentZ);
            segments.Add(Line.CreateBound(segmentStart, studEnd));
        }

        return segments;
    }

    /// <summary>
    /// Merges overlapping intervals
    /// </summary>
    private static List<(double min, double max)> MergeIntervals(List<(double min, double max)> intervals)
    {
        if (intervals.Count == 0)
            return intervals;

        var sorted = intervals.OrderBy(i => i.min).ToList();
        var merged = new List<(double min, double max)> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            var current = sorted[i];
            var last = merged[merged.Count - 1];

            if (current.min <= last.max)
            {
                // Overlapping intervals, merge them
                merged[merged.Count - 1] = (last.min, Math.Max(last.max, current.max));
            }
            else
            {
                // Non-overlapping, add as new interval
                merged.Add(current);
            }
        }

        return merged;
    }


    /// <summary>
    /// Creates a horizontal stud along the wall curve
    /// </summary>
    public static void CreateHorizontalStud(
        Document document,
        Curve wallCurve,
        double elevation,
        XYZ wallNormal,
        double wallWidth,
        double studOffsetFeet)
    {
        var horizontalOffset = Transform.CreateTranslation(
            wallNormal * wallWidth * 0.5 +
            XYZ.BasisZ * elevation);

        var studCurve = wallCurve.CreateTransformed(horizontalOffset);
        var offsetDistance = UnitUtils.ConvertToInternalUnits(studOffsetFeet, UnitTypeId.Feet) * 0.5;

        CreatePairedStuds(
            document,
            studCurve,
            XYZ.BasisZ * offsetDistance,
            wallNormal);
    }

    /// <summary>
    /// Creates framing around door and window openings
    /// </summary>
    public static void CreateOpeningFrame(
        Document document,
        FamilyInstance opening,
        XYZ wallNormal,
        double wallWidth,
        double studOffsetFeet)
    {
        var boundingBox = opening.get_BoundingBox(null);
        if (boundingBox == null)
            return;

        var normalOffset = wallNormal * wallWidth * 0.5;
        var offsetDistance = UnitUtils.ConvertToInternalUnits(studOffsetFeet, UnitTypeId.Feet) * 0.5;

        // Calculate corner points
        var bottomLeft = boundingBox.Min + normalOffset;
        var topLeft = new XYZ(boundingBox.Min.X, boundingBox.Min.Y, boundingBox.Max.Z) + normalOffset;
        var topRight = new XYZ(boundingBox.Max.X, boundingBox.Min.Y, boundingBox.Max.Z) + normalOffset;
        var bottomRight = new XYZ(boundingBox.Max.X, boundingBox.Min.Y, boundingBox.Min.Z) + normalOffset;

        // Calculate directions
        var openingDirection = (topRight - topLeft).Normalize();
        var verticalOffsetDirection = openingDirection.CrossProduct(wallNormal).Normalize();

        // Create vertical studs (left and right)
        CreatePairedStuds(
            document,
            Line.CreateBound(bottomLeft, topLeft),
            verticalOffsetDirection * offsetDistance,
            wallNormal);

        CreatePairedStuds(
            document,
            Line.CreateBound(bottomRight, topRight),
            verticalOffsetDirection * offsetDistance,
            wallNormal);

        // Create top horizontal stud
        CreatePairedStuds(
            document,
            Line.CreateBound(topLeft, topRight),
            XYZ.BasisZ * offsetDistance,
            wallNormal);

        // Create bottom stud for windows only
        if (opening.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows)
        {
            CreatePairedStuds(
                document,
                Line.CreateBound(bottomLeft, bottomRight),
                XYZ.BasisZ * offsetDistance,
                wallNormal);
        }
    }

    /// <summary>
    /// Creates a pair of offset studs from a base curve
    /// </summary>
    private static void CreatePairedStuds(
        Document document,
        Curve baseCurve,
        XYZ offsetVector,
        XYZ wallNormal)
    {
        var offset1 = Transform.CreateTranslation(offsetVector);
        var offset2 = Transform.CreateTranslation(-offsetVector);

        var stud1 = baseCurve.CreateTransformed(offset1);
        var stud2 = baseCurve.CreateTransformed(offset2);

        CreateModelCurve(document, stud1, wallNormal, stud1.GetEndPoint(0));
        CreateModelCurve(document, stud2, wallNormal, stud2.GetEndPoint(0));
    }

    /// <summary>
    /// Creates a model curve on a sketch plane
    /// </summary>
    private static void CreateModelCurve(
        Document document,
        Curve curve,
        XYZ normal,
        XYZ origin)
    {
        var plane = Plane.CreateByNormalAndOrigin(normal, origin);
        var sketchPlane = SketchPlane.Create(document, plane);
        document.Create.NewModelCurve(curve, sketchPlane);
    }
}