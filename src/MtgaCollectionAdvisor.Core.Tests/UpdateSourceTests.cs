using MtgaCollectionAdvisor.Core.Hosting;
using Xunit;

namespace MtgaCollectionAdvisor.Core.Tests;

public sealed class UpdateSourceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void OverrideFrom_Should_ReturnNull_When_Blank(string? value)
    {
        Assert.Null(UpdateSource.OverrideFrom(value));
    }

    [Fact]
    public void OverrideFrom_Should_ReturnTrimmedValue_When_Set()
    {
        Assert.Equal(@"C:\rel", UpdateSource.OverrideFrom(@" C:\rel "));
    }
}
