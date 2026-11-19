using System.Globalization;

namespace AnalyseTool.Services.Parameters;

/// <summary>
///     Reading and writing parameter values as text, in the document's display units.
/// </summary>
public static class ParameterValues
{
    /// <summary>Built-in, project or shared.</summary>
    public static ParameterOrigin GetParameterOrigin(this Parameter parameter)
    {
        if (parameter.IsShared)
        {
            return ParameterOrigin.Shared;
        }

        return parameter.Id.Value > -1 ? ParameterOrigin.Project : ParameterOrigin.BuiltIn;
    }

    /// <summary>The value as text: a double in the document's display unit, invariant culture; an id as its number.</summary>
    public static string GetParameterValue(this Parameter parameter)
    {
        return parameter.StorageType switch
        {
            StorageType.Double => GetDoubleParameterValue(parameter),
            StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.String => parameter.AsString() ?? string.Empty,
            StorageType.ElementId => parameter.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
    }

    /// <summary>Writes a value given as text, parsed by the storage type; a double is taken in the document's display unit.</summary>
    /// <exception cref="ArgumentException">The text cannot be converted to the parameter's storage type.</exception>
    public static void SetParameterValue(this Parameter parameter, string value)
    {
        var name = parameter.Definition.Name;
        switch (parameter.StorageType)
        {
            case StorageType.Double:
                if (!TryParseDouble(value, out var doubleValue))
                {
                    throw new ArgumentException($"Parameter '{name}': cannot convert '{value}' to Double.");
                }

                parameter.Set(GetDoubleInternalValue(parameter, doubleValue));
                break;
            case StorageType.Integer:
                if (!TryParseInteger(value, out var intValue))
                {
                    throw new ArgumentException($"Parameter '{name}': cannot convert '{value}' to Integer.");
                }

                parameter.Set(intValue);
                break;
            case StorageType.String:
                parameter.Set(value);
                break;
            case StorageType.ElementId:
                if (!TryParseElementId(value, out var elementIdValue))
                {
                    throw new ArgumentException($"Parameter '{name}': cannot convert '{value}' to ElementId.");
                }

                parameter.Set(new ElementId(elementIdValue));
                break;
        }
    }

    /// <summary>Double text: invariant culture, a comma taken as the decimal separator, whitespace ignored.</summary>
    internal static bool TryParseDouble(string value, out double result)
    {
        return double.TryParse(value.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>Integer text, read the same way on every machine: invariant culture, surrounding whitespace ignored.</summary>
    internal static bool TryParseInteger(string value, out int result)
    {
        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>ElementId text, 64-bit: ids above <see cref="int.MaxValue" /> exist since Revit 2024.</summary>
    internal static bool TryParseElementId(string value, out long result)
    {
        return long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    ///     What a parameter's value means: its spec (<c>length</c>, <c>area</c>, <c>number</c>, <c>string</c>…) and, for a
    ///     measurable spec, the document's display unit (<c>millimeters</c>…) — the unit every value here is read and written in.
    ///     Unit is null for a non-measurable spec; spec is null only when the definition carries no data type.
    /// </summary>
    public static (string? Spec, string? Unit) DescribeUnits(this Parameter parameter)
    {
        var specTypeId = parameter.Definition?.GetDataType();
        if (specTypeId is null || string.IsNullOrEmpty(specTypeId.TypeId))
        {
            return (null, null);
        }

        var spec = ShortForgeId(specTypeId.TypeId);
        if (!UnitUtils.IsMeasurableSpec(specTypeId))
        {
            return (spec, null);
        }

        var unit = parameter.Element.Document.GetUnits().GetFormatOptions(specTypeId)?.GetUnitTypeId();
        return (spec, unit is null || string.IsNullOrEmpty(unit.TypeId) ? null : ShortForgeId(unit.TypeId));
    }

    /// <summary>
    ///     <c>autodesk.spec.aec:length-2.0.0</c> → <c>length</c>; <c>autodesk.spec:spec.string-2.0.0</c> → <c>string</c>;
    ///     <c>autodesk.unit.unit:millimeters-1.0.1</c> → <c>millimeters</c>.
    /// </summary>
    internal static string ShortForgeId(string typeId)
    {
        var text = typeId;
        var colon = text.LastIndexOf(':');
        if (colon >= 0)
        {
            text = text[(colon + 1)..];
        }

        var dash = text.IndexOf('-');
        if (dash > 0)
        {
            text = text[..dash];
        }

        return text.StartsWith("spec.", StringComparison.Ordinal) ? text[5..] : text;
    }

    private static string GetDoubleParameterValue(Parameter parameter)
    {
        var value = parameter.AsDouble();
        var unitTypeId = GetProjectUnitTypeId(parameter);
        if (unitTypeId is not null)
        {
            value = UnitUtils.ConvertFromInternalUnits(value, unitTypeId);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static double GetDoubleInternalValue(Parameter parameter, double value)
    {
        var unitTypeId = GetProjectUnitTypeId(parameter);
        return unitTypeId is null ? value : UnitUtils.ConvertToInternalUnits(value, unitTypeId);
    }

    private static ForgeTypeId? GetProjectUnitTypeId(Parameter parameter)
    {
        var specTypeId = parameter.Definition.GetDataType();
        if (!UnitUtils.IsMeasurableSpec(specTypeId))
        {
            return null;
        }

        return parameter.Element.Document.GetUnits().GetFormatOptions(specTypeId)?.GetUnitTypeId();
    }
}
