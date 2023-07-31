using Azure.Storage.Blobs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Likvido.Azure.Storage
{
    /// <summary>
    /// This is only a mock interface, do not inject it anywhere instead use IAzureStorageServiceFactory for your needs
    /// </summary>
    public interface IAzureStorageService
    {
        Task DeleteAsync(Uri uri);
        IEnumerable<Uri> Find(string prefix);
        Uri Rename(string tempFileName, string fileName);
        Uri Set(string key, Stream content, bool overwrite = true, Dictionary<string, string> metadata = null);
        Task DeleteAsync(string key);
        Task<MemoryStream> GetAsync(Uri uri);
        Task<MemoryStream> GetAsync(string blobName);
        string GetBlobNameFromUri(Uri uri);
        Task<Uri> RenameAsync(string tempFileName, string fileName);
        Task<Uri> SetAsync(string key, Stream content, string friendlyName = null, bool overwrite = true, Dictionary<string, string> metadata = null);
        Task<string> GetBlobSasUriAsync(string url);
        Task<IDictionary<string, string>> GetMetadataAsync(string key);
        Task<IDictionary<string, string>> GetMetadataAsync(Uri uri);
        Task<IDictionary<string, string>> GetMetadataAsync(BlobClient blob);
    }
}