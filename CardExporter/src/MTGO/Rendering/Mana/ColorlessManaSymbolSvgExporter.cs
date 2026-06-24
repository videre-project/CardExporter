/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Reflection;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Media;

using CardExporter.MTGO.Rendering;

namespace CardExporter.MTGO.Rendering.Mana;

internal sealed class ColorlessManaSymbolSvgExporter
{
  private const double ViewBoxWidth = 146;
  private const double ViewBoxHeight = 156;
  private const string CardGlyphTypeName = "Shiny.Card.Utils.CardGlyph";
  private const string ConverterTypeName = "Shiny.Card.Converters.ColorlessManaSymbolConverter";
  private const string BadgeFacePath =
    "M72.8,-0.453339 C113.12,-0.453339 145.787,32.2133 145.787,72.3467 C145.787,112.667 113.12,145.333 72.8,145.333 C32.6667,145.333 -2.54313E-06,112.667 -2.54313E-06,72.3467 C-2.54313E-06,32.2133 32.6667,-0.453339 72.8,-0.453339";

  private readonly object _converter;
  private readonly MethodInfo _convertMethod;
  private readonly MethodInfo _createCardGlyphMethod;

  public ColorlessManaSymbolSvgExporter(string cardAssemblyPath)
  {
    Assembly cardAssembly = LoadCardAssembly(cardAssemblyPath);
    InitializeWpfResourceContext(cardAssembly);
    Type converterType = cardAssembly.GetType(ConverterTypeName, throwOnError: true)!;
    Type cardGlyphType = cardAssembly.GetType(CardGlyphTypeName, throwOnError: true)!;

    _converter = Activator.CreateInstance(converterType) ??
      throw new InvalidOperationException($"{ConverterTypeName} could not be created.");
    _convertMethod = converterType.GetMethod(
      "Convert",
      new[] { typeof(object), typeof(Type), typeof(object), typeof(CultureInfo) }
    ) ?? throw new InvalidOperationException($"{ConverterTypeName}.Convert was not found.");
    _createCardGlyphMethod = cardGlyphType.GetMethod(
      "Create",
      BindingFlags.Public | BindingFlags.Static,
      binder: null,
      new[] { typeof(string), typeof(bool) },
      modifiers: null
    ) ?? throw new InvalidOperationException($"{CardGlyphTypeName}.Create was not found.");
  }

  public SvgAsset Convert(ManaSymbolResource resource)
  {
    string token = resource.NormalizedSymbol.Trim('{', '}');
    Geometry geometry = ConvertToken(token);
    string svg = BuildSvg(geometry);
    return new SvgAsset(
      resource.Slug,
      "svg",
      "image/svg+xml",
      Encoding.UTF8.GetBytes(svg),
      "mtgo-colorless-mana-converter"
    );
  }

  private Geometry ConvertToken(string token)
  {
    object cardGlyph = _createCardGlyphMethod.Invoke(
      obj: null,
      new object[] { GetCardGlyphKey(token), false }
    ) ?? throw new InvalidOperationException($"MTGO CardGlyph could not be created for {token}.");

    object result = _convertMethod.Invoke(
      _converter,
      new object?[] { cardGlyph, typeof(Geometry), null, CultureInfo.InvariantCulture }
    ) ?? throw new InvalidOperationException($"MTGO colorless mana converter returned null for {token}.");

    return result is Geometry geometry
      ? geometry
      : throw new InvalidOperationException(
        $"MTGO colorless mana converter returned {result.GetType().FullName} for {token}, not Geometry."
      );
  }

  private static string GetCardGlyphKey(string token)
  {
    if (int.TryParse(token, CultureInfo.InvariantCulture, out int value) &&
        value >= 10 &&
        value <= 20)
    {
      return ((char)('a' + value - 10)).ToString();
    }

    return token;
  }

  private static string BuildSvg(Geometry geometry)
  {
    StringBuilder svg = new StringBuilder();
    svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {ViewBoxWidth:0.########} {ViewBoxHeight:0.########}\" width=\"{ViewBoxWidth:0.########}\" height=\"{ViewBoxHeight:0.########}\">\n");
    svg.Append("  <ellipse cx=\"73\" cy=\"83\" rx=\"73\" ry=\"73\" fill=\"#000000\" />\n");
    svg.Append("  <path");
    AppendAttribute(svg, "d", BadgeFacePath);
    AppendAttribute(svg, "fill", "#cabfbb");
    svg.Append(" />\n");
    svg.Append("  <path");
    AppendAttribute(svg, "d", SvgPathDataWriter.Write(geometry));
    AppendAttribute(svg, "fill", "#000000");
    svg.Append(" />\n");
    svg.Append("</svg>\n");
    return svg.ToString();
  }

  private static Assembly LoadCardAssembly(string cardAssemblyPath)
  {
    string appDirectory = Path.GetDirectoryName(cardAssemblyPath) ??
      throw new InvalidOperationException($"Card assembly path {cardAssemblyPath} does not have a parent directory.");

    AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
    {
      string? assemblyName = new AssemblyName(args.Name).Name;
      if (string.IsNullOrWhiteSpace(assemblyName))
      {
        return null;
      }

      string adjacentAssemblyPath = Path.Combine(appDirectory, assemblyName + ".dll");
      return File.Exists(adjacentAssemblyPath)
        ? Assembly.LoadFrom(adjacentAssemblyPath)
        : null;
    };

    return Assembly.LoadFrom(cardAssemblyPath);
  }

  private static void InitializeWpfResourceContext(Assembly cardAssembly)
  {
    _ = PackUriHelper.Create(
      new Uri("application:///", UriKind.Absolute),
      new Uri("/__cardexporter_pack_uri_init", UriKind.Relative)
    );

    if (Application.ResourceAssembly is null)
    {
      Application.ResourceAssembly = cardAssembly;
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
}
