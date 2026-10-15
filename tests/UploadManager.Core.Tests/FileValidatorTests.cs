using UploadManager.Core.Configuration;
using UploadManager.Core.Validation;

namespace UploadManager.Core.Tests;

public sealed class FileValidatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Valid_file_is_accepted()
    {
        var path = _temp.CreateFile("report.pdf", 100);

        var result = FileValidator.Validate(path, TestData.Pdf());

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(100, result.SizeBytes);
    }

    [Fact]
    public void Extension_check_ignores_case()
    {
        var path = _temp.CreateFile("REPORT.PDF");

        Assert.True(FileValidator.Validate(path, TestData.Pdf()).IsValid);
    }

    [Fact]
    public void Wrong_extension_is_rejected()
    {
        var path = _temp.CreateFile("image.png");

        var result = FileValidator.Validate(path, TestData.Pdf());

        Assert.False(result.IsValid);
        Assert.Contains("nicht erlaubt", result.ErrorMessage);
        Assert.Contains("PDF", result.ErrorMessage);
    }

    [Fact]
    public void Missing_file_is_rejected()
    {
        var result = FileValidator.Validate(Path.Combine(_temp.Path, "missing.pdf"), TestData.Pdf());

        Assert.False(result.IsValid);
        Assert.Contains("nicht gefunden", result.ErrorMessage);
    }

    [Fact]
    public void Directory_is_rejected()
    {
        var directory = _temp.CreateDirectory("folder.pdf");

        var result = FileValidator.Validate(directory, TestData.Pdf());

        Assert.False(result.IsValid);
        Assert.Contains("Ordner", result.ErrorMessage);
    }

    [Fact]
    public void Empty_file_is_rejected()
    {
        var path = _temp.CreateFile("empty.pdf", 0);

        var result = FileValidator.Validate(path, TestData.Pdf());

        Assert.False(result.IsValid);
        Assert.Contains("leer", result.ErrorMessage);
    }

    [Fact]
    public void Too_large_file_is_rejected()
    {
        var path = _temp.CreateFile("big.pdf", 2048);

        var result = FileValidator.Validate(path, TestData.Pdf(maxSizeMb: 0.001));

        Assert.False(result.IsValid);
        Assert.Contains("zu groß", result.ErrorMessage);
        Assert.Equal(2048, result.SizeBytes);
    }

    [Fact]
    public void Empty_extension_list_allows_every_file_type()
    {
        var path = _temp.CreateFile("anything.xyz");
        var definition = new UploadSlotDefinition { Key = "any", Title = "Any" };

        Assert.True(FileValidator.Validate(path, definition).IsValid);
        Assert.Equal("alle Dateitypen", FileValidator.DescribeExtensions(definition));
    }

    [Theory]
    [InlineData("pdf", ".pdf")]
    [InlineData(".PDF", ".pdf")]
    [InlineData(" .Docx ", ".docx")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeExtension_produces_lowercase_with_leading_dot(string? input, string expected) =>
        Assert.Equal(expected, UploadSlotDefinition.NormalizeExtension(input));

    [Fact]
    public void DescribeExtensions_lists_distinct_uppercase_names()
    {
        var definition = new UploadSlotDefinition { AllowedExtensions = [".xlsx", "csv", ".XLSX"] };

        Assert.Equal("XLSX, CSV", FileValidator.DescribeExtensions(definition));
    }
}
