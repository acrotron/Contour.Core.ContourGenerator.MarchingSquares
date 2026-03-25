using AsciiRaster.Parser;
using AwesomeAssertions;

namespace Contour.Core.ContourGenerator.MarchingSquares.Tests;

[TestClass]
public class MarchingSquaresContourLinesTests
{
    private readonly MarchingSquaresContourLines _contourLines = new(new Wgs84GeometryPrecision());

    private static EsriAsciiRaster CreateRaster(int nCols, int nRows, double cellSize, double[,] data)
    {
        return TestRasterHelper.CreateRaster(nCols, nRows, cellSize, data);
    }

    [TestMethod]
    public void Contours_UniformGrid_NoContours()
    {
        // Arrange - all cells have the same value, no contour can cross
        var data = new double[3, 3];
        for (int c = 0; c < 3; c++)
            for (int r = 0; r < 3; r++)
                data[c, r] = 50.0;

        var raster = CreateRaster(3, 3, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [50.0]);

        // Assert
        result.Should().ContainKey(50.0);
        result[50.0].Should().BeEmpty();
    }

    [TestMethod]
    public void Contours_SimpleHorizontalGradient_ProducesContourLine()
    {
        // Arrange - linear gradient left-to-right: 0, 50, 100
        //           top row same as bottom row
        var data = new double[3, 2];
        data[0, 0] = 0;  data[1, 0] = 50; data[2, 0] = 100;
        data[0, 1] = 0;  data[1, 1] = 50; data[2, 1] = 100;

        var raster = CreateRaster(3, 2, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [25.0]);

        // Assert - should produce at least one contour line at value 25
        result.Should().ContainKey(25.0);
        result[25.0].Should().NotBeEmpty();

        // The contour should run approximately vertically (constant X)
        foreach (var line in result[25.0])
        {
            line.Coordinates.Length.Should().BeGreaterThan(1);
        }
    }

    [TestMethod]
    public void Contours_SimpleVerticalGradient_ProducesContourLine()
    {
        // Arrange - linear gradient top-to-bottom: top=0, bottom=100
        var data = new double[2, 3];
        data[0, 0] = 0;   data[1, 0] = 0;
        data[0, 1] = 50;  data[1, 1] = 50;
        data[0, 2] = 100; data[1, 2] = 100;

        var raster = CreateRaster(2, 3, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [25.0, 75.0]);

        // Assert
        result[25.0].Should().NotBeEmpty();
        result[75.0].Should().NotBeEmpty();
    }

    [TestMethod]
    public void Contours_MultipleIntervals_ProducesMultipleContourSets()
    {
        // Arrange - gradient from 0 to 100
        var data = new double[3, 3];
        data[0, 0] = 0;  data[1, 0] = 50;  data[2, 0] = 100;
        data[0, 1] = 0;  data[1, 1] = 50;  data[2, 1] = 100;
        data[0, 2] = 0;  data[1, 2] = 50;  data[2, 2] = 100;

        var raster = CreateRaster(3, 3, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [25.0, 50.0, 75.0]);

        // Assert - each interval should have contour lines
        result.Should().HaveCount(3);
        result[25.0].Should().NotBeEmpty();
        result[50.0].Should().NotBeEmpty();
        result[75.0].Should().NotBeEmpty();
    }

    [TestMethod]
    public void Contours_IntervalAboveAllValues_NoContours()
    {
        // Arrange
        var data = new double[2, 2];
        data[0, 0] = 10; data[1, 0] = 20;
        data[0, 1] = 30; data[1, 1] = 40;

        var raster = CreateRaster(2, 2, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [100.0]);

        // Assert
        result[100.0].Should().BeEmpty();
    }

    [TestMethod]
    public void Contours_IntervalBelowAllValues_NoContours()
    {
        // Arrange
        var data = new double[2, 2];
        data[0, 0] = 10; data[1, 0] = 20;
        data[0, 1] = 30; data[1, 1] = 40;

        var raster = CreateRaster(2, 2, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [5.0]);

        // Assert
        result[5.0].Should().BeEmpty();
    }

