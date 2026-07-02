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

using Avalonia;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caly.Core.Services;

/// <summary>
/// JSON converter for Avalonia Rect that supports both:
/// 1. Array format: [x1, y1, x2, y2] (used by MinerU/Popo APIs)
/// 2. Object format: {"X":..., "Y":..., "Width":..., "Height":...} (default System.Text.Json for Rect)
/// </summary>
public sealed class RectJsonConverter : JsonConverter<Rect>
{
    public override Rect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.StartArray => ReadFromArray(ref reader),
            JsonTokenType.StartObject => ReadFromObject(ref reader),
            _ => throw new JsonException($"Cannot deserialize Rect from token type: {reader.TokenType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, Rect value, JsonSerializerOptions options)
    {
        // Serialize as object format for round-trip compatibility with Avalonia.Rect
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteNumber("Width", value.Width);
        writer.WriteNumber("Height", value.Height);
        writer.WriteEndObject();
    }

    private static Rect ReadFromArray(ref Utf8JsonReader reader)
    {
        var coords = new double[4];
        int i = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray && i < 4)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                coords[i++] = reader.GetDouble();
            }
        }

        if (i < 4)
            throw new JsonException($"Rect array must contain at least 4 numbers, got {i}");

        // bbox format: [x1, y1, x2, y2] → Rect(x1, y1, x2-x1, y2-y1)
        return new Rect(coords[0], coords[1], coords[2] - coords[0], coords[3] - coords[1]);
    }

    private static Rect ReadFromObject(ref Utf8JsonReader reader)
    {
        double x = 0, y = 0, width = 0, height = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propName = reader.GetString();
                reader.Read();

                switch (propName)
                {
                    case "X" or "x":
                        x = reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : 0;
                        break;
                    case "Y" or "y":
                        y = reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : 0;
                        break;
                    case "Width" or "width":
                        width = reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : 0;
                        break;
                    case "Height" or "height":
                        height = reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : 0;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }

        return new Rect(x, y, width, height);
    }
}
