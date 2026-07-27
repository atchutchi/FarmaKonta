namespace Nofarma.Application.Identity.Authentication;

public sealed class CurrentSession
{
    public LocalSession? Active { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public void Activate(LocalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Active = session;
    }

    public void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
    }

    public void Clear()
    {
        Active = null;
        DisplayName = string.Empty;
    }
}
