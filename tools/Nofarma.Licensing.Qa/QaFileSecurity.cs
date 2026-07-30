using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Nofarma.Licensing.Qa;

internal static class QaFileSecurity
{
    internal static void ProtectForCurrentUser(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ProtectForCurrentUserWindows(path);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectForCurrentUserWindows(string path)
    {
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException(
                "The current Windows user identity is unavailable.");
        var security = new FileSecurity();
        security.SetOwner(currentUser);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}
