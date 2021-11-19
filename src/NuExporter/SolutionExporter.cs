using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using Newtonsoft.Json;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Serilog;
using NuExporter.Dto;
using NuExporter.NuGet;

namespace NuExporter;

public class SolutionExporter
{
    private readonly IPublicPackagesProvider _publicPackagesProvider;
    private readonly IPackageDependencyInfoProvider _packageDependencyInfoProvider;

    private readonly Dictionary<string, string> _propertiesToExport = new(StringComparer.OrdinalIgnoreCase)
        {
            {Projects.ManagePackageVersionsCentrally, "false"},
            {"TargetFramework", ""},
            {"TargetFrameworks", ""},
        };

    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _packagePublicPrivateDictionary =
        new(StringComparer.CurrentCultureIgnoreCase);

    private readonly Dictionary<string, string> _packageMappingDictionary =
        new(StringComparer.CurrentCultureIgnoreCase);

    private readonly JsonSerializerSettings _serializerSettings = new()
    {
        Formatting = Formatting.Indented, DefaultValueHandling = DefaultValueHandling.Ignore
    };

    public SolutionExporter(IPublicPackagesProvider publicPackagesProvider,
        IPackageDependencyInfoProvider packageDependencyInfoProvider)
    {
        _publicPackagesProvider = publicPackagesProvider;
        _packageDependencyInfoProvider = packageDependencyInfoProvider;
    }

