// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Metaplay.Server.AdminApi
{
    public class InMemoryFileInfo : IFileInfo
    {
        string  _fileName;
        byte[]  _bytes;

        public InMemoryFileInfo(string fileName, byte[] bytes)
        {
            _fileName = fileName;
            _bytes = bytes;
        }

        public bool             Exists              => true;
        public bool             IsDirectory         => false;
        public DateTimeOffset   LastModified        => DateTimeOffset.UtcNow;
        public long             Length              => _bytes.Length;
        public string           Name                => _fileName;
        public string           PhysicalPath        => null;
        public Stream           CreateReadStream()  => new MemoryStream(_bytes);
    }

    public class InMemoryFileProvider : IFileProvider
    {
        Dictionary<string, byte[]> _files;

        public InMemoryFileProvider(Dictionary<string, byte[]> files)
        {
            _files = files;
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
          return NotFoundDirectoryContents.Singleton;
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            // Curling 'http://localhost:5550' or 'http://localhost:5550/' for some reason causes subpath to be '//index.html'
            if (subpath.StartsWith("//", StringComparison.Ordinal))
                subpath = subpath.Substring(1);

            if (_files.TryGetValue(subpath, out byte[] bytes))
                return new InMemoryFileInfo(Path.GetFileName(subpath), bytes);

            return new NotFoundFileInfo(subpath);
        }

        public IChangeToken Watch(string filter)
        {
            return NullChangeToken.Singleton;
        }
    }

    /// <summary>
    /// Fallback for the pre-built dashboard files. Shows an error guiding the user to get the dashboard up and running.
    /// </summary>
    public static class DashboardFallback
    {
        public static IFileProvider CreateFallbackFileProvider(string webRootPath)
        {
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>
            {
                { "/index.html", Encoding.UTF8.GetBytes(@$"
<html>
  <head>
    <title>Dashboard Error</title>
    <style>
      code {{
        background: #e0e0e0;
        padding: 2px;
      }}
    </style>
  </head>
  <body>
    <h1>ERROR: Pre-built Metaplay LiveOps Dashboard not found!</h1>
    <p>The pre-built LiveOps Dashboard was not found in its expected directory <code>{webRootPath}</code>! There are two ways to fix this:</p>
    <ol>
      <li>
        <p>When running locally, it is recommended to serve the Dashboard in development mode with:
          <code>MyGame/Backend/Dashboard$ pnpm install --frozen-lockfile && pnpm dev</code>
        </p>
        <p>The Dashboard can then be accessed from <a href='http://localhost:5551'>http&colon;//localhost:5551</a>.</p>
      </li>
      <li>
        <p>Alternatively, the Dashboard can be pre-built with:
          <code>MyGame/Backend/Dashboard$ pnpm install --frozen-lockfile && pnpm build</code>
        </p>
        <p>After the build has succeeded, restart the game server and refresh this page.</p>
      </li>
    </ol>
  </body>
</html>")
                }
            };

            return new InMemoryFileProvider(files);
        }
    }
}
