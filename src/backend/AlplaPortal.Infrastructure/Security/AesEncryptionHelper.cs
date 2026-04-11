using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AlplaPortal.Infrastructure.Security;

/// <summary>
/// AES-256 encryption helper for sensitive configuration values.
/// Uses a 32-byte key derived from HMAC-SHA256 of the provided passphrase.
/// IV is prepended to the ciphertext for storage.
/// </summary>
public static class AesEncryptionHelper
{
    // Fallback key material — should be overridden via AppConfig:EncryptionKey
    private const string DefaultKeyMaterial = "AlplaPortal_SmtpSettings_AES256_Key_2026";

    public static string Encrypt(string plainText, string? keyMaterial = null)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var key = DeriveKey(keyMaterial ?? DefaultKeyMaterial);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to ciphertext: [16-byte IV][ciphertext]
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string cipherText, string? keyMaterial = null)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        var key = DeriveKey(keyMaterial ?? DefaultKeyMaterial);
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Extract IV from the first 16 bytes
        var iv = new byte[16];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
        aes.IV = iv;

        var cipherBytes = new byte[fullCipher.Length - 16];
        Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Derives a consistent 32-byte (256-bit) key from any passphrase
    /// using HMAC-SHA256.
    /// </summary>
    private static byte[] DeriveKey(string passphrase)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("AlplaPortal_Salt_2026"));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(passphrase));
    }
}
