using AsciiRaster.Parser;
using AwesomeAssertions;

namespace Contour.Core.ContourGenerator.MarchingSquares.Tests;

[TestClass]
public class ContourGeneratorTests
{
    private static readonly Wgs84GeometryPrecision Precision = new();

    private readonly ContourGenerator _generator = new(
        new MarchingSquaresContourLines(Precision),
        new MarchingSquaresContourPolygons(Precision));

    private static EsriAsciiRaster CreateGradientRaster()
    {
        // 5x5 grid with gradient from 0 to 100
        var data = new double[5, 5];
        for (int col = 0; col < 5; col++)
            for (int row = 0; row < 5; row++)
                data[col, row] = col * 25.0; // 0, 25, 50, 75, 100

        return TestRasterHelper.CreateRaster(5, 5, 1.0, data);
    }

    [TestMethod]
    public void GenerateContourLines_WithoutSetInput_Throws()
    {
        // Act & Assert
        var act = () => _generator.GenerateContourLines([50.0]);
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void GenerateContourPolygons_WithoutSetInput_Throws()
    {
        // Act & Assert
        var act = () => _generator.GenerateContourPolygons([50.0]);
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void GenerateContourLines_GradientRaster_ProducesLines()
    {
        // Arrange
        _generator.SetInput(RasterGrid.FromRaster(CreateGradientRaster()));

        // Act
        var result = _generator.GenerateContourLines([25.0, 50.0, 75.0]);

        // Assert
        result.Should().HaveCount(3);
        result[50.0].Should().NotBeEmpty();
    }

    [TestMethod]
    public void GenerateContourPolygons_GradientRaster_ProducesPolygons()
    {
        // Arrange
        _generator.SetInput(RasterGrid.FromRaster(CreateGradientRaster()));

        // Act
        var result = _generator.GenerateContourPolygons([50.0]);

        // Assert
        result.Should().ContainKey(50.0);
        result[50.0].Area.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void GenerateContourLines_EmptyIntervals_ReturnsEmptyDictionary()
    {
        // Arrange
        _generator.SetInput(RasterGrid.FromRaster(CreateGradientRaster()));

        // Act
        var result = _generator.GenerateContourLines([]);

        // Assert
        result.Should().BeEmpty();
    }

    // --- FromNodes code path tests (matching the WPF app flow) ---

    [TestMethod]
    public void GenerateContourLines_FromNodes_GradientGrid_ProducesLines()
    {
        // Arrange - 5x5 gradient grid via FromNodes (the WPF app path)
        var nodes = new NetTopologySuite.Geometries.CoordinateM[5, 5];
        for (int col = 0; col < 5; col++)
            for (int row = 0; row < 5; row++)
                nodes[col, row] = new NetTopologySuite.Geometries.CoordinateM(
                    col * 1.0, row * 1.0, col * 25.0); // gradient 0..100

        var grid = RasterGrid.FromNodes(nodes, -9999);
        _generator.SetInput(grid);

        // Act
        var result = _generator.GenerateContourLines([25.0, 50.0, 75.0]);

        // Assert
        result.Should().HaveCount(3);
        result[25.0].Should().NotBeEmpty("FromNodes should produce contour lines at 25");
        result[50.0].Should().NotBeEmpty("FromNodes should produce contour lines at 50");
        result[75.0].Should().NotBeEmpty("FromNodes should produce contour lines at 75");

        // Verify contour lines have valid coordinates (not NaN)
        foreach (var interval in result.Keys)
        {
            foreach (var line in result[interval])
            {
                line.Coordinates.Length.Should().BeGreaterThan(1);
                foreach (var coord in line.Coordinates)
                {
                    double.IsNaN(coord.X).Should().BeFalse($"X should not be NaN at interval {interval}");
                    double.IsNaN(coord.Y).Should().BeFalse($"Y should not be NaN at interval {interval}");
                }
            }
        }
    }

    [TestMethod]
    public void GenerateContourPolygons_FromNodes_GradientGrid_ProducesPolygons()
    {
        // Arrange
        var nodes = new NetTopologySuite.Geometries.CoordinateM[5, 5];
        for (int col = 0; col < 5; col++)
            for (int row = 0; row < 5; row++)
                nodes[col, row] = new NetTopologySuite.Geometries.CoordinateM(
                    col * 1.0, row * 1.0, col * 25.0);

        var grid = RasterGrid.FromNodes(nodes, -9999);
        _generator.SetInput(grid);

        // Act
        var result = _generator.GenerateContourPolygons([50.0]);

        // Assert
        result.Should().ContainKey(50.0);
        result[50.0].Area.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void GenerateContourLines_FromNodes_MatchesFromRaster()
    {
        // Arrange - same data via both paths
        double[,] data = new double[5, 5];
        for (int col = 0; col < 5; col++)
            for (int row = 0; row < 5; row++)
                data[col, row] = col * 25.0;

        // Path 1: FromRaster
        var raster = TestRasterHelper.CreateRaster(5, 5, 1.0, data);
        var generatorA = new ContourGenerator(
            new MarchingSquaresContourLines(Precision),
            new MarchingSquaresContourPolygons(Precision));
        generatorA.SetInput(RasterGrid.FromRaster(raster));
        var resultA = generatorA.GenerateContourLines([50.0]);

        // Path 2: FromNodes (using same raw coordinates as FromRaster would compute)
        var nodes = new NetTopologySuite.Geometries.CoordinateM[5, 5];
        for (int col = 0; col < 5; col++)
            for (int row = 0; row < 5; row++)
            {
                // FromRaster uses: xOrigin + col*cellSize, yOrigin + (cellRows - row)*cellSize
                double x = 0 + col * 1.0;
                double y = 0 + (4 - row) * 1.0; // Y-flip like FromRaster
                nodes[col, row] = new NetTopologySuite.Geometries.CoordinateM(x, y, data[col, row]);
            }

        var grid = RasterGrid.FromNodes(nodes, -9999);
        var generatorB = new ContourGenerator(
            new MarchingSquaresContourLines(Precision),
            new MarchingSquaresContourPolygons(Precision));
        generatorB.SetInput(grid);
        var resultB = generatorB.GenerateContourLines([50.0]);

        // Assert - both should produce non-empty results
        resultA[50.0].Should().NotBeEmpty("FromRaster path should produce lines");
        resultB[50.0].Should().NotBeEmpty("FromNodes path should produce lines");

        // Both paths should produce the same number of contour line segments
        resultA[50.0].Count.Should().Be(resultB[50.0].Count,
            "FromNodes and FromRaster should produce the same number of contour lines");
    }
}
