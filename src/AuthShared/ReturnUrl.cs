namespace AuthShared;

public static class ReturnUrl
{
    /// <summary>
    /// Only allows relative, same-site return URLs to prevent open-redirect abuse
    /// (e.g. ?returnUrl=https://evil.com). Mirrors IUrlHelper.IsLocalUrl:
    /// must start with "/" (but not "//" or "/\") or "~/". Anything else → "/".
    /// </summary>
    public static string Sanitize(string? url) =>
        !string.IsNullOrEmpty(url)
        && ((url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')))
            || (url.Length > 1 && url[0] == '~' && url[1] == '/'))
            ? url
            : "/";
}
