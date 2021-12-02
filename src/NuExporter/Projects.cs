using System.IO;

namespace NuExporter;

public static class Projects
{
    public static string ManagePackageVersionsCentrally = "ManagePackageVersionsCentrally";
    public static string TargetFramework = "TargetFramework";
    public static string TargetFrameworks = "TargetFrameworks";
    public static string PackageVersion = "PackageVersion";
    public static string Version = "Version";
    public static string PackageReference = "PackageReference";
    public static string ProjectReference = "ProjectReference";
    public static string MicrosoftNETSdk = "Microsoft.NET.Sdk";
    public static string AssemblyName = "AssemblyName";
    public static string NuspecFile = "NuspecFile";
    public static string NoBuild = "NoBuild";
    public static string IncludeBuildOutput = "IncludeBuildOutput";
    public static string PackageOutputPath = "PackageOutputPath";
    public static string DisableImplicitFSharpCoreReference = "DisableImplicitFSharpCoreReference";
    public static string DisableImplicitSystemValueTupleReference = "DisableImplicitSystemValueTupleReference";

    // Same as ProjectRootElement.Create() but without all its mess (unnecessary xml version and namespace).
    public static void CreateEmptyProject(string path)
    {
        File.WriteAllText(path, "<Project/>");
    }
}
