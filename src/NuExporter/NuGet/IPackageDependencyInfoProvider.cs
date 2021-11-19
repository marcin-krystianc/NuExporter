using System.Threading.Tasks;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NuExporter.NuGet
{
    public interface IPackageDependencyInfoProvider
    {
        Task<FindPackageByIdDependencyInfo> GetDependencyInfoAsync(PackageIdentity packageIdentity);
    }
}
