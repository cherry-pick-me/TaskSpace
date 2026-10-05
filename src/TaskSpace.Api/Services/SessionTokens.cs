using System.Security.Cryptography;
using System.Text;

namespace TaskSpace.Api.Services;

public static class SessionTokens
{
    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool IsWellFormed(string token)
    {
        Span<byte> bytes = stackalloc byte[32];
        return token.Length == 44 && Convert.TryFromBase64String(token, bytes, out var length)
            && length == 32 && Convert.ToBase64String(bytes) == token;
    }
}
