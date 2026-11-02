using XamppControl.Updates;

namespace XamppControl.Tests;

public sealed class VersionUtilTests
{
    [Theory]
    [InlineData("8.4.27", "8.4.27")]
    [InlineData("php-8.4.27-Win32-vs17-x64.zip", "8.4.27")]
    [InlineData("MariaDB 12.3.3", "12.3.3")]
    [InlineData("v0.2.1", "0.2.1")]
    [InlineData("12.3", "12.3")]
    public void FromText_FindsTheFirstDottedNumber(string text, string expected) =>
        Assert.Equal(expected, VersionUtil.FromText(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("8")]
    public void FromText_ReturnsNullWithoutADottedNumber(string? text) =>
        Assert.Null(VersionUtil.FromText(text));

    [Fact]
    public void Parse_ComparesLikeVersionNumbers()
    {
        Assert.True(VersionUtil.Parse("8.4.10") > VersionUtil.Parse("8.4.9"));
        Assert.True(VersionUtil.Parse("12.3.3") > VersionUtil.Parse("11.4.5"));
        Assert.True(VersionUtil.Parse("v0.2.1") > VersionUtil.Parse("v0.2.0"));
    }

    [Fact]
    public void Parse_UnknownTextIsZero() =>
        Assert.Equal(new Version(0, 0), VersionUtil.Parse("unknown"));

    [Theory]
    [InlineData("8.4.27", "8.4")]
    [InlineData("MariaDB 12.3.3", "12.3")]
    [InlineData(null, "0.0")]
    public void MajorMinor_KeepsTwoParts(string? version, string expected) =>
        Assert.Equal(expected, VersionUtil.MajorMinor(version));
}
