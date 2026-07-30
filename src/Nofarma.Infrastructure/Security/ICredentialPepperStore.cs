namespace Nofarma.Infrastructure.Security;

public interface ICredentialPepperStore
{
    byte[] GetOrCreate();
}
