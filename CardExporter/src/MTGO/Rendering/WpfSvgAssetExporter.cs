/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;

using CardExporter.MTGO.Rendering.Mana;

namespace CardExporter.MTGO.Rendering;

internal sealed class WpfSvgAssetExporter
{
  private readonly WpfSvgResourceIndex _index;
  private readonly IReadOnlyDictionary<string, byte[]> _embeddedImages;
  private int _clipPathCounter;

  public WpfSvgAssetExporter(
    string xaml,
    IReadOnlyDictionary<string, byte[]>? embeddedImages = null
  )
  {
    _index = WpfSvgResourceIndex.Parse(xaml);
    _embeddedImages = embeddedImages ?? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
  }

  public SvgAsset ConvertManaSymbol(ManaSymbolResource resource)
  {
    if (string.Equals(resource.ElementName, "GenericManaTemplate", StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException("Generic mana symbols must be converted with ColorlessManaSymbolSvgExporter.");
    }

    XElement element = _index.FindKeyedElement(resource.MTGOKey) ??
      throw new InvalidOperationException($"Mana symbol resource {resource.MTGOKey} was not found.");

    string svg = ConvertKeyedElementToSvg(element, 146, 156);
    return new SvgAsset(
      resource.Slug,
      "svg",
      "image/svg+xml",
      Encoding.UTF8.GetBytes(svg),
      "wpf-vector"
    );
  }

  public IReadOnlyList<SvgAsset> ConvertCardCounters()
  {
    List<SvgAsset> assets = new List<SvgAsset>();
    foreach (XElement collection in _index.Document.Descendants()
      .Where(element => WpfSvgResourceIndex.IsElement(element, "DrawingCollection")))
    {
      string? key = WpfSvgResourceIndex.ReadKey(collection);
      if (string.IsNullOrWhiteSpace(key) ||
          !key.EndsWith("Collection", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string counterName = key[..^"Collection".Length];
      string slug = ToKebabCase(counterName);
      Matrix transform = ReadCounterTransform(counterName);
      string svg = ConvertDrawingCollectionToSvg(collection, transform);
      bool hasEmbeddedRaster = collection
        .DescendantsAndSelf()
        .Any(element => WpfSvgResourceIndex.IsElement(element, "ImageDrawing"));
      assets.Add(new SvgAsset(
        slug,
        "svg",
        "image/svg+xml",
        Encoding.UTF8.GetBytes(svg),
        hasEmbeddedRaster ? "wpf-vector-with-embedded-raster" : "wpf-vector"
      ));
    }

    return assets
      .OrderBy(asset => asset.Slug, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private string ConvertKeyedElementToSvg(XElement element, double defaultWidth, double defaultHeight)
  {
    if (WpfSvgResourceIndex.IsElement(element, "DrawingImage"))
    {
      XElement drawing = element
        .Descendants()
        .First(descendant => WpfSvgResourceIndex.IsElement(descendant, "DrawingImage.Drawing"));
      XElement drawingElement = drawing.Elements().First();
      return ConvertDrawingElementToSvg(drawingElement, Matrix.Identity, defaultWidth, defaultHeight);
    }

    if (WpfSvgResourceIndex.IsElement(element, "DataTemplate"))
    {
      XElement? image = element
        .Descendants()
        .FirstOrDefault(descendant => WpfSvgResourceIndex.IsElement(descendant, "Image"));
      string? imageSource = image is null ? null : WpfSvgResourceIndex.AttributeValue(image, "Source");
      if (!string.IsNullOrWhiteSpace(imageSource) &&
          WpfSvgResourceIndex.TryReadStaticResource(imageSource, out string sourceKey) &&
          _index.FindKeyedElement(sourceKey) is XElement drawingImage)
      {
        return ConvertKeyedElementToSvg(drawingImage, defaultWidth, defaultHeight);
      }

      XElement canvas = element
        .Descendants()
        .First(descendant => WpfSvgResourceIndex.IsElement(descendant, "Canvas"));
      XElement? viewbox = canvas
        .Ancestors()
        .FirstOrDefault(ancestor => WpfSvgResourceIndex.IsElement(ancestor, "Viewbox"));
      double canvasWidth = _index.GetDouble(canvas, "Width", defaultWidth);
      double canvasHeight = _index.GetDouble(canvas, "Height", defaultHeight);
      Rect visualBounds = CalculateCanvasBounds(canvas);
      double viewBoxWidth = Math.Max(canvasWidth, visualBounds.IsEmpty ? canvasWidth : visualBounds.Right);
      double viewBoxHeight = Math.Max(canvasHeight, visualBounds.IsEmpty ? canvasHeight : visualBounds.Bottom);
      double width = viewbox is null ? viewBoxWidth : _index.GetDouble(viewbox, "Width", defaultWidth);
      double height = viewbox is null ? viewBoxHeight : _index.GetDouble(viewbox, "Height", defaultHeight);
      return ConvertCanvasToSvg(canvas, width, height, viewBoxWidth, viewBoxHeight);
    }

    throw new InvalidOperationException($"Unsupported keyed SVG resource element {element.Name.LocalName}.");
  }

  private string ConvertDrawingCollectionToSvg(XElement collection, Matrix transform)
  {
    Rect bounds = CalculateDrawingBounds(collection.Elements(), transform);
    if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
    {
      bounds = new Rect(0, 0, 33, 24);
    }

    Matrix normalizedTransform = transform;
    normalizedTransform.OffsetX -= bounds.X;
    normalizedTransform.OffsetY -= bounds.Y;

    StringBuilder defs = new StringBuilder();
    StringBuilder body = new StringBuilder();
    string transformAttribute = SvgPathDataWriter.Matrix(normalizedTransform);
    if (!string.IsNullOrEmpty(transformAttribute))
    {
      body.Append(CultureInfo.InvariantCulture, $"  <g transform=\"{transformAttribute}\">\n");
    }

    foreach (XElement child in collection.Elements())
    {
      AppendDrawingElement(child, body, defs);
    }

    if (!string.IsNullOrEmpty(transformAttribute))
    {
      body.Append("  </g>\n");
    }

    return WrapSvg(bounds.Width, bounds.Height, bounds.Width, bounds.Height, defs, body);
  }

  private string ConvertDrawingElementToSvg(
    XElement drawingElement,
    Matrix transform,
    double width,
    double height
  )
  {
    StringBuilder defs = new StringBuilder();
    StringBuilder body = new StringBuilder();
    string transformAttribute = SvgPathDataWriter.Matrix(transform);
    if (!string.IsNullOrEmpty(transformAttribute))
    {
      body.Append(CultureInfo.InvariantCulture, $"  <g transform=\"{transformAttribute}\">\n");
    }

    AppendDrawingElement(drawingElement, body, defs);

    if (!string.IsNullOrEmpty(transformAttribute))
    {
      body.Append("  </g>\n");
    }

    return WrapSvg(width, height, width, height, defs, body);
  }

  private string ConvertCanvasToSvg(
    XElement canvas,
    double width,
    double height,
    double viewBoxWidth,
    double viewBoxHeight
  )
  {
    StringBuilder defs = new StringBuilder();
    StringBuilder body = new StringBuilder();
    bool injectPhyrexianBackground = ShouldInjectGenericPhyrexianBackground(canvas);
    bool phyrexianBackgroundInjected = false;
    foreach (XElement child in canvas.Elements())
    {
      AppendCanvasElement(child, body, defs);
      if (injectPhyrexianBackground &&
          !phyrexianBackgroundInjected &&
          WpfSvgResourceIndex.IsElement(child, "Ellipse") &&
          string.Equals(WpfSvgResourceIndex.AttributeValue(child, "Name"), "shadow", StringComparison.OrdinalIgnoreCase))
      {
        AppendGenericPhyrexianBackground(body);
        phyrexianBackgroundInjected = true;
      }
    }

    return WrapSvg(width, height, viewBoxWidth, viewBoxHeight, defs, body);
  }

  private void AppendDrawingElement(XElement element, StringBuilder body, StringBuilder defs)
  {
    if (WpfSvgResourceIndex.IsElement(element, "DrawingGroup"))
    {
      Matrix groupTransform = Matrix.Identity;
      XElement? transformElement = _index.FindPropertyElement(element, "DrawingGroup.Transform")?.Elements().FirstOrDefault();
      if (transformElement is not null)
      {
        groupTransform = _index.ResolveTransformElement(transformElement);
      }

      string transformAttribute = SvgPathDataWriter.Matrix(groupTransform);
      if (!string.IsNullOrEmpty(transformAttribute))
      {
        body.Append(CultureInfo.InvariantCulture, $"    <g transform=\"{transformAttribute}\">\n");
      }

      foreach (XElement child in element.Elements().Where(child => !child.Name.LocalName.EndsWith(".Transform", StringComparison.Ordinal)))
      {
        AppendDrawingElement(child, body, defs);
      }

      if (!string.IsNullOrEmpty(transformAttribute))
      {
        body.Append("    </g>\n");
      }

      return;
    }

    if (WpfSvgResourceIndex.IsElement(element, "GeometryDrawing"))
    {
      AppendGeometryDrawing(element, body);
      return;
    }

    if (WpfSvgResourceIndex.IsElement(element, "ImageDrawing"))
    {
      AppendImageDrawing(element, body);
    }
  }

  private void AppendGeometryDrawing(XElement element, StringBuilder body)
  {
    Geometry geometry = ReadGeometryDrawingGeometry(element);
    SvgPaint fill = _index.ResolvePaint(WpfSvgResourceIndex.AttributeValue(element, "Brush"));
    (SvgPaint Stroke, double Thickness)? pen = ReadPen(element);

    body.Append("    <path");
    AppendAttribute(body, "d", SvgPathDataWriter.Write(geometry));
    AppendPaintAttributes(body, "fill", fill);

    if (geometry is PathGeometry pathGeometry && pathGeometry.FillRule == FillRule.EvenOdd)
    {
      AppendAttribute(body, "fill-rule", "evenodd");
    }

    if (pen is not null)
    {
      AppendPaintAttributes(body, "stroke", pen.Value.Stroke);
      AppendAttribute(body, "stroke-width", Format(pen.Value.Thickness));
    }

    body.Append(" />\n");
  }

  private void AppendImageDrawing(XElement element, StringBuilder body)
  {
    string? source = WpfSvgResourceIndex.AttributeValue(element, "ImageSource");
    string? key = NormalizePackUri(source);
    if (key is null ||
        !_embeddedImages.TryGetValue(key, out byte[]? bytes) ||
        bytes is null)
    {
      return;
    }

    Rect rect = Rect.Parse(WpfSvgResourceIndex.AttributeValue(element, "Rect") ?? "0,0,0,0");
    string contentType = key.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
      key.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        ? "image/jpeg"
        : "image/png";

    body.Append("    <image");
    AppendAttribute(body, "x", Format(rect.X));
    AppendAttribute(body, "y", Format(rect.Y));
    AppendAttribute(body, "width", Format(rect.Width));
    AppendAttribute(body, "height", Format(rect.Height));
    AppendAttribute(body, "href", $"data:{contentType};base64,{Convert.ToBase64String(bytes)}");
    body.Append(" />\n");
  }

  private void AppendCanvasElement(XElement element, StringBuilder body, StringBuilder defs)
  {
    if (WpfSvgResourceIndex.IsElement(element, "Ellipse"))
    {
      double left = _index.GetDouble(element, "Canvas.Left");
      double top = _index.GetDouble(element, "Canvas.Top");
      double width = _index.GetDouble(element, "Width");
      double height = _index.GetDouble(element, "Height");
      SvgPaint fill = _index.ResolvePaint(_index.GetValue(element, "Fill"));
      SvgPaint stroke = _index.ResolvePaint(_index.GetValue(element, "Stroke"));
      double strokeThickness = _index.GetDouble(element, "StrokeThickness", 1);

      body.Append("  <ellipse");
      AppendAttribute(body, "cx", Format(left + width / 2));
      AppendAttribute(body, "cy", Format(top + height / 2));
      AppendAttribute(body, "rx", Format(width / 2));
      AppendAttribute(body, "ry", Format(height / 2));
      AppendPaintAttributes(body, "fill", fill);
      AppendPaintAttributes(body, "stroke", stroke);
      if (stroke.Value != "none")
      {
        AppendAttribute(body, "stroke-width", Format(strokeThickness));
      }

      body.Append(" />\n");
      return;
    }

    if (WpfSvgResourceIndex.IsElement(element, "Path"))
    {
      AppendCanvasPath(element, body, defs);
    }
  }

  private void AppendCanvasPath(XElement element, StringBuilder body, StringBuilder defs)
  {
    string? data = _index.ResolveGeometryData(_index.GetValue(element, "Data"));
    if (string.IsNullOrWhiteSpace(data))
    {
      return;
    }

    Geometry geometry = SvgPathDataWriter.ParseGeometry(data);
    SvgPaint fill = _index.ResolvePaint(_index.GetValue(element, "Fill"));
    SvgPaint stroke = _index.ResolvePaint(_index.GetValue(element, "Stroke"));
    double strokeThickness = _index.GetDouble(element, "StrokeThickness", 1);
    CanvasPathTransforms transforms = BuildCanvasPathTransforms(element, geometry);

    string? clipPathId = BuildClipPath(element, defs);
    bool hasClipPath = !string.IsNullOrWhiteSpace(clipPathId);

    if (hasClipPath)
    {
      string placementTransform = SvgPathDataWriter.Matrix(transforms.Placement);
      if (!string.IsNullOrEmpty(placementTransform))
      {
        body.Append(CultureInfo.InvariantCulture, $"  <g transform=\"{placementTransform}\">\n");
      }
    }

    if (hasClipPath)
    {
      body.Append(CultureInfo.InvariantCulture, $"  <g clip-path=\"url(#{clipPathId})\">\n");
    }

    body.Append("  <path");
    AppendAttribute(body, "d", SvgPathDataWriter.Write(geometry));
    AppendPaintAttributes(body, "fill", fill);
    AppendPaintAttributes(body, "stroke", stroke);
    if (stroke.Value != "none")
    {
      AppendAttribute(body, "stroke-width", Format(strokeThickness));
    }

    Matrix pathTransform = hasClipPath
      ? transforms.Stretch
      : transforms.Combined;
    string pathTransformAttribute = SvgPathDataWriter.Matrix(pathTransform);
    if (!string.IsNullOrEmpty(pathTransformAttribute))
    {
      AppendAttribute(body, "transform", pathTransformAttribute);
    }

    body.Append(" />\n");

    if (hasClipPath)
    {
      body.Append("  </g>\n");
    }

    if (hasClipPath)
    {
      string placementTransform = SvgPathDataWriter.Matrix(transforms.Placement);
      if (!string.IsNullOrEmpty(placementTransform))
      {
        body.Append("  </g>\n");
      }
    }
  }

  private Matrix BuildCanvasPathTransform(XElement element, Geometry geometry)
  {
    return BuildCanvasPathTransforms(element, geometry).Combined;
  }

  private CanvasPathTransforms BuildCanvasPathTransforms(XElement element, Geometry geometry)
  {
    double left = _index.GetDouble(element, "Canvas.Left");
    double top = _index.GetDouble(element, "Canvas.Top");
    double width = _index.GetDouble(element, "Width", geometry.Bounds.Width);
    double height = _index.GetDouble(element, "Height", geometry.Bounds.Height);
    string? stretch = _index.GetValue(element, "Shape.Stretch") ?? _index.GetValue(element, "Stretch");
    Matrix stretchTransform = Matrix.Identity;

    if (string.Equals(stretch, "Fill", StringComparison.OrdinalIgnoreCase) &&
        width > 0 &&
        height > 0 &&
        geometry.Bounds.Width > 0 &&
        geometry.Bounds.Height > 0)
    {
      stretchTransform.Translate(-geometry.Bounds.X, -geometry.Bounds.Y);
      stretchTransform.Scale(width / geometry.Bounds.Width, height / geometry.Bounds.Height);
    }

    Size elementSize = new Size(width, height);
    string? renderTransform = _index.GetValue(element, "RenderTransform");
    Matrix renderTransformMatrix = _index.ResolveTransform(
      renderTransform,
      elementSize,
      _index.GetValue(element, "UIElement.RenderTransformOrigin") ??
        _index.GetValue(element, "RenderTransformOrigin")
    );

    XElement? renderTransformElement = _index
      .FindPropertyElement(element, "UIElement.RenderTransform")
      ?.Elements()
      .FirstOrDefault();
    if (renderTransformElement is not null)
    {
      renderTransformMatrix.Append(_index.ResolveTransformElement(
        renderTransformElement,
        elementSize,
        _index.GetValue(element, "UIElement.RenderTransformOrigin") ??
          _index.GetValue(element, "RenderTransformOrigin")
      ));
    }

    Matrix placementTransform = renderTransformMatrix;
    placementTransform.Translate(left, top);

    Matrix combinedTransform = stretchTransform;
    combinedTransform.Append(placementTransform);
    return new CanvasPathTransforms(stretchTransform, placementTransform, combinedTransform);
  }

  private string? BuildClipPath(XElement element, StringBuilder defs)
  {
    XElement? clipElement = _index.FindPropertyElement(element, "UIElement.Clip")?.Elements().FirstOrDefault();
    if (clipElement is null)
    {
      return null;
    }

    Geometry geometry = new XElementGeometry(clipElement).ToGeometry();
    Matrix transform = Matrix.Identity;
    string? transformValue = WpfSvgResourceIndex.AttributeValue(clipElement, "Transform");
    if (!string.IsNullOrWhiteSpace(transformValue))
    {
      transform = _index.ResolveTransform(transformValue);
    }

    string id = string.Create(CultureInfo.InvariantCulture, $"clip{++_clipPathCounter}");
    defs.Append(CultureInfo.InvariantCulture, $"  <clipPath id=\"{id}\">\n");
    defs.Append("    <path");
    AppendAttribute(defs, "d", SvgPathDataWriter.Write(geometry));
    string transformAttribute = SvgPathDataWriter.Matrix(transform);
    if (!string.IsNullOrEmpty(transformAttribute))
    {
      AppendAttribute(defs, "transform", transformAttribute);
    }

    defs.Append(" />\n");
    defs.Append("  </clipPath>\n");
    return id;
  }

  private Rect CalculateDrawingBounds(IEnumerable<XElement> elements, Matrix transform)
  {
    Rect bounds = Rect.Empty;
    foreach (XElement element in elements)
    {
      bounds = Union(bounds, CalculateDrawingElementBounds(element, transform));
    }

    return bounds;
  }

  private Rect CalculateDrawingElementBounds(XElement element, Matrix transform)
  {
    if (WpfSvgResourceIndex.IsElement(element, "DrawingGroup"))
    {
      Matrix groupTransform = Matrix.Identity;
      XElement? transformElement = _index.FindPropertyElement(element, "DrawingGroup.Transform")?.Elements().FirstOrDefault();
      if (transformElement is not null)
      {
        groupTransform = _index.ResolveTransformElement(transformElement);
      }

      Matrix childTransform = transform;
      childTransform.Append(groupTransform);
      return CalculateDrawingBounds(
        element.Elements().Where(child => !child.Name.LocalName.EndsWith(".Transform", StringComparison.Ordinal)),
        childTransform
      );
    }

    if (WpfSvgResourceIndex.IsElement(element, "GeometryDrawing"))
    {
      return TransformBounds(ReadGeometryDrawingGeometry(element).Bounds, transform);
    }

    if (WpfSvgResourceIndex.IsElement(element, "ImageDrawing"))
    {
      Rect rect = Rect.Parse(WpfSvgResourceIndex.AttributeValue(element, "Rect") ?? "0,0,0,0");
      return TransformBounds(rect, transform);
    }

    return Rect.Empty;
  }

  private Rect CalculateCanvasBounds(XElement canvas)
  {
    Rect bounds = Rect.Empty;
    foreach (XElement child in canvas.Elements())
    {
      bounds = Union(bounds, CalculateCanvasElementBounds(child));
    }

    return bounds;
  }

  private Rect CalculateCanvasElementBounds(XElement element)
  {
    if (WpfSvgResourceIndex.IsElement(element, "Ellipse"))
    {
      return new Rect(
        _index.GetDouble(element, "Canvas.Left"),
        _index.GetDouble(element, "Canvas.Top"),
        _index.GetDouble(element, "Width"),
        _index.GetDouble(element, "Height")
      );
    }

    if (WpfSvgResourceIndex.IsElement(element, "Path"))
    {
      string? data = _index.ResolveGeometryData(_index.GetValue(element, "Data"));
      if (string.IsNullOrWhiteSpace(data))
      {
        return Rect.Empty;
      }

      Geometry geometry = SvgPathDataWriter.ParseGeometry(data);
      return TransformBounds(geometry.Bounds, BuildCanvasPathTransform(element, geometry));
    }

    return Rect.Empty;
  }

  private static Rect TransformBounds(Rect bounds, Matrix transform)
  {
    if (bounds.IsEmpty)
    {
      return Rect.Empty;
    }

    bounds.Transform(transform);
    return bounds;
  }

  private static Rect Union(Rect bounds, Rect rect)
  {
    if (rect.IsEmpty)
    {
      return bounds;
    }

    if (bounds.IsEmpty)
    {
      return rect;
    }

    bounds.Union(rect);
    return bounds;
  }

  private Geometry ReadGeometryDrawingGeometry(XElement element)
  {
    string? geometryValue = WpfSvgResourceIndex.AttributeValue(element, "Geometry");
    if (!string.IsNullOrWhiteSpace(geometryValue))
    {
      string? geometryData = _index.ResolveGeometryData(geometryValue);
      if (!string.IsNullOrWhiteSpace(geometryData))
      {
        return SvgPathDataWriter.ParseGeometry(geometryData);
      }
    }

    XElement? geometryElement = _index.FindPropertyElement(element, "GeometryDrawing.Geometry")?.Elements().FirstOrDefault();
    if (geometryElement is not null)
    {
      return new XElementGeometry(geometryElement).ToGeometry();
    }

    throw new InvalidOperationException("GeometryDrawing did not contain geometry data.");
  }

  private (SvgPaint Stroke, double Thickness)? ReadPen(XElement element)
  {
    XElement? penElement = _index.FindPropertyElement(element, "GeometryDrawing.Pen")?.Elements().FirstOrDefault();
    if (penElement is null)
    {
      return null;
    }

    return (
      _index.ResolvePaint(WpfSvgResourceIndex.AttributeValue(penElement, "Brush")),
      _index.GetDouble(penElement, "Thickness", 1)
    );
  }

  private static bool ShouldInjectGenericPhyrexianBackground(XElement canvas)
  {
    string? canvasName = WpfSvgResourceIndex.AttributeValue(canvas, "Name");
    return !string.IsNullOrWhiteSpace(canvasName) &&
      canvasName.Contains("Phyrexian", StringComparison.OrdinalIgnoreCase) &&
      !canvas
        .Elements()
        .Any(element => WpfSvgResourceIndex.IsElement(element, "Ellipse") &&
          string.Equals(WpfSvgResourceIndex.AttributeValue(element, "Name"), "color", StringComparison.OrdinalIgnoreCase));
  }

  private static void AppendGenericPhyrexianBackground(StringBuilder body)
  {
    body.Append("  <ellipse");
    AppendAttribute(body, "cx", "73");
    AppendAttribute(body, "cy", "73");
    AppendAttribute(body, "rx", "73");
    AppendAttribute(body, "ry", "73");
    AppendAttribute(body, "fill", "#cabfbb");
    AppendAttribute(body, "stroke", "#000000");
    AppendAttribute(body, "stroke-width", "1");
    body.Append(" />\n");
  }

  private Matrix ReadCounterTransform(string counterName)
  {
    XElement? transformElement = _index.FindKeyedElement(counterName + "Transform");
    return transformElement is null ? Matrix.Identity : _index.ResolveTransformElement(transformElement);
  }

  private static string WrapSvg(
    double width,
    double height,
    double viewBoxWidth,
    double viewBoxHeight,
    StringBuilder defs,
    StringBuilder body
  )
  {
    StringBuilder svg = new StringBuilder();
    svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Format(viewBoxWidth)} {Format(viewBoxHeight)}\" width=\"{Format(width)}\" height=\"{Format(height)}\">\n");
    if (defs.Length > 0)
    {
      svg.Append(" <defs>\n");
      svg.Append(defs);
      svg.Append(" </defs>\n");
    }

    svg.Append(body);
    svg.Append("</svg>\n");
    return svg.ToString();
  }

  private static void AppendPaintAttributes(StringBuilder builder, string attributePrefix, SvgPaint paint)
  {
    AppendAttribute(builder, attributePrefix, paint.Value);
    if (paint.Opacity < 1)
    {
      AppendAttribute(builder, attributePrefix + "-opacity", Format(paint.Opacity));
    }
  }

  private static void AppendAttribute(StringBuilder builder, string name, string value)
  {
    builder.Append(' ');
    builder.Append(name);
    builder.Append("=\"");
    builder.Append(SecurityElement.Escape(value));
    builder.Append('"');
  }

  private static string Format(double value) =>
    value.ToString("0.########", CultureInfo.InvariantCulture);

  private static string? NormalizePackUri(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    const string componentMarker = ";component/";
    int componentIndex = value.IndexOf(componentMarker, StringComparison.OrdinalIgnoreCase);
    string key = componentIndex >= 0
      ? value[(componentIndex + componentMarker.Length)..]
      : value;

    return key.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
  }

  private static string ToKebabCase(string value)
  {
    StringBuilder builder = new StringBuilder();
    for (int i = 0; i < value.Length; i++)
    {
      char character = value[i];
      if (i > 0 && char.IsUpper(character) && !char.IsUpper(value[i - 1]))
      {
        builder.Append('-');
      }

      builder.Append(char.ToLowerInvariant(character));
    }

    return builder.ToString();
  }
}

internal sealed record CanvasPathTransforms(
  Matrix Stretch,
  Matrix Placement,
  Matrix Combined
);

internal sealed record SvgAsset(
  string Slug,
  string Extension,
  string ContentType,
  byte[] Bytes,
  string ConversionMethod
);
