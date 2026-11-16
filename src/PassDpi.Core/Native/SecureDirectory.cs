using System.Security.AccessControl;
using System.Security.Principal;

namespace PassDpi.Core.Native;

/// <summary>
/// Creates directories with an explicit, non-inherited ACL so a standard user can never
/// write into folders the SYSTEM service reads executables or config from.
/// </summary>
public static class SecureDirectory
{
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    /// <param name="usersCanRead">Program Files: users need read+execute to run the UI. ProgramData: no access at all.</param>
    public static void Ensure(string path, bool usersCanRead)
    {
        var info = new DirectoryInfo(path);
        var sec = new DirectorySecurity();
        sec.SetOwner(Admins);
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        if (usersCanRead)
            sec.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        if (!info.Exists) info.Create(sec);
        else info.SetAccessControl(sec);
    }

    /// <summary>True when no non-admin principal has write-type rights on the directory.</summary>
    public static bool IsProtected(string path)
    {
        if (!Directory.Exists(path)) return false;
        var sec = new DirectoryInfo(path).GetAccessControl();
        const FileSystemRights write = FileSystemRights.WriteData | FileSystemRights.AppendData |
                                       FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes |
                                       FileSystemRights.Delete | FileSystemRights.ChangePermissions |
                                       FileSystemRights.TakeOwnership | FileSystemRights.DeleteSubdirectoriesAndFiles;
        var trusted = new HashSet<string>
        {
            System.Value, Admins.Value,
            "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", // TrustedInstaller
            "S-1-3-0", // CREATOR OWNER (only applies to creator-owned children)
        };
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (r.AccessControlType != AccessControlType.Allow) continue;
            if (trusted.Contains(r.IdentityReference.Value)) continue;
            if ((r.FileSystemRights & write) != 0) return false;
        }
        return true;
    }
}
