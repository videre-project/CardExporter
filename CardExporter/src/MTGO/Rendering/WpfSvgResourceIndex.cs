/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;


namespace CardExporter.MTGO.Rendering;

internal sealed class WpfSvgResourceIndex
{
  private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

  private readonly XDocument _document;
  private readonly Dictionary<string, XElement> _keyedElements;

  private WpfSvgResourceIndex(XDocument document)
  {
    _document = document;
    _keyedElements = document
      .Descendants()
      .Select(element => (Element: element, Key: ReadKey(element)))
      .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
      .ToDictionary(
        entry => entry.Key!,
        entry => entry.Element,
        StringComparer.OrdinalIgnoreCase
      );
  }

  public static WpfSvgResourceIndex Parse(string xaml) =>
    new WpfSvgResourceIndex(XDocument.Parse(xaml, LoadOptions.PreserveWhitespace));

  public XDocument Document => _document;

  public XElement? FindKeyedElement(string key) =>
    _keyedElements.TryGetValue(key, out XElement? element) ? element : null;

  public string? GetValue(XElement element, string propertyName)
  {
    string? value = AttributeValue(element, propertyName);
    if (!string.IsNullOrWhiteSpace(value))
    {
      return value;
    }

    Dictionary<string, string> styleValues = GetStyleValues(element);
    return styleValues.TryGetValue(propertyName, out string? styleValue) ? styleValue : null;
  }

  public double GetDouble(XElement element, string propertyName, double defaultValue = 0)
  {
    string? value = GetValue(element, propertyName);
    return TryParseDouble(value, out double parsed) ? parsed : defaultValue;
  }

  public SvgPaint ResolvePaint(string? value)
  {
    if (string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, "{x:Null}", StringComparison.OrdinalIgnoreCase))
    {
      return SvgPaint.None;
    }

    string trimmed = value.Trim();
    if (TryReadStaticResource(trimmed, out string? resourceKey) &&
        FindKeyedElement(resourceKey) is XElement resource &&
        IsElement(resource, "SolidColorBrush"))
    {
      SvgPaint paint = SvgPaint.FromWpfColor(AttributeValue(resource, "Color"));
      if (TryParseDouble(AttributeValue(resource, "Opacity"), out double opacity))
      {
        paint = paint with { Opacity = paint.Opacity * opacity };
      }

      return paint;
    }

