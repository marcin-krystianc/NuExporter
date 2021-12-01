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
using NuGet.Packaging;

namespace NuExporter;

public class SolutionExporter
{
    private readonly IPublicPackagesProvider _publicPackagesProvider;
    private readonly IPackageDependencyInfoProvider _packageDependencyInfoProvider;

    private readonly Dictionary<string, string> _propertiesToExport = new(StringComparer.OrdinalIgnoreCase)
        {
            {Projects.ManagePackageVersionsCentrally, "false"},
            {Projects.TargetFramework, ""},
            {Projects.TargetFrameworks, ""},
            {Projects.DisableImplicitFSharpCoreReference, "false"},
            {Projects.DisableImplicitSystemValueTupleReference, "false"},
        };

    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _packagePublicPrivateDictionary =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _packageMappingDictionary =
        new(StringComparer.OrdinalIgnoreCase);

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

        var projects = LoadProjects(projectsToProcess.Select(x => x.AbsolutePath).ToList());
        var (projectDtos, packageDtos, mapping) = await GetProjectsToExport(anonymize, projects);
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

        if (mapping.Any())
        {
            var path = Path.Combine(outputPath, "mapping.txt");
            Log.Information("Writing {Path}", path);

            await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, mapping.Select(x => $"{x.Item1}:{x.Item2}")));
        }
    }

    private async Task<(List<ProjectDto>, List<ProjectDto>, List<(string, string)>)> GetProjectsToExport(bool anonymize, IEnumerable<Project> projects)
    {
        var dtos = new List<ProjectDto>();

        foreach (var project in projects)
        {
            if (project.FullPath.Contains(".fsproj"))
            {
            }

            Dictionary<string, string> packageVersions = null;

            if (project.IsCentralPackageVersionManagementEnabled())
            {
                packageVersions = project.GetItems(Projects.PackageVersion)
                    .ToDictionary(x => x.EvaluatedInclude, x => x.Metadata(Projects.Version).EvaluatedValue,
                        StringComparer.OrdinalIgnoreCase);
            }

            var projectReferences = project.GetItemsIgnoringCondition(Projects.ProjectReference)
                .Where(x => !x.IsImplicitlyDefined())
                .ToLookup(x => x.Xml.Condition ?? string.Empty);

            var packageReferences = project.GetItemsIgnoringCondition(Projects.PackageReference)
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
                Sdk = Projects.MicrosoftNETSdk.Equals(project.Xml.Sdk, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : project.Xml.Sdk ?? string.Empty,

                ProjectReferences = projectReferences.Any()
                    ? projectReferences.ToDictionary(x => x.Key,
                        x => x.Select(projectItem => Path.GetFileName(projectItem.EvaluatedInclude)).ToList())
                    : null,

                PackageReferences = packageReferences.Any()
                    ? packageReferences.ToDictionary(x => x.Key,
                        x => x.DistinctBy(x => x.EvaluatedInclude, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(y => y.EvaluatedInclude, y => y.VersionString() ?? packageVersions?.GetValueOrDefault(y.EvaluatedInclude), StringComparer.OrdinalIgnoreCase))
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

        Dictionary<string, string> projectMapping = null;
        var mapping = new List<(string, string)>();
        if (anonymize)
        {
            projectMapping = allReferencedProjects
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

            mapping.AddRange(projectMapping.Select(x => (x.Key, x.Value)));
        }

        lock (_packageMappingDictionary)
        {
            mapping.AddRange(_packageMappingDictionary.Select(x => (x.Key, x.Value)));
        }

        return (dtos, packageDtos, mapping);
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
        await Parallel.ForEachAsync(packageIds, async (x, _) => await IsPublicPackageAsync(x));

        var exportedPackages = new HashSet<PackageIdentity>();
        var packagesToExport = new Queue<PackageIdentity>();

        foreach (var (id, version) in allReferencedPackages)
        {
            if (await IsPublicPackageAsync(id) || string.IsNullOrWhiteSpace(version))
                continue;

            var versionRange = VersionRange.Parse(version);
            if (!versionRange.HasLowerBound || !versionRange.IsMinInclusive)
                continue;

            var packageIdentity = new PackageIdentity(id, versionRange.MinVersion);
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
                Log.Warning("Cannot find {Package} in any nuget source", packageIdentity);
                continue;
            }

            var mappedId = await MapPackageNameAsync(anonymize, packageIdentity.Id);
            var packageReferencesDictionary = new Dictionary<string, Dictionary<string, string>>();

            foreach (var dependencyGroup in dependencyInfo.DependencyGroups)
            {
                var packageReferences = new Dictionary<string, string>();
                var condition = dependencyGroup.TargetFramework.GetFrameworkString();

                foreach (var packageDependency in dependencyGroup.Packages)
                {
                    var mappedDependencyId = await MapPackageNameAsync(anonymize, packageDependency.Id);
                    packageReferences.Add(mappedDependencyId, packageDependency.VersionRange.ToShortString());

                    if (!await IsPublicPackageAsync(packageDependency.Id))
                    {
                        if (!packageDependency.VersionRange.HasLowerBound || !packageDependency.VersionRange.IsMinInclusive)
                            continue;

                        packagesToExport.Enqueue(new PackageIdentity(packageDependency.Id,
                            packageDependency.VersionRange.MinVersion));
                    }
                }

                if (packageReferences.Any())
                {
                    packageReferencesDictionary.Add(condition, packageReferences);
                }
            }

            if (packageIdentity.Version == null)
                continue;

            var dto = new ProjectDto
            {
                ProjectName = $"{mappedId}_{packageIdentity.Version}.csproj",
                PackageReferences = packageReferencesDictionary.Any() ? packageReferencesDictionary : null,
                Properties = new Dictionary<string, string>
                {
                    {Projects.AssemblyName, mappedId},
                    {Projects.NuspecFile, $"{mappedId}.nuspec"},
                    {Projects.Version, packageIdentity.Version.ToString()},
                    {Projects.TargetFrameworks, "netstandard2.0"},
                    {Projects.NoBuild, "true"},
                    {Projects.IncludeBuildOutput, "false"},
                    {Projects.PackageOutputPath, "../artifacts"},
                },
            };

            packageDtos.Add(dto);
        }

        return packageDtos;
    }

    private IReadOnlyList<Project> LoadProjects(IReadOnlyList<string> paths)
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
