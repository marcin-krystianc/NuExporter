using System.Collections.Generic;

namespace NuExporter.Dto;

public class ProjectDto
{
    public string ProjectName { get; set; }
    public string Sdk { get; set; }
    public Dictionary<string, string> Properties { get; set; }
    public Dictionary<string /*condition*/, List<string>> ProjectReferences { get; set; }
    public Dictionary<string /*condition*/, Dictionary<string, string>> PackageReferences { get; set; }
}
