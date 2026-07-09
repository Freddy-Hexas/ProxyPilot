using System.Security.Principal;

namespace ProcessProxyManager.Native;

public sealed class AdministratorDetector
{
    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
