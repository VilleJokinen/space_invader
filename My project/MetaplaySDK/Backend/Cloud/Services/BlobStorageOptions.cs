// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services
{
    /// <summary>
    /// Storage backend for BlobStorage. Use S3 in the cloud.
    /// </summary>
    public enum BlobStorageBackend
    {
        None,   // No backend, BlobStorage is disabled
        Disk,   // Store blobs on the local disk
        S3,     // Store blobs in an S3 bucket.
    }

    /// <summary>
    /// Configure general-purpose persistent storage for blobs. Supports separate publicly-readable and private storages.
    /// Primary purposes are:
    /// - Cluster-writable, publicly-readable CDN for files that clients will download (e.g. published SharedGameConfig archive files).
    /// - Cluster-private storage for blobs that aren't appropriate to store in the database.
    /// </summary>
    [RuntimeOptions("BlobStorage", isStatic: true, "Configuration options for the persistent storage.")]
    public class BlobStorageOptions : RuntimeOptionsBase
    {
        [MetaDescription("Which backend to use for persisting files (`None`, `Disk` or `S3`).")]
        public BlobStorageBackend       Backend             { get; private set; } =
            IsServerApplication ? (IsLocalEnvironment ? BlobStorageBackend.Disk : BlobStorageBackend.S3) : BlobStorageBackend.None;

        // For when Backend is Disk
        // \note There's no actual public-private difference in the Disk Backend.
        //       These are only separate for consistency with the public and private buckets of the S3 Backend.
        [MetaDescription("If `Backend` is `Disk`: The folder path into which publicly readable content is written. See also `S3PublicBucketName`.")]
        public string                   DiskPublicPath      { get; private set; } = "bin/PublicBlobStorage";
        [MetaDescription("If `Backend` is `Disk`: The folder path into which private content is written. See also `S3PrivateBucketName`.")]
        public string                   DiskPrivatePath     { get; private set; } = "bin/PrivateBlobStorage";

        // For when Backend is S3
        [MetaDescription("If `Backend` is `S3`: The AWS region to use.")]
        public string                   S3Region            { get; private set; } = null;
        [MetaDescription("If `Backend` is `S3`: Name of the bucket into which publicly readable content is written.")]
        public string                   S3PublicBucketName  { get; private set; } = null;
        [MetaDescription("If `Backend` is `S3`: Name of the bucket into which private content is written.")]
        public string                   S3PrivateBucketName { get; private set; } = null;
        [MetaDescription("If `Backend` is `S3`: The public URL for the CDN.")]
        public string                   S3PublicBucketCdnUrl{ get; private set; } = null;
        [MetaDescription("Deprecated. ~~Explicit AWS/S3 access keys. Leave undefined to use system-level settings, eg, IRSA.~~")]
        public string                   AwsAccessKey        { get; private set; } = null;
        [Sensitive]
        [MetaDescription("Deprecated. ~~Explicit AWS secret key.~~")]
        public string                   AwsSecretKey        { get; private set; } = null;

        public override Task OnLoadedAsync()
        {
            // Validate the options

            switch (Backend)
            {
                case BlobStorageBackend.None:
                    break;

                case BlobStorageBackend.Disk:
                    if (string.IsNullOrEmpty(DiskPublicPath))       throw new InvalidOperationException("Must provide valid BlobStorageOptions.DiskPublicPath when Backend is Disk");
                    if (string.IsNullOrEmpty(DiskPrivatePath))      throw new InvalidOperationException("Must provide valid BlobStorageOptions.DiskPrivatePath when Backend is Disk");
                    break;

                case BlobStorageBackend.S3:
                    if (string.IsNullOrEmpty(S3Region))             throw new InvalidOperationException("Must provide valid BlobStorageOptions.S3Region when Backend is S3");
                    if (string.IsNullOrEmpty(S3PublicBucketName))   throw new InvalidOperationException("Must provide valid BlobStorageOptions.S3PublicBucketName when Backend is S3");
                    if (string.IsNullOrEmpty(S3PrivateBucketName))  throw new InvalidOperationException("Must provide valid BlobStorageOptions.S3PrivateBucketName when Backend is S3");

                    if (!Uri.TryCreate(S3PublicBucketCdnUrl, UriKind.Absolute, out Uri cdnUri))
                        throw new InvalidOperationException($"Must provide valid absolute url BlobStorageOptions.S3PublicBucketCdnUrl when Backend is S3. Got {S3PublicBucketCdnUrl}");
                    if (cdnUri.Scheme != "https")
                        throw new InvalidOperationException($"Must provide valid https url BlobStorageOptions.S3PublicBucketCdnUrl when Backend is S3. Got {S3PublicBucketCdnUrl}");
                    if (!cdnUri.AbsolutePath.EndsWith('/'))
                        throw new InvalidOperationException($"BlobStorageOptions.S3PublicBucketCdnUrl must end with a trailing slash. Got {S3PublicBucketCdnUrl}");
                    break;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Create a <see cref="IBlobStorage"/> backed by the publicly-readable storage.
        /// </summary>
        /// <param name="path">The path inside the public blob storage.</param>
        public IBlobStorage CreatePublicBlobStorage(string path)
        {
            switch (Backend)
            {
                case BlobStorageBackend.None: throw new InvalidOperationException("Cannot create BlobStorage with backend == None");
                case BlobStorageBackend.Disk: return new DiskBlobStorage(Path.Combine(DiskPublicPath, path));
                case BlobStorageBackend.S3:   return new S3BlobStorage(AwsAccessKey, AwsSecretKey, S3Region, S3PublicBucketName, path);
                default:
                    throw new InvalidOperationException($"Invalid BlobStorageBackend.{Backend}");
            }
        }

        /// <summary>
        /// Create a <see cref="IBlobStorage"/> backed by the cluster-private storage.
        /// </summary>
        /// <param name="path">The path inside the private blob storage.</param>
        public IBlobStorage CreatePrivateBlobStorage(string path)
        {
            switch (Backend)
            {
                case BlobStorageBackend.None: throw new InvalidOperationException("Cannot create BlobStorage with backend == None");
                case BlobStorageBackend.Disk: return new DiskBlobStorage(Path.Combine(DiskPrivatePath, path));
                case BlobStorageBackend.S3:   return new S3BlobStorage(AwsAccessKey, AwsSecretKey, S3Region, S3PrivateBucketName, path);
                default:
                    throw new InvalidOperationException($"Invalid BlobStorageBackend.{Backend}");
            }
        }
    }
}
