using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NuExporter.NuGet
{
    public class PackageDependencyInfoProvider : IPackageDependencyInfoProvider
    {
        private readonly List<SourceRepository> _repositories;
        private readonly SourceCacheContext _cacheContext = new ();

        public PackageDependencyInfoProvider()
        {
            var providers = Repository.Provider.GetCoreV3();
            var settings = global::NuGet.Configuration.Settings.LoadDefaultSettings(null);
            var sources = PackageSourceProvider.LoadPackageSources(settings);
            _repositories = sources
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.IsHttp)
                .Select(x => new SourceRepository(x, providers))
                .ToList();
        }

        public async Task<FindPackageByIdDependencyInfo> GetDependencyInfoAsync(PackageIdentity packageIdentity)
        {
            foreach (var repository in _repositories)
            {
                var resource = await repository.GetResourceAsync<FindPackageByIdResource>();
                var dependencyInfo = await resource.GetDependencyInfoAsync(packageIdentity.Id, packageIdentity.Version, new SourceCacheContext(),
                    NullLogger.Instance, CancellationToken.None);

                if (dependencyInfo != null)
                    return dependencyInfo;
            }

            return null;
        }
    }
}
