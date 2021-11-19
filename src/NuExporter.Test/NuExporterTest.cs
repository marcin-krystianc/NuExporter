using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NuExporter.Test;

public class NuExporterTest : BaseFixture
{
    public NuExporterTest() : base(copyInputToOutput:false)
    {
    }

    [Test]
    public async Task SmokeTest()
    {
        var options = new Options
        {
            Anonymize = true,
            OutputPath = TestDataOutput,
            SolutionFile = Path.Combine(TestDataInput, "Solution.sln"),
            PublicPackagesSource = Path.Combine(TestDataInput, "nuget"),
        };

        var uut = new NuExporter();
        await uut.DoWorkAsync(options);
    }

    internal override void DiffFiles(string fileId, string expectedFile, string actualFile)
    {
        var fileExtension = Path.GetExtension(expectedFile);
        if (fileExtension != ".sln")
            base.DiffFiles(fileId, expectedFile, actualFile);
    }
}
