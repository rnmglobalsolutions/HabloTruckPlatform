using System.Security.Cryptography;

namespace HabloTruckPlatform.Functions.Infrastructure;

public static class InviteCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I,O,1,0
    private const int DefaultLen = 6;

    public static string NewCode(string prefix = "HT", int len = DefaultLen)
    {
        if (len < 4) len = 4;

        Span<byte> bytes = stackalloc byte[len];
        RandomNumberGenerator.Fill(bytes);

        Span<char> chars = stackalloc char[len];
        for (int i = 0; i < len; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        // HT-AB12CD
        return $"{prefix.ToUpperInvariant()}-{new string(chars)}";
    }

    public static string Normalize(string code)
        => code.Trim().ToUpperInvariant();
}