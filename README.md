# Contour.Core.ContourGenerator.MarchingSquares

A .NET library for generating contour lines and contour polygons from raster grids using the Marching Squares algorithm. Built on [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) geometries and the [Contour.Core](https://github.com/acrotron/Contour.Core) abstraction layer.

## How It Works

Each raster cell (2x2 group of grid nodes) is subdivided into 4 sub-triangles by connecting the corners to a bilinear-interpolated center point. Contour lines and polygons are then traced through this triangle mesh at specified elevation intervals.

```
TL -------- TR          TL -------- TR
|            |          | \   T    / |
|            |   -->    |  L  +  R   |
|            |          | /   B    \ |
BL -------- BR          BL -------- BR
```

## Installation

```shell
dotnet add package Contour.Core.ContourGenerator.MarchingSquares
```

## Usage

### From grid nodes

Build the grid from nodes with their coordinates (projected or transformed, e.g. to WGS84) and the data value as M.
To contour an ESRI ASCII raster, read it with a raster parser and pass its cells as nodes:

```csharp
using NetTopologySuite.Geometries;
using Contour.Core.ContourGenerator.MarchingSquares;

// Build a grid from pre-computed nodes [col, row] with M = elevation value
CoordinateM[,] nodes = new CoordinateM[nCols, nRows];
// ... populate nodes ...

var grid = RasterGrid.FromNodes(nodes, noDataValue: -9999);

var generator = new ContourGenerator(
    new MarchingSquaresContourLines(geometryPrecision),
    new MarchingSquaresContourPolygons(geometryPrecision));

generator.SetInput(grid);

Dictionary<double, List<LineString>> lines = generator.GenerateContourLines(intervals);
Dictionary<double, MultiPolygon> polygons = generator.GenerateContourPolygons(intervals);
```

## Key Features

- **Contour lines** - traces isolines across the triangle mesh at specified elevation intervals
- **Contour polygons** - generates filled polygons for areas above each contour level, with parallel processing across intervals
- **Grid nodes as input** - any regular grid of nodes with coordinates and values; no file format dependency
- **NoData handling** - cells with missing data are excluded from the mesh
- **Progress reporting** - polygon generation supports `IProgress<OperationProgress>` for tracking long-running operations

## Dependencies

| Package | Description |
|---------|-------------|
| [Contour.Core](https://github.com/bjorn-ali-goransson/Contour.Core) | Core interfaces (`IContourGenerator`, `IContourLines`, `IContourPolygons`) |
| [NetTopologySuite](https://www.nuget.org/packages/NetTopologySuite) | Geometry types and spatial operations |

## Version 2

Version 2 removes `RasterGrid.FromRaster(EsriAsciiRaster)` and with it the dependency on AsciiRaster.Parser (and
its transitive ProjNET4GeoAPI, LGPL-2.1), so the package depends only on MIT and BSD-licensed code. Build the grid
with `RasterGrid.FromNodes` instead: node [col, row] at x = x0 + col · cellSize and y = y0 + (nRows − 1 − row) ·
cellSize, with the cell value as M, gives the same triangles.

## License

[MIT](LICENSE) - Copyright (c) 2026 Acrotron
