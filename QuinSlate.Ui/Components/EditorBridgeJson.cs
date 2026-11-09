using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Windows.UI;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The JSON plumbing of the editor bridge: building outgoing messages, reading fields off incoming
/// ones, and serialising colours as CSS. Shared by <see cref="EditorHost"/> and
/// <see cref="EditorSearchChannel"/>, so every message is read and written the same way.
/// </summary>
internal static class EditorBridgeJson
{
    private const double MaxAlpha = 255.0;

    /// <summary>Builds one message object from the fields <paramref name="writeBody"/> writes.</summary>
    public static string Build(Action<Utf8JsonWriter> writeBody)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A colour as opaque CSS <c>rgb()</c>.</summary>
    public static string ToCssRgb(Color color)
    {
        return $"rgb({color.R},{color.G},{color.B})";
    }

    /// <summary>A colour as CSS <c>rgba()</c>, keeping its alpha.</summary>
    public static string ToCssRgba(Color color)
    {
        double alpha = color.A / MaxAlpha;
        return string.Format(CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3:0.###})", color.R, color.G, color.B, alpha);
    }

    /// <summary>A string field, or empty when it is missing or not a string.</summary>
    public static string GetString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return string.Empty;
    }

    /// <summary>An integer field, or 0 when it is missing or not a number.</summary>
    public static int GetInt(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetInt32();
        }

        return 0;
    }

    /// <summary>A number field, or 0 when it is missing or not a number.</summary>
    public static double GetDouble(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }

        return 0;
    }

    /// <summary>A boolean field; false unless it is present and <c>true</c>.</summary>
    public static bool GetBool(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && (value.ValueKind == JsonValueKind.True);
    }
}
