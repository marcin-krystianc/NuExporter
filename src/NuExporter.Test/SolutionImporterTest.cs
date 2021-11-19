using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace NuExporter.Test;

[TestFixture]
public class SolutionImporterTest: BaseFixture
{
    public SolutionImporterTest () : base(copyInputToOutput:true)
    {
    }

    [Test]
    public async Task ImportsSolution()
    {
        var uut = new SolutionImporter();
        var path = Path.Combine(TestDataOutput, "solution.json");
        await uut.ImportSolutionAsync(path);
    }

    internal override void DiffFiles(string fileId, string expectedFile, string actualFile)
    {
        var fileExtension = Path.GetExtension(expectedFile);
        if (fileExtension != ".sln")
            base.DiffFiles(fileId, expectedFile, actualFile);
    }
}
