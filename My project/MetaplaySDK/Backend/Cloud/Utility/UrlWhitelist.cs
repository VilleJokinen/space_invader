// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Metaplay.Cloud.Utility
{
    /// <summary>
    /// Runtime-options parseable list of allowed Url stems. This should be defined in RuntimeOption as:
    /// <code>
    /// [MetaDescription("...")]
    /// public UrlWhitelist MyWhitelist { get; private set; } = UrlWhitelist.DenyAll();
    /// </code>
    /// <para>
    /// Supported syntaxes:
    /// <code>
    /// // Deny all
    /// MyWhitelist:
    ///
    /// // Allow all
    /// MyWhitelist: '*'
    /// MyWhitelist:
    ///  - '*'
    ///
    /// // Allow specific
    /// MyWhitelist: http://example.org/
    /// MyWhitelist:
    ///  - http://example.org/
    ///  - http://example.com/somePrefix/
    ///  - myscheme://foobar.package.name/exactPath
    /// </code>
    /// </para>
    /// <para>
    /// Matching rules:
    /// <list type="bullet">
    /// <item>scheme must match exactly, case insensitive</item>
    /// <item>domain name must match exactly, case insensitive</item>
    /// <item>resolved port must match exactly</item>
    /// <item>http://example.org/ is equal to http://example.org:80/</item>
    /// <item>https://example.org/ is equal to https://example.org:443/</item>
    /// <item>http://example.com/somePrefix/ matches http://example.com/somePrefix/ and sub paths like http://example.com/somePrefix/suburl</item>
    /// <item>http://example.com/somePath matches only path /somePath</item>
    /// <item>?query may not be specified. Any ?query matches.</item>
    /// </list>
    /// </para>
    /// </summary>
    [TypeConverter(typeof(UrlWhitelistTypeConverter))]
    public class UrlWhitelist
    {
        List<Uri> _allowedUrlStems;
        bool _allowAll;

        public bool IsAllowAll => _allowAll;
        public IEnumerable<Uri> AllowedUrls => _allowedUrlStems;

        UrlWhitelist(List<Uri> allowedUrlStems, bool allowAll)
        {
            _allowedUrlStems = allowedUrlStems;
            _allowAll = allowAll;
        }

        /// <summary>
        /// A <see cref="UrlWhitelist"/> that denies all urls.
        /// </summary>
        public static UrlWhitelist DenyAll => new UrlWhitelist(new List<Uri>(), allowAll: false);

        /// <summary>
        /// A <see cref="UrlWhitelist"/> that allows all urls.
        /// </summary>
        public static UrlWhitelist AllowAll => new UrlWhitelist(new List<Uri>(), allowAll: true);

        /// <summary>
        /// Creates <see cref="UrlWhitelist"/> from the list of allowed patterns.
        /// </summary>
        public static UrlWhitelist Allow(params string[] allowedUrlStems)
        {
            List<Uri> allowed = new List<Uri>();
            foreach (string stem in allowedUrlStems)
            {
                if (stem == "*")
                {
                    if (allowedUrlStems.Length != 1)
                        throw new InvalidOperationException("URL Whitelist containing wildcard (*) may not contain any other elements. Got: " + string.Join(";", allowedUrlStems));
                    return AllowAll;
                }

                Uri uri = new Uri(stem);
                if (!uri.IsAbsoluteUri)
                    throw new InvalidOperationException("URL Whitelist may not contain relative URLs. Got: " + stem);
                if (!string.IsNullOrEmpty(uri.UserInfo))
                    throw new InvalidOperationException("URL Whitelist may not contain user credentials. Got: " + stem);
                if (!string.IsNullOrEmpty(uri.Query))
                    throw new InvalidOperationException("URL Whitelist may not contain Query part. Got: " + stem);
                if (!string.IsNullOrEmpty(uri.Fragment))
                    throw new InvalidOperationException("URL Whitelist may not contain Fragment part. Got: " + stem);

                if (uri.AbsolutePath == "/" && !stem.EndsWith('/'))
                    throw new InvalidOperationException("URL Whitelist may not contain host name without any path. Path must be at minimum /, since all URLs will at minimum have the /. Got: " + stem);

                if (!IsAscii(uri.Host))
                    throw new InvalidOperationException("URL Whitelist hostname must be ASCII. For international domains, you should use the punycode representation. Got: " + stem);

                allowed.Add(uri);
            }

            return new UrlWhitelist(allowed, allowAll: false);
        }

        /// <summary>
        /// Returns true if the given <paramref name="url"/> is in the set of allowed urls.
        /// </summary>
        public bool IsAllowed(Uri url)
        {
            // Any relative addresses are not in the whitelist
            if (!url.IsAbsoluteUri)
                return false;

            // Parse URI again to check it's properly escaped. This ensures /../ and /./ blocks are collapsed and we
            // will be doing the whitelist check on the (presumably) effective URI.
            if (url != new Uri(url.AbsoluteUri))
                return false;

            // Any user params are forbidden
            if (!string.IsNullOrEmpty(url.UserInfo))
                return false;

            // Allow only ASCII for host. Scheme is always (subset of) ascii
            if (!IsAscii(url.Host))
                return false;

            foreach (Uri stem in _allowedUrlStems)
            {
                // Compare Scheme, host and port. No normalization or escaping. Default port is substituted. Case insensitive.
                if (Uri.Compare(url, stem, UriComponents.Scheme | UriComponents.Host | UriComponents.StrongPort, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
                    continue;

                // Exact match?
                if (url.AbsolutePath == stem.AbsolutePath)
                    return true;

                // If stem ends with /, then prefix match?
                if (stem.AbsolutePath.EndsWith("/", StringComparison.Ordinal) && url.AbsolutePath.StartsWith(stem.AbsolutePath, StringComparison.Ordinal))
                    return true;
            }

            if (_allowAll)
                return true;

            return false;
        }

        static bool IsAscii(string str)
        {
            return Encoding.UTF8.GetByteCount(str) == str.Length;
        }
    }

    // Parsing support for RuntimeOptions
    public sealed class UrlWhitelistTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string) || sourceType == typeof(string[]);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            string[] urls;

            if (value is string stringValue)
            {
                if (string.IsNullOrEmpty(stringValue))
                    urls = Array.Empty<string>();
                else
                    urls = new string[] { stringValue };
            }
            else
                urls = (string[])value;

            return UrlWhitelist.Allow(urls);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) => false;

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) => throw new NotSupportedException();
    }
}
