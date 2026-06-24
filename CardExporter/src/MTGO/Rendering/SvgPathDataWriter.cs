/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;


namespace CardExporter.MTGO.Rendering;

internal static class SvgPathDataWriter
{
  public static string Write(Geometry geometry)
  {
    PathGeometry pathGeometry = PathGeometry.CreateFromGeometry(geometry);
    StringBuilder builder = new StringBuilder();
    foreach (PathFigure figure in pathGeometry.Figures)
    {
      Append(builder, "M");
      Append(builder, figure.StartPoint.X);
      Append(builder, figure.StartPoint.Y);

      foreach (PathSegment segment in figure.Segments)
      {
        WriteSegment(builder, segment);
      }

      if (figure.IsClosed)
      {
        Append(builder, "Z");
      }
    }

    return builder.ToString().Trim();
  }

  public static string Matrix(Matrix matrix)
  {
    if (matrix.IsIdentity)
    {
      return string.Empty;
    }

    return string.Create(
      CultureInfo.InvariantCulture,
      $"matrix({matrix.M11:0.########} {matrix.M12:0.########} {matrix.M21:0.########} {matrix.M22:0.########} {matrix.OffsetX:0.########} {matrix.OffsetY:0.########})"
    );
  }

  public static Geometry ParseGeometry(string data)
  {
    return Geometry.Parse(data.Trim());
  }

  public static Geometry ReadGeometry(XElementGeometry geometry)
  {
    return geometry.ToGeometry();
  }

  private static void WriteSegment(StringBuilder builder, PathSegment segment)
  {
    switch (segment)
    {
      case LineSegment line:
        Append(builder, "L");
        Append(builder, line.Point.X);
        Append(builder, line.Point.Y);
        break;

      case BezierSegment bezier:
        Append(builder, "C");
        Append(builder, bezier.Point1.X);
        Append(builder, bezier.Point1.Y);
        Append(builder, bezier.Point2.X);
        Append(builder, bezier.Point2.Y);
        Append(builder, bezier.Point3.X);
        Append(builder, bezier.Point3.Y);
        break;

      case QuadraticBezierSegment quadratic:
        Append(builder, "Q");
        Append(builder, quadratic.Point1.X);
        Append(builder, quadratic.Point1.Y);
        Append(builder, quadratic.Point2.X);
        Append(builder, quadratic.Point2.Y);
        break;

      case PolyLineSegment polyLine:
        foreach (Point point in polyLine.Points)
        {
          Append(builder, "L");
          Append(builder, point.X);
          Append(builder, point.Y);
        }
        break;

      case PolyBezierSegment polyBezier:
        for (int i = 0; i + 2 < polyBezier.Points.Count; i += 3)
        {
          Append(builder, "C");
          Append(builder, polyBezier.Points[i].X);
          Append(builder, polyBezier.Points[i].Y);
          Append(builder, polyBezier.Points[i + 1].X);
          Append(builder, polyBezier.Points[i + 1].Y);
          Append(builder, polyBezier.Points[i + 2].X);
          Append(builder, polyBezier.Points[i + 2].Y);
        }
        break;

      case PolyQuadraticBezierSegment polyQuadratic:
        for (int i = 0; i + 1 < polyQuadratic.Points.Count; i += 2)
        {
          Append(builder, "Q");
          Append(builder, polyQuadratic.Points[i].X);
          Append(builder, polyQuadratic.Points[i].Y);
          Append(builder, polyQuadratic.Points[i + 1].X);
          Append(builder, polyQuadratic.Points[i + 1].Y);
        }
        break;

      case ArcSegment arc:
        Append(builder, "A");
        Append(builder, arc.Size.Width);
        Append(builder, arc.Size.Height);
        Append(builder, arc.RotationAngle);
        Append(builder, arc.IsLargeArc ? 1 : 0);
        Append(builder, arc.SweepDirection == SweepDirection.Clockwise ? 1 : 0);
        Append(builder, arc.Point.X);
        Append(builder, arc.Point.Y);
        break;

      default:
        throw new InvalidOperationException($"Unsupported WPF path segment {segment.GetType().Name}.");
    }
  }

  private static void Append(StringBuilder builder, string value)
  {
    if (builder.Length > 0)
    {
      builder.Append(' ');
    }

    builder.Append(value);
  }

  private static void Append(StringBuilder builder, double value)
  {
    Append(builder, value.ToString("0.########", CultureInfo.InvariantCulture));
  }
}

internal sealed class XElementGeometry
{
  private readonly System.Xml.Linq.XElement _element;

  public XElementGeometry(System.Xml.Linq.XElement element)
  {
    _element = element;
  }

  public Geometry ToGeometry()
  {
    if (WpfSvgResourceIndex.IsElement(_element, "EllipseGeometry"))
    {
      string centerValue = WpfSvgResourceIndex.AttributeValue(_element, "Center") ?? "0,0";
      string[] parts = centerValue.Split(',', StringSplitOptions.TrimEntries);
      double x = parts.Length > 0 ? double.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
      double y = parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
      return new EllipseGeometry(
        new Point(x, y),
        ReadDouble("RadiusX", 0),
        ReadDouble("RadiusY", 0)
      );
    }

    if (WpfSvgResourceIndex.IsElement(_element, "RectangleGeometry"))
    {
      Rect rect = Rect.Parse(WpfSvgResourceIndex.AttributeValue(_element, "Rect") ?? "0,0,0,0");
      return new RectangleGeometry(
        rect,
        ReadDouble("RadiusX", 0),
        ReadDouble("RadiusY", 0)
      );
    }

    if (WpfSvgResourceIndex.IsElement(_element, "PathGeometry"))
    {
      string figures = WpfSvgResourceIndex.AttributeValue(_element, "Figures") ?? string.Empty;
      Geometry geometry = Geometry.Parse(figures);
      string? fillRule = WpfSvgResourceIndex.AttributeValue(_element, "FillRule");
      if (geometry is PathGeometry pathGeometry &&
          string.Equals(fillRule, "EvenOdd", StringComparison.OrdinalIgnoreCase))
      {
        pathGeometry.FillRule = FillRule.EvenOdd;
      }

      return geometry;
    }

    throw new InvalidOperationException($"Unsupported geometry element {_element.Name.LocalName}.");
  }

  private double ReadDouble(string attributeName, double defaultValue) =>
    WpfSvgResourceIndex.TryParseDouble(
      WpfSvgResourceIndex.AttributeValue(_element, attributeName),
      out double parsed
    )
      ? parsed
      : defaultValue;
}
