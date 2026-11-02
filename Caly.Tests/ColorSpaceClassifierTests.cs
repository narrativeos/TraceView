using Caly.Core.Services;
using UglyToad.PdfPig.Graphics.Colors;

namespace Caly.Tests;

public class ColorSpaceClassifierTests
{
    // --- MapFamily: every renderer color space maps to the matching fine-grained family ---

    [Theory]
    [InlineData(ColorSpace.DeviceGray, PdfColorSpaceFamily.DeviceGray)]
    [InlineData(ColorSpace.DeviceRGB, PdfColorSpaceFamily.DeviceRGB)]
    [InlineData(ColorSpace.DeviceCMYK, PdfColorSpaceFamily.DeviceCMYK)]
    [InlineData(ColorSpace.CalGray, PdfColorSpaceFamily.CalGray)]
    [InlineData(ColorSpace.CalRGB, PdfColorSpaceFamily.CalRGB)]
    [InlineData(ColorSpace.Lab, PdfColorSpaceFamily.Lab)]
    [InlineData(ColorSpace.ICCBased, PdfColorSpaceFamily.ICCBased)]
    [InlineData(ColorSpace.Separation, PdfColorSpaceFamily.Separation)]
    [InlineData(ColorSpace.DeviceN, PdfColorSpaceFamily.DeviceN)]
    [InlineData(ColorSpace.Indexed, PdfColorSpaceFamily.Indexed)]
    [InlineData(ColorSpace.Pattern, PdfColorSpaceFamily.Pattern)]
    public void MapFamily_MapsEveryColorSpace(ColorSpace cs, PdfColorSpaceFamily expected)
    {
        Assert.Equal(expected, ColorSpaceClassifier.MapFamily(cs));
    }

    // --- IsAtRisk: the core "will this render approximately?" rule ---

    [Theory]
    [InlineData(PdfColorSpaceFamily.ICCBased, null)]
    [InlineData(PdfColorSpaceFamily.Unsupported, null)]
    [InlineData(PdfColorSpaceFamily.Indexed, PdfColorSpaceFamily.ICCBased)]
    [InlineData(PdfColorSpaceFamily.Separation, PdfColorSpaceFamily.ICCBased)]
    [InlineData(PdfColorSpaceFamily.DeviceN, PdfColorSpaceFamily.ICCBased)]
    public void IsAtRisk_True_WhenIccOrItsBaseOrAlternateIsIcc(PdfColorSpaceFamily family, PdfColorSpaceFamily? underlying)
    {
        Assert.True(ColorSpaceClassifier.IsAtRisk(family, underlying));
    }

    [Theory]
    [InlineData(PdfColorSpaceFamily.DeviceGray, null)]
    [InlineData(PdfColorSpaceFamily.DeviceRGB, null)]
    [InlineData(PdfColorSpaceFamily.DeviceCMYK, null)]
    [InlineData(PdfColorSpaceFamily.CalGray, null)]
    [InlineData(PdfColorSpaceFamily.CalRGB, null)]
    [InlineData(PdfColorSpaceFamily.Lab, null)]
    [InlineData(PdfColorSpaceFamily.Pattern, null)]
    [InlineData(PdfColorSpaceFamily.Indexed, PdfColorSpaceFamily.DeviceRGB)]
    [InlineData(PdfColorSpaceFamily.Separation, PdfColorSpaceFamily.DeviceCMYK)]
    [InlineData(PdfColorSpaceFamily.DeviceN, PdfColorSpaceFamily.DeviceRGB)]
    public void IsAtRisk_False_WhenFaithfullyRendered(PdfColorSpaceFamily family, PdfColorSpaceFamily? underlying)
    {
        Assert.False(ColorSpaceClassifier.IsAtRisk(family, underlying));
    }

    // --- Describe: produces an explanatory note; the ICCBased note calls out the missing profile ---

    [Fact]
    public void Describe_ICCBased_CallsOutTheMissingProfile()
    {
        string note = ColorSpaceClassifier.Describe(PdfColorSpaceFamily.ICCBased, PdfColorSpaceFamily.DeviceRGB);
        Assert.Contains("ICC", note);
        Assert.Contains("NOT applied", note);
    }

    [Fact]
    public void Describe_DeviceSpaces_AreNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(ColorSpaceClassifier.Describe(PdfColorSpaceFamily.DeviceRGB)));
        Assert.False(string.IsNullOrWhiteSpace(ColorSpaceClassifier.Describe(PdfColorSpaceFamily.DeviceCMYK)));
    }
}