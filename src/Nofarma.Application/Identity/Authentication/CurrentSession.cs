namespace Nofarma.Application.Identity.Authentication;

public sealed class CurrentSession
{
    public LocalSession? Active { get; private set; }

    public void Activate(LocalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Active = session;
    }

    public void Clear() => Active = null;
}
