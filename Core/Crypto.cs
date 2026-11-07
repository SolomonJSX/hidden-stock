using System.Security.Cryptography;
using System.Text;

namespace HiddenStock.Core;

public static class Crypto
{
    public const int KeySize = 32;
    public const int TagSize = 16;
    public const int NonceSize = 12;

    public static byte[] Random(int count)
    {
        var buffer = new byte[count];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }

    public static byte[] Derive(byte[] key, string label) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(label));

    public static byte[] Derive(byte[] key, byte[] label) =>
        HMACSHA256.HashData(key, label);

    public static byte[] Encrypt(byte[] key, byte[] nonce, byte[] plain, byte[] aad, out byte[] tag)
    {
        var cipher = new byte[plain.Length];
        tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, aad);
        return cipher;
    }

    public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] cipher, byte[] tag, byte[] aad)
    {
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, aad);
        return plain;
    }

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    public static byte[] FromHex(string hex) => Convert.FromHexString(hex);
}
