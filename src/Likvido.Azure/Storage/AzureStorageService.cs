﻿using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web;

namespace Likvido.Azure.Storage
{
    public class AzureStorageService : IAzureStorageService
    {
        private readonly BlobContainerClient blobContainerClient;
        private readonly StorageConfiguration storageConfiguration;

        public AzureStorageService(StorageConfiguration storageConfiguration, string containerName)
        {
            this.storageConfiguration = storageConfiguration;

            var blobServiceClient = new BlobServiceClient(storageConfiguration.ConnectionString);
            blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
        }

        public async Task DeleteAsync(Uri uri)
        {
            var absolutePath = uri.AbsolutePath;
            if (absolutePath.StartsWith("/"))
            {
                absolutePath = absolutePath.Substring(1);
            }

            if (absolutePath.StartsWith(blobContainerClient.Name))
            {
                absolutePath = absolutePath.Substring(blobContainerClient.Name.Length + 1);
            }

            await DeleteAsync(absolutePath).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string key)
        {
            var blob = blobContainerClient.GetBlobClient(HttpUtility.UrlDecode(key));
            await blob.DeleteIfExistsAsync().ConfigureAwait(false);
        }

        public IEnumerable<Uri> Find(string prefix)
        {
            foreach (var blob in blobContainerClient.GetBlobs(BlobTraits.None, BlobStates.None, prefix))
            {
                yield return blobContainerClient.GetBlobClient(blob.Name)?.Uri;
            }
        }

        public Uri Rename(string tempFileName, string fileName)
        {
            var existingBlob = blobContainerClient.GetBlobClient(tempFileName);

            if (existingBlob?.Exists() == true)
            {
                var newBlob = blobContainerClient.GetBlobClient(fileName);
                newBlob.StartCopyFromUri(existingBlob.Uri);

                return newBlob.Uri;
            }

            return null;
        }

        public Uri Set(string key, Stream content, bool overwrite = true, Dictionary<string, string> metadata = null)
        {
            return Set(key, content, overwrite, 0, metadata);
        }

        private Uri Set(string key, Stream content, bool overwrite = true, int iteration = 0, Dictionary<string, string> metadata = null)
        {
            content.Seek(0, SeekOrigin.Begin);

            string duplicateAwareKey = key;
            if (!overwrite)
            {
                duplicateAwareKey = (iteration > 0) ?
                    $"{Path.GetDirectoryName(key).Replace('\\', '/')}/{Path.GetFileNameWithoutExtension(key)}({iteration.ToString()}){Path.GetExtension(key)}"
                    : key;
            }

            var blob = blobContainerClient.GetBlobClient(HttpUtility.UrlDecode(duplicateAwareKey));

            try
            {
                blob.Upload(content, overwrite: overwrite);
                if (metadata != null)
                {
                    blob.SetMetadata(metadata);
                }
            }
            catch (RequestFailedException ex)
            {
                if (ex.Status == (int)System.Net.HttpStatusCode.Conflict)
                {
                    return Set(key, content, overwrite, ++iteration, metadata);
                }
            }

            return blob.Uri;
        }

        public async Task<MemoryStream> GetAsync(Uri uri)
        {
            return await GetAsync(GetBlobNameFromUri(uri)).ConfigureAwait(false);
        }

