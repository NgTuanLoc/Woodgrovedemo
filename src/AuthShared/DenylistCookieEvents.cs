using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace AuthShared;

public static class DenylistCookieEvents
{
    /// <summary>
    /// Rejects the cookie principal when its Keycloak session id (sid) was revoked
    /// via back-channel logout. Run this FIRST when composing OnValidatePrincipal.
    /// A principal without a sid claim (cookie issued before this feature) is left
    /// alone — documented pitfall.
    /// </summary>
    public static Task RejectIfRevokedAsync(CookieValidatePrincipalContext context)
    {
        var sid = context.Principal?.FindFirst("sid")?.Value;
        if (string.IsNullOrEmpty(sid)) return Task.CompletedTask;

        var denylist = context.HttpContext.RequestServices
            .GetRequiredService<ISessionDenylist>();
        if (denylist.IsRevoked(sid))
            context.RejectPrincipal(); // sets Principal = null → treated as anonymous

        return Task.CompletedTask;
    }
}
