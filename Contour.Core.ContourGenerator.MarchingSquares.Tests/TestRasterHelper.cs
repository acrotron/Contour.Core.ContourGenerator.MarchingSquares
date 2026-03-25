using System.Globalization;
using AsciiRaster.Parser;

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
