using System.Security.Cryptography;

public static class HashHelper
{
    public static string ComputeHash(byte[] data, string hashAlgorithm)
    {
        using HashAlgorithm hash = hashAlgorithm.ToUpperInvariant() switch
        {
            "MD5" => MD5.Create(),
            "SHA1" => SHA1.Create(),
            "SHA256" => SHA256.Create(),
            "SHA384" => SHA384.Create(),
            "SHA512" => SHA512.Create(),
            _ => throw new InvalidOperationException("Invalid hash algorithm specified.")
        };

        return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
}
