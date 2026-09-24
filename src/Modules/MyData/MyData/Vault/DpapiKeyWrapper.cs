using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace DigitalBrain.MyData;

public sealed class DpapiKeyWrapper : IKeyWrapper
{
    public string Wrap(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToBase64String(Protect(key));
    }

    public byte[] Unwrap(string wrapped)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wrapped);
        return Unprotect(Convert.FromBase64String(wrapped));
    }

    private static byte[] Protect(byte[] data) => Transform(data, protect: true);

    private static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI key wrapping is available on Windows only; hosted deployments use the Key Vault wrapper.");
        }

        var input = new DataBlob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var entropy = new DataBlob { Length = 0, Data = IntPtr.Zero };
            DataBlob output;
            if (protect)
            {
                if (!CryptProtectData(ref input, "digitalbrain-mydata", ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
                {
                    throw DpapiError();
                }
            }
            else if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
            {
                throw DpapiError();
            }

            return Extract(output);
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
        }
    }

    private static CryptographicException DpapiError() =>
        new($"DPAPI failed with error {Marshal.GetLastWin32Error()}.");

    private static byte[] Extract(DataBlob blob)
    {
        try
        {
            var result = new byte[blob.Length];
            Marshal.Copy(blob.Data, result, 0, blob.Length);
            return result;
        }
        finally
        {
            LocalFree(blob.Data);
        }
    }

    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
