namespace DigitalBrain.Runtime.Orleans;

/// <summary>Protects the master key at rest in <c>&lt;Home&gt;/secrets/master.key</c>.</summary>
public interface IMachineKeyProtector
{
    /// <summary>The bytes written to the key file.</summary>
    byte[] Protect(byte[] key);

    /// <summary>The key from the key file's bytes.</summary>
    byte[] Unprotect(byte[] stored);
}

/// <summary>The protector for this machine.</summary>
public static class MachineKeyProtectors
{
    /// <summary>DPAPI (current user) on Windows; an owner-only (0600) key file elsewhere.</summary>
    public static IMachineKeyProtector ForCurrentPlatform()
        => OperatingSystem.IsWindows() ? new DpapiMachineKeyProtector() : new KeyFileMachineKeyProtector();
}
