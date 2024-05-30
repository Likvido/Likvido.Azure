using System.Linq;
using Azure.Storage;

namespace Likvido.Azure.Storage
{
    public class StorageConfiguration
    {
        public string ConnectionString { get; set; }

        internal StorageSharedKeyCredential GetStorageSharedKeyCredential()
        {
            var accountInfo = ConnectionString.Split(';').Where(x => x.Length > 0).Select(x => x.Split(new[] { '=' }, 2)).ToDictionary(x => x[0], x => x[1]);

            return new StorageSharedKeyCredential(accountInfo["AccountName"], accountInfo["AccountKey"]);
        }
    }
}
