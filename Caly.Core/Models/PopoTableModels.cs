// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caly.Core.Models;

/// <summary>
/// Represents a column header of a table_data node.
/// The newer Popo format emits headers as objects:
/// { "index": 0, "name": "...", "group": "...", "unit": "..." };
/// older formats emit plain strings (only Name is populated).
/// Group is the parent category of a multi-tier header (e.g. "绝缘电阻(GΩ)"
/// over the 15s/60s/10min sub-columns); Unit is the measurement unit.
/// </summary>
public sealed class TableHeader
{
    /// <summary>
    /// Zero-based column index within the source table.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Column name (also the key used to look up cells in a row).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent group of a multi-tier header (null when the header is flat).
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Measurement unit (e.g. "Ω", "%", "kVA"); null when not applicable.
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// Flat display name for single-row UI headers:
    /// "group·name" when a group is present and not already part of the name,
    /// with " (unit)" appended when a unit is present and not already part of the text.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var text = Name;
            if (!string.IsNullOrEmpty(Group) &&
                !text.Contains(Group, StringComparison.Ordinal))
            {
                text = Group + "·" + text;
            }
            if (!string.IsNullOrEmpty(Unit) &&
                !text.Contains(Unit, StringComparison.Ordinal))
            {
                text += " (" + Unit + ")";
            }
            return text;
        }
    }
}

/// <summary>
/// JSON converter for <see cref="TableHeader"/> that accepts both the newer
/// object form ({ index, name, group, unit }) and the legacy plain-string form
/// (so previously cached popo.json files keep loading).
/// </summary>
public sealed class TableHeaderJsonConverter : JsonConverter<TableHeader>
{
    public override TableHeader? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString();
            return string.IsNullOrEmpty(name) ? null : new TableHeader { Name = name };
        }

        // Object form — reader is positioned at StartObject.
        var header = new TableHeader();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    var property = reader.GetString();
                    reader.Read();
                    switch (property)
                    {
                        case "index" when reader.TokenType == JsonTokenType.Number:
                            header.Index = reader.GetInt32();
                            break;
                        case "name":
                            header.Name = reader.GetString() ?? string.Empty;
                            break;
                        case "group":
                            header.Group = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            break;
                        case "unit":
                            header.Unit = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                    break;

                case JsonTokenType.EndObject:
                    return header;
            }
        }
        return header;
    }

    public override void Write(Utf8JsonWriter writer, TableHeader value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", value.Index);
        writer.WriteString("name", value.Name);
        writer.WriteString("group", value.Group);
        writer.WriteString("unit", value.Unit);
        writer.WriteEndObject();
    }
}

/// <summary>
/// Represents a single cell value in a structured Popo table
/// (a cell in a table_data row, or the value of a table_key_value pair).
/// Raw is the display string; Value is the typed value (string, number, or null);
/// Unit is an optional unit extracted from the header/key (e.g., "Ω", "%", "kVA").
/// </summary>
public sealed class TableCellValue
{
    /// <summary>
    /// Raw display string of the cell.
    /// </summary>
    public string Raw { get; set; } = string.Empty;

    /// <summary>
    /// Typed value as returned by the Popo API (string, number, or null).
    /// Use <see cref="Raw"/> for display to avoid type-conversion issues.
    /// Note: this JsonElement references the JsonDocument the tree was parsed
    /// from; prefer <see cref="IsNumeric"/> for type checks at render time.
    /// </summary>
    public JsonElement Value { get; set; }

    /// <summary>
    /// Whether the typed value is a JSON number. Captured at parse time so
    /// render-time code never has to touch <see cref="Value"/> (whose backing
    /// JsonDocument may no longer be available by then).
    /// </summary>
    public bool IsNumeric { get; set; }

    /// <summary>
    /// Optional unit (e.g., "Ω", "%", "kVA", "Hz").
    /// </summary>
    public string? Unit { get; set; }
}

/// <summary>
/// Represents a single data row of a table_data node.
/// Cells maps column header text to the cell value.
/// </summary>
public sealed class TableDataRow
{
    /// <summary>
    /// Zero-based row index within the source table.
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// Column header → cell value mapping.
    /// </summary>
    public Dictionary<string, TableCellValue> Cells { get; set; } = new();

    /// <summary>
    /// PDF page number where this row is located.
    /// </summary>
    public int Page { get; set; }
}

/// <summary>
/// Represents a single key-value pair of a table_key_value node.
/// </summary>
public sealed class TableKeyValuePair
{
    /// <summary>
    /// Key (label) text.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Value of the pair.
    /// </summary>
    public TableCellValue Value { get; set; } = new();

    /// <summary>
    /// Zero-based row index within the source table.
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// PDF page number where this pair is located.
    /// </summary>
    public int Page { get; set; }
}
