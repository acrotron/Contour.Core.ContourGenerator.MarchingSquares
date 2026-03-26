using System.Collections.Concurrent;
using Contour.Core.Interfaces;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

namespace Contour.Core.ContourGenerator.MarchingSquares;

/// <summary>
/// Generates contour polygons by tracing the boundary of connected above-interval regions
/// instead of creating individual triangle polygons and unioning them.
/// <para>
/// Algorithm: For each interval, classify triangles as Above/Below/Crossing, flood-fill
/// connected components of contributing triangles (Above ∪ Crossing), collect directed
/// boundary segments for each component, chain them into closed rings, and build polygons.
/// </para>
/// <para>
/// Complexity is O(n) where n is the number of triangles, compared to the union-based
/// approach which is O(n log n) or worse. No geometric union operations are performed.
/// </para>
/// </summary>
public class BoundaryTracingContourPolygons : IContourPolygons
{
    private readonly IGeometryPrecision _geometryPrecision;

    public BoundaryTracingContourPolygons(IGeometryPrecision geometryPrecision)
    {
        _geometryPrecision = geometryPrecision;
    }

    /// <inheritdoc />
    public Dictionary<double, MultiPolygon> Contours(IList<TriExt> tris, double[] intervals,
        IProgress<OperationProgress>? progress = null)
    {
        var result = new ConcurrentDictionary<double, MultiPolygon>();
        var gf = _geometryPrecision.GeometryFactory;
        int completed = 0;
        int total = intervals.Length;

        // Build ID-to-index lookup once (triangle IDs are sequential from 0 in RasterGrid)
        int maxId = 0;
        for (int i = 0; i < tris.Count; i++)
        {
            if (tris[i].Id > maxId) maxId = tris[i].Id;
        }

        var idToIdx = new int[maxId + 1];
        Array.Fill(idToIdx, -1);
        for (int i = 0; i < tris.Count; i++)
        {
            idToIdx[tris[i].Id] = i;
        }

        Parallel.ForEach(intervals, interval =>
        {
            var mp = BuildPolygonsForInterval(tris, idToIdx, interval, gf);
            if (mp != null)
            {
                result[interval] = mp;
            }

            int done = Interlocked.Increment(ref completed);
            progress?.Report(new OperationProgress(
                $"Contour polygons: {done}/{total} intervals"));
        });

        return new Dictionary<double, MultiPolygon>(result);
    }

    private static MultiPolygon? BuildPolygonsForInterval(
        IList<TriExt> tris, int[] idToIdx, double interval, GeometryFactory gf)
    {
        int n = tris.Count;

        // Step 1: Classify all triangles
        // 0 = Below, 1 = Above, 2 = Crossing
        var cls = new byte[n];
        for (int i = 0; i < n; i++)
        {
            if (tris[i].LowestVertexValue > interval)
                cls[i] = 1; // Above
            else if (tris[i].HighestVertexValue < interval)
                cls[i] = 0; // Below
            else
                cls[i] = 2; // Crossing
        }

        // Step 2: Flood-fill connected components of contributing triangles (Above ∪ Crossing)
        var compId = new int[n];
        Array.Fill(compId, -1);
        var components = new List<List<int>>();

        for (int i = 0; i < n; i++)
        {
            if (cls[i] == 0 || compId[i] >= 0) continue;

            int cid = components.Count;
            var comp = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            compId[i] = cid;

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                comp.Add(idx);

                foreach (var nb in tris[idx].GetAdjacentTriangles())
                {
                    int nIdx = LookupIndex(nb.Id, idToIdx);
                    if (nIdx >= 0 && cls[nIdx] != 0 && compId[nIdx] < 0)
                    {
                        compId[nIdx] = cid;
                        queue.Enqueue(nIdx);
                    }
                }
            }

            components.Add(comp);
        }

        if (components.Count == 0) return null;

        // Step 3: For each component, collect boundary segments and build polygons
        var allPolygons = new List<Polygon>();
        foreach (var comp in components)
        {
            BuildComponentPolygons(tris, cls, idToIdx, compId, comp, interval, gf, allPolygons);
        }

