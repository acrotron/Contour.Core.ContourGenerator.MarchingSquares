using AsciiRaster.Parser;
using AwesomeAssertions;

namespace Contour.Core.ContourGenerator.MarchingSquares.Tests;

[TestClass]
public class RasterGridTests
{
    private static EsriAsciiRaster CreateRaster(int nCols, int nRows, double cellSize, double[,] data)
    {
        return TestRasterHelper.CreateRaster(nCols, nRows, cellSize, data);
    }

    [TestMethod]
    public void FromRaster_3x3Grid_Creates2x2CellGrid()
    {
        // Arrange - 3x3 grid nodes = 2x2 cells = 8 sub-triangles per cell = 16 total
        var data = new double[3, 3];
        for (int col = 0; col < 3; col++)
            for (int row = 0; row < 3; row++)
                data[col, row] = 10.0;

        var raster = CreateRaster(3, 3, 1.0, data);

        // Act
        RasterGrid grid = RasterGrid.FromRaster(raster);

        // Assert
        grid.CellCols.Should().Be(2);
        grid.CellRows.Should().Be(2);
        grid.GetAllTriangles().Should().HaveCount(16); // 4 cells * 4 sub-triangles
    }

    [TestMethod]
    public void FromRaster_CenterValue_IsBilinearAverage()
    {
        // Arrange - 2x2 grid nodes = 1 cell with known corner values
        var data = new double[2, 2];
        data[0, 0] = 10.0; // TL
        data[1, 0] = 20.0; // TR
        data[0, 1] = 30.0; // BL
        data[1, 1] = 40.0; // BR

        var raster = CreateRaster(2, 2, 1.0, data);

        // Act
        RasterGrid grid = RasterGrid.FromRaster(raster);

        // Assert - center should be average of 4 corners = 25.0
        var tris = grid.GetAllTriangles();
        tris.Should().HaveCount(4);

        // All sub-triangles share the center point (P2M = center)
        foreach (var tri in tris)
        {
            tri.P2M.M.Should().Be(25.0);
        }
    }

    [TestMethod]
    public void FromRaster_NoDataCorner_ExcludesCell()
    {
        // Arrange - 2x2 grid with one NoData value
        var data = new double[2, 2];
        data[0, 0] = 10.0;
        data[1, 0] = -9999; // NoData
        data[0, 1] = 30.0;
        data[1, 1] = 40.0;

        var raster = CreateRaster(2, 2, 1.0, data);

        // Act
        RasterGrid grid = RasterGrid.FromRaster(raster);

        // Assert - cell is excluded, no triangles
        grid.GetAllTriangles().Should().BeEmpty();
    }

    [TestMethod]
    public void FromRaster_CoordinatePositions_AreCorrect()
    {
        // Arrange - 2x2 grid at origin with cellSize=10
        var data = new double[2, 2];
        data[0, 0] = 1.0; // TL
        data[1, 0] = 2.0; // TR
        data[0, 1] = 3.0; // BL
        data[1, 1] = 4.0; // BR

        var raster = CreateRaster(2, 2, 10.0, data);

        // Act
        RasterGrid grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Assert - Triangle 0 (top): TL → TR → C
        var t0 = tris[0];
        t0.P0M.X.Should().Be(0.0);  // TL X
        t0.P0M.Y.Should().Be(10.0); // TL Y (top)
        t0.P1M.X.Should().Be(10.0); // TR X
        t0.P1M.Y.Should().Be(10.0); // TR Y (top)
        t0.P2M.X.Should().Be(5.0);  // Center X
        t0.P2M.Y.Should().Be(5.0);  // Center Y
    }

    [TestMethod]
    public void FromRaster_SubTriangleAdjacency_WithinCell()
    {
        // Arrange - single cell
        var data = new double[2, 2];
        for (int c = 0; c < 2; c++)
            for (int r = 0; r < 2; r++)
                data[c, r] = 10.0;

        var raster = CreateRaster(2, 2, 1.0, data);

        // Act
        RasterGrid grid = RasterGrid.FromRaster(raster);
        var tris = grid.GetAllTriangles();

        // Assert - each triangle should have exactly 2 within-cell neighbors
        // (no cross-cell neighbors for a single cell)
        foreach (var tri in tris)
        {
            int adjacentCount = tri.GetAdjacentTriangles().Count();
            adjacentCount.Should().Be(2, "each sub-triangle in a single cell has 2 within-cell neighbors");
        }
    }

    [TestMethod]
    public void FromRaster_CrossCellAdjacency_IsEstablished()
    {
        // Arrange - 3x2 grid (2 cells side by side)
        var data = new double[3, 2];
        for (int c = 0; c < 3; c++)
            for (int r = 0; r < 2; r++)
                data[c, r] = 10.0;

        var raster = CreateRaster(3, 2, 1.0, data);

        // Act
        RasterGrid grid = RasterGrid.FromRaster(raster);

        // Assert - right triangle of left cell and left triangle of right cell should be adjacent
        var rightTriOfLeftCell = grid.SubTriangles[0, 0, 1]; // right sub-tri of cell (0,0)
        var leftTriOfRightCell = grid.SubTriangles[1, 0, 3]; // left sub-tri of cell (1,0)

        rightTriOfLeftCell.Should().NotBeNull();
        leftTriOfRightCell.Should().NotBeNull();

        // They share an edge, so they should be adjacent
        rightTriOfLeftCell!.GetAdjacentTriangles()
            .Should().Contain(leftTriOfRightCell);
    }

