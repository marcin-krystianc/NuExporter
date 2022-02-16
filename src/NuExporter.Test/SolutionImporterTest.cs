using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NuExporter.Test;

[TestFixture]
public class SolutionImporterTest : BaseFixture
{
    public SolutionImporterTest() : base(copyInputToOutput: true)
    {
    }

    [Test]
    public async Task ImportsSolution()
    {
        var uut = new SolutionImporter();
        var path = Path.Combine(TestDataOutput, "solution.json");
        await uut.ImportSolutionAsync(path);
    }

    [Test]
    public async Task ImportsPackages()
    {
        var uut = new SolutionImporter();
        var path = Path.Combine(TestDataOutput, "packages.json");
        await uut.ImportSolutionAsync(path);

        Assert.That(Path.Combine(TestDataOutput, "global-packages"), Does.Exist);
        Assert.That(Path.Combine(TestDataOutput, "artifacts"), Does.Exist);
    }

    internal override void DiffFiles(string fileId, string expectedFile, string actualFile)
    {
        var fileExtension = Path.GetExtension(expectedFile);
        if (fileExtension != ".sln")
            base.DiffFiles(fileId, expectedFile, actualFile);
    }
}
