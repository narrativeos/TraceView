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

namespace Caly.Core.Services.EntityProcessors.Organization.Models;

/// <summary>
/// Types of relations between two ORGANIZATION entities.
/// </summary>
public enum OrganizationRelationType
{
    /// <summary>Full name compound parts (e.g., "华为" + "技术有限公司").</summary>
    FullName,

    /// <summary>Abbreviation ↔ full name (e.g., "北大" ↔ "北京大学").</summary>
    Abbreviation,

    /// <summary>Parent-subsidiary / ownership hierarchy (e.g., "华为" → "华为云").</summary>
    Subsidiary,

    /// <summary>Sibling / parallel organizations mentioned together (conj).</summary>
    Sibling,

    /// <summary>Cross-block coreference (same org, different names).</summary>
    Coreference
}

/// <summary>
/// Classification of an organization name's structure.
/// </summary>
public enum OrganizationNameType
{
    /// <summary>Full formal name with a recognized suffix (公司/大学/委员会 etc.).</summary>
    FullFormalName,

    /// <summary>Likely abbreviation — short name without suffix (2-5 chars).</summary>
    Abbreviation,

    /// <summary>Name with geographic prefix but non-standard suffix.</summary>
    GeographicPrefix,

    /// <summary>Generic term that may or may not be a real organization.</summary>
    Generic,

    /// <summary>Not an organization name.</summary>
    Invalid
}