    [TestMethod]
    public void FromRaster_LargerGrid_CorrectTriangleCount()
    {
        // Arrange - 5x4 grid = 4*3 = 12 cells = 48 sub-triangles
        var data = new double[5, 4];
        for (int c = 0; c < 5; c++)
            for (int r = 0; r < 4; r++)
                data[c, r] = c + r;

        var raster = CreateRaster(5, 4, 1.0, data);

        // Act
        RasterGrid grid = RasterGrid.FromRaster(raster);

        // Assert
        grid.CellCols.Should().Be(4);
        grid.CellRows.Should().Be(3);
        grid.GetAllTriangles().Should().HaveCount(48);
    }

    // --- FromNodes tests (WPF app code path) ---

    private static NetTopologySuite.Geometries.CoordinateM[,] CreateGradientNodes(
        int nCols, int nRows, double cellSize)
    {
        var nodes = new NetTopologySuite.Geometries.CoordinateM[nCols, nRows];
        for (int col = 0; col < nCols; col++)
            for (int row = 0; row < nRows; row++)
                nodes[col, row] = new NetTopologySuite.Geometries.CoordinateM(
                    col * cellSize, row * cellSize, col * 25.0);
        return nodes;
    }

    [TestMethod]
    public void FromNodes_3x3Grid_Creates2x2CellGrid()
    {
        // Arrange
        var nodes = new NetTopologySuite.Geometries.CoordinateM[3, 3];
        for (int col = 0; col < 3; col++)
            for (int row = 0; row < 3; row++)
                nodes[col, row] = new NetTopologySuite.Geometries.CoordinateM(col, row, 10.0);

        // Act
        RasterGrid grid = RasterGrid.FromNodes(nodes, -9999);

        // Assert
        grid.CellCols.Should().Be(2);
        grid.CellRows.Should().Be(2);
        grid.GetAllTriangles().Should().HaveCount(16);
    }

    [TestMethod]
    public void FromNodes_CenterValue_IsBilinearAverage()
    {
        // Arrange
        var nodes = new NetTopologySuite.Geometries.CoordinateM[2, 2];
        nodes[0, 0] = new NetTopologySuite.Geometries.CoordinateM(0, 0, 10.0);
        nodes[1, 0] = new NetTopologySuite.Geometries.CoordinateM(1, 0, 20.0);
        nodes[0, 1] = new NetTopologySuite.Geometries.CoordinateM(0, 1, 30.0);
        nodes[1, 1] = new NetTopologySuite.Geometries.CoordinateM(1, 1, 40.0);

        // Act
        RasterGrid grid = RasterGrid.FromNodes(nodes, -9999);
        var tris = grid.GetAllTriangles();

        // Assert
        tris.Should().HaveCount(4);
        foreach (var tri in tris)
        {
            tri.P2M.M.Should().Be(25.0);
        }
    }

    [TestMethod]
    public void FromNodes_NoDataCorner_ExcludesCell()
    {
        // Arrange
        var nodes = new NetTopologySuite.Geometries.CoordinateM[2, 2];
        nodes[0, 0] = new NetTopologySuite.Geometries.CoordinateM(0, 0, 10.0);
        nodes[1, 0] = new NetTopologySuite.Geometries.CoordinateM(1, 0, -9999);
        nodes[0, 1] = new NetTopologySuite.Geometries.CoordinateM(0, 1, 30.0);
        nodes[1, 1] = new NetTopologySuite.Geometries.CoordinateM(1, 1, 40.0);

        // Act
        RasterGrid grid = RasterGrid.FromNodes(nodes, -9999);

        // Assert
        grid.GetAllTriangles().Should().BeEmpty();
    }

    [TestMethod]
    public void FromNodes_SubTriangleAdjacency_WithinCell()
    {
        // Arrange
        var nodes = new NetTopologySuite.Geometries.CoordinateM[2, 2];
        nodes[0, 0] = new NetTopologySuite.Geometries.CoordinateM(0, 0, 10.0);
        nodes[1, 0] = new NetTopologySuite.Geometries.CoordinateM(1, 0, 20.0);
        nodes[0, 1] = new NetTopologySuite.Geometries.CoordinateM(0, 1, 30.0);
        nodes[1, 1] = new NetTopologySuite.Geometries.CoordinateM(1, 1, 40.0);

        // Act
        RasterGrid grid = RasterGrid.FromNodes(nodes, -9999);
        var tris = grid.GetAllTriangles();

        // Assert
        foreach (var tri in tris)
        {
            int adjacentCount = tri.GetAdjacentTriangles().Count();
            adjacentCount.Should().Be(2, "each sub-triangle in a single cell has 2 within-cell neighbors");
        }
    }

    [TestMethod]
    public void FromNodes_CrossCellAdjacency_IsEstablished()
    {
        // Arrange - 3x2 nodes (2 cells side by side)
        var nodes = new NetTopologySuite.Geometries.CoordinateM[3, 2];
        for (int col = 0; col < 3; col++)
            for (int row = 0; row < 2; row++)
                nodes[col, row] = new NetTopologySuite.Geometries.CoordinateM(col, row, 10.0);

        // Act
        RasterGrid grid = RasterGrid.FromNodes(nodes, -9999);

        // Assert
        var rightTriOfLeftCell = grid.SubTriangles[0, 0, 1];
        var leftTriOfRightCell = grid.SubTriangles[1, 0, 3];

        rightTriOfLeftCell.Should().NotBeNull();
        leftTriOfRightCell.Should().NotBeNull();

        rightTriOfLeftCell!.GetAdjacentTriangles()
            .Should().Contain(leftTriOfRightCell);
    }
}