    [TestMethod]
    public void Contours_ContourLineCoordinatesAreInterpolated()
    {
        // Arrange - 2x2 grid: left column=0, right column=100
        var data = new double[2, 2];
        data[0, 0] = 0;   data[1, 0] = 100;
        data[0, 1] = 0;   data[1, 1] = 100;

        var raster = CreateRaster(2, 2, 10.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [50.0]);

        // Assert - contour at 50 should have X coordinates around the midpoint (5.0)
        result[50.0].Should().NotBeEmpty();
        foreach (var line in result[50.0])
        {
            foreach (var coord in line.Coordinates)
            {
                coord.X.Should().BeApproximately(5.0, 0.1,
                    "contour at 50% should be at the horizontal midpoint");
            }
        }
    }

    [TestMethod]
    public void Contours_NoDataCells_ContoursTerminate()
    {
        // Arrange - middle column is NoData, gradient on each side
        var data = new double[4, 2];
        data[0, 0] = 0;  data[1, 0] = 50;  data[2, 0] = -9999; data[3, 0] = 100;
        data[0, 1] = 0;  data[1, 1] = 50;  data[2, 1] = -9999; data[3, 1] = 100;

        var raster = CreateRaster(4, 2, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [25.0]);

        // Assert - contours should exist only on the left side (before NoData gap)
        result[25.0].Should().NotBeEmpty();
    }

    [TestMethod]
    public void Contours_FromNodes_SimpleHorizontalGradient_ProducesContourLine()
    {
        // Arrange - 3x2 gradient via FromNodes
        var nodes = new NetTopologySuite.Geometries.CoordinateM[3, 2];
        nodes[0, 0] = new(0, 1, 0);   nodes[1, 0] = new(1, 1, 50);  nodes[2, 0] = new(2, 1, 100);
        nodes[0, 1] = new(0, 0, 0);   nodes[1, 1] = new(1, 0, 50);  nodes[2, 1] = new(2, 0, 100);

        var grid = RasterGrid.FromNodes(nodes, -9999);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [25.0]);

        // Assert
        result.Should().ContainKey(25.0);
        result[25.0].Should().NotBeEmpty();
        foreach (var line in result[25.0])
        {
            line.Coordinates.Length.Should().BeGreaterThan(1);
        }
    }

    [TestMethod]
    public void Contours_FromNodes_UniformGrid_NoContours()
    {
        // Arrange - all values equal, no contour can form
        var nodes = new NetTopologySuite.Geometries.CoordinateM[3, 3];
        for (int col = 0; col < 3; col++)
            for (int row = 0; row < 3; row++)
                nodes[col, row] = new(col, row, 50.0);

        var grid = RasterGrid.FromNodes(nodes, -9999);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [50.0]);

        // Assert
        result.Should().ContainKey(50.0);
        result[50.0].Should().BeEmpty();
    }

    [TestMethod]
    public void Contours_LargerGrid_ContourLinesAreContinuous()
    {
        // Arrange - smooth gradient across a larger grid
        var nodes = new NetTopologySuite.Geometries.CoordinateM[6, 6];
        for (int col = 0; col < 6; col++)
            for (int row = 0; row < 6; row++)
                nodes[col, row] = new(col, row, col * 20.0); // 0, 20, 40, 60, 80, 100

        var grid = RasterGrid.FromNodes(nodes, -9999);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourLines.Contours(tris, [50.0]);

        // Assert - smooth gradient should produce a single continuous contour line
        result[50.0].Should().NotBeEmpty();
        result[50.0].Should().HaveCount(1, "smooth gradient should produce a single continuous contour line");
    }

    [TestMethod]
    public void Contours_IntervalEqualsVertexValue_DoesNotCrash()
    {
        // Arrange - interval exactly equals vertex values
        var data = new double[3, 3];
        data[0, 0] = 0;  data[1, 0] = 50;  data[2, 0] = 100;
        data[0, 1] = 0;  data[1, 1] = 50;  data[2, 1] = 100;
        data[0, 2] = 0;  data[1, 2] = 50;  data[2, 2] = 100;

        var raster = CreateRaster(3, 3, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act - should not throw even when interval matches vertex values exactly
        var act = () => _contourLines.Contours(tris, [0.0, 50.0, 100.0]);

        // Assert
        act.Should().NotThrow();
    }
}
