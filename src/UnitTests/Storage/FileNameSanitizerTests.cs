using Likvido.Azure.Storage;
using Shouldly;
using Xunit;

namespace UnitTests.Storage;

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("simple.pdf", "simple.pdf")]
    [InlineData("document.txt", "document.txt")]
    [InlineData("my-file_123.jpg", "my-file_123.jpg")]
    public void Sanitize_WhenGivenAsciiFilename_ShouldReturnUnchanged(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Søren.pdf", "Soren.pdf")]
    [InlineData("Ånderå.pdf", "Andera.pdf")]
    [InlineData("René.txt", "Rene.txt")]
    [InlineData("Müller.doc", "Muller.doc")]
    [InlineData("Ångström.csv", "Angstrom.csv")]
    public void Sanitize_WhenGivenAccentedCharacters_ShouldConvertToAscii(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("送货单.pdf", "___.pdf")]
    [InlineData("文件.txt", "__.txt")]
    [InlineData("Файл.doc", "____.doc")]
    public void Sanitize_WhenGivenNonLatinCharacters_ShouldReplaceWithUnderscore(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null!, null!)]
    public void Sanitize_WhenGivenEmptyOrNull_ShouldReturnSame(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }
}
