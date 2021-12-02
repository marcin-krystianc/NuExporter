using System;
using System.Linq;
using Microsoft.Build.Evaluation;

namespace NuExporter;

public static class ProjectExtensions
{
    public static bool IsImplicitlyDefined(this ProjectItem self)
    {
        var isImplicitFSharpCore = self.Xml.ContainingProject?.FullPath.EndsWith("Microsoft.FSharp.NetSdk.props",
            StringComparison.CurrentCultureIgnoreCase);

        var isImplicitlyDefined = self.Metadata.SingleOrDefault(x => x.Name == "IsImplicitlyDefined");
        return isImplicitlyDefined?.EvaluatedValue == "true" || isImplicitFSharpCore == true;
    }

    public static string VersionString(this ProjectItem self)
    {
        return self.Metadata(Projects.Version)?.EvaluatedValue;
    }

    public static ProjectMetadata Metadata(this ProjectItem self, string name)
    {
        return self.Metadata.SingleOrDefault(x => x.Name == Projects.Version);
    }

    public static bool IsCentralPackageVersionManagementEnabled(this Project project)
    {
        return "true".Equals(project.GetPropertyValue(Projects.ManagePackageVersionsCentrally), StringComparison.OrdinalIgnoreCase);
    }
}
