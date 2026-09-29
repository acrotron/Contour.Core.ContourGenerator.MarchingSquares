using System.Globalization;
using AsciiRaster.Parser;
using NetTopologySuite.Geometries;

namespace Contour.Core.ContourGenerator.MarchingSquares.Tests;

/// <summary>
/// Creates <see cref="EsriAsciiRaster"/> instances by writing temporary .asc files
/// and reading them back with <see cref="FileReader"/>, since property setters are internal.
/// </summary>
internal static class TestRasterHelper
{
    /// <summary>
    /// Creates an <see cref="EsriAsciiRaster"/> from a column-major data array.
    /// </summary>
    /// <param name="nCols">Number of columns.</param>
    /// <param name="nRows">Number of rows.</param>
    /// <param name="cellSize">Cell size.</param>
    /// <param name="data">Data indexed as [col, row], row 0 = top (north).</param>
    /// <param name="xllCorner">X lower-left corner (default 0).</param>
    /// <param name="yllCorner">Y lower-left corner (default 0).</param>
    /// <param name="noDataValue">NoData value (default -9999).</param>
    internal static EsriAsciiRaster CreateRaster(
        int nCols, int nRows, double cellSize, double[,] data,
        double xllCorner = 0, double yllCorner = 0, double noDataValue = -9999)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_raster_{Guid.NewGuid():N}.asc");
        try
        {
            WriteAscFile(path, nCols, nRows, cellSize, xllCorner, yllCorner, noDataValue, data);
            return new FileReader().Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Builds a <see cref="RasterGrid"/> from an <see cref="EsriAsciiRaster"/> through <see cref="RasterGrid.FromNodes"/>:
    /// node [col, row] at x = x0 + col * cellSize and y = y0 + (nRows - 1 - row) * cellSize (row 0 is the top), with the
    /// raster's value as M. This is what the library's former <c>RasterGrid.FromRaster</c> did.
    /// </summary>
    internal static RasterGrid ToGrid(EsriAsciiRaster raster)
    {
        double xOrigin = !double.IsNaN(raster.XLLCorner) ? raster.XLLCorner : raster.XLLCenter - raster.CellSize / 2.0;
        double yOrigin = !double.IsNaN(raster.YLLCorner) ? raster.YLLCorner : raster.YLLCenter - raster.CellSize / 2.0;
        var nodes = new CoordinateM[raster.NCols, raster.NRows];
        for (int row = 0; row < raster.NRows; row++)
        {
            for (int col = 0; col < raster.NCols; col++)
            {
                nodes[col, row] = new CoordinateM(
                    xOrigin + col * raster.CellSize,
                    yOrigin + (raster.NRows - 1 - row) * raster.CellSize,
                    raster.Data[col, row]);
            }
        }

        return RasterGrid.FromNodes(nodes, raster.NoDataValue);
    }

    private static void WriteAscFile(
        string path, int nCols, int nRows, double cellSize,
        double xllCorner, double yllCorner, double noDataValue, double[,] data)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine($"ncols {nCols}");
        writer.WriteLine($"nrows {nRows}");
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"xllcorner {xllCorner}"));
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"yllcorner {yllCorner}"));
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"cellsize {cellSize}"));
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"NODATA_value {noDataValue}"));

        for (int row = 0; row < nRows; row++)
        {
            var values = new string[nCols];
            for (int col = 0; col < nCols; col++)
            {
                values[col] = data[col, row].ToString(CultureInfo.InvariantCulture);
            }
            writer.WriteLine(string.Join(' ', values));
        }
    }
}
