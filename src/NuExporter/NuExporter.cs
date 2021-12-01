using System.IO;
using System.Threading.Tasks;
using NuExporter.NuGet;

namespace NuExporter;

public class NuExporter
{
    public async Task DoWorkAsync(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.SolutionFile))
        {
            var solutionExporter = new SolutionExporter(
                new PublicPackagesProvider(options.PublicPackagesSource),
                new PackageDependencyInfoProvider()
            );

            await solutionExporter.ProcessAsync(
                anonymize: options.Anonymize!.Value,
                solutionFilePath: options.SolutionFile,
                outputPath: options.OutputPath
            );
        }

        var solutionImporter = new SolutionImporter();
        foreach (var jsonFile in Directory.GetFiles(options.OutputPath, "*.json"))
        {
            await solutionImporter.ImportSolutionAsync(jsonFile);
        }
    }
}
