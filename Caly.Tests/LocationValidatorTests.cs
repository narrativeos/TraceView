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

using Caly.Core.Utilities;

namespace Caly.Tests;

public class LocationValidatorTests
{
    // ──────────────────────────────────────────────
    //  IsLikelyNotLocation — Invalid cases
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("2024", "纯数字")]
    [InlineData("12345", "纯数字")]
    [InlineData("!!!", "纯标点符号")]
    [InlineData("Test", "不含汉字")]
    [InlineData("abc123", "不含汉字")]
    [InlineData("A", "单字符非汉字")]
    [InlineData("1", "纯数字")]
    [InlineData("第一条", "序数词模式")]
    [InlineData("第二十五条", "序数词模式")]
    [InlineData("第3项", "序数词模式")]
    [InlineData("(2024)", "不含汉字")]
    [InlineData("第25号", "以号结尾")]
    [InlineData("25号", "编号类名称")]
    [InlineData("某地", "占位词")]
    [InlineData("此处", "占位词")]
    [InlineData("该处", "占位词")]
    [InlineData("这里", "占位词")]
    // v2: new rules
    [InlineData("8线", "非标准地名")]
    [InlineData("衔铁6", "非标准地名")]
    [InlineData("01-2023-4239号", "编号")]
    [InlineData("程序", "技术术语")]
    [InlineData("上半球", "技术术语")]
    [InlineData("霍尼韦尔 EHV", "公司")]
    // v3: OCR mixed text
    [InlineData("图_Lillian", "OCR混合")]
    [InlineData("foo_bar", "OCR混合")]
    public void Validate_Invalid_ReturnsInvalid(string name, string expectedReasonContains)
    {
        var (result, reason) = LocationValidator.Validate(name);
        Assert.Equal(LocationValidationResult.Invalid, result);
        Assert.Contains(expectedReasonContains, reason);
    }

    // ──────────────────────────────────────────────
    //  IsAmbiguousAbbreviation — Ambiguous cases
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("京", "单字地名")]
    [InlineData("沪", "单字地名")]
    [InlineData("东", "单字方位词")]
    [InlineData("南", "单字方位词")]
    [InlineData("西", "单字方位词")]
    [InlineData("北", "单字方位词")]
    [InlineData("中", "单字方位词")]
    [InlineData("河", "单字通名")]
    [InlineData("山", "单字通名")]
    [InlineData("海", "单字通名")]
    [InlineData("京西", "方位简称")]
    [InlineData("苏南", "方位简称")]
    [InlineData("江南", "方位简称")]
    [InlineData("川西", "方位简称")]
    [InlineData("塞外", "方位简称")]
    public void Validate_Ambiguous_ReturnsAmbiguous(string name, string expectedReasonContains)
    {
        var (result, reason) = LocationValidator.Validate(name);
        Assert.Equal(LocationValidationResult.Ambiguous, result);
        Assert.Contains(expectedReasonContains, reason);
    }

    // ──────────────────────────────────────────────
    //  Valid cases
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("北京")]
    [InlineData("上海市")]
    [InlineData("海淀区")]
    [InlineData("五道口")]
    [InlineData("长安街")]
    [InlineData("未央宫")]
    [InlineData("玄武门")]
    [InlineData("大雁塔")]
    public void Validate_Valid_ReturnsValid(string name)
    {
        var (result, reason) = LocationValidator.Validate(name);
        Assert.Equal(LocationValidationResult.Valid, result);
        Assert.Equal("有效地名", reason);
    }

    // ──────────────────────────────────────────────
    //  Edge cases
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyOrWhitespace_ReturnsInvalid(string? name)
    {
        var (result, _) = LocationValidator.Validate(name ?? "");
        Assert.Equal(LocationValidationResult.Invalid, result);
    }

    [Fact]
    public void Validate_ShortChineseName_ReturnsValid()
    {
        // 2-char Chinese names are now valid (e.g., "北京", "上海")
        var (result, _) = LocationValidator.Validate("长河");
        Assert.Equal(LocationValidationResult.Valid, result);
    }

    [Fact]
    public void Validate_ThreeCharChineseName_ReturnsValid()
    {
        var (result, _) = LocationValidator.Validate("长安城");
        Assert.Equal(LocationValidationResult.Valid, result);
    }

    // ──────────────────────────────────────────────
    //  Backward compatibility
    // ──────────────────────────────────────────────

    [Fact]
    public void IsLikelyNotLocation_PureDigits_ReturnsTrue()
    {
        Assert.True(LocationValidator.IsLikelyNotLocation("123"));
    }

    [Fact]
    public void IsLikelyNotLocation_Beijing_ReturnsFalse()
    {
        Assert.False(LocationValidator.IsLikelyNotLocation("北京"));
    }

    [Fact]
    public void IsAmbiguousAbbreviation_SingleChar_ReturnsTrue()
    {
        Assert.True(LocationValidator.IsAmbiguousAbbreviation("京"));
    }

    [Fact]
    public void IsAmbiguousAbbreviation_Beijing_ReturnsFalse()
    {
        Assert.False(LocationValidator.IsAmbiguousAbbreviation("北京"));
    }

    // ──────────────────────────────────────────────
    //  ClassifyType
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("餐厅", LocationType.GenericFacility)]
    [InlineData("地铁站", LocationType.GenericFacility)]
    [InlineData("博物馆", LocationType.GenericFacility)]
    [InlineData("高铁站", LocationType.GenericFacility)]
    [InlineData("纪念馆", LocationType.GenericFacility)]
    [InlineData("观景台", LocationType.GenericFacility)]
    [InlineData("北京", LocationType.PlaceName)]
    [InlineData("故宫", LocationType.PlaceName)]
    [InlineData("上海", LocationType.PlaceName)]
    public void ClassifyType_ReturnsCorrectType(string name, LocationType expectedType)
    {
        var type = LocationValidator.ClassifyType(name);
        Assert.Equal(expectedType, type);
    }
}
