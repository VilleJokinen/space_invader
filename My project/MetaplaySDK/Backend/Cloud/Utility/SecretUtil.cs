// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Metaplay.Core;
using System;
using System.ComponentModel;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Utility
{
    public class SecretStringConfigConverter : ConfigurationConverterBase
    {
        public override bool CanConvertTo(ITypeDescriptorContext ctx, Type type)
        {
            return false; // return type == typeof(string);
        }

        public override bool CanConvertFrom(ITypeDescriptorContext ctx, Type type)
        {
            return type == typeof(string);
        }

        public override object ConvertTo(ITypeDescriptorContext ctx, CultureInfo ci, object value, Type type)
        {
            throw new NotSupportedException();
        }

        public override object ConvertFrom(ITypeDescriptorContext ctx, CultureInfo ci, object data)
        {
            string source = (string)data;
            return new SecretString(source);
        }
    }

    /// <summary>
    /// Wrapper for a sensitive/secret string value that can be resolved from an external source (AWS Secret Manager, file, etc.).
    /// Note: The secret value is hidden from the dashboard and pretty-printed values, but it is kept in memory in unencrypted form.
    /// Only low-value secrets should be stored using this class. For better security, passwords should be avoided altogether.
    /// </summary>
    [TypeConverter(typeof(SecretStringConfigConverter))]
    public class SecretString
    {
        /// <summary>
        /// Source from where to resolve the value of the secret.
        /// </summary>
        public string Source { get; private set; }

        /// <summary>
        /// Accessor to get/set the value of the secret.
        /// </summary>
        [IgnoreDataMember, Sensitive]
        public string Value
        {
            get
            {
                if (!IsResolved)
                    throw new InvalidOperationException("Trying to use an unresolved secret, make sure you call ResolveValueAsync() before using the value");
                return _value;
            }
        }

        // \todo [petri] obfuscate value in memory
        [IgnoreDataMember]
        string _value;

        public bool IsResolved { get; private set; } = false;

        SecretString() { }
        public SecretString(string source) { Source = source; }

        public async Task ResolveValueAsync(IMetaLogger log)
        {
            _value = await SecretUtil.ResolveSecretAsync(log, Source, defaultToFile: false);
            IsResolved = true;
        }
    }

    public static class SecretUtil
    {
        public const string UnsafePrefix = "unsafe://";
        public const string FilePrefix = "file://";
        public const string AwsSecretsManagerPrefix = "aws-sm://";

        /// <summary>
        /// Resolve a secret from the given source. It can either be one of the supported secret storages (currently
        /// AWS Secrets Managers, others might be added later) or otherwise a path to a file is assumed.
        /// </summary>
        /// <param name="log"></param>
        /// <param name="secretPath"></param>
        /// <returns></returns>
        public static async Task<string> ResolveSecretAsync(IMetaLogger log, string secretPath, bool defaultToFile = true)
        {
            if (secretPath.StartsWith(UnsafePrefix, StringComparison.Ordinal))
            {
                // For unsafe:// secrets, just grab the payload after the prefix
                string secret = secretPath.Substring(UnsafePrefix.Length);
                return secret;
            }
            else if (secretPath.StartsWith(AwsSecretsManagerPrefix, StringComparison.Ordinal))
            {
                string awsSmUrl = secretPath.Substring(AwsSecretsManagerPrefix.Length);
                try
                {
                    return await GetAwsSecretsManagerSecretAsync(awsSmUrl);
                }
                catch (Exception)
                {
                    log.Error("Failed to resolve secret {AwsSmPath}", awsSmUrl);
                    throw;
                }
            }
            else
            {
                bool isFile = secretPath.StartsWith(FilePrefix, StringComparison.Ordinal);
                if (isFile || defaultToFile)
                {
                    string filePath = isFile ? secretPath.Substring(FilePrefix.Length) : secretPath;
                    return await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                }
                else
                    throw new ArgumentException($"Invalid secret source path '{secretPath}'", nameof(secretPath));
            }
        }

        static async Task<string> GetAwsSecretsManagerSecretAsync(string awsRegionAndSmPath)
        {
            // \todo [petri] this is quite hacky and will probably be changed, but for now, support specifying region with aws-sm://<region>#<path-to-secret>
            string[] parts = awsRegionAndSmPath.Split("#");
            RegionEndpoint region = RegionEndpoint.GetBySystemName(parts[0]);
            var config = new AmazonSecretsManagerConfig { RegionEndpoint = region };
            var client = new AmazonSecretsManagerClient(config);

            var request = new GetSecretValueRequest
            {
                SecretId = parts[1]
            };

            var response = await client.GetSecretValueAsync(request).ConfigureAwait(false);

            return response.SecretString;
        }
    }
}
