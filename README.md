# Contour.Core.ContourGenerator.MarchingSquares

A .NET library for generating contour lines and contour polygons from raster grids using the Marching Squares algorithm. Built on [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) geometries and the [Contour.Core](https://github.com/bjorn-ali-goransson/Contour.Core) abstraction layer.

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

### From an ESRI ASCII Raster file

```csharp
using AsciiRaster.Parser;
using Contour.Core.ContourGenerator.MarchingSquares;

// Parse the raster file
var raster = EsriAsciiRaster.Read("elevation.asc");

// Build the triangle mesh
var grid = RasterGrid.FromRaster(raster);

// Create the generator
var generator = new ContourGenerator(
    new MarchingSquaresContourLines(geometryPrecision),
    new MarchingSquaresContourPolygons(geometryPrecision));

generator.SetInput(grid);

// Generate contour lines at 10m intervals
double[] intervals = [10, 20, 30, 40, 50];
Dictionary<double, List<LineString>> contourLines = generator.GenerateContourLines(intervals);

// Generate contour polygons (with optional progress reporting)
Dictionary<double, MultiPolygon> contourPolygons = generator.GenerateContourPolygons(intervals);
```

### From pre-transformed coordinates

When grid coordinates have already been projected or transformed (e.g., to WGS84):

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
- **Dual input paths** - accepts ESRI ASCII raster files or pre-transformed coordinate arrays
- **NoData handling** - cells with missing data are excluded from the mesh
- **Progress reporting** - polygon generation supports `IProgress<OperationProgress>` for tracking long-running operations

## Dependencies

| Package | Description |
|---------|-------------|
| [Contour.Core](https://github.com/bjorn-ali-goransson/Contour.Core) | Core interfaces (`IContourGenerator`, `IContourLines`, `IContourPolygons`) |
| [AsciiRaster.Parser](https://www.nuget.org/packages/AsciiRaster.Parser) | ESRI ASCII raster file parsing |
| [NetTopologySuite](https://www.nuget.org/packages/NetTopologySuite) | Geometry types and spatial operations |

## License

[MIT](LICENSE) - Copyright (c) 2026 Acrotron
