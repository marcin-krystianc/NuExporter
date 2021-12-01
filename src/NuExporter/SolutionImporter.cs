using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Build.Construction;
using Newtonsoft.Json;
using NuGet.Versioning;
using Serilog;
using NuExporter.Dto;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace NuExporter;

public class SolutionImporter
{
    public async Task ImportSolutionAsync(string jsonPath)
    {
        Log.Information("Importing solution from '{Path}'", jsonPath);

        var json = await File.ReadAllTextAsync(jsonPath);
        var dtos = JsonConvert.DeserializeObject<ProjectDto[]>(json);

        var solutionName = Path.GetFileNameWithoutExtension(jsonPath);
        var workingDirectory = Path.Combine(Path.GetDirectoryName(jsonPath)!, solutionName);
        var processRunner = new ProcessRunner(workingDirectory);

        if (Directory.Exists(workingDirectory))
            Directory.Delete(workingDirectory, true);

        Directory.CreateDirectory(workingDirectory);

        var solutionPath = Path.Combine(workingDirectory, $"{solutionName}.sln");
        Log.Information("Creating '{SolutionPath}'", solutionPath);
        await processRunner.RunAsync("dotnet", "new", "sln", "-n", solutionName);

        var projectPaths = new Queue<string>();
        foreach (var dto in dtos)
        {
            var projectPath = await Task.Run(() => WriteProject(workingDirectory, dto));
            projectPaths.Enqueue(projectPath);
        }

        // What is the command line length limit?
        var cmdLengthLimit = 32000; // https://devblogs.microsoft.com/oldnewthing/20031210-00/?p=41553
        var batch = new List<string>();
        var leftCharacters = cmdLengthLimit;
        while (projectPaths.Any() || batch.Any())
        {
            if (projectPaths.Any())
            {
                var projectPath = Path.GetRelativePath(workingDirectory, projectPaths.Dequeue());
                if (!batch.Any() || leftCharacters > projectPath.Length)
                {
                    batch.Add(projectPath);
                    leftCharacters -= projectPath.Length;
                    continue;
                }
            }

            if (batch.Any())
            {
                await processRunner.RunAsync("dotnet", new[] {"sln", "add"}.Concat(batch).ToArray());
                batch.Clear();
                leftCharacters = cmdLengthLimit;
            }
        }
    }

