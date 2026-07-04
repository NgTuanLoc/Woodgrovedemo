using AuthShared;

namespace AuthShared.Tests;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/", "/")]
    [InlineData("~/settings", "~/settings")]
    [InlineData("//evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("https://evil.com", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void Sanitize_only_allows_local_urls(string? input, string expected)
    {
        Assert.Equal(expected, ReturnUrl.Sanitize(input));
    }
}
