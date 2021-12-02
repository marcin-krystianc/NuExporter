using System.Threading.Tasks;

namespace NuExporter.NuGet
{
    public interface IPublicPackagesProvider
    {
        Task<bool> IsPublicAsync(string packageId);
    }
}
