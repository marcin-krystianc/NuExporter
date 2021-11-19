using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Build.Construction;
using Newtonsoft.Json;
using NuGet.Versioning;
using Serilog;
using NuExporter.Dto;

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

        foreach (var dto in dtos)
        {
            var projectPath = await Task.Run(() => WriteProject(workingDirectory, dto));
            await processRunner.RunAsync("dotnet", "sln", "add", projectPath);
        }
    }

    private string WriteProject(string directory, ProjectDto projectDto)
    {
        var projectDirectory = Path.Combine(directory, Path.GetFileNameWithoutExtension(projectDto.ProjectName));
        var defaultNamespace = Path.GetFileNameWithoutExtension(projectDto.ProjectName);
        var projectPath = Path.Combine(projectDirectory, projectDto.ProjectName);
        if (Directory.Exists(projectDirectory))
            Directory.Delete(projectDirectory, true);

        Directory.CreateDirectory(projectDirectory);

        var isCpvmEnabled = "true".Equals(projectDto.Properties?.GetValueOrDefault(Projects.ManagePackageVersionsCentrally),
            StringComparison.OrdinalIgnoreCase);

        // Avoids mess (unnecessary xml version and namespace) from ProjectRootElement.Create
        File.WriteAllText(projectPath, @"<Project/>");
        var project = ProjectRootElement.Open(projectPath);

        project.Sdk = projectDto.Sdk ?? "Microsoft.NET.Sdk";
        project.ToolsVersion = null;

        if (projectDto.Properties != null)
        {
            foreach (var property in projectDto.Properties)
            {
                project.AddProperty(property.Key, property.Value);
            }
        }

        if (projectDto.ProjectReferences != null)
        {
            foreach (var (condition, projectReferences) in projectDto.ProjectReferences)
            {
                var itemGroup = project.AddItemGroup();
                if (!string.IsNullOrWhiteSpace(condition))
                    itemGroup.Condition = condition;

                foreach (var projectReference in projectReferences)
                {
                    itemGroup.AddItem("ProjectReference", $@"..\{Path.GetFileNameWithoutExtension(projectReference)}\{projectReference}");
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
                    var item = itemGroup.AddItem("PackageReference", $"{id}");
                    if (isCpvmEnabled)
                    {
                        packageVersionsToExport[id] = version;
                    }
                    else
                    {
                        item.AddMetadata("Version", version ?? string.Empty, true);
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

        project.Save(projectPath);

        if (isCpvmEnabled)
        {
            var packagesPath = Path.GetFullPath(Path.Combine(projectDirectory, "..", "Directory.Packages.props"));
            if (!File.Exists(packagesPath))
                File.WriteAllText(packagesPath, @"<Project/>");

            var packages = ProjectRootElement.Open(packagesPath);
            var existingVersions =  packages.Items
                .Where(x => x.ItemType == "PackageVersion")
                .ToDictionary(x => x.Include, x => x.Metadata.Single(x => "Version".Equals(x.Name, StringComparison.OrdinalIgnoreCase)).Value,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var packageVersionToExport in packageVersionsToExport)
            {
                if (existingVersions.TryGetValue(packageVersionToExport.Key, out var existingVersion))
                {
                    if (!VersionRange.Parse(existingVersion).Equals(VersionRange.Parse(packageVersionToExport.Value)))
                    {
                        throw new Exception(
                            $"Conflicting central versions for '{packageVersionToExport.Key}': {existingVersion} != {packageVersionToExport.Value}");
                    }
                }
                else
                {
                    var item = packages.AddItem("PackageVersion", packageVersionToExport.Key);
                    item.AddMetadata("Version", packageVersionToExport.Value, true);
                }
            }

            packages.Save();
        }

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
