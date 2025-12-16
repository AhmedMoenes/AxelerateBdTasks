namespace Task3.Utils;

public static class GeometryUtils
{
    /// <summary>
    /// Gets all boundary points from segments
    /// </summary>
    public static List<XYZ> GetBoundaryPoints(IList<BoundarySegment> segments)
    {
        var points = new List<XYZ>();

        foreach (var segment in segments)
        {
            var curve = segment.GetCurve();
            points.Add(curve.GetEndPoint(0));
        }

        return points;
    }

    /// <summary>
    /// Calculates the centroid of a polygon using the shoelace formula
    /// </summary>
    public static XYZ CalculatePolygonCentroid(List<XYZ> points)
    {
        var n = points.Count;
        double area = 0.0;
        double cx = 0.0;
        double cy = 0.0;

        for (int i = 0; i < n; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % n];

            var crossProduct = current.X * next.Y - next.X * current.Y;
            area += crossProduct;
            cx += (current.X + next.X) * crossProduct;
            cy += (current.Y + next.Y) * crossProduct;
        }

        area *= 0.5;

        if (Math.Abs(area) < 1e-10)
        {
            return CalculateAveragePoint(points);
        }

        cx /= (6.0 * area);
        cy /= (6.0 * area);

        var z = points[0].Z;

        return new XYZ(cx, cy, z);
    }

    /// <summary>
    /// Calculates the average position of all points (fallback method)
    /// </summary>
    private static XYZ CalculateAveragePoint(List<XYZ> points)
    {
        var sum = points.Aggregate(XYZ.Zero, (current, point) => current + point);
        return sum / points.Count;
    }

    /// <summary>
    /// Computes convex hull using Graham scan algorithm
    /// </summary>
    public static List<XYZ> ComputeConvexHull(List<XYZ> points)
    {
        if (points.Count < 3)
            return points;

        // Find the lowest point (and leftmost if tied)
        var lowest = points[0];
        foreach (var p in points)
        {
            if (p.Y < lowest.Y || (Math.Abs(p.Y - lowest.Y) < 0.001 && p.X < lowest.X))
            {
                lowest = p;
            }
        }

        // Sort points by polar angle with respect to lowest point
        var sortedPoints = points
            .Where(p => !p.IsAlmostEqualTo(lowest))
            .OrderBy(p => Math.Atan2(p.Y - lowest.Y, p.X - lowest.X))
            .ThenBy(p => p.DistanceTo(lowest))
            .ToList();

        sortedPoints.Insert(0, lowest);

        // Build convex hull
        var hull = new List<XYZ> { sortedPoints[0], sortedPoints[1] };

        for (int i = 2; i < sortedPoints.Count; i++)
        {
            while (hull.Count > 1 && CrossProduct(hull[hull.Count - 2], hull[hull.Count - 1], sortedPoints[i]) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(sortedPoints[i]);
        }

        return hull;
    }

    /// <summary>
    /// Calculates cross product for orientation test
    /// </summary>
    private static double CrossProduct(XYZ a, XYZ b, XYZ c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }
}