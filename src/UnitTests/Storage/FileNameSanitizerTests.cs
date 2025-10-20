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
    [InlineData("René.txt", "Rene.txt")]
    [InlineData("Müller.doc", "Muller.doc")]
    [InlineData("Ångström.csv", "Angstrom.csv")]
    [InlineData("Łódź.pdf", "Lodz.pdf")]
    public void Sanitize_WhenGivenEuropeanAccents_ShouldConvertToAscii(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Søren's Faktúra.pdf", "Sorens_Faktura.pdf")]
    [InlineData("René François Müller.pdf", "Rene_Francois_Muller.pdf")]
    [InlineData("test™©®.pdf", "test___.pdf")]
    [InlineData("file<name>.txt", "file_name_.txt")]
    [InlineData("my:file|name.doc", "my_file_name.doc")]
    public void Sanitize_WhenGivenSpecialCharacters_ShouldReplaceWithUnderscore(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("送货单.pdf", "file.pdf")]
    [InlineData("文件.txt", "file.txt")]
    [InlineData("Файл.doc", "file.doc")]
    public void Sanitize_WhenGivenNonLatinCharacters_ShouldFallbackToFile(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "file")]
    [InlineData("   ", "file")]
    [InlineData(null, "file")]
    public void Sanitize_WhenGivenEmptyOrNull_ShouldReturnFile(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData(".pdf", "file.pdf")]
    [InlineData(".txt", "file.txt")]
    public void Sanitize_WhenGivenOnlyExtension_ShouldPrependFile(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void Sanitize_WhenGivenMultipleConsecutiveUnderscores_ShouldCollapseToSingle()
    {
        // Arrange
        var input = "test___file___name.pdf";

        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe("test_file_name.pdf");
    }

    [Fact]
    public void Sanitize_WhenGivenVeryLongFilename_ShouldTruncateToMaxLength()
    {
        // Arrange
        var longName = new string('a', 300) + ".pdf";

        // Act
        var result = FileNameSanitizer.Sanitize(longName);

        // Assert
        result.Length.ShouldBeLessThanOrEqualTo(255);
        result.ShouldEndWith(".pdf");
    }

    [Theory]
    [InlineData("document.multiple.dots.pdf", "document.multiple.dots.pdf")]
    [InlineData("file.v2.0.final.txt", "file.v2.0.final.txt")]
    public void Sanitize_WhenGivenMultipleDots_ShouldPreserveStructure(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("_leading.pdf", "leading.pdf")]
    [InlineData("trailing_.pdf", "trailing.pdf")]
    [InlineData("_both_.pdf", "both.pdf")]
    public void Sanitize_WhenGivenLeadingOrTrailingUnderscores_ShouldTrim(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Test@123.pdf", "Test_123.pdf")]
    [InlineData("file#name$.txt", "file_name_.txt")]
    [InlineData("my&file.doc", "my_file.doc")]
    public void Sanitize_WhenGivenSpecialSymbols_ShouldReplaceWithUnderscore(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("ÆØÅ.pdf", "AOA.pdf")]
    [InlineData("æøå.txt", "aoa.txt")]
    public void Sanitize_WhenGivenScandinavianCharacters_ShouldConvertToAscii(string input, string expected)
    {
        // Act
        var result = FileNameSanitizer.Sanitize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void Sanitize_WhenGivenCustomReplacementChar_ShouldUseIt()
    {
        // Arrange
        var input = "test file.pdf";

        // Act
        var result = FileNameSanitizer.Sanitize(input, '-');

        // Assert
        result.ShouldBe("test-file.pdf");
    }
}
