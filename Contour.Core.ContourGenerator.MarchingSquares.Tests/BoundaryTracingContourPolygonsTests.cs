using AsciiRaster.Parser;
using AwesomeAssertions;

namespace Contour.Core.ContourGenerator.MarchingSquares.Tests;

[TestClass]
public class BoundaryTracingContourPolygonsTests
{
    private static readonly LccGeometryPrecision Precision = new();
    private readonly BoundaryTracingContourPolygons _boundary = new(Precision);
    private readonly MarchingSquaresContourPolygons _original = new(Precision);

    private static EsriAsciiRaster CreateRaster(int nCols, int nRows, double cellSize, double[,] data)
    {
        return TestRasterHelper.CreateRaster(nCols, nRows, cellSize, data);
    }

    [TestMethod]
    public void AllAbove_MatchesOriginal()
    {
        var data = new double[2, 2];
        data[0, 0] = 100; data[1, 0] = 100;
        data[0, 1] = 100; data[1, 1] = 100;

        var tris = RasterGrid.FromRaster(CreateRaster(2, 2, 1.0, data)).GetAllTriangles();

        var original = _original.Contours(tris, [50.0]);
        var boundary = _boundary.Contours(tris, [50.0]);

        boundary.Should().ContainKey(50.0);
        boundary[50.0].Area.Should().BeApproximately(original[50.0].Area, 0.001,
            $"Boundary area={boundary[50.0].Area}, Original area={original[50.0].Area}");
    }

    [TestMethod]
    public void AllBelow_MatchesOriginal()
    {
        var data = new double[2, 2];
        data[0, 0] = 10; data[1, 0] = 10;
        data[0, 1] = 10; data[1, 1] = 10;

        var tris = RasterGrid.FromRaster(CreateRaster(2, 2, 1.0, data)).GetAllTriangles();

        var original = _original.Contours(tris, [50.0]);
        var boundary = _boundary.Contours(tris, [50.0]);

        boundary.Should().NotContainKey(50.0);
    }

    [TestMethod]
    public void PartiallyAbove_MatchesOriginal()
    {
        var data = new double[2, 2];
        data[0, 0] = 0;   data[1, 0] = 100;
        data[0, 1] = 0;   data[1, 1] = 100;

        var tris = RasterGrid.FromRaster(CreateRaster(2, 2, 1.0, data)).GetAllTriangles();

        var original = _original.Contours(tris, [50.0]);
        var boundary = _boundary.Contours(tris, [50.0]);

        boundary.Should().ContainKey(50.0);
        Console.WriteLine($"Original area: {original[50.0].Area}, Boundary area: {boundary[50.0].Area}");
        Console.WriteLine($"Original WKT: {original[50.0]}");
        Console.WriteLine($"Boundary WKT: {boundary[50.0]}");
        boundary[50.0].Area.Should().BeApproximately(original[50.0].Area, 0.001);
    }

    [TestMethod]
    public void Gradient3x3_MatchesOriginal()
    {
        var data = new double[3, 3];
        data[0, 0] = 0;  data[1, 0] = 50;  data[2, 0] = 100;
        data[0, 1] = 0;  data[1, 1] = 50;  data[2, 1] = 100;
        data[0, 2] = 0;  data[1, 2] = 50;  data[2, 2] = 100;

        var tris = RasterGrid.FromRaster(CreateRaster(3, 3, 1.0, data)).GetAllTriangles();
        double[] intervals = [25.0, 75.0];

        var original = _original.Contours(tris, intervals);
        var boundary = _boundary.Contours(tris, intervals);

        foreach (double interval in intervals)
        {
            boundary.Should().ContainKey(interval, $"Missing interval {interval}");
            Console.WriteLine($"Interval {interval}: Original area={original[interval].Area}, Boundary area={boundary[interval].Area}");
            Console.WriteLine($"  Original WKT: {original[interval]}");
            Console.WriteLine($"  Boundary WKT: {boundary[interval]}");
            boundary[interval].Area.Should().BeApproximately(original[interval].Area, 0.001,
                $"Area mismatch at interval {interval}");
        }
    }

