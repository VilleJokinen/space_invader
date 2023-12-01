// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services.Geolocation
{
    [RuntimeOptions("Geolocation", isStatic: false, "Configuration options for geolocating players' location based on their IP address.")]
    public class GeolocationOptions : RuntimeOptionsBase
    {
        [MetaDescription("Enables geolocation via [MaxMind](https://www.maxmind.com/).")]
        public bool     Enabled                     { get; private set; } = false;
        [MetaDescription("The path to the MaxMind license key.")]
        public string   MaxMindLicenseKeyPath       { get; private set; } = null;

        [IgnoreDataMember, Sensitive]
        public string MaxMindLicenseKey { get; private set; } // Resolved from MaxMindLicenseKeyPath on load; null if no path given

        public override async Task OnLoadedAsync()
        {
            if (Enabled)
            {
                if (MaxMindLicenseKeyPath == null)
                    throw new InvalidOperationException("MaxMindLicenseKeyPath must be defined when Enabled is true. Enabling geolocation without auto-updates is not currently supported.");

                MaxMindLicenseKey = await GetMaxMindLicenseKeyAsync(MaxMindLicenseKeyPath).ConfigureAwait(false);
            }
        }

        async Task<string> GetMaxMindLicenseKeyAsync(string licenseKeyPath)
        {
            string licenseKey = await SecretUtil.ResolveSecretAsync(Log, licenseKeyPath).ConfigureAwait(false);

            // Check that the license key format seems valid:
            // nonempty, all ASCII printable non-whitespace characters (i.e. value 33 to 126 inclusive).
            // Not a comprehensive check, done mainly to guard against trailing newlines in the file.

            if (licenseKey == "")
                throw new InvalidOperationException($"MaxMind license key is empty");

            foreach (char ch in licenseKey)
            {
                if (ch < 33 || ch > 126)
                    throw new InvalidOperationException($"MaxMind license key contains UTF-16 value {(int)ch} which isn't a printable non-whitespace ASCII character");
            }

            return licenseKey;
        }
    }
}
