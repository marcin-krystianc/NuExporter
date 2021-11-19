using CommandLine;

namespace NuExporter;

public class Options
{
    [Option("solution-file", Required = true, HelpText = "Solution file to process")]
    public string SolutionFile { get; init; }

    [Option("output-path", Required = true, HelpText = "Where to write anonymized solution")]
    public string OutputPath { get; init; }

    [Option(Default = true, HelpText = "Should projects be anonymized")]
    public bool Anonymize { get; init; }

    [Option("public-packages-source", Default = "https://api.nuget.org/v3/index.json", HelpText = "NuGet source to check which packages are public")]
    public string PublicPackagesSource { get; init; }
}
