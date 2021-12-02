using System.IO;
using System.Reflection;

namespace NuExporter;

static class Resources
{
    internal static string GetResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream stream = assembly.GetManifestResourceStream(resourceName);
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