    private string WriteProject(string directory, ProjectDto projectDto)
    {
        var projectDirectory = Path.Combine(directory, Path.GetFileNameWithoutExtension(projectDto.ProjectName));
        var defaultNamespace = Path.GetFileNameWithoutExtension(projectDto.ProjectName)
            .Replace(".", "_");

        var projectPath = Path.Combine(projectDirectory, projectDto.ProjectName);
        Directory.CreateDirectory(projectDirectory);

        var isCpvmEnabled = "true".Equals(
            projectDto.Properties?.GetValueOrDefault(Projects.ManagePackageVersionsCentrally),
            StringComparison.OrdinalIgnoreCase);

        Projects.CreateEmptyProject(projectPath);
        var project = ProjectRootElement.Open(projectPath);

        project.Sdk = projectDto.Sdk ?? Projects.MicrosoftNETSdk;
        project.ToolsVersion = null;

        string nuspecFile = null;
        projectDto.Properties?.TryGetValue(Projects.NuspecFile, out nuspecFile);

        if (projectDto.Properties != null)
        {
            foreach (var property in projectDto.Properties)
            {
                project.AddProperty(property.Key, property.Value);
            }
        }

        if (nuspecFile == null)
        {
            if (projectDto.ProjectReferences != null)
            {
                foreach (var (condition, projectReferences) in projectDto.ProjectReferences)
                {
                    var itemGroup = project.AddItemGroup();
                    if (!string.IsNullOrWhiteSpace(condition))
                        itemGroup.Condition = condition;

                    foreach (var projectReference in projectReferences)
                    {
                        itemGroup.AddItem(Projects.ProjectReference,
                            $@"..\{Path.GetFileNameWithoutExtension(projectReference)}\{projectReference}");
                    }
                }
            }

            var packageVersionsToExport = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (projectDto.PackageReferences != null)
            {
                foreach (var (condition, packageReference) in projectDto.PackageReferences)
                {
                    var itemGroup = project.AddItemGroup();
                    if (!string.IsNullOrWhiteSpace(condition))
                        itemGroup.Condition = condition;

                    foreach (var (id, version) in packageReference)
                    {
                        var item = itemGroup.AddItem(Projects.PackageReference, $"{id}");
                        if (isCpvmEnabled)
                        {
                            packageVersionsToExport[id] = version;
                        }
                        else
                        {
                            item.AddMetadata(Projects.Version, version ?? string.Empty, true);
                        }
                    }
                }
            }

            if (".fsproj".Equals(Path.GetExtension(projectDto.ProjectName), StringComparison.OrdinalIgnoreCase))
            {
                var code = GetResource("fsharp")
                    .Replace("<namespace>", defaultNamespace);

                File.WriteAllText(Path.Combine(projectDirectory, "MyModule.fs"), code);
                project.AddItem("Compile", "MyModule.fs");
            }
            else
            {
                var code = GetResource("csharp")
                    .Replace("<namespace>", defaultNamespace);

                File.WriteAllText(Path.Combine(projectDirectory, "MyModule.cs"), code);
            }

            if (isCpvmEnabled)
            {
                var packagesPath = Path.GetFullPath(Path.Combine(projectDirectory, "..", "Directory.Packages.props"));
                if (!File.Exists(packagesPath))
                    Projects.CreateEmptyProject(packagesPath);

                var packages = ProjectRootElement.Open(packagesPath);
                var existingVersions = packages.Items
                    .Where(x => x.ItemType == Projects.PackageVersion)
                    .ToDictionary(x => x.Include,
                        x => x.Metadata.Single(x => Projects.Version.Equals(x.Name, StringComparison.OrdinalIgnoreCase))
                            .Value,
                        StringComparer.OrdinalIgnoreCase);

                foreach (var packageVersionToExport in packageVersionsToExport)
                {
                    if (existingVersions.TryGetValue(packageVersionToExport.Key, out var existingVersion))
                    {
                        if (!VersionRange.Parse(existingVersion)
                                .Equals(VersionRange.Parse(packageVersionToExport.Value)))
                        {
                            throw new Exception(
                                $"Conflicting central versions for '{packageVersionToExport.Key}': {existingVersion} != {packageVersionToExport.Value}");
                        }
                    }
                    else
                    {
                        var item = packages.AddItem(Projects.PackageVersion, packageVersionToExport.Key);
                        item.AddMetadata(Projects.Version, packageVersionToExport.Value, true);
                    }
                }

                packages.Save();
            }
        }
        else
        {
            var packageDependencyGroups = new List<PackageDependencyGroup>();
            if (projectDto.PackageReferences != null)
            {
                foreach (var (framework, packageReferences) in projectDto.PackageReferences)
                {
                    var packageDependencies = new List<PackageDependency>();
                    foreach (var (id, version) in packageReferences)
                    {
                        PackageDependency packageDependency;
                        if (string.IsNullOrWhiteSpace(version))
                        {
                            packageDependency = new PackageDependency(id);
                        }
                        else
                        {
                            packageDependency = new PackageDependency(id, VersionRange.Parse(version));
                        }

                        packageDependencies.Add(packageDependency);
                    }

                    packageDependencyGroups.Add(new PackageDependencyGroup(NuGetFramework.Parse(framework),
                        packageDependencies));
                }
            }

            var metadata = new ManifestMetadata
            {
                Id = projectDto.Properties[Projects.AssemblyName],
                Version = NuGetVersion.Parse(projectDto.Properties[Projects.Version]),
                Authors = new []{"NuExporter"},
                Description = "Created with NuExporter",
                DependencyGroups = packageDependencyGroups.Any()
                    ? packageDependencyGroups
                    : null,
            };

            var manifest = new Manifest(metadata);
                var manifestPath = Path.Combine(projectDirectory, nuspecFile);
                using var stream = File.OpenWrite(manifestPath);
                manifest.Save(stream);
        }

        project.Save(projectPath);
        return projectPath;
    }

    private static string GetResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream stream = assembly.GetManifestResourceStream(resourceName);
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
