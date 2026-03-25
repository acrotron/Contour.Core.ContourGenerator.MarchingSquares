using Contour.Core.Interfaces;
using NetTopologySuite.Geometries;

namespace Contour.Core.ContourGenerator.MarchingSquares;

/// <summary>
/// Contour generator that implements the Esri grid-based contouring approach.
/// Subdivides raster cells into sub-triangles via bilinear center interpolation,
/// then traces contour lines and polygons through the resulting mesh.
/// </summary>
public class ContourGenerator : IContourGenerator
{
    private readonly IContourLines _contourLines;
    private readonly IContourPolygons _contourPolygons;
    private IList<TriExt>? _triangles;

    public ContourGenerator(IContourLines contourLines, IContourPolygons contourPolygons)
    {
        _contourLines = contourLines;
        _contourPolygons = contourPolygons;
    }

    /// <inheritdoc />
    public void SetTriangles(IList<TriExt> triangles) => _triangles = triangles;

    /// <summary>
    /// Sets a pre-built raster grid as input.
    /// Use when grid coordinates have been pre-transformed (e.g., projected to WGS84).
    /// </summary>
    public void SetInput(RasterGrid grid) => SetTriangles(grid.GetAllTriangles());

    /// <inheritdoc />
    public Dictionary<double, List<LineString>> GenerateContourLines(double[] intervals)
    {
        if (_triangles == null)
        {
            throw new InvalidOperationException("No input set. Call SetTriangles before generating contours.");
        }

        return _contourLines.Contours(_triangles, intervals);
    }

    /// <inheritdoc />
    public Dictionary<double, MultiPolygon> GenerateContourPolygons(double[] intervals,
        IProgress<OperationProgress>? progress = null)
    {
        if (_triangles == null)
        {
            throw new InvalidOperationException("No input set. Call SetTriangles before generating contours.");
        }

        return _contourPolygons.Contours(_triangles, intervals, progress);
    }
}
