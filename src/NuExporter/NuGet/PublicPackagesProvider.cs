using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace NuExporter.NuGet
{
    public class PublicPackagesProvider : IPublicPackagesProvider
    {
        private readonly SourceRepository _repository;
        private readonly SourceCacheContext _cacheContext = new();

        public PublicPackagesProvider(string publicSource)
        {
            var source = new PackageSource(publicSource);
            var providers = Repository.Provider.GetCoreV3();
            _repository = new SourceRepository(source, providers);
        }

        public async Task<bool> IsPublicAsync(string packageId)
        {
            var resource = await _repository.GetResourceAsync<MetadataResource>();
            var latestVersion = await resource.GetLatestVersion(packageId, true, false, _cacheContext,
                NullLogger.Instance, CancellationToken.None);

            return latestVersion != null;
        }
    }
}
