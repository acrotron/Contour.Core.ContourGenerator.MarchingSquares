using System.Collections.Concurrent;
using Contour.Core.Interfaces;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace Contour.Core.ContourGenerator.MarchingSquares;

/// <summary>
/// Generates contour polygons from a grid of sub-triangles using the Esri marching squares approach.
/// For each interval, collects full and partial sub-triangle polygons above the contour level
/// and merges them into multi-polygons via CascadedPolygonUnion. Intervals are processed in parallel.
/// </summary>
public class MarchingSquaresContourPolygons : IContourPolygons
{
    private readonly IGeometryPrecision _geometryPrecision;

    public MarchingSquaresContourPolygons(IGeometryPrecision geometryPrecision)
    {
        _geometryPrecision = geometryPrecision;
    }

    public Dictionary<double, MultiPolygon> Contours(IList<TriExt> tris, double[] intervals,
        IProgress<OperationProgress>? progress = null)
    {
        var result = new ConcurrentDictionary<double, MultiPolygon>();
        var gf = _geometryPrecision.GeometryFactory;
        int completed = 0;
        int total = intervals.Length;

        Parallel.ForEach(intervals, interval =>
        {
            var polygons = CollectPolygons(tris, interval, gf);
            
            MultiPolygon? merged = MergePolygons(polygons);

            if (merged != null)
            {
                result[interval] = merged;
            }

            int done = Interlocked.Increment(ref completed);

            progress?.Report(new OperationProgress($"Contour polygons: {done}/{total} intervals ({polygons.Count:N0}/{tris.Count:N0} triangles)"));
        });

        return new Dictionary<double, MultiPolygon>(result);
    }

    private static List<Geometry> CollectPolygons(IList<TriExt> tris, double interval, GeometryFactory gf)
    {
        var polygons = new List<Geometry>(tris.Count);

        foreach (TriExt tri in tris)
        {
            if (tri.LowestVertexValue > interval)
            {
                polygons.Add(tri.ToPolygon(gf));
            }
            else if (tri.HighestVertexValue < interval)
            {
                continue;
            }
            else
            {
                List<Intersection> intersections = Intersection.GetIntersections(interval, tri);
                if (intersections.Count != 2) continue;

                var coords = new CoordinateM[5];

                coords[0] = intersections[0].TriEdge.Start.M > interval
                    ? intersections[0].TriEdge.Start
                    : intersections[0].TriEdge.End;
                coords[1] = intersections[0].InterpolatedValue;
                coords[2] = intersections[1].InterpolatedValue;
                coords[3] = intersections[1].TriEdge.Start.M > interval
                    ? intersections[1].TriEdge.Start
                    : intersections[1].TriEdge.End;
                coords[4] = coords[0];

                var ring = gf.CreateLinearRing(coords);
                var polygon = gf.CreatePolygon(ring);

                if (polygon.IsValid)
                {
                    polygons.Add(polygon);
                }
            }
        }

        return polygons;
    }

    private static MultiPolygon? MergePolygons(List<Geometry> polygons)
    {
        if (polygons.Count == 0) return null;
        if (polygons.Count == 1) return new MultiPolygon([(Polygon)polygons[0]]);

        Geometry result = CascadedPolygonUnion.Union(polygons);

        return result switch
        {
            Polygon p => new MultiPolygon([p]),
            MultiPolygon mp => mp,
            _ => null
        };
    }
}
