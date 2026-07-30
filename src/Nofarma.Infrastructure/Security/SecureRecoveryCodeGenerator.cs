using System.Security.Cryptography;
using Nofarma.Application.Abstractions;

namespace Nofarma.Infrastructure.Security;

public sealed class SecureRecoveryCodeGenerator : IRecoveryCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int GroupCount = 5;
    private const int GroupLength = 4;

    public string Generate()
    {
        string[] groups = new string[GroupCount];
        for (int groupIndex = 0; groupIndex < GroupCount; groupIndex++)
        {
            char[] characters = new char[GroupLength];
            for (int characterIndex = 0; characterIndex < GroupLength; characterIndex++)
            {
                characters[characterIndex] =
                    Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            groups[groupIndex] = new string(characters);
        }

        return string.Join('-', groups);
    }
}
