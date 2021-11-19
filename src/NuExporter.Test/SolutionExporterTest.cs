using System;
using System.IO;
using System.Threading.Tasks;
using FakeItEasy;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NUnit.Framework;
using NuExporter.NuGet;

namespace NuExporter.Test;

[TestFixture]
public class SolutionExporterTest : BaseFixture
{
    private readonly IPublicPackagesProvider _allPublicPackagesProvider = A.Fake<IPublicPackagesProvider>();
    private readonly IPublicPackagesProvider _allPrivatePackagesProvider = A.Fake<IPublicPackagesProvider>();
    private readonly IPackageDependencyInfoProvider _packageDependencyInfoProvider = A.Fake<IPackageDependencyInfoProvider>();

    private readonly PackageIdentity _nuget13 = new("NuGet001", new NuGetVersion("13.0.1"));
    public SolutionExporterTest () : base(copyInputToOutput:false)
    {
    }

    [SetUp]
    public void SetUp()
    {
        A.CallTo(() => _allPublicPackagesProvider.IsPublicAsync(A<string>.Ignored)).Returns(true);
        A.CallTo(() => _allPrivatePackagesProvider.IsPublicAsync(A<string>.Ignored)).Returns(false);
        A.CallTo(() => _packageDependencyInfoProvider.GetDependencyInfoAsync(A<PackageIdentity>.That.IsEqualTo(_nuget13)))
            .Returns(
                new FindPackageByIdDependencyInfo(_nuget13, new[] {
                        new PackageDependencyGroup(NuGetFramework.Parse(".NETStandard2.0"),
                            new PackageDependency[] { new("NuGet003", VersionRange.Parse("2.0.0")) })
                    },
                    Array.Empty<FrameworkSpecificGroup>()));
    }

    [Test]
    public async Task PublicPackages()
    {
        var uut = new SolutionExporter(_allPublicPackagesProvider, _packageDependencyInfoProvider);

        await uut.ProcessAsync(
            anonymize: false,
            solutionFilePath: Path.Combine(TestDataInput, "Solution.sln"),
            outputPath: TestDataOutput
        );
    }

    [Test]
    public async Task PublicPackagesAnonymized()
    {
        var uut = new SolutionExporter(_allPublicPackagesProvider, _packageDependencyInfoProvider);
        await uut.ProcessAsync(
            anonymize: true,
            solutionFilePath: Path.Combine(TestDataInput, "Solution.sln"),
            outputPath: TestDataOutput
        );
    }

    [Test]
    public async Task PrivatePackages()
    {
        var uut = new SolutionExporter(_allPrivatePackagesProvider, _packageDependencyInfoProvider);
        await uut.ProcessAsync(
            anonymize: false,
            solutionFilePath: Path.Combine(TestDataInput, "Solution.sln"),
            outputPath: TestDataOutput
        );
    }

    [Test]
    public async Task PrivatePackagesAnonymized()
    {
        var uut = new SolutionExporter(_allPrivatePackagesProvider, _packageDependencyInfoProvider);
        await uut.ProcessAsync(
            anonymize: true,
            solutionFilePath: Path.Combine(TestDataInput, "Solution.sln"),
            outputPath: TestDataOutput
        );
    }
}
