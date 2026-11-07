using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace HiddenStock.Core;

public static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn, string szDataDescr, ref DataBlob pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn, IntPtr ppszDataDescr, ref DataBlob pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] data, byte[]? entropy = null) => Call(true, data, entropy);

    public static byte[] Unprotect(byte[] data, byte[]? entropy = null) => Call(false, data, entropy);

    private static byte[] Call(bool protect, byte[] data, byte[]? entropy)
    {
        var inPtr = Marshal.AllocHGlobal(Math.Max(1, data.Length));
        var entropyPtr = IntPtr.Zero;
        try
        {
            Marshal.Copy(data, 0, inPtr, data.Length);
            var inBlob = new DataBlob { cbData = data.Length, pbData = inPtr };
            var entropyBlob = default(DataBlob);

            if (entropy is { Length: > 0 })
            {
                entropyPtr = Marshal.AllocHGlobal(entropy.Length);
                Marshal.Copy(entropy, 0, entropyPtr, entropy.Length);
                entropyBlob = new DataBlob { cbData = entropy.Length, pbData = entropyPtr };
            }

            DataBlob outBlob;
            var ok = protect
                ? CryptProtectData(ref inBlob, "HiddenStock", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, out outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, out outBlob);

            if (!ok)
                throw new CryptographicException("DPAPI вернул ошибку " + Marshal.GetLastWin32Error());

            try
            {
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                LocalFree(outBlob.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            if (entropyPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(entropyPtr);
        }
    }
}
