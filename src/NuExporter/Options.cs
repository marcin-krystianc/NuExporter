using CommandLine;

namespace NuExporter;

public class Options
{
    [Option("solution-file", HelpText = "Solution file to process")]
    public string SolutionFile { get; init; }

    [Option("output-path", Required = true, HelpText = "Where to write exported solution")]
    public string OutputPath { get; init; }

    [Option("anonymize", Default = true, HelpText = "Anonymize project and package names")]
    public bool? Anonymize { get; init; }

    [Option("public-packages-source", Default = "https://api.nuget.org/v3/index.json", HelpText = "NuGet source to check which packages are public and which are private")]
    public string PublicPackagesSource { get; init; }
}