    public async Task ProcessAsync(string outputPath, string solutionFilePath, bool anonymize)
    {
        Log.Information("Exporting {SolutionFile}", solutionFilePath);

        var solutionFile = SolutionFile.Parse(solutionFilePath);
        var projectsToProcess = solutionFile.ProjectsInOrder
            .Where(x => x.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
            .OrderBy(x => x.AbsolutePath)
            .ToList();

        Log.Information("Found {Count} projects", projectsToProcess.Count);

        Directory.CreateDirectory(outputPath);

        var projects = LoadProjects(projectsToProcess.Select(x => x.AbsolutePath));
        var (projectDtos, packageDtos) = await GetProjectsToExport(anonymize, projects);
        if (projectDtos.Any())
        {
            var path = Path.Combine(outputPath, "solution.json");
            Log.Information("Writing {Path}", path);

            await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(projectDtos, _serializerSettings));
        }

        if (packageDtos.Any())
        {
            var path = Path.Combine(outputPath, "packages.json");
            Log.Information("Writing {Path}", path);

            await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(packageDtos, _serializerSettings));
        }
    }

    private async Task<(List<ProjectDto>, List<ProjectDto>)> GetProjectsToExport(bool anonymize, IEnumerable<Project> projects)
    {
        var dtos = new List<ProjectDto>();

        foreach (var project in projects)
        {
            Dictionary<string, string> packageVersions = null;

            if (project.IsCentralPackageVersionManagementEnabled())
            {
                packageVersions = project.GetItems("PackageVersion")
                    .ToDictionary(x => x.EvaluatedInclude, x => x.Metadata("Version").EvaluatedValue,
                        StringComparer.OrdinalIgnoreCase);
            }

            var projectReferences = project.GetItemsIgnoringCondition("ProjectReference")
                .Where(x => !x.IsImplicitlyDefined())
                .ToLookup(x => x.Xml.Condition ?? string.Empty);

            var packageReferences = project.GetItemsIgnoringCondition("PackageReference")
                .Where(x => !x.IsImplicitlyDefined())
                .ToLookup(x => x.Xml.Condition ?? string.Empty);

            var properties = project.Properties
                .Where(x => _propertiesToExport.Keys.Contains(x.Name))
                .Where(x => !x.EvaluatedValue.Equals(_propertiesToExport[x.Name],
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            var projectDto = new ProjectDto
            {
                ProjectName = Path.GetFileName(project.FullPath),
                Sdk = "Microsoft.NET.Sdk".Equals(project.Xml.Sdk, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : project.Xml.Sdk ?? string.Empty,

                ProjectReferences = projectReferences.Any()
                    ? projectReferences.ToDictionary(x => x.Key,
                        x => x.Select(x => Path.GetFileName(x.EvaluatedInclude)).ToList())
                    : null,

                PackageReferences = packageReferences.Any()
                    ? packageReferences.ToDictionary(x => x.Key,
                        x => x.ToDictionary(y => y.EvaluatedInclude,
                            y => y.VersionString() ?? packageVersions?.GetValueOrDefault(y.EvaluatedInclude)))
                    : null,

                Properties = properties.Any()
                    ? properties.ToDictionary(x => x.Name, x => x.EvaluatedValue)
                    : null,
            };

            dtos.Add(projectDto);
        }

        var packageDtos = await GetPackagesToExport(anonymize, dtos);

        var allReferencedProjects = dtos
            .Where(x => x.ProjectReferences != null)
            .SelectMany(x => x.ProjectReferences)
            .SelectMany(x => x.Value)
            .ToList();

        var allProjectNames = dtos
            .Select(x => x.ProjectName)
            .ToList();

        if (anonymize)
        {
            var projectMapping = allReferencedProjects
                .Concat(allProjectNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .Select((x, i) => (x, i))
                .ToDictionary(x => x.x, x => $"Project{x.i + 1}{Path.GetExtension(x.x)}", StringComparer.OrdinalIgnoreCase);

            foreach (var dto in dtos)
            {
                dto.ProjectName = projectMapping[dto.ProjectName];
                dto.ProjectReferences = dto.ProjectReferences?.ToDictionary(kv => kv.Key,
                    kv => kv.Value.Select(x => projectMapping[x]).ToList());

                if (dto.PackageReferences != null)
                {
                    var packageReferences = new Dictionary<string, Dictionary<string, string>>();
                    foreach (var (condition, unmappedPackageReferences) in dto.PackageReferences)
                    {
                        var dependencies = new Dictionary<string, string>();
                        packageReferences.Add(condition, dependencies);
                        foreach (var (unmappedId, version) in unmappedPackageReferences)
                        {
                            var mappedId = await MapPackageNameAsync(anonymize, unmappedId);
                            dependencies.Add(mappedId, version);
                        }
                    }

                    dto.PackageReferences = packageReferences;
                }
            }
        }

        return (dtos, packageDtos);
    }

    private async Task<List<ProjectDto>> GetPackagesToExport(bool anonymize, IEnumerable<ProjectDto> dtos)
    {
        var allReferencedPackages = dtos
            .Where(x => x.PackageReferences != null)
            .SelectMany(x => x.PackageReferences)
            .SelectMany(x => x.Value)
            .ToList();

        var packageIds = allReferencedPackages
            .Select(x => x.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // precompute package privacy
        await Parallel.ForEachAsync(packageIds, async (x, ct) => await IsPublicPackageAsync(x));

        var exportedPackages = new HashSet<PackageIdentity>();
        var packagesToExport = new Queue<PackageIdentity>();

        foreach (var (id, version) in allReferencedPackages.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            if (await IsPublicPackageAsync(id))
                continue;

            var packageIdentity = new PackageIdentity(id, NuGetVersion.Parse(version));
            packagesToExport.Enqueue(packageIdentity);
        }

        var packageDtos = new List<ProjectDto>();
        while (packagesToExport.Any())
        {
            var packageIdentity = packagesToExport.Dequeue();
            if (!exportedPackages.Add(packageIdentity))
                continue;

            var dependencyInfo = await _packageDependencyInfoProvider.GetDependencyInfoAsync(packageIdentity);
            if (dependencyInfo == null)
            {
                Log.Warning("Cannot find {Package} in any nuget source", packageIdentity.Id);
                continue;
            }

            var mappedId = await MapPackageNameAsync(anonymize, packageIdentity.Id);
            var packageReferencesDictionary = new Dictionary<string, Dictionary<string, string>>();
            var projectReferencesDictionary = new Dictionary<string, List<string>>();

            foreach (var dependencyGroup in dependencyInfo.DependencyGroups)
            {
                var packageReferences = new Dictionary<string, string>();
                var projectReferences = new List<string>();
                var targetFramework = dependencyGroup.TargetFramework.GetShortFolderName();
                var condition = $" '$(TargetFramework)' == '{targetFramework}' ";

                foreach (var packageDependency in dependencyGroup.Packages)
                {
                    if (!await IsPublicPackageAsync(packageDependency.Id))
                    {
                        if (packageDependency.VersionRange.HasLowerBound)
                        {
                            packagesToExport.Enqueue(new PackageIdentity(packageDependency.Id,
                                packageDependency.VersionRange.MinVersion));

                            var mappedDependencyId = await MapPackageNameAsync(anonymize, packageDependency.Id);
                            projectReferences.Add($"{mappedDependencyId}_{packageDependency.VersionRange.MinVersion}.csproj");
                        }
                    }
                    else
                    {
                        var mappedDependencyId = await MapPackageNameAsync(anonymize, packageDependency.Id);
                        packageReferences.Add(mappedDependencyId, packageDependency.VersionRange.ToShortString());
                    }
                }

                if (packageReferences.Any())
                {
                    packageReferencesDictionary.Add(condition, packageReferences);
                }

                if (projectReferences.Any())
                {
                    projectReferencesDictionary.Add(condition, projectReferences);
                }
            }

            var dto = new ProjectDto
            {
                ProjectName = $"{mappedId}_{packageIdentity.Version}.csproj",
                PackageReferences = packageReferencesDictionary.Any() ? packageReferencesDictionary : null,
                ProjectReferences = projectReferencesDictionary.Any() ? projectReferencesDictionary : null,
                Properties = new Dictionary<string, string>
                {
                    {"AssemblyName", mappedId},
                    {"Version", packageIdentity.Version.ToString()},
                    {
                        "TargetFrameworks", dependencyInfo.DependencyGroups.Any()
                            ? string.Join(";",
                                dependencyInfo.DependencyGroups.Select(x => x.TargetFramework.GetShortFolderName()))
                            : "netstandard1.0"
                    },
                },
            };

            packageDtos.Add(dto);
        }

        return packageDtos;
    }

    private IReadOnlyList<Project> LoadProjects(IEnumerable<string> paths)
    {
        using (var projectCollection = new ProjectCollection())
        {
            var evaluationContext = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared);
            var projectOptions = new ProjectOptions
            {
                ProjectCollection = projectCollection,
                LoadSettings = ProjectLoadSettings.IgnoreEmptyImports | ProjectLoadSettings.IgnoreInvalidImports |
                               ProjectLoadSettings.RecordDuplicateButNotCircularImports |
                               ProjectLoadSettings.IgnoreMissingImports,
                EvaluationContext = evaluationContext,
            };

            var projects = new ConcurrentDictionary<string, Project>(StringComparer.OrdinalIgnoreCase);

            Project LoadProject(string path) =>
                projects.GetOrAdd((Path.GetFullPath(path)), x => Project.FromFile(x, projectOptions));

            // preload projects in parallel
            Parallel.ForEach(paths, x => LoadProject(x));

            return paths
                .Select(x => LoadProject(x))
                .ToList();
        }
    }

    private async Task<bool> IsPublicPackageAsync(string id)
    {
        return await _packagePublicPrivateDictionary.GetOrAdd(id,
            x => new Lazy<Task<bool>>(() => _publicPackagesProvider.IsPublicAsync(x))).Value;
    }

    private async Task<string> MapPackageNameAsync(bool anonymize, string id)
    {
        if (!anonymize || await IsPublicPackageAsync(id))
            return id;

        lock (_packageMappingDictionary)
        {
            if (!_packageMappingDictionary.TryGetValue(id, out var mappedId))
            {
                mappedId = $"Package{_packageMappingDictionary.Count + 1}";
                _packageMappingDictionary.Add(id, mappedId);
            }

            return mappedId;
        }
    }
}
