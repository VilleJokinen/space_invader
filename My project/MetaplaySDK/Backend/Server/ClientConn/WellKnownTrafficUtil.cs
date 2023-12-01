// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace Metaplay.Server
{
    public class WellKnownTrafficInfo
    {
        public string KindLabel     { get; private set; }
        public string Description   { get; private set; }

        public WellKnownTrafficInfo(string kindLabel, string description)
        {
            KindLabel = kindLabel ?? throw new ArgumentNullException(nameof(kindLabel));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }
    }

    public static class WellKnownTrafficUtil
    {
        public static WellKnownTrafficInfo TryGetWellKnownTrafficInfo(IEnumerable<byte> incomingBuffer)
        {
            WellKnownPrefixSpec prefixSpec = s_wellKnownPrefixes.FirstOrDefault(prefixSpec => incomingBuffer.StartsWith(prefixSpec.Prefix));
            if (prefixSpec != null)
            {
                string prefixDescription = prefixSpec.IsUtf8Prefix
                                           ? Invariant($"{prefixSpec.Prefix.Length}-byte string \"{System.Text.Encoding.UTF8.GetString(prefixSpec.Prefix)}\"")
                                           : Invariant($"{prefixSpec.Prefix.Length} bytes [{ Util.BytesToString(prefixSpec.Prefix) }]");

                return new WellKnownTrafficInfo(
                    kindLabel: prefixSpec.KindLabel,
                    description: $"Payload starts with {prefixDescription}");
            }

            return null;
        }

        class WellKnownPrefixSpec
        {
            public byte[]   Prefix          { get; private set; }
            public bool     IsUtf8Prefix    { get; private set; }
            public string   KindLabel       { get; private set; }

            public WellKnownPrefixSpec(byte[] prefix, bool isUtf8Prefix, string kindLabel)
            {
                Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
                IsUtf8Prefix = isUtf8Prefix;
                KindLabel = kindLabel ?? throw new ArgumentNullException(nameof(kindLabel));
            }

            public WellKnownPrefixSpec(string prefixUtf8, string kindLabel)
                : this(
                    prefix:         System.Text.Encoding.UTF8.GetBytes(prefixUtf8),
                    isUtf8Prefix:   true,
                    kindLabel:      kindLabel)
            {
            }
        }

        static readonly WellKnownPrefixSpec[] s_wellKnownPrefixes = new WellKnownPrefixSpec[]
        {
            // HTTP
            new WellKnownPrefixSpec(prefixUtf8: "GET ",     kindLabel: "HttpGET"),
            new WellKnownPrefixSpec(prefixUtf8: "HEAD ",    kindLabel: "HttpHEAD"),
            new WellKnownPrefixSpec(prefixUtf8: "POST ",    kindLabel: "HttpPOST"),
            new WellKnownPrefixSpec(prefixUtf8: "PUT ",     kindLabel: "HttpPUT"),
            new WellKnownPrefixSpec(prefixUtf8: "DELETE ",  kindLabel: "HttpDELETE"),
            new WellKnownPrefixSpec(prefixUtf8: "CONNECT ", kindLabel: "HttpCONNECT"),
            new WellKnownPrefixSpec(prefixUtf8: "OPTIONS ", kindLabel: "HttpOPTIONS"),
            new WellKnownPrefixSpec(prefixUtf8: "TRACE ",   kindLabel: "HttpTRACE"),
            new WellKnownPrefixSpec(prefixUtf8: "PATCH ",   kindLabel: "HttpPATCH"),

            // SIP (Session Initiation Protocol)
            new WellKnownPrefixSpec(prefixUtf8: "REGISTER ",    kindLabel: "SipREGISTER"),
            new WellKnownPrefixSpec(prefixUtf8: "INVITE ",      kindLabel: "SipINVITE"),
            new WellKnownPrefixSpec(prefixUtf8: "ACK ",         kindLabel: "SipACK"),
            new WellKnownPrefixSpec(prefixUtf8: "BYE ",         kindLabel: "SipBYE"),
            new WellKnownPrefixSpec(prefixUtf8: "CANCEL ",      kindLabel: "SipCANCEL"),
            new WellKnownPrefixSpec(prefixUtf8: "UPDATE ",      kindLabel: "SipUPDATE"),
            new WellKnownPrefixSpec(prefixUtf8: "REFER ",       kindLabel: "SipREFER"),
            new WellKnownPrefixSpec(prefixUtf8: "PRACK ",       kindLabel: "SipPRACK"),
            new WellKnownPrefixSpec(prefixUtf8: "SUBSCRIBE ",   kindLabel: "SipSUBSCRIBE"),
            new WellKnownPrefixSpec(prefixUtf8: "NOTIFY ",      kindLabel: "SipNOTIFY"),
            new WellKnownPrefixSpec(prefixUtf8: "PUBLISH ",     kindLabel: "SipPUBLISH"),
            new WellKnownPrefixSpec(prefixUtf8: "MESSAGE ",     kindLabel: "SipMESSAGE"),
            new WellKnownPrefixSpec(prefixUtf8: "INFO ",        kindLabel: "SipINFO"),
            // \todo [nuutti] Distinguish between HTTP OPTIONS and SIP OPTIONS
            //new WellKnownPrefixSpec(prefixUtf8: "OPTIONS ",        kindLabel: "SipOPTIONS"),
        };
    }
}
