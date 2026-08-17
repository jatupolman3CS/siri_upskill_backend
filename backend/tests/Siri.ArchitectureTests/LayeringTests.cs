using NetArchTest.Rules;

namespace Siri.ArchitectureTests;

/// <summary>
/// Enforces the layering/boundary rules from backend.md and ARCHITECTURE.md §2. Everything here
/// trivially passes today because modules are still empty stubs — that is expected. These rules
/// exist to catch *real* violations once feature work starts; if one of them goes red later, the
/// design broke a boundary — fix the design, not the test (backend.md: "ถ้าเทสต์ตัวนี้แดงแปลว่า
/// ออกแบบผิด อย่าไปแก้เทสต์").
/// </summary>
public class LayeringTests
{
    [Fact]
    public void Types_OutsideSiriPersistence_DoNotReferenceEntityFrameworkCore()
    {
        var violations = new List<string>();

        foreach (var (projectName, assembly) in ModuleAssemblyCatalog.NonPersistenceAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
                .GetResult();

            if (!result.IsSuccessful)
            {
                violations.Add($"{projectName}: {string.Join(", ", result.FailingTypeNames ?? [])}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "EF Core must stay an implementation detail of Siri.Persistence, but leaked into:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void Types_InModuleDomainNamespaces_DoNotReferenceAspNetCore()
    {
        var violations = new List<string>();

        foreach (var module in ModuleAssemblyCatalog.Modules)
        {
            var domainNamespace = $"{module.RootNamespace}.Domain";

            var result = Types.InAssembly(module.Assembly)
                .That().ResideInNamespaceStartingWith(domainNamespace)
                .Should().NotHaveDependencyOn("Microsoft.AspNetCore")
                .GetResult();

            if (!result.IsSuccessful)
            {
                violations.Add($"{domainNamespace}: {string.Join(", ", result.FailingTypeNames ?? [])}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Domain code must not know about HTTP/ASP.NET Core (backend.md: \"ห้ามให้ Domain รู้จัก EF, HttpContext, หรือ DTO\"), but it does in:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void ModuleAssemblies_DoNotReferenceOtherModules_DomainOrInfrastructureNamespaces()
    {
        var violations = new List<string>();

        foreach (var module in ModuleAssemblyCatalog.Modules)
        {
            var forbiddenNamespaces = ModuleAssemblyCatalog.Modules
                .Where(other => other.RootNamespace != module.RootNamespace)
                .SelectMany(other => new[] { $"{other.RootNamespace}.Domain", $"{other.RootNamespace}.Infrastructure" })
                .ToArray();

            var result = Types.InAssembly(module.Assembly)
                .Should()
                .NotHaveDependencyOnAny(forbiddenNamespaces)
                .GetResult();

            if (!result.IsSuccessful)
            {
                violations.Add($"{module.RootNamespace}: {string.Join(", ", result.FailingTypeNames ?? [])}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "A module must only reach another module through its Contracts/ namespace (backend.md: " +
            "\"Module A เรียก Module B ผ่าน Contracts/ เท่านั้น ห้าม reference Domain/Infrastructure ของ module อื่น\"), but:\n" +
            string.Join("\n", violations));
    }
}
