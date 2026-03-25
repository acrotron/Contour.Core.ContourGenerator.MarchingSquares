using Contour.Core.Interfaces;
using NetTopologySuite.Geometries;

namespace Contour.Core.ContourGenerator.MarchingSquares;

/// <summary>
/// Generates contour lines from a grid of sub-triangles using the Esri marching squares approach.
/// Traces contour lines across sub-triangle boundaries by following shared edges.
/// </summary>
public class MarchingSquaresContourLines : IContourLines
{
    private readonly IGeometryPrecision _geometryPrecision;

    public MarchingSquaresContourLines(IGeometryPrecision geometryPrecision)
    {
        _geometryPrecision = geometryPrecision;
    }

    /// <summary>
    /// Generates contour lines for all specified intervals.
    /// </summary>
    public Dictionary<double, List<LineString>> Contours(IList<TriExt> tris, double[] intervals)
    {
        var contours = new Dictionary<double, List<LineString>>();

        foreach (double interval in intervals)
        {
            contours[interval] = GetContoursForInterval(tris, interval);
        }

        return contours;
    }

    private List<LineString> GetContoursForInterval(IList<TriExt> tris, double interval)
    {
        var contours = new List<LineString>();
        var visited = new HashSet<int>();

        foreach (TriExt triangle in tris)
        {
            if (!visited.Add(triangle.Id))
            {
                continue;
            }

            List<Intersection> intersections = Intersection.GetIntersections(interval, triangle);

            if (intersections.Count != 2)
            {
                continue;
            }

            // Start tracing a contour line from this triangle in both directions
            var points = new List<Coordinate>();
            var stack = new Stack<(TriExt Triangle, Intersection Intersection, bool IsSideA)>();

            stack.Push((triangle, intersections[0], true));
            stack.Push((triangle, intersections[1], false));

            while (stack.Count > 0)
            {
                var (currentTriangle, intersection, isSideA) = stack.Pop();

                visited.Add(currentTriangle.Id);

                if (isSideA)
                {
                    points.Add(intersection.InterpolatedValue);
                }
                else
                {
                    points.Insert(0, intersection.InterpolatedValue);
                }

                // Find the neighbor triangle that shares the intersection edge
                foreach (TriExt neighbor in currentTriangle.GetAdjacentTriangles())
                {
                    if (visited.Contains(neighbor.Id))
                    {
                        continue;
                    }

                    if (!neighbor.HasSharedEdge(intersection.TriEdge))
                    {
                        continue;
                    }

                    List<Intersection> neighborIntersections = Intersection.GetIntersections(interval, neighbor);

                    if (neighborIntersections.Count != 2)
                    {
                        continue;
                    }

                    // Push the intersection that is NOT on the shared edge
                    stack.Push(neighborIntersections[0].TriEdge.IsEqual(intersection.TriEdge)
                        ? (neighbor, neighborIntersections[1], isSideA)
                        : (neighbor, neighborIntersections[0], isSideA));
                }
            }

            if (points.Count > 1)
            {
                LineString contourLine = _geometryPrecision.GeometryFactory.CreateLineString(points.ToArray());
                contours.Add(contourLine);
            }
        }

        return contours;
    }
}