        public async Task<MemoryStream> GetAsync(string blobName)
        {
            try
            {
                var stream = new MemoryStream();
                var blob = blobContainerClient.GetBlobClient(blobName);
                await blob.DownloadToAsync(stream).ConfigureAwait(false);
                stream.Position = 0;
                return stream;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public string GetBlobNameFromUri(Uri uri)
        {
            var path = HttpUtility.UrlDecode(uri.AbsolutePath);
            var containerNameIndex = path.IndexOf(blobContainerClient.Name);

            if (containerNameIndex >= 0)
            {
                return path.Substring(containerNameIndex + blobContainerClient.Name.Length + 1);
            }

            throw new ArgumentException("The provided URI does not belong to the container of this service");
        }

        public async Task<IDictionary<string, string>> GetMetadataAsync(string key)
        {
            return await GetMetadataAsync(blobContainerClient.GetBlobClient(key)).ConfigureAwait(false);
        }

        public async Task<IDictionary<string, string>> GetMetadataAsync(Uri uri)
        {
            if (uri.AbsolutePath.Contains(blobContainerClient.Name))
            {
                return await GetMetadataAsync(new BlobClient(uri)).ConfigureAwait(false);
            }

            return null;
        }

        public async Task<IDictionary<string, string>> GetMetadataAsync(BlobClient blob)
        {
            var blobProperties = await blob.GetPropertiesAsync().ConfigureAwait(false);

            return blobProperties.Value.Metadata;
        }

        public async Task<Uri> RenameAsync(string tempFileName, string fileName)
        {
            var existingBlob = blobContainerClient.GetBlobClient(tempFileName);
            if (existingBlob?.Exists())
            {
                var newBlob = blobContainerClient.GetBlobClient(fileName);
                var blobObj = newBlob as BlobClient;
                if (blobObj?.Exists())
                {
                    await blobObj.StartCopyFromUriAsync(existingBlob.Uri).ConfigureAwait(false);
                }
                return newBlob.Uri;
            }
            return null;
        }

        public async Task<Uri> SetAsync(string key, Stream content, string friendlyName = null, bool overwrite = true, Dictionary<string, string> metadata = null)
        {
            return await SetAsync(key, content, friendlyName, overwrite, 0, metadata).ConfigureAwait(false);
        }

        private async Task<Uri> SetAsync(string key, Stream content, string friendlyName = null, bool overwrite = true, int iteration = 0, Dictionary<string, string> metadata = null)
        {
            content.Seek(0, SeekOrigin.Begin);
            string duplicateAwareKey = key;
            if (!overwrite)
            {
                duplicateAwareKey = (iteration > 0) ?
                    $"{Path.GetDirectoryName(key).Replace('\\', '/')}/{Path.GetFileNameWithoutExtension(key)}({iteration.ToString()}){Path.GetExtension(key)}"
                    : key;
            }

            var blob = blobContainerClient.GetBlobClient(HttpUtility.UrlDecode(duplicateAwareKey));

            try
            {
                await blob.UploadAsync(content, overwrite: overwrite).ConfigureAwait(false);
                if (metadata != null)
                {
                    await blob.SetMetadataAsync(metadata).ConfigureAwait(false);
                }
            }
            catch (RequestFailedException ex)
            {
                if (ex.Status == (int)System.Net.HttpStatusCode.Conflict)
                {
                    return await SetAsync(key, content, friendlyName, overwrite, ++iteration, metadata).ConfigureAwait(false);
                }
            }

            if (!string.IsNullOrWhiteSpace(friendlyName))
            {
                // Get the existing properties
                BlobProperties properties = await blob.GetPropertiesAsync().ConfigureAwait(false);

                var headers = new BlobHttpHeaders
                {
                    ContentDisposition = $"attachment; filename={friendlyName}",
                    ContentType = "application/octet-stream",

                    // Populate remaining headers with 
                    // the pre-existing properties
                    CacheControl = properties.CacheControl,
                    ContentEncoding = properties.ContentEncoding,
                    ContentHash = properties.ContentHash
                };

                // Set the blob's properties.
                await blob.SetHttpHeadersAsync(headers);
            }

            return blob.Uri;
        }

        public async Task<string> GetBlobSasUriAsync(string url)
        {
            EnsureDomainIsAllowed(url);
            var (accountName, accountKey) = storageConfiguration.GetStorageAccountInfo();
            var blobClient = new BlobClient(new Uri(url), credential: new StorageSharedKeyCredential(accountName, accountKey));
            var exist = await blobClient.ExistsAsync().ConfigureAwait(false);
            if (!exist)
            {
                return null;
            }

            //  Defines the resource being accessed and for how long the access is allowed.
            var blobSasBuilder = new BlobSasBuilder
            {
                ExpiresOn = DateTime.UtcNow.AddMinutes(1)
            };

            //  Defines the type of permission.
            blobSasBuilder.SetPermissions(BlobSasPermissions.Read);
            var sasBlobToken = blobClient.GenerateSasUri(blobSasBuilder);
            return sasBlobToken.AbsoluteUri;
        }

        private void EnsureDomainIsAllowed(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Url cannot be null or empty");
            }

            var uri = new Uri(url);
            // Azure blob storage url must be in the format https://<account>.blob.core.windows.net/<container>/<blob>
            if (!uri.Host.ToLower().EndsWith("core.windows.net"))
            {
                throw new InvalidOperationException($"Url must be a blob storage url. The domain {uri.Host} is not allowed");
            }
        }
    }
}
