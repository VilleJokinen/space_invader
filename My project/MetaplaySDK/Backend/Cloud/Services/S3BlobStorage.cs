// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Metaplay.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services
{
    public class S3BlobStorage : IBlobStorage
    {
        IAmazonS3       _client;
        string          _bucketName;
        string          _basePath;
        S3CannedACL     _cannedACL;

        // \todo [petri] deprecate and remove explicit accessKey and secretKey -- assume always using external credentials (like IRSA)
        public S3BlobStorage(string accessKey, string secretKey, string regionName, string bucketName, string basePath, string cannedACL = null)
        {
            RegionEndpoint region = RegionEndpoint.GetBySystemName(regionName);
            bool explicitAccessKeys = !string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey);
            _client     = explicitAccessKeys ? new AmazonS3Client(accessKey, secretKey, region) : new AmazonS3Client(region);
            _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
            _basePath   = basePath ?? throw new ArgumentNullException(nameof(basePath));
            if (cannedACL != null)
            {
                if (cannedACL != "BucketOwnerFullControl")
                    throw new ArgumentException($"Invalid value for cannedACL: {cannedACL}, only 'BucketOwnerFullControl' is supported");
                _cannedACL = S3CannedACL.BucketOwnerFullControl;
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        string GetEntryKeyName(string fileName) => Path.Join(_basePath, fileName);

        #region IBlobRepository

        public async Task<byte[]> GetAsync(string fileName)
        {
            return await ReadObjectAsync(GetEntryKeyName(fileName)).ConfigureAwait(false);
        }

        public async Task PutAsync(string fileName, byte[] bytes, BlobStoragePutHints hintsMaybe = null)
        {
            string contentType = hintsMaybe?.ContentType ?? "binary/octet-stream";
            try
            {
                await WriteObjectAsync(GetEntryKeyName(fileName), bytes, contentType).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Let's inject fileName into the exception for easier debuggability.
                // \note: Use IOException to be consistent with Disk backend.
                throw new IOException($"S3 write to {fileName} failed", ex);
            }
        }

        public Task DeleteAsync(string fileName)
        {
            throw new System.NotImplementedException();
        }

        #endregion // IBlobRepository

        async Task<byte[]> ReadObjectAsync(string keyName)
        {
            GetObjectRequest request = new GetObjectRequest
            {
                BucketName  = _bucketName,
                Key         = keyName
            };

            try
            {
                using (GetObjectResponse response = await _client.GetObjectAsync(request).ConfigureAwait(false))
                {
                    // Return null if object doesn't exist
                    if (response.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                        return null;

                    using (Stream responseStream = response.ResponseStream)
                    using (MemoryStream memStream = new MemoryStream())
                    {
                        //string title = response.Metadata["x-amz-meta-title"]; // Assume you have "title" as medata added to the object.
                        //string contentType = response.Headers["Content-Type"];
                        //Console.WriteLine("Object metadata, Title: {0}", title);
                        //Console.WriteLine("Content type: {0}", contentType);

                        await responseStream.CopyToAsync(memStream).ConfigureAwait(false);
                        byte[] bytes = memStream.ToArray();
                        return bytes;
                    }
                }
            }
            catch (AmazonS3Exception ex)
            {
                // Handle NoSuchKey
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;

                DebugLog.Warning("Failed to GetObject key {KeyName} from bucket {BucketName}: {Exception}", keyName, _bucketName, ex);
                throw;
            }
        }

        async Task WriteObjectAsync(string keyName, byte[] bytes, string contentType)
        {
            using (MemoryStream memStream = new MemoryStream(bytes))
            {
                PutObjectRequest putRequest = new PutObjectRequest
                {
                    BucketName  = _bucketName,
                    Key         = keyName,
                    ContentType = contentType,
                    InputStream = memStream,
                    CannedACL   = _cannedACL
                };
                /*PutObjectResponse response =*/ await _client.PutObjectAsync(putRequest).ConfigureAwait(false);
            }
        }

        public string GetPresignedUrl(string filePath)
        {
            GetPreSignedUrlRequest request = new GetPreSignedUrlRequest
            {
                BucketName  = _bucketName,
                Key         = GetEntryKeyName(filePath),
                Protocol    = Protocol.HTTPS,
                Expires     = DateTime.Now.AddDays(7),
            };

            string preSignedUrl = _client.GetPreSignedURL(request);
            return preSignedUrl;
        }
    }
}
