using System.Security.Cryptography;

namespace Quarry.Sample.WebApp.Services;

public sealed class PasswordHasher
{
    private const int SaltSize = 32;
    private const int HashSize = 64;
    private const int Iterations = 600_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    public (byte[] hash, byte[] salt) Hash(string password)
    {
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, Algorithm, HashSize);

        return (hash, salt);
    }

    public bool Verify(string password, byte[] hash, byte[] salt)
    {
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, Algorithm, HashSize);

        return CryptographicOperations.FixedTimeEquals(actual, hash);
    }
}
