using AsciiRaster.Parser;
using NetTopologySuite.Geometries;

namespace Contour.Core.ContourGenerator.MarchingSquares;

/// <summary>
/// Builds a grid of sub-triangles from an <see cref="EsriAsciiRaster"/>.
/// Each raster cell (2x2 group of grid nodes) is subdivided into 4 triangles
/// by connecting corners to a bilinear-interpolated center point.
/// </summary>
public class RasterGrid
{
    /// <summary>
    /// Sub-triangles indexed by [col, row, subTriIndex].
    /// SubTriIndex 0-3 corresponds to top, right, bottom, left sub-triangles.
    /// Null entries indicate cells with NoData corners.
    /// </summary>
    public TriExt?[,,] SubTriangles { get; }

    /// <summary>
    /// Number of cell columns (NCols - 1).
    /// </summary>
    public int CellCols { get; }

    /// <summary>
    /// Number of cell rows (NRows - 1).
    /// </summary>
    public int CellRows { get; }

    private RasterGrid(TriExt?[,,] subTriangles, int cellCols, int cellRows)
    {
        SubTriangles = subTriangles;
        CellCols = cellCols;
        CellRows = cellRows;
    }

    /// <summary>
    /// Creates a <see cref="RasterGrid"/> from pre-computed node coordinates.
    /// Each node has X, Y (geographic) and M (data value).
    /// Useful when coordinates have already been transformed (e.g., from projected to WGS84).
    /// </summary>
    /// <param name="nodes">Grid nodes indexed as [col, row] with M = data value.</param>
    /// <param name="noDataValue">Value indicating missing data.</param>
    public static RasterGrid FromNodes(CoordinateM[,] nodes, double noDataValue)
    {
        int nCols = nodes.GetLength(0);
        int nRows = nodes.GetLength(1);
        int cellCols = nCols - 1;
        int cellRows = nRows - 1;
        var subTriangles = new TriExt?[cellCols, cellRows, 4];

        int triId = 0;

        for (int row = 0; row < cellRows; row++)
        {
            for (int col = 0; col < cellCols; col++)
            {
                var tl = nodes[col, row];
                var tr = nodes[col + 1, row];
                var bl = nodes[col, row + 1];
                var br = nodes[col + 1, row + 1];

                if (IsNoData(tl.M, noDataValue) || IsNoData(tr.M, noDataValue) ||
                    IsNoData(bl.M, noDataValue) || IsNoData(br.M, noDataValue))
                {
                    continue;
                }

                BuildCell(subTriangles, col, row, ref triId, tl, tr, bl, br);
            }
        }

        SetAdjacency(subTriangles, cellCols, cellRows);

        return new RasterGrid(subTriangles, cellCols, cellRows);
    }

    /// <summary>
    /// Creates a <see cref="RasterGrid"/> from an <see cref="EsriAsciiRaster"/>.
    /// Each cell gets 4 sub-triangles via bilinear center interpolation.
    /// Adjacency relationships are established for contour tracing.
    /// </summary>
    public static RasterGrid FromRaster(EsriAsciiRaster raster)
    {
        int cellCols = raster.NCols - 1;
        int cellRows = raster.NRows - 1;
        var subTriangles = new TriExt?[cellCols, cellRows, 4];

        // Determine origin coordinates
        double xOrigin = !double.IsNaN(raster.XLLCorner) ? raster.XLLCorner : raster.XLLCenter - raster.CellSize / 2.0;
        double yOrigin = !double.IsNaN(raster.YLLCorner) ? raster.YLLCorner : raster.YLLCenter - raster.CellSize / 2.0;

        int triId = 0;

        // Build sub-triangles for each cell
        for (int row = 0; row < cellRows; row++)
        {
            for (int col = 0; col < cellCols; col++)
            {
                // Data is stored as [col, row] where row 0 is the top of the raster file
                double tlVal = raster.Data[col, row];
                double trVal = raster.Data[col + 1, row];
                double blVal = raster.Data[col, row + 1];
                double brVal = raster.Data[col + 1, row + 1];

                // Skip cells with any NoData corner
                if (tlVal == raster.NoDataValue || trVal == raster.NoDataValue ||
                    blVal == raster.NoDataValue || brVal == raster.NoDataValue)
                {
                    continue;
                }

                // Calculate spatial coordinates
                // Row 0 in data = top of raster = highest Y value
                double xLeft = xOrigin + col * raster.CellSize;
                double xRight = xOrigin + (col + 1) * raster.CellSize;
                double yTop = yOrigin + (cellRows - row) * raster.CellSize;
                double yBottom = yOrigin + (cellRows - row - 1) * raster.CellSize;

                // Corner coordinates with M values
                var tl = new CoordinateM(xLeft, yTop, tlVal);
                var tr = new CoordinateM(xRight, yTop, trVal);
                var bl = new CoordinateM(xLeft, yBottom, blVal);
                var br = new CoordinateM(xRight, yBottom, brVal);

                BuildCell(subTriangles, col, row, ref triId, tl, tr, bl, br);
            }
        }

        // Establish adjacency relationships
        SetAdjacency(subTriangles, cellCols, cellRows);

        return new RasterGrid(subTriangles, cellCols, cellRows);
    }

