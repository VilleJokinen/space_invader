// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.Network
{
    public static class MetaplayHostnameUtil
    {
        static string EnsureOrRemoveSuffix(string input, string suffix, bool shouldHaveSuffix)
        {
            string baseElement;
            if (input.EndsWith(suffix, System.StringComparison.Ordinal))
                baseElement = input.Substring(0, input.Length - suffix.Length);
            else
                baseElement = input;

            if (shouldHaveSuffix)
                return baseElement + suffix;
            else
                return baseElement;
        }

        public static string GetV4V6SpecificHost(string hostname, bool isIPv4)
        {
            if (hostname == "localhost" || hostname == "127.0.0.1" || hostname == "[::1]")
            {
                return isIPv4 ? "127.0.0.1" : "[::1]";
            }

            // first element gets "-ipv6"
            string[] elements = hostname.Split('.');
            if (elements.Length > 0)
                elements[0] = EnsureOrRemoveSuffix(elements[0], "-ipv6", shouldHaveSuffix: isIPv4 == false);
            return string.Join(".", elements);
        }
    }
}
