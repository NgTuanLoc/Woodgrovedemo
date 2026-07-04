using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace AuthShared;

/// <summary>Tracks Keycloak session ids (sid) revoked via back-channel logout.</summary>
public interface ISessionDenylist
{
    void Revoke(string sid);
    bool IsRevoked(string sid);
}

// In-memory, single-instance only — fine for this dev/learning setup.
// Production would use a distributed cache so all instances see revocations.
public sealed class MemorySessionDenylist(IMemoryCache cache, TimeSpan ttl) : ISessionDenylist
{
    public void Revoke(string sid) => cache.Set(Key(sid), true, ttl);
    public bool IsRevoked(string sid) => cache.TryGetValue(Key(sid), out _);
    private static string Key(string sid) => "revoked-sid:" + sid;
}

public static class SessionDenylistServiceCollectionExtensions
{
    /// <param name="ttl">Should match the auth cookie lifetime — an entry only
    /// needs to outlive every cookie that could carry its sid.</param>
    public static IServiceCollection AddSessionDenylist(this IServiceCollection services, TimeSpan ttl)
    {
        services.AddMemoryCache();
        services.AddSingleton<ISessionDenylist>(sp =>
            new MemorySessionDenylist(sp.GetRequiredService<IMemoryCache>(), ttl));
        return services;
    }
}
