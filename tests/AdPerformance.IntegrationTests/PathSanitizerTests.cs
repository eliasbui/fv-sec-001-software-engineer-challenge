using AdPerformance.CLI;
using FluentAssertions;

namespace AdPerformance.IntegrationTests;

public sealed class PathSanitizerTests
{
    [Fact]
    public void NormalizePath_ReturnsAbsolutePath()
    {
        var result = PathSanitizer.NormalizePath("./relative/path.csv", "--input");
        Path.IsPathFullyQualified(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("abc\0def")]
    [InlineData("abc\ndef")]
    [InlineData("abc\rdef")]
    [InlineData("abc\u0001def")]
    public void NormalizePath_RejectsControlCharacters(string bad)
    {
        var act = () => PathSanitizer.NormalizePath(bad, "--input");
        act.Should().Throw<ArgumentException>().WithMessage("*control character*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizePath_RejectsEmpty(string bad)
    {
        var act = () => PathSanitizer.NormalizePath(bad, "--input");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("line1\nline2", "line1 line2")]
    [InlineData("line1\r\nline2", "line1  line2")]
    [InlineData("with\ttab", "with tab")]
    [InlineData("\u0001\u0002\u0003", "")]
    [InlineData(null, "")]
    public void ForLog_StripsControlCharacters(string? input, string expected)
    {
        PathSanitizer.ForLog(input).Should().Be(expected);
    }
}
