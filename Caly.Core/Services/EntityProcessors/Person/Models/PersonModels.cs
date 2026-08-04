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

namespace Caly.Core.Services.EntityProcessors.Person.Models;

/// <summary>
/// Types of relations between two PERSON entities.
/// </summary>
public enum PersonRelationType
{
    /// <summary>Alias/courtesy name/alternative name (e.g., "曹操" ↔ "曹孟德").</summary>
    Alias,

    /// <summary>Family/kinship relationship (e.g., "张先生" → "儿子").</summary>
    Family,

    /// <summary>Colleague/associated person (e.g., "张三" and "李四").</summary>
    Colleague,

    /// <summary>Title/role decorating a name (e.g., "邓公" → "小平").</summary>
    Title,

    /// <summary>Cross-block coreference (same person, different names).</summary>
    Coreference
}

/// <summary>
/// Classification of a person name's structure.
/// Used by <see cref="PersonValidator"/> to determine how to process the name.
/// </summary>
public enum PersonNameType
{
    /// <summary>Standard Chinese full name (2-4 characters, contains a known surname).</summary>
    FullChineseName,

    /// <summary>Name with a title suffix (e.g., "邓小平同志"). Core name can be extracted.</summary>
    TitleSuffixed,

    /// <summary>Foreign/Western name (alphabetic characters, multi-word).</summary>
    ForeignName,

    /// <summary>Single name — could be a given name, art name, or nickname.</summary>
    SingleName,

    /// <summary>Pure title/role word — not a real person name (e.g., "先生", "女士").</summary>
    TitleOnly,

    /// <summary>Cannot classify the name structure.</summary>
    Unknown
}

/// <summary>
/// Family/kinship role extracted from a title or dependency pattern.
/// </summary>
public enum KinshipRole
{
    /// <summary>Not a kinship relation.</summary>
    None,

    /// <summary>Parent (father/mother).</summary>
    Parent,

    /// <summary>Child (son/daughter).</summary>
    Child,

    /// <summary>Sibling (brother/sister).</summary>
    Sibling,

    /// <summary>Spouse (husband/wife).</summary>
    Spouse,

    /// <summary>Other/unknown kinship.</summary>
    Other
}
