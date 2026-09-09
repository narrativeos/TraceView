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

using Caly.Core.Services;

namespace Caly.Tests;

/// <summary>
/// Tests for <see cref="SemanticAnalysisService.SanitizeTextForNlp"/> — the F0
/// input-hygiene step that cleans OCR markers and HTML tags before NLP.
/// </summary>
public class SemanticTextSanitizerTests
{
    [Fact]
    public void NullOrEmpty_ReturnsUnchanged()
    {
        Assert.Null(SemanticAnalysisService.SanitizeTextForNlp(null!));
        Assert.Equal("", SemanticAnalysisService.SanitizeTextForNlp(""));
    }

    [Fact]
    public void RemovesOcrMarkers()
    {
        Assert.Equal("甲乙", SemanticAnalysisService.SanitizeTextForNlp("甲<|txt_split|>乙"));
        Assert.Equal("AB", SemanticAnalysisService.SanitizeTextForNlp("A<|line_break|>B"));
    }

    [Fact]
    public void StripsSubSupKeepingContent()
    {
        Assert.Equal("SF6", SemanticAnalysisService.SanitizeTextForNlp("SF<sub>6</sub>"));
        Assert.Equal("CO2", SemanticAnalysisService.SanitizeTextForNlp("CO<sub>2</sub>"));
        Assert.Equal("x2", SemanticAnalysisService.SanitizeTextForNlp("x<sup>2</sup>"));
    }

    [Fact]
    public void StripsInlineFormattingTagsKeepingContent()
    {
        Assert.Equal("变压器", SemanticAnalysisService.SanitizeTextForNlp("<b>变压器</b>"));
        Assert.Equal("额定电压", SemanticAnalysisService.SanitizeTextForNlp("<strong>额定电压</strong>"));
        Assert.Equal("abc", SemanticAnalysisService.SanitizeTextForNlp("<span>abc</span>"));
        Assert.Equal("abc", SemanticAnalysisService.SanitizeTextForNlp("<em>abc</em>"));
    }

    [Fact]
    public void StripsTagWithAttributes()
    {
        Assert.Equal("abc", SemanticAnalysisService.SanitizeTextForNlp("<span class=\"x\">abc</span>"));
    }

    [Fact]
    public void ReplacesBlockTagsWithSpace()
    {
        Assert.Equal("A B", SemanticAnalysisService.SanitizeTextForNlp("<p>A</p><p>B</p>"));
        Assert.Equal("A B", SemanticAnalysisService.SanitizeTextForNlp("A<br>B"));
        Assert.Equal("A B", SemanticAnalysisService.SanitizeTextForNlp("A<br/>B"));
    }

    [Fact]
    public void PreservesLegitimateAngleBrackets()
    {
        // Space after '<' → not a tag, preserved.
        Assert.Equal("U < 5", SemanticAnalysisService.SanitizeTextForNlp("U < 5"));
        // '<substance>' has no tag boundary after 'sub' → preserved.
        Assert.Equal("<substance>", SemanticAnalysisService.SanitizeTextForNlp("<substance>"));
    }

    [Fact]
    public void HandlesRealWorldSample()
    {
        var input = "750kV</sup> 的 SF<sub>6</sub> 气体<|txt_split|>绝缘电阻";
        var result = SemanticAnalysisService.SanitizeTextForNlp(input);

        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.Contains("750kV", result);
        Assert.Contains("SF6", result);
        Assert.Contains("绝缘电阻", result);
    }
}