    return SvgPaint.FromWpfColor(trimmed);
  }

  public string? ResolveGeometryData(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    string trimmed = value.Trim();
    if (TryReadStaticResource(trimmed, out string? resourceKey) &&
        FindKeyedElement(resourceKey) is XElement resource)
    {
      if (IsElement(resource, "Geometry"))
      {
        return resource.Value.Trim();
      }

      if (IsElement(resource, "PathGeometry"))
      {
        return AttributeValue(resource, "Figures");
      }
    }

    return trimmed;
  }

  public Matrix ResolveTransform(string? value, Size? elementSize = null, string? origin = null)
  {
    if (string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, "{x:Null}", StringComparison.OrdinalIgnoreCase))
    {
      return Matrix.Identity;
    }

    if (TryReadStaticResource(value, out string? resourceKey) &&
        FindKeyedElement(resourceKey) is XElement resource)
    {
      return ResolveTransformElement(resource, elementSize, origin);
    }

    return Matrix.Identity;
  }

  public Matrix ResolveTransformElement(XElement element, Size? elementSize = null, string? origin = null)
  {
    Matrix transform = Matrix.Identity;
    if (IsElement(element, "TransformGroup"))
    {
      foreach (XElement child in element.Elements())
      {
        transform.Append(ResolveTransformElement(child));
      }
    }
    else if (IsElement(element, "ScaleTransform"))
    {
      double scaleX = ReadDoubleAttribute(element, "ScaleX", 1);
      double scaleY = ReadDoubleAttribute(element, "ScaleY", 1);
      double centerX = ReadDoubleAttribute(element, "CenterX", 0);
      double centerY = ReadDoubleAttribute(element, "CenterY", 0);
      transform.ScaleAt(scaleX, scaleY, centerX, centerY);
    }
    else if (IsElement(element, "RotateTransform"))
    {
      double angle = ReadDoubleAttribute(element, "Angle", 0);
      double centerX = ReadDoubleAttribute(element, "CenterX", 0);
      double centerY = ReadDoubleAttribute(element, "CenterY", 0);
      transform.RotateAt(angle, centerX, centerY);
    }
    else if (IsElement(element, "TranslateTransform"))
    {
      transform.Translate(
        ReadDoubleAttribute(element, "X", 0),
        ReadDoubleAttribute(element, "Y", 0)
      );
    }
    else if (IsElement(element, "MatrixTransform"))
    {
      string? matrixValue = AttributeValue(element, "Matrix");
      if (!string.IsNullOrWhiteSpace(matrixValue))
      {
        double[] values = matrixValue
          .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
          .ToArray();
        if (values.Length == 6)
        {
          transform = new Matrix(values[0], values[1], values[2], values[3], values[4], values[5]);
        }
      }
    }

    if (elementSize is not null &&
        !transform.IsIdentity &&
        TryParseOrigin(origin, out double originX, out double originY))
    {
      double centerX = elementSize.Value.Width * originX;
      double centerY = elementSize.Value.Height * originY;
      Matrix centeredTransform = Matrix.Identity;
      centeredTransform.Translate(-centerX, -centerY);
      centeredTransform.Append(transform);
      centeredTransform.Translate(centerX, centerY);
      return centeredTransform;
    }

    return transform;
  }

  public XElement? FindPropertyElement(XElement element, string propertyName) =>
    element.Elements().FirstOrDefault(child => IsElement(child, propertyName));

  public static string? ReadKey(XElement element) =>
    element.Attribute(XamlNamespace + "Key")?.Value ?? AttributeValue(element, "Key");

  public static bool IsElement(XElement element, string localName) =>
    string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

  public static string? AttributeValue(XElement element, string localName) =>
    element
      .Attributes()
      .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
      ?.Value;

  public static bool TryReadStaticResource(string value, out string key)
  {
    const string prefix = "{StaticResource ";
    key = string.Empty;
    string trimmed = value.Trim();
    if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
        !trimmed.EndsWith('}'))
    {
      return false;
    }

    key = trimmed[prefix.Length..^1].Trim();
    return !string.IsNullOrWhiteSpace(key);
  }

  public static bool TryParseDouble(string? value, out double parsed)
  {
    parsed = 0;
    return !string.IsNullOrWhiteSpace(value) &&
      double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
  }

  private Dictionary<string, string> GetStyleValues(XElement element)
  {
    string? styleReference = AttributeValue(element, "Style");
    if (string.IsNullOrWhiteSpace(styleReference) ||
        !TryReadStaticResource(styleReference, out string styleKey))
    {
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    return ResolveStyleValues(styleKey);
  }

  private Dictionary<string, string> ResolveStyleValues(string styleKey)
  {
    Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (FindKeyedElement(styleKey) is not XElement style ||
        !IsElement(style, "Style"))
    {
      return values;
    }

    string? basedOn = AttributeValue(style, "BasedOn");
    if (!string.IsNullOrWhiteSpace(basedOn) &&
        TryReadStaticResource(basedOn, out string parentStyleKey))
    {
      foreach (var entry in ResolveStyleValues(parentStyleKey))
      {
        values[entry.Key] = entry.Value;
      }
    }

    foreach (XElement setter in style.Elements().Where(element => IsElement(element, "Setter")))
    {
      string? property = AttributeValue(setter, "Property");
      string? value = AttributeValue(setter, "Value");
      if (!string.IsNullOrWhiteSpace(property) && value is not null)
      {
        values[property] = value;
      }
    }

    return values;
  }

  private static double ReadDoubleAttribute(XElement element, string localName, double defaultValue) =>
    TryParseDouble(AttributeValue(element, localName), out double parsed) ? parsed : defaultValue;

  private static bool TryParseOrigin(string? origin, out double x, out double y)
  {
    x = 0;
    y = 0;
    if (string.IsNullOrWhiteSpace(origin))
    {
      return false;
    }

    string[] parts = origin.Split(',', StringSplitOptions.TrimEntries);
    return parts.Length == 2 &&
      TryParseDouble(parts[0], out x) &&
      TryParseDouble(parts[1], out y);
  }
}

internal sealed record SvgPaint(string Value, double Opacity)
{
  public static SvgPaint None { get; } = new SvgPaint("none", 1);

  public static SvgPaint FromWpfColor(string? color)
  {
    if (string.IsNullOrWhiteSpace(color))
    {
      return None;
    }

    string normalized = color.Trim();
    if (string.Equals(normalized, "Transparent", StringComparison.OrdinalIgnoreCase))
    {
      return new SvgPaint("transparent", 0);
    }

    if (string.Equals(normalized, "Black", StringComparison.OrdinalIgnoreCase))
    {
      return new SvgPaint("#000000", 1);
    }

    if (string.Equals(normalized, "White", StringComparison.OrdinalIgnoreCase))
    {
      return new SvgPaint("#ffffff", 1);
    }

    if (normalized.StartsWith('#') && normalized.Length == 9)
    {
      int alpha = int.Parse(normalized.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
      string rgb = "#" + normalized.Substring(3, 6);
      return new SvgPaint(rgb, alpha / 255d);
    }

    return new SvgPaint(normalized, 1);
  }
}
