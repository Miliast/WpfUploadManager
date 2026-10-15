using UploadManager.Core.Configuration;

namespace UploadManager.Core.Tests;

public sealed class UploadOptionsValidatorTests
{
    private readonly UploadOptionsValidator _validator = new();

    [Fact]
    public void Default_test_options_are_valid() =>
        Assert.True(_validator.Validate(null, TestData.Options()).Succeeded);

    [Fact]
    public void At_least_one_slot_is_required()
    {
        var result = _validator.Validate(null, new UploadOptions());

        Assert.True(result.Failed);
    }

    [Fact]
    public void Duplicate_keys_are_rejected()
    {
        var options = TestData.Options();
        options.Slots[1].Key = options.Slots[0].Key.ToUpperInvariant();

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Doppelte"));
    }

    [Fact]
    public void Http_mode_requires_absolute_endpoint()
    {
        var options = TestData.Options();
        options.UseSimulation = false;
        options.Endpoint = null;

        Assert.True(_validator.Validate(null, options).Failed);

        options.Endpoint = new Uri("https://example.org/upload");
        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void Max_size_must_be_positive()
    {
        var options = TestData.Options();
        options.Slots[0].MaxSizeMb = 0;

        Assert.True(_validator.Validate(null, options).Failed);
    }
}
