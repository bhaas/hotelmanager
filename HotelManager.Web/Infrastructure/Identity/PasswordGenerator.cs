using System.Security.Cryptography;

namespace HotelManager.Web.Infrastructure.Identity;

public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";

    /// <summary>Creates a random password without ambiguous characters (0/O, 1/l/I) that satisfies the identity policy.</summary>
    public static string Create(int length = 14)
    {
        var all = Upper + Lower + Digits;
        var characters = new char[length];
        characters[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        characters[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        characters[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        for (var index = 3; index < length; index++)
        {
            characters[index] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        RandomNumberGenerator.Shuffle(characters.AsSpan());
        return new string(characters);
    }
}
