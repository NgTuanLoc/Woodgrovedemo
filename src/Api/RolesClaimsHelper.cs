using System.Security.Claims;

public static class RolesClaimsHelper
{
    // Keycloak emits a flat multivalued "roles" claim (see realm import).
    public const string RoleClaimType = "roles";
    public const string NameClaimType = "preferred_username";
}
