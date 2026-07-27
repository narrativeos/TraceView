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

using System.Collections.Generic;

namespace Caly.Core.Models;

/// <summary>
/// Story map configuration embedded in geolibre.json's "storymap" field (GeoLibre 0.2.0 format).
/// Represents a scroll-driven narrative experience with chapters that
/// animate the map between locations.
/// </summary>
public class GeoStoryConfig
{
    /// <summary>Story title displayed in the presentation header.</summary>
    public string Title { get; set; } = "";

    /// <summary>Optional subtitle.</summary>
    public string Subtitle { get; set; } = "";

    /// <summary>Optional byline / author credit.</summary>
    public string Byline { get; set; } = "";

    /// <summary>Footer text (supports inline HTML).</summary>
    public string Footer { get; set; } = "";

    /// <summary>Panel theme: "light" or "dark".</summary>
    public string Theme { get; set; } = "light";

    /// <summary>Whether to show a marker at each chapter center.</summary>
    public bool ShowMarkers { get; set; } = true;

    /// <summary>Color of the chapter markers (hex color).</summary>
    public string MarkerColor { get; set; } = "#3FB1CE";

    /// <summary>Whether to show an inset minimap in the corner.</summary>
    public bool Inset { get; set; } = false;

    /// <summary>Position of the inset minimap: "top-left", "bottom-right", etc.</summary>
    public string InsetPosition { get; set; } = "bottom-left";

    /// <summary>Whether to hide the chapter navigation dots.</summary>
    public bool HideChapterNav { get; set; } = false;

    /// <summary>Start slide content (or "none").</summary>
    public string StartSlide { get; set; } = "none";

    /// <summary>End slide content (or "none").</summary>
    public string EndSlide { get; set; } = "none";

    /// <summary>Ordered list of story chapters.</summary>
    public List<GeoChapter> Chapters { get; set; } = new();
}

/// <summary>
/// A single chapter in a story map, controlling the map camera and layer effects
/// when the reader scrolls to this chapter.
/// </summary>
public class GeoChapter
{
    /// <summary>Unique chapter identifier (e.g., "chapter-0").</summary>
    public string Id { get; set; } = "";

    /// <summary>Chapter title displayed in the scrolling panel.</summary>
    public string Title { get; set; } = "";

    /// <summary>Chapter description / body text (supports inline HTML).</summary>
    public string Description { get; set; } = "";

    /// <summary>Optional image URL displayed alongside the chapter.</summary>
    public string Image { get; set; } = "";

    /// <summary>Panel alignment: "left", "center", "right", or "full".</summary>
    public string Alignment { get; set; } = "right";

    /// <summary>Map transition type: "flyTo", "easeTo", or "jumpTo".</summary>
    public string MapAnimation { get; set; } = "flyTo";

    /// <summary>Whether to hide the text panel (map-only chapter).</summary>
    public bool Hidden { get; set; }

    /// <summary>Whether to slowly rotate the camera after the transition settles.</summary>
    public bool RotateAnimation { get; set; }

    /// <summary>Camera center longitude.</summary>
    public double Lng { get; set; }

    /// <summary>Camera center latitude.</summary>
    public double Lat { get; set; }

    /// <summary>Camera zoom level (0 = world, ~12 = city, ~18 = building).</summary>
    public double Zoom { get; set; } = 3;

    /// <summary>Camera pitch in degrees (0 = top-down, 60 = horizon).</summary>
    public double Pitch { get; set; }

    /// <summary>Camera bearing in degrees (0 = north up).</summary>
    public double Bearing { get; set; }

    /// <summary>Layer effects to apply when this chapter becomes active.</summary>
    public List<GeoLayerEffect> OnChapterEnter { get; set; } = new();

    /// <summary>Layer effects to apply when leaving this chapter.</summary>
    public List<GeoLayerEffect> OnChapterExit { get; set; } = new();

    /// <summary>Narrative beats within this chapter, each with its own spatial semantics.</summary>
    public List<GeoBeat> Beats { get; set; } = new();
}

/// <summary>
/// A layer opacity transition triggered on chapter enter or exit.
/// References a layer by its ID (matching the "layers" array in geolibre.json).
/// </summary>
public class GeoLayerEffect
{
    /// <summary>Layer ID to affect (e.g., "layer-adverbial", "layer-subject").</summary>
    public string LayerId { get; set; } = "";

    /// <summary>Target opacity (0.0 = fully transparent, 1.0 = fully opaque).</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Transition duration in milliseconds.</summary>
    public int Duration { get; set; } = 500;
}

/// <summary>
/// A narrative beat within a chapter, representing a single spatial event
/// with its own camera position and spatial semantics.
/// Beats enable fine-grained camera micro-adjustments within a chapter.
/// </summary>
public class GeoBeat
{
    /// <summary>Location name for this beat.</summary>
    public string LocationName { get; set; } = "";

    /// <summary>Spatial role: CONTAINER, TARGET, ORIGIN, PATH, ACTOR.</summary>
    public string SpatialRole { get; set; } = "";

    /// <summary>Verb spatial class for this beat.</summary>
    public string Verb { get; set; } = "";

    /// <summary>Beat camera longitude.</summary>
    public double Lng { get; set; }

    /// <summary>Beat camera latitude.</summary>
    public double Lat { get; set; }

    /// <summary>Beat camera zoom.</summary>
    public double Zoom { get; set; } = 12;

    /// <summary>Beat camera pitch.</summary>
    public double Pitch { get; set; }

    /// <summary>Beat camera bearing.</summary>
    public double Bearing { get; set; }
}
