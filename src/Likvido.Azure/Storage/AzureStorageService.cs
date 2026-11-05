using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace Likvido.Azure.Storage
{
    public class AzureStorageService : IAzureStorageService
    {
        // Highest "(n)" suffix tried when overwrite is false and the name is taken.
        internal const int MaxDuplicateSuffix = 100;

        private readonly BlobContainerClient blobContainerClient;
        private readonly Func<Uri, BlobClient> createBlobClient;

        public AzureStorageService(StorageConfiguration storageConfiguration, string containerName)
        {
            var blobServiceClient = new BlobServiceClient(storageConfiguration.ConnectionString);
            blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
            var storageSharedKeyCredential = storageConfiguration.GetStorageSharedKeyCredential();
            createBlobClient = uri => new BlobClient(uri, storageSharedKeyCredential);
        }

        // createBlobClient builds the client for a URL that passed the container check; tests pass a fake.
        internal AzureStorageService(BlobContainerClient blobContainerClient, Func<Uri, BlobClient> createBlobClient)
        {
            this.blobContainerClient = blobContainerClient;
            this.createBlobClient = createBlobClient;
        }

        public async Task DeleteAsync(Uri uri)
        {
            EnsureInOwnContainer(uri, nameof(uri));
            await DeleteAsync(createBlobClient(uri)).ConfigureAwait(false);
        }

        public async Task<bool> DeleteIfExistsAsync(Uri uri)
        {
            EnsureInOwnContainer(uri, nameof(uri));
            var response = await createBlobClient(uri).DeleteIfExistsAsync().ConfigureAwait(false);
            return response.Value;
        }

        public async Task DeleteAsync(string key)
        {
            await DeleteAsync(blobContainerClient.GetBlobClient(HttpUtility.UrlDecode(key))).ConfigureAwait(false);
        }

        public IEnumerable<Uri> Find(string prefix)
        {
            foreach (var blob in blobContainerClient.GetBlobs(BlobTraits.None, BlobStates.None, prefix, default))
            {
                yield return blobContainerClient.GetBlobClient(blob.Name)?.Uri;
            }
        }

        public async Task<Uri> RenameAsync(string tempFileName, string fileName)
        {
            var existingBlob = blobContainerClient.GetBlobClient(tempFileName);

            if (existingBlob == null || !await existingBlob.ExistsAsync().ConfigureAwait(false))
            {
                return null;
            }

            var newBlob = blobContainerClient.GetBlobClient(fileName);
            await newBlob.StartCopyFromUriAsync(existingBlob.Uri).ConfigureAwait(false);

            return newBlob.Uri;
        }

        public async Task<Uri> SetAsync(string key, Stream content, string friendlyName = null, bool overwrite = true, Dictionary<string, string> metadata = null)
        {
            var options = new BlobUploadOptions { Metadata = metadata };
            if (!overwrite)
            {
                // Upload only when no blob exists under this name.
                options.Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All };
            }

            if (!string.IsNullOrWhiteSpace(friendlyName))
            {
                options.HttpHeaders = new BlobHttpHeaders
                {
                    // Sanitize filename to ensure it contains only ASCII characters
                    ContentDisposition = $"attachment; filename={FileNameSanitizer.Sanitize(friendlyName)}",
                    ContentType = "application/octet-stream"
                };
            }

            for (var iteration = 0; ; iteration++)
            {
                var duplicateAwareKey = overwrite || iteration == 0
                    ? key
                    : $"{Path.GetDirectoryName(key)?.Replace('\\', '/')}/{Path.GetFileNameWithoutExtension(key)}({iteration.ToString()}){Path.GetExtension(key)}";

                var blob = blobContainerClient.GetBlobClient(HttpUtility.UrlDecode(duplicateAwareKey));
                content.Seek(0, SeekOrigin.Begin);

                try
                {
                    await blob.UploadAsync(content, options).ConfigureAwait(false);
                    return blob.Uri;
                }
                catch (RequestFailedException ex) when (!overwrite && ex.Status == (int)HttpStatusCode.Conflict && ex.ErrorCode == BlobErrorCode.BlobAlreadyExists)
                {
                    if (iteration >= MaxDuplicateSuffix)
                    {
                        throw new InvalidOperationException($"Could not upload '{key}': the name and all {MaxDuplicateSuffix} numbered alternatives already exist.", ex);
                    }
                }
            }
        }

        public async Task<MemoryStream> GetAsync(Uri uri)
        {
            if (IsInOwnContainer(uri, nameof(uri)))
            {
                return await GetAsync(createBlobClient(uri)).ConfigureAwait(false);
            }

            return null;
        }

        public async Task<MemoryStream> GetAsync(string blobName)
        {
            return await GetAsync(blobContainerClient.GetBlobClient(blobName)).ConfigureAwait(false);
        }

        public async Task<IDictionary<string, string>> GetMetadataAsync(string key)
        {
            return await GetMetadataAsync(blobContainerClient.GetBlobClient(key)).ConfigureAwait(false);
        }

        public async Task<IDictionary<string, string>> GetMetadataAsync(Uri uri)
        {
            if (IsInOwnContainer(uri, nameof(uri)))
            {
                return await GetMetadataAsync(createBlobClient(uri)).ConfigureAwait(false);
            }

            return null;
        }

        public async Task<string> GetBlobSasUriAsync(string url, DateTime? expiresOn = null)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("The blob URL must be an absolute URL.", nameof(url));
            }

            EnsureInOwnContainer(uri, nameof(url));
            var blobClient = createBlobClient(uri);
            var exist = await blobClient.ExistsAsync().ConfigureAwait(false);
            if (!exist)
            {
                return null;
            }

            //  Defines the resource being accessed and for how long the access is allowed.
            var blobSasBuilder = new BlobSasBuilder
            {
                ExpiresOn = expiresOn ?? DateTime.UtcNow.AddMinutes(1)
            };

            //  Defines the type of permission.
            blobSasBuilder.SetPermissions(BlobSasPermissions.Read);
            var sasBlobToken = blobClient.GenerateSasUri(blobSasBuilder);
            return sasBlobToken.AbsoluteUri;
        }

        private static async Task DeleteAsync(BlobClient blobClient)
        {
            await blobClient.DeleteIfExistsAsync().ConfigureAwait(false);
        }

        private static async Task<MemoryStream> GetAsync(BlobClient blobClient)
        {
            var stream = new MemoryStream();
            await blobClient.DownloadToAsync(stream).ConfigureAwait(false);
            stream.Position = 0;

            return stream;
        }

        private static async Task<IDictionary<string, string>> GetMetadataAsync(BlobClient blob)
        {
            var blobProperties = await blob.GetPropertiesAsync().ConfigureAwait(false);

            return blobProperties.Value.Metadata;
        }

        private void EnsureInOwnContainer(Uri uri, string paramName)
        {
            if (!IsInOwnContainer(uri, paramName))
            {
                throw new ArgumentException($"The blob URL is not in the container '{blobContainerClient.Name}'.", paramName);
            }
        }

        // The request for a URL is signed with this account's key and sent to the URL's host, so the URL must be on
        // this service's own endpoint: same scheme, host and port. Throws when it is not; returns false when it is on
        // the endpoint but in another container. The container is the first path segment, or the second on a
        // path-style endpoint such as Azurite's http://127.0.0.1:10000/devstoreaccount1/<container>.
        private bool IsInOwnContainer(Uri uri, string paramName)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(paramName);
            }

            if (!uri.IsAbsoluteUri)
            {
                throw new ArgumentException("The blob URL must be an absolute URL.", paramName);
            }

            var ownUri = blobContainerClient.Uri;
            if (!string.Equals(uri.Scheme, ownUri.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"The blob URL must use {ownUri.Scheme}, the scheme of this storage account's endpoint.", paramName);
            }

            if (!string.Equals(uri.Host, ownUri.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != ownUri.Port)
            {
                throw new ArgumentException($"The blob URL's host '{uri.Authority}' is not this storage account's host '{ownUri.Authority}'.", paramName);
            }

            var ownPath = ownUri.AbsolutePath.TrimEnd('/');
            var accountPath = ownPath.Substring(0, ownPath.LastIndexOf('/') + 1);
            var path = uri.AbsolutePath;
            if (!path.StartsWith(accountPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"The blob URL's path is not under this storage account's path '{accountPath}'.", paramName);
            }

            var containerEnd = path.IndexOf('/', accountPath.Length);
            if (containerEnd < 0 || containerEnd == path.Length - 1)
            {
                throw new ArgumentException("The blob URL must name a blob inside a container.", paramName);
            }

            var container = Uri.UnescapeDataString(path.Substring(accountPath.Length, containerEnd - accountPath.Length));
            return string.Equals(container, blobContainerClient.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
