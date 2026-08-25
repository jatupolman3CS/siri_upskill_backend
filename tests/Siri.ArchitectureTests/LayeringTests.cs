using NetArchTest.Rules;

namespace Siri.ArchitectureTests;

/// <summary>
/// Enforces the layering/boundary rules from backend.md and ARCHITECTURE.md §2. Most of this still
/// trivially passes because most modules are still empty stubs — Identity (P0-14) is the first
/// exception, with real Domain/Infrastructure code these rules now actually exercise. If one of
/// them goes red, that normally means the design broke a boundary — fix the design, not the test
/// (backend.md: "ถ้าเทสต์ตัวนี้แดงแปลว่าออกแบบผิด อย่าไปแก้เทสต์"). The one time these tests
/// themselves were reshaped (splitting the EF Core check into a Domain-only rule alongside the
/// existing AspNetCore-Domain-only rule) was to match what ARCHITECTURE.md §2 already documented —
/// Infrastructure/ is allowed to know about EF Core, only Domain/ isn't — not to weaken it.
/// </summary>
public class LayeringTests
{
    [Fact]
    public void NonModuleAssemblies_DoNotReferenceEntityFrameworkCore()
    {
        // Hosts and cross-cutting projects (SharedKernel, Api, Workers, Integrations.*) have no
        // Infrastructure/ layer of their own — for these, EF Core must stay entirely inside
        // Siri.Persistence. (Modules are checked separately below, scoped to Domain/ only — see
        // Types_InModuleDomainNamespaces_DoNotReferenceEntityFrameworkCore.)
        var nonModuleAssemblies = ModuleAssemblyCatalog.NonPersistenceAssemblies
            .Where(entry => ModuleAssemblyCatalog.Modules.All(module => module.RootNamespace != entry.Key));

        var violations = new List<string>();

        foreach (var (projectName, assembly) in nonModuleAssemblies)
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
    public void Types_InModuleDomainNamespaces_DoNotReferenceEntityFrameworkCore()
    {
        // Modules ARE allowed to reference EF Core in their own Infrastructure/ — that's where EF
        // configuration classes live by design (ARCHITECTURE.md §2: "Infrastructure/ # repository,
        // EF config, external call"), and AppDbContext picks them up by scanning the loaded module
        // assemblies (see AppDbContext.GetModuleAssemblies) specifically so modules don't need a
        // circular reference back from Siri.Persistence. Domain/ stays persistence-ignorant though
        // (ARCHITECTURE.md: "Domain/ # entity + business rule ล้วน ไม่รู้จัก EF/HTTP"; backend.md:
        // "ห้ามให้ Domain รู้จัก EF, HttpContext, หรือ DTO") — this is the EF-Core counterpart of
        // Types_InModuleDomainNamespaces_DoNotReferenceAspNetCore above, scoped the same way.
        var violations = new List<string>();

        foreach (var module in ModuleAssemblyCatalog.Modules)
        {
            var domainNamespace = $"{module.RootNamespace}.Domain";

            var result = Types.InAssembly(module.Assembly)
                .That().ResideInNamespaceStartingWith(domainNamespace)
                .Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
                .GetResult();

            if (!result.IsSuccessful)
            {
                violations.Add($"{domainNamespace}: {string.Join(", ", result.FailingTypeNames ?? [])}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Domain code must not know about EF Core (backend.md: \"ห้ามให้ Domain รู้จัก EF, HttpContext, หรือ DTO\"), but it does in:\n" +
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