    [TestMethod]
    public void Gradient5x5_MatchesOriginal()
    {
        var data = new double[5, 5];
        for (int col = 0; col < 5; col++)
            for (int row = 0; row < 5; row++)
                data[col, row] = col * 25.0;

        var tris = RasterGrid.FromRaster(CreateRaster(5, 5, 1.0, data)).GetAllTriangles();
        double[] intervals = [25.0, 50.0, 75.0];

        var original = _original.Contours(tris, intervals);
        var boundary = _boundary.Contours(tris, intervals);

        foreach (double interval in intervals)
        {
            boundary.Should().ContainKey(interval, $"Missing interval {interval}");
            Console.WriteLine($"Interval {interval}: Original area={original[interval].Area:F6}, Boundary area={boundary[interval].Area:F6}");
            boundary[interval].Area.Should().BeApproximately(original[interval].Area, 0.001,
                $"Area mismatch at interval {interval}");
        }
    }

    [TestMethod]
    public void IntervalEqualsVertexValue_DoesNotCrash()
    {
        var data = new double[3, 3];
        data[0, 0] = 0;  data[1, 0] = 50;  data[2, 0] = 100;
        data[0, 1] = 0;  data[1, 1] = 50;  data[2, 1] = 100;
        data[0, 2] = 0;  data[1, 2] = 50;  data[2, 2] = 100;

        var tris = RasterGrid.FromRaster(CreateRaster(3, 3, 1.0, data)).GetAllTriangles();

        var act = () => _boundary.Contours(tris, [0.0, 50.0, 100.0]);
        act.Should().NotThrow();
    }

    [TestMethod]
    public void StudyInmJob1_MatchesOriginal()
    {
        // Load INM J1 raster
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "Contour.Aedt.Comparison",
            "testdata", "study_inm", "INMASCIINoiseValues_J1.txt");

        if (!File.Exists(path))
        {
            // Try alternative path
            path = @"C:\Users\bjorn\Documents\GitHub\nmplotconversion\src\Contour.Aedt.Comparison\testdata\study_inm\INMASCIINoiseValues_J1.txt";
        }

        if (!File.Exists(path))
        {
            Assert.Inconclusive("INM J1 test data not found");
            return;
        }

        var raster = new FileReader().Read(path);
        var tris = RasterGrid.FromRaster(raster).GetAllTriangles();
        Console.WriteLine($"Triangle count: {tris.Count}");

        double[] intervals = [35.0, 50.0, 65.0];

        var original = _original.Contours(tris, intervals);
        var boundary = _boundary.Contours(tris, intervals);

        foreach (double interval in intervals)
        {
            bool origHas = original.ContainsKey(interval);
            bool boundHas = boundary.ContainsKey(interval);

            Console.WriteLine($"Interval {interval}: original={origHas}, boundary={boundHas}");

            if (origHas)
            {
                Console.WriteLine($"  Original: area={original[interval].Area:F2}, numGeometries={original[interval].NumGeometries}");
            }

            if (boundHas)
            {
                Console.WriteLine($"  Boundary: area={boundary[interval].Area:F2}, numGeometries={boundary[interval].NumGeometries}, valid={boundary[interval].IsValid}");
            }

            if (origHas && boundHas)
            {
                double origArea = original[interval].Area;
                double boundArea = boundary[interval].Area;
                double relError = Math.Abs(origArea - boundArea) / origArea;
                Console.WriteLine($"  Relative error: {relError:P4}");
                relError.Should().BeLessThan(0.01, $"Area mismatch at interval {interval}: orig={origArea:F2}, bound={boundArea:F2}");
            }
            else
            {
                origHas.Should().Be(boundHas, $"Key presence mismatch at interval {interval}");
            }
        }
    }
}
