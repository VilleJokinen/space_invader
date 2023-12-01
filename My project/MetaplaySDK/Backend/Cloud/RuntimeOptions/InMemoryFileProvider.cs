// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System;
using System.IO;
using System.Text;

namespace Metaplay.Cloud.RuntimeOptions
{
    class InMemoryFileProvider : IFileProvider
    {
        readonly byte[] _value;

        class FileInfo : IFileInfo
        {
            readonly byte[] _value;

            public FileInfo(byte[] value)
            {
                _value = value;
            }

            bool IFileInfo.Exists => true;
            bool IFileInfo.IsDirectory => false;
            DateTimeOffset IFileInfo.LastModified => throw new NotImplementedException();
            long IFileInfo.Length => _value.LongLength;
            string IFileInfo.Name => "file";
            string IFileInfo.PhysicalPath => null;
            Stream IFileInfo.CreateReadStream() => new MemoryStream(_value);
        }

        public InMemoryFileProvider(byte[] content)
        {
            _value = content;
        }

        IDirectoryContents IFileProvider.GetDirectoryContents(string subpath)
        {
            throw new System.NotImplementedException();
        }

        IFileInfo IFileProvider.GetFileInfo(string subpath)
        {
            if (subpath == "file")
                return new FileInfo(_value);
            throw new System.NotImplementedException();
        }

        IChangeToken IFileProvider.Watch(string filter)
        {
            throw new System.NotImplementedException();
        }
    }

    public static class InMemoryFileProviderExtensions
    {
        public static IConfigurationBuilder AddYaml(this IConfigurationBuilder builder, string yamlContents)
        {
            return builder.AddYamlFile(new InMemoryFileProvider(content: Encoding.UTF8.GetBytes(yamlContents)), "file", optional: false, reloadOnChange: false);
        }
    }
}
