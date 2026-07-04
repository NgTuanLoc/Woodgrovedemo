using AuthShared;
using Microsoft.Extensions.Caching.Memory;

namespace AuthShared.Tests;

public class SessionDenylistTests
{
    private static MemorySessionDenylist NewDenylist() =>
        new(new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromHours(8));

    [Fact]
    public void Revoked_sid_is_reported_revoked()
    {
        var denylist = NewDenylist();
        denylist.Revoke("sess-123");
        Assert.True(denylist.IsRevoked("sess-123"));
    }

    [Fact]
    public void Unknown_sid_is_not_revoked()
    {
        Assert.False(NewDenylist().IsRevoked("sess-999"));
    }
}
