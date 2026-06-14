using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AutoMidiPlayer.WPF.Helpers;

public static class Crypt
{
    private static readonly byte[] DefaultKey = Encoding.UTF8.GetBytes("AutoMidiPlayerCacheKey1234567890");
    private static readonly byte[] DefaultIV = Encoding.UTF8.GetBytes("AutoMidiPlyr1234");

    public static byte[] Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = DefaultKey;
        aes.IV = DefaultIV;
        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainText);
        }
        return ms.ToArray();
    }

    public static string Decrypt(byte[] cipherText)
    {
        using var aes = Aes.Create();
        aes.Key = DefaultKey;
        aes.IV = DefaultIV;
        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(cipherText);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd();
    }

    public static byte[] EncryptBytes(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = DefaultKey;
        aes.IV = DefaultIV;
        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(data, 0, data.Length);
            cs.FlushFinalBlock();
        }
        return ms.ToArray();
    }

    public static byte[] DecryptBytes(byte[] encryptedData)
    {
        using var aes = Aes.Create();
        aes.Key = DefaultKey;
        aes.IV = DefaultIV;
        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(encryptedData);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var msOut = new MemoryStream();
        cs.CopyTo(msOut);
        return msOut.ToArray();
    }

    public static string EncryptToBase64(string plainText)
    {
        return Convert.ToBase64String(Encrypt(plainText));
    }

    public static string DecryptFromBase64(string base64CipherText)
    {
        return Decrypt(Convert.FromBase64String(base64CipherText));
    }
}
