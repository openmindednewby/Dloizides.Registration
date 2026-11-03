using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Model;
using Dloizides.Registration.Tests.Support;
using Shouldly;

namespace Dloizides.Registration.Tests;

public sealed class AcReg8PackageShapeTests
{
    [Fact]
    public void PublicInterfaces_WhenListed_AreExactlyTheFivePorts()
    {
        var expected = new[]
        {
            nameof(IIdentityProvisioner),
            nameof(IRegistrationResultEnricher),
            nameof(IRegistrationScreen),
            nameof(ITenantProvisioner),
            nameof(IVerificationChannel),
        };

        var found = typeof(IRegistrationScreen).Assembly.GetExportedTypes()
            .Where(t => t.IsInterface)
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal);

        found.ShouldBe(expected);
    }

    [Fact]
    public void TenantProvisionResult_WhenInspected_CarriesNoTenantId()
    {
        var names = typeof(TenantProvisionResult).GetProperties().Select(p => p.Name);

        var carriesId = names.Contains("TenantId", StringComparer.Ordinal);

        carriesId.ShouldBeFalse();
    }

    [DenylistFact]
    public void PackageFiles_WhenScanned_NameNoDeniedWord()
    {
        var root = PackageRoot();
        var names = PackageDenylist.LoadForThisRun();

        var hits = PackageDenylist.FilesNaming(root, names);

        PackageDenylist.ScannableFiles(root).Count.ShouldBeGreaterThan(30);
        hits.ShouldBeEmpty();
    }

    [DenylistFact]
    public void FilesNaming_WithPlantedListedName_FindsIt()
    {
        var names = PackageDenylist.LoadForThisRun();
        var root = Directory.CreateTempSubdirectory("denylist-probe").FullName;
        var planted = Path.Combine(root, "Planted.cs");
        File.WriteAllText(planted, $"public sealed class {names[0]}Screen {{ }}");

        var hits = PackageDenylist.FilesNaming(root, names);

        hits.ShouldBe([planted]);
        Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData("class AbqScreen { }", true)]
    [InlineData("class MyAbqScreen { }", true)]
    [InlineData("the abq service", true)]
    [InlineData("ABQ_KEY", true)]
    [InlineData("class Yabq { } seabqless", false)]
    public void FilesNaming_WithShortNameInText_MatchesOnlyWordParts(string text, bool expected)
    {
        var root = Directory.CreateTempSubdirectory("denylist-short").FullName;
        File.WriteAllText(Path.Combine(root, "Sample.cs"), text);

        var hits = PackageDenylist.FilesNaming(root, ["abq"]);

        (hits.Count == 1).ShouldBe(expected);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void FilesNaming_WithPlantedLongNameInsideIdentifier_FindsIt()
    {
        var root = Directory.CreateTempSubdirectory("denylist-long").FullName;
        File.WriteAllText(Path.Combine(root, "Sample.cs"), "class WidgetqScreen { }");

        var hits = PackageDenylist.FilesNaming(root, ["widgetq"]);

        hits.Count.ShouldBe(1);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void FilesNaming_WithPlantedNameInFilenameOnly_FindsIt()
    {
        var root = Directory.CreateTempSubdirectory("denylist-file").FullName;
        File.WriteAllText(Path.Combine(root, "WidgetqScreen.cs"), "class Clean { }");

        var hits = PackageDenylist.FilesNaming(root, ["widgetq"]);

        hits.Count.ShouldBe(1);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void SkipReason_WhenNoListAndNotRequired_Skips()
    {
        var empty = Directory.CreateTempSubdirectory("denylist-skip").FullName;

        var reason = PackageDenylist.SkipReason(_ => null, empty);

        reason.ShouldNotBeNull();
        Directory.Delete(empty);
    }

    [Fact]
    public void SkipReason_WhenNoListAndRequired_RunsSoItFails()
    {
        var empty = Directory.CreateTempSubdirectory("denylist-required").FullName;

        var reason = PackageDenylist.SkipReason(
            variable => string.Equals(variable, PackageDenylist.RequiredVariable, StringComparison.Ordinal) ? "1" : null,
            empty);

        reason.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => PackageDenylist.Load(_ => null, empty));
        Directory.Delete(empty);
    }

    [Fact]
    public void Load_WithListVariable_ReadsIt()
    {
        var empty = Directory.CreateTempSubdirectory("denylist-env").FullName;

        var names = PackageDenylist.Load(variable => string.Equals(variable, PackageDenylist.ListVariable, StringComparison.Ordinal) ? "alpha, beta" : null, empty);

        names.ShouldBe(["alpha", "beta"]);
        Directory.Delete(empty);
    }

    [Fact]
    public void PublishScript_WhenRead_RunsDenylistTestAsRequired()
    {
        var script = File.ReadAllText(Path.Combine(PackageRoot(), "publish.ps1"));

        var requires = script.Contains($"$env:{PackageDenylist.RequiredVariable} = '1'", StringComparison.Ordinal);
        var runs = script.Contains(nameof(PackageFiles_WhenScanned_NameNoDeniedWord), StringComparison.Ordinal);

        requires.ShouldBeTrue();
        runs.ShouldBeTrue();
        script.ShouldContain("Passed:");
    }

    [Fact]
    public void CiWorkflow_WhenRead_FailsOnZeroTestsAndReadsOnly()
    {
        var workflow = File.ReadAllText(Path.Combine(PackageRoot(), ".github", "workflows", "ci.yml"));

        var failsOnZero = workflow.Contains("grep -Eq 'Passed:", StringComparison.Ordinal);
        var readsOnly = workflow.Contains("contents: read", StringComparison.Ordinal);

        failsOnZero.ShouldBeTrue();
        readsOnly.ShouldBeTrue();
    }

    private static string PackageRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dloizides.Registration.slnx")))
            directory = directory.Parent;
        directory.ShouldNotBeNull();
        return directory.FullName;
    }
}
