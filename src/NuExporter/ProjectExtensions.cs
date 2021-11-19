using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Evaluation;

namespace NuExporter;

public static class ProjectExtensions
{
    public static bool IsImplicitlyDefined(this ProjectItem self)
    {
        var isImplicitlyDefined = self.Metadata.SingleOrDefault(x => x.Name == "IsImplicitlyDefined");
        return isImplicitlyDefined?.EvaluatedValue == "true";
    }

    public static string VersionString(this ProjectItem self)
    {
        return self.Metadata("Version")?.EvaluatedValue;
    }

    public static ProjectMetadata Metadata(this ProjectItem self, string name)
    {
        return self.Metadata.SingleOrDefault(x => x.Name == "Version");
    }

    public static bool IsCentralPackageVersionManagementEnabled(this Project project)
    {
        return "true".Equals(project.GetPropertyValue(Projects.ManagePackageVersionsCentrally), StringComparison.OrdinalIgnoreCase);
    }
}