    private static void BuildCell(TriExt?[,,] subTriangles, int col, int row,
        ref int triId, CoordinateM tl, CoordinateM tr, CoordinateM bl, CoordinateM br)
    {
        double centerX = (tl.X + tr.X + bl.X + br.X) / 4.0;
        double centerY = (tl.Y + tr.Y + bl.Y + br.Y) / 4.0;
        double centerM = (tl.M + tr.M + bl.M + br.M) / 4.0;
        var center = new CoordinateM(centerX, centerY, centerM);

        subTriangles[col, row, 0] = new TriExt(triId++, tl, tr, center);
        subTriangles[col, row, 1] = new TriExt(triId++, tr, br, center);
        subTriangles[col, row, 2] = new TriExt(triId++, br, bl, center);
        subTriangles[col, row, 3] = new TriExt(triId++, bl, tl, center);
    }

    /// <summary>
    /// Sets up adjacency relationships between all sub-triangles.
    /// Within a cell, adjacent triangles share radial edges to the center.
    /// Across cells, adjacent triangles share the outer cell edge.
    /// </summary>
    private static void SetAdjacency(TriExt?[,,] tris, int cellCols, int cellRows)
    {
        for (int row = 0; row < cellRows; row++)
        {
            for (int col = 0; col < cellCols; col++)
            {
                var t0 = tris[col, row, 0]; // top
                var t1 = tris[col, row, 1]; // right
                var t2 = tris[col, row, 2]; // bottom
                var t3 = tris[col, row, 3]; // left

                if (t0 == null) continue; // entire cell is NoData

                // Within-cell adjacency (radial edges)
                // Edge convention: edge 0 = P0→P1 (outer), edge 1 = P1→P2 (radial), edge 2 = P2→P0 (radial)
                // Tri N edge 1 ↔ Tri (N+1)%4 edge 2
                SetAdjacentSafe(t0, 1, t1, 2);
                SetAdjacentSafe(t1, 1, t2, 2);
                SetAdjacentSafe(t2, 1, t3, 2);
                SetAdjacentSafe(t3, 1, t0, 2);

                // Cross-cell adjacency (outer edges)
                // Tri 0 edge 0 (top outer) ↔ cell above's Tri 2 edge 0 (bottom outer)
                if (row > 0)
                {
                    var neighborTri = tris[col, row - 1, 2];
                    SetAdjacentSafe(t0, 0, neighborTri, 0);
                }

                // Tri 1 edge 0 (right outer) ↔ cell right's Tri 3 edge 0 (left outer)
                if (col < cellCols - 1)
                {
                    var neighborTri = tris[col + 1, row, 3];
                    SetAdjacentSafe(t1, 0, neighborTri, 0);
                }

                // Tri 2 edge 0 (bottom outer) ↔ cell below's Tri 0 edge 0 (top outer)
                if (row < cellRows - 1)
                {
                    var neighborTri = tris[col, row + 1, 0];
                    SetAdjacentSafe(t2, 0, neighborTri, 0);
                }

                // Tri 3 edge 0 (left outer) ↔ cell left's Tri 1 edge 0 (right outer)
                if (col > 0)
                {
                    var neighborTri = tris[col - 1, row, 1];
                    SetAdjacentSafe(t3, 0, neighborTri, 0);
                }
            }
        }
    }

    /// <summary>
    /// Sets bidirectional adjacency between two triangles on the specified edges.
    /// NTS Tri.SetAdjacent(Coordinate, Tri) sets the neighbor on the edge opposite to the given vertex.
    /// For edge index i, the opposite vertex is at index (i + 2) % 3.
    /// </summary>
    private static void SetAdjacentSafe(TriExt? tri, int edgeIndex, TriExt? neighbor, int neighborEdgeIndex)
    {
        if (tri == null || neighbor == null) return;

        // The opposite vertex index for edge i is (i + 2) % 3
        var triOppositeVertex = tri.GetCoordinate((edgeIndex + 2) % 3);
        var neighborOppositeVertex = neighbor.GetCoordinate((neighborEdgeIndex + 2) % 3);

        tri.SetAdjacent(triOppositeVertex, neighbor);
        neighbor.SetAdjacent(neighborOppositeVertex, tri);
    }

    private static bool IsNoData(double value, double noDataValue)
    {
        return double.IsNaN(value) || value == noDataValue;
    }

    /// <summary>
    /// Returns all non-null sub-triangles as a flat list.
    /// </summary>
    public IList<TriExt> GetAllTriangles()
    {
        var result = new List<TriExt>();

        for (int row = 0; row < CellRows; row++)
        {
            for (int col = 0; col < CellCols; col++)
            {
                for (int sub = 0; sub < 4; sub++)
                {
                    var tri = SubTriangles[col, row, sub];

                    if (tri != null)
                    {
                        result.Add(tri);
                    }
                }
            }
        }
        return result;
    }
}
