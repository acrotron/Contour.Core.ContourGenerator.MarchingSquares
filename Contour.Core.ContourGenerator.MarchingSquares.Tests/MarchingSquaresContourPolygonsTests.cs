using AsciiRaster.Parser;
using AwesomeAssertions;

namespace Contour.Core.ContourGenerator.MarchingSquares.Tests;

[TestClass]
public class MarchingSquaresContourPolygonsTests
{
    private readonly MarchingSquaresContourPolygons _contourPolygons = new(new Wgs84GeometryPrecision());

    private static EsriAsciiRaster CreateRaster(int nCols, int nRows, double cellSize, double[,] data)
    {
        return TestRasterHelper.CreateRaster(nCols, nRows, cellSize, data);
    }

    [TestMethod]
    public void Contours_AllAboveInterval_SinglePolygon()
    {
        // Arrange - all values well above the interval
        var data = new double[2, 2];
        data[0, 0] = 100; data[1, 0] = 100;
        data[0, 1] = 100; data[1, 1] = 100;

        var raster = CreateRaster(2, 2, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourPolygons.Contours(tris, [50.0]);

        // Assert
        result.Should().ContainKey(50.0);
        result[50.0].Area.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void Contours_AllBelowInterval_EmptyResult()
    {
        // Arrange - all values below the interval
        var data = new double[2, 2];
        data[0, 0] = 10; data[1, 0] = 10;
        data[0, 1] = 10; data[1, 1] = 10;

        var raster = CreateRaster(2, 2, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourPolygons.Contours(tris, [50.0]);

        // Assert
        result.Should().NotContainKey(50.0);
    }

    [TestMethod]
    public void Contours_PartiallyAbove_ProducesPartialPolygons()
    {
        // Arrange - gradient across a cell
        var data = new double[2, 2];
        data[0, 0] = 0;   data[1, 0] = 100;
        data[0, 1] = 0;   data[1, 1] = 100;

        var raster = CreateRaster(2, 2, 1.0, data);
        var grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Act
        var result = _contourPolygons.Contours(tris, [50.0]);

        // Assert - should have a polygon covering the above-50 portion
        result.Should().ContainKey(50.0);
        double area = result[50.0].Area;
        area.Should().BeGreaterThan(0);

        // The polygon should cover less than the full cell area
        double fullCellArea = 1.0; // 1x1 cell
        area.Should().BeLessThan(fullCellArea);
    }

    [TestMethod]
    public void Contours_MultipleIntervals_HigherIntervalSmallerArea()
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
        var result = _contourPolygons.Contours(tris, [25.0, 75.0]);

        // Assert - higher interval should produce a smaller polygon
        result.Should().ContainKey(25.0);
        result.Should().ContainKey(75.0);
        double area25 = result[25.0].Area;
        double area75 = result[75.0].Area;
        area75.Should().BeLessThan(area25);
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
        var act = () => _contourPolygons.Contours(tris, [0.0, 50.0, 100.0]);

        // Assert
        act.Should().NotThrow();
    }
}
