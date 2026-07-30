using System.Reflection;

namespace Nofarma.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    public static TheoryData<string, string[]> CoreProjects => new()
    {
        { "Nofarma.Domain", [] },
        { "Nofarma.Contracts", [] },
        { "Nofarma.Application", ["Nofarma.Contracts", "Nofarma.Domain"] }
    };

    [Theory]
    [MemberData(nameof(CoreProjects))]
    public void CoreProjectsReferenceOnlyAllowedNofarmaProjects(
        string assemblyName,
        string[] allowedReferences)
    {
        Assembly assembly = Assembly.Load(assemblyName);
        string[] actual = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Nofarma.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] forbidden = actual
            .Except(allowedReferences, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(forbidden);
    }
}
