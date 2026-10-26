namespace Platform.WebApi.Security;

/// <summary>
/// The portal a browser session belongs to (LIS-CR-014 G1): the laboratory portal (<c>/lab/…</c> and every client page)
/// or the Super Admin portal (<c>/admin/…</c>). Each has its own refresh cookie, so one browser keeps one session per portal.
/// </summary>
public enum SessionPortal
{
    Client,
    Admin,
}