        if (allPolygons.Count == 0) return null;
        return gf.CreateMultiPolygon(allPolygons.ToArray());
    }

    private static void BuildComponentPolygons(
        IList<TriExt> tris, byte[] cls, int[] idToIdx, int[] compId,
        List<int> component, double interval, GeometryFactory gf, List<Polygon> results)
    {
        int cid = compId[component[0]];
        var segments = new List<BoundarySegment>();

        foreach (int idx in component)
        {
            var tri = tris[idx];

            if (cls[idx] == 1) // Above
            {
                AddAboveTriangleBoundary(tri, idToIdx, compId, cid, segments);
            }
            else // Crossing
            {
                AddCrossingTriangleBoundary(tri, interval, idToIdx, compId, cid, segments);
            }
        }

        if (segments.Count < 3) return;

        ChainSegmentsAndBuildPolygons(segments, gf, results);
    }

    /// <summary>
    /// For an Above triangle, add each edge that faces a non-contributing neighbor as a boundary segment.
    /// </summary>
    private static void AddAboveTriangleBoundary(
        TriExt tri, int[] idToIdx, int[] compId, int cid, List<BoundarySegment> segments)
    {
        for (int e = 0; e < 3; e++)
        {
            if (IsNeighborInComponent(tri, e, idToIdx, compId, cid)) continue;

            var (start, end) = GetEdgeVertices(tri, e);
            segments.Add(new BoundarySegment(start, end));
        }
    }

    /// <summary>
    /// For a Crossing triangle, add the contour segment and any partial/full edges
    /// that face non-contributing neighbors.
    /// </summary>
    private static void AddCrossingTriangleBoundary(
        TriExt tri, double interval, int[] idToIdx, int[] compId, int cid,
        List<BoundarySegment> segments)
    {
        var intersections = GetCanonicalIntersections(interval, tri);

        if (intersections.Count != 2)
        {
            // Degenerate crossing (contour passes through vertex).
            // If any vertex is strictly above, treat as Above for boundary purposes.
            if (tri.HighestVertexValue > interval)
            {
                AddAboveTriangleBoundary(tri, idToIdx, compId, cid, segments);
            }

            return;
        }

        // Identify which edges are crossed
        int crossedIdx0 = FindEdgeIndex(tri, intersections[0].TriEdge);
        int crossedIdx1 = FindEdgeIndex(tri, intersections[1].TriEdge);
        if (crossedIdx0 < 0 || crossedIdx1 < 0) return;

        var interp0 = intersections[0].InterpolatedValue;
        var interp1 = intersections[1].InterpolatedValue;

        CoordinateM above0 = intersections[0].TriEdge.Start.M > interval
            ? intersections[0].TriEdge.Start
            : intersections[0].TriEdge.End;
        CoordinateM above1 = intersections[1].TriEdge.Start.M > interval
            ? intersections[1].TriEdge.Start
            : intersections[1].TriEdge.End;

        // Edge A: above_v0 → interp_0 (partial edge on first crossed edge)
        if (!IsNeighborInComponent(tri, crossedIdx0, idToIdx, compId, cid))
        {
            segments.Add(new BoundarySegment(above0, interp0));
        }

        // Edge B: interp_0 → interp_1 (contour segment — always a boundary)
        segments.Add(new BoundarySegment(interp0, interp1));

        // Edge C: interp_1 → above_v1 (partial edge on second crossed edge)
        if (!IsNeighborInComponent(tri, crossedIdx1, idToIdx, compId, cid))
        {
            segments.Add(new BoundarySegment(interp1, above1));
        }

        // Edge D: above_v1 → above_v0 (uncrossed edge, only if vertices are distinct)
        if (!above0.Equals2D(above1))
        {
            int uncrossedIdx = 3 - crossedIdx0 - crossedIdx1; // edges 0+1+2 = 3
            if (!IsNeighborInComponent(tri, uncrossedIdx, idToIdx, compId, cid))
            {
                segments.Add(new BoundarySegment(above1, above0));
            }
        }
    }

    /// <summary>
    /// Compute edge intersections using canonical vertex ordering so that adjacent triangles
    /// sharing an edge produce identical intersection coordinates (avoiding floating-point divergence).
    /// The vertex with M &lt;= interval is always passed as the first argument to Interpolate.
    /// </summary>
    private static List<Intersection> GetCanonicalIntersections(double interval, TriExt tri)
    {
        var intersections = new List<Intersection>(2);
        CheckEdgeCanonical(tri.P0M, tri.P1M, interval, intersections);
        CheckEdgeCanonical(tri.P1M, tri.P2M, interval, intersections);
        CheckEdgeCanonical(tri.P2M, tri.P0M, interval, intersections);
        return intersections;
    }

    private static void CheckEdgeCanonical(CoordinateM v1, CoordinateM v2, double interval,
        List<Intersection> intersections)
    {
        if (!Utilities.ContourPassesThroughEdge(v1.M, v2.M, interval)) return;

        // Canonical: always interpolate from vertex with M <= interval.
        // This ensures adjacent triangles sharing this edge get the exact same result.
        CoordinateM interp = v1.M <= interval
            ? Utilities.Interpolate(v1, v2, interval)
            : Utilities.Interpolate(v2, v1, interval);

        intersections.Add(new Intersection(interp, new TriEdge(v1, v2)));
    }

    /// <summary>
    /// Finds which edge index (0, 1, 2) of the triangle matches the given TriEdge.
    /// </summary>
    private static int FindEdgeIndex(TriExt tri, TriEdge edge)
    {
        for (int i = 0; i < 3; i++)
        {
            if (tri.GetEdge(i).IsEqual(edge)) return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns the start and end vertices of a triangle edge by index.
    /// </summary>
    private static (CoordinateM Start, CoordinateM End) GetEdgeVertices(TriExt tri, int edgeIndex)
    {
        return edgeIndex switch
        {
            0 => (tri.P0M, tri.P1M),
            1 => (tri.P1M, tri.P2M),
            2 => (tri.P2M, tri.P0M),
            _ => throw new ArgumentOutOfRangeException(nameof(edgeIndex))
        };
    }

    /// <summary>
    /// Checks whether the neighbor across a given edge belongs to the same connected component.
    /// NTS Tri stores adjacency by opposite-vertex index: edge e → adjacency slot (e+2)%3.
    /// </summary>
    private static bool IsNeighborInComponent(TriExt tri, int edgeIndex,
        int[] idToIdx, int[] compId, int cid)
    {
        int adjSlot = (edgeIndex + 2) % 3;
        if (!tri.HasAdjacent(adjSlot)) return false;

        var nb = (TriExt)tri.GetAdjacent(adjSlot);
        int nIdx = LookupIndex(nb.Id, idToIdx);
        return nIdx >= 0 && compId[nIdx] == cid;
    }

    private static int LookupIndex(int triId, int[] idToIdx)
    {
        if (triId < 0 || triId >= idToIdx.Length) return -1;
        return idToIdx[triId];
    }

    /// <summary>
    /// Chains boundary segments into closed rings (undirected) and builds polygons.
    /// Segments are treated as undirected edges: the traversal direction is determined by
    /// the ring topology, not the original segment direction (which varies depending on
    /// triangle winding and vertex ordering). Zero-length segments are skipped.
    /// </summary>
    private static void ChainSegmentsAndBuildPolygons(
        List<BoundarySegment> segments, GeometryFactory gf, List<Polygon> results)
    {
        // Filter zero-length segments (occur when intersection coincides with a vertex,
        // e.g. center point M = interval)
        var edges = new List<(Coordinate A, Coordinate B)>();
        for (int i = 0; i < segments.Count; i++)
        {
            if (!segments[i].Start.Equals2D(segments[i].End))
            {
                edges.Add((segments[i].Start, segments[i].End));
            }
        }

        if (edges.Count < 3) return;

        // Build undirected adjacency: coordinate → list of (neighbor coordinate, edge index)
        var adj = new Dictionary<(double X, double Y), List<(Coordinate Neighbor, int EdgeIdx)>>(edges.Count);

        for (int i = 0; i < edges.Count; i++)
        {
            AddAdjacency(adj, edges[i].A, edges[i].B, i);
            AddAdjacency(adj, edges[i].B, edges[i].A, i);
        }

        // Trace closed rings by following undirected edges
        var usedEdge = new bool[edges.Count];
        var rings = new List<LinearRing>();

        for (int i = 0; i < edges.Count; i++)
        {
            if (usedEdge[i]) continue;

            var coords = new List<Coordinate>();
            Coordinate current = edges[i].A;
            int curEdge = i;

            while (curEdge >= 0 && !usedEdge[curEdge])
            {
                usedEdge[curEdge] = true;
                coords.Add(current);

                // Follow edge to the other endpoint
                Coordinate next = edges[curEdge].A.Equals2D(current)
                    ? (Coordinate)edges[curEdge].B
                    : (Coordinate)edges[curEdge].A;

                // Find the next unused edge at the other endpoint
                curEdge = -1;
                var key = (next.X, next.Y);
                if (adj.TryGetValue(key, out var candidates))
                {
                    for (int c = 0; c < candidates.Count; c++)
                    {
                        if (!usedEdge[candidates[c].EdgeIdx])
                        {
                            curEdge = candidates[c].EdgeIdx;
                            break;
                        }
                    }
                }

                current = next;
            }

            if (coords.Count >= 3)
            {
                coords.Add(coords[0]); // close ring
                rings.Add(gf.CreateLinearRing(coords.ToArray()));
            }
        }

        if (rings.Count == 0) return;

        if (rings.Count == 1)
        {
            var ring = EnsureCCW(rings[0], gf);
            results.Add(gf.CreatePolygon(ring));
            return;
        }

        // Multiple rings: the ring with the largest area is the outer ring, others are holes.
        // A single connected component has exactly one outer boundary but may have multiple holes
        // (islands of below-interval values inside the region).
        int outerIdx = 0;
        double maxArea = 0;
        for (int i = 0; i < rings.Count; i++)
        {
            double area = Math.Abs(Area.OfRing(rings[i].Coordinates));
            if (area > maxArea)
            {
                maxArea = area;
                outerIdx = i;
            }
        }

        var outer = EnsureCCW(rings[outerIdx], gf);
        var holes = new LinearRing[rings.Count - 1];
        int hi = 0;
        for (int i = 0; i < rings.Count; i++)
        {
            if (i == outerIdx) continue;
            holes[hi++] = EnsureCW(rings[i], gf);
        }

        results.Add(gf.CreatePolygon(outer, holes));
    }

    private static void AddAdjacency(
        Dictionary<(double X, double Y), List<(Coordinate Neighbor, int EdgeIdx)>> adj,
        Coordinate from, Coordinate to, int edgeIdx)
    {
        var key = (from.X, from.Y);
        if (!adj.TryGetValue(key, out var list))
        {
            list = new List<(Coordinate, int)>(4);
            adj[key] = list;
        }

        list.Add((to, edgeIdx));
    }

    /// <summary>
    /// Ensures a ring is counter-clockwise (outer ring convention in NTS).
    /// </summary>
    private static LinearRing EnsureCCW(LinearRing ring, GeometryFactory gf)
    {
        if (Orientation.IsCCW(ring.Coordinates)) return ring;
        return gf.CreateLinearRing(ring.Coordinates.Reverse().ToArray());
    }

    /// <summary>
    /// Ensures a ring is clockwise (hole convention in NTS).
    /// </summary>
    private static LinearRing EnsureCW(LinearRing ring, GeometryFactory gf)
    {
        if (!Orientation.IsCCW(ring.Coordinates)) return ring;
        return gf.CreateLinearRing(ring.Coordinates.Reverse().ToArray());
    }

    private readonly struct BoundarySegment(CoordinateM start, CoordinateM end)
    {
        public CoordinateM Start { get; } = start;
        public CoordinateM End { get; } = end;
    }
}
