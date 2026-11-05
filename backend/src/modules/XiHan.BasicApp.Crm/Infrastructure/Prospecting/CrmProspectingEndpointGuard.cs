namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Validates the final URI reported by an HTTP response against an adapter's fixed HTTPS endpoint.
/// This is a URI-level guard only; it does not validate the remote IP address or prevent DNS rebinding.
/// </summary>
public static class CrmProspectingEndpointGuard
{
    public static bool IsApprovedResponseUri(Uri? actualUri, Uri approvedEndpoint)
    {
        ArgumentNullException.ThrowIfNull(approvedEndpoint);

        return actualUri is not null
            && string.Equals(actualUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && actualUri.IsDefaultPort
            && string.IsNullOrEmpty(actualUri.UserInfo)
            && string.Equals(actualUri.Host, approvedEndpoint.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(actualUri.AbsolutePath, approvedEndpoint.AbsolutePath, StringComparison.Ordinal)
            && string.IsNullOrEmpty(actualUri.Fragment)
            && string.Equals(approvedEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && approvedEndpoint.IsDefaultPort
            && string.IsNullOrEmpty(approvedEndpoint.UserInfo);
    }
}
