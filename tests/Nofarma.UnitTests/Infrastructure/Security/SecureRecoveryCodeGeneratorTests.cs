using Nofarma.Infrastructure.Security;

namespace Nofarma.UnitTests.Infrastructure.Security;

public sealed class SecureRecoveryCodeGeneratorTests
{
    [Fact]
    public void GenerateUsesFiveReadableGroupsWithoutAmbiguousCharacters()
    {
        var generator = new SecureRecoveryCodeGenerator();

        string code = generator.Generate();
        string compact = code.Replace("-", string.Empty, StringComparison.Ordinal);

        Assert.Matches("^[A-HJ-NP-Z2-9]{4}(-[A-HJ-NP-Z2-9]{4}){4}$", code);
        Assert.Equal(20, compact.Length);
        Assert.DoesNotContain('0', code);
        Assert.DoesNotContain('1', code);
        Assert.DoesNotContain('I', code);
        Assert.DoesNotContain('O', code);
    }
}
