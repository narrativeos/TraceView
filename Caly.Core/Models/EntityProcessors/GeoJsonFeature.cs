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

namespace Caly.Core.Models.EntityProcessors;

/// <summary>
/// Parsed feature from locations.geojson for use in entity enrichment.
/// Provides geocoded location data without re-calling Nominatim.
/// </summary>
public class GeoJsonFeature
{
    /// <summary>Original location name (from semantic entity).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>OSM display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Geocoding status: "success", "failed", or "not_processed".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Latitude (only for successful geocoding).</summary>
    public double? Latitude { get; set; }

    /// <summary>Longitude (only for successful geocoding).</summary>
    public double? Longitude { get; set; }

    /// <summary>OSM type: "node", "way", "relation".</summary>
    public string OsmType { get; set; } = string.Empty;

    /// <summary>OSM ID.</summary>
    public long OsmId { get; set; }

    /// <summary>OSM category.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>OSM type (e.g., "administrative", "city").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>OSM addresstype.</summary>
    public string Addresstype { get; set; } = string.Empty;

    /// <summary>OSM place rank.</summary>
    public int PlaceRank { get; set; }

    /// <summary>OSM importance score.</summary>
    public double Importance { get; set; }

    /// <summary>Geocoding confidence (0.0–1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Match type (e.g., "exact", "fuzzy", "homophone").</summary>
    public string MatchType { get; set; } = string.Empty;

    /// <summary>The query text that actually hit Nominatim.</summary>
    public string QueryText { get; set; } = string.Empty;

    /// <summary>Bounding box: [minLat, maxLat, minLon, maxLon].</summary>
    public double[]? Bbox { get; set; }
}
