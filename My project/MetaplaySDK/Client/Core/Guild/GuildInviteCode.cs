// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Checksum;
using Metaplay.Core.Model;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Security.Cryptography;

namespace Metaplay.Core.Guild
{
    /// <summary>
    /// 48-bit integer represented as a human-readable and hard-to-typo string. The code is encoded
    /// into altered base32 alphabet and contains a checksum to detect typos.
    /// Example: AAAA-BBBB-CCCC
    /// </summary>
    [MetaSerializable]
    [JsonConverter(typeof(GuildInviteCodeConverter))]
    public struct GuildInviteCode : IComparable<GuildInviteCode>, IEquatable<GuildInviteCode>
    {
        [MetaMember(1)] public ulong Raw { get; private set; }

        /// <summary>
        /// Maximum value for <see cref="Raw"/>. Inclusive.
        /// </summary>
        public const ulong MaxRawValue = ((1UL) << 48) - 1;

        GuildInviteCode(ulong raw)
        {
            if (raw > MaxRawValue)
                throw new ArgumentOutOfRangeException(nameof(raw));
            Raw = raw;
        }

        /// <summary>
        /// Constructs Invite code from raw long value. Value must be less than or equal to <see cref="MaxRawValue"/>.
        /// </summary>
        public static GuildInviteCode FromRaw(ulong value)
        {
            return new GuildInviteCode(value);
        }

        /// <summary>
        /// Constructs Invite code from 6 bytes.
        /// </summary>
        public static GuildInviteCode FromBytes(byte[] bytes)
        {
            ulong raw = (((ulong)bytes[0]) << 40)
                      + (((ulong)bytes[1]) << 32)
                      + (((ulong)bytes[2]) << 24)
                      + (((ulong)bytes[3]) << 16)
                      + (((ulong)bytes[4]) << 8)
                      + (((ulong)bytes[5]) << 0);
            return new GuildInviteCode(raw);
        }

        /// <summary>
        /// Generates a new invite code using secure, unpredictable random source.
        /// </summary>
        public static GuildInviteCode CreateNew()
        {
#if UNITY_2018_1_OR_NEWER
            byte[] inviteCodeBytes = new byte[6];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(inviteCodeBytes);
            }
#else
            byte[] inviteCodeBytes = RandomNumberGenerator.GetBytes(6);
#endif
            return FromBytes(inviteCodeBytes);
        }

        /// <summary>
        /// Generates a new invite code using insecure, predicatable random source.
        /// </summary>
        public static GuildInviteCode CreateNewUnsafe(RandomPCG rng)
        {
            return GuildInviteCode.FromRaw(rng.NextULong() % (MaxRawValue + 1));
        }

        /// <summary>
        /// Converts code into a human-readable and hard-to-typo string. The code is encoded
        /// into altered base32 alphabet and contains a checksum to detect typos.
        /// Example: AAAA-BBBB-CCCC
        /// </summary>
        public override string ToString()
        {
            byte[] bytes = new byte[7]
            {
                (byte)(Raw >> 40),
                (byte)(Raw >> 32),
                (byte)(Raw >> 24),
                (byte)(Raw >> 16),
                (byte)(Raw >> 8),
                (byte)(Raw),
                0x00
            };
            bytes[6] = (byte)(Crc8.ComputeCrc8(bytes, 0, 6, Crc8.Polynomial.SMBus) ^ MetaplayCore.Options.GuildInviteCodeSalt);

            string encoded = Base32Codec.EncodeToString(bytes, padding: false);

            // 8 <-> L mapping (L is too similar to 1, I)
            encoded = encoded.Replace('L', '8');

            // 9 <-> 6 mapping (6 is too similar to G)
            encoded = encoded.Replace('6', '9');

            // groups
            return $"{encoded.Substring(0, 4)}-{encoded.Substring(4, 4)}-{encoded.Substring(8, 4)}";
        }

        /// <summary>
        /// Parses the invite code from the stirng. Some typos are automatically corrected:
        /// 1 and L are converted to I, and 0 is converted to O. Missing delimiters are tolerated.
        /// If code could be parsed and passes checksum check, the method returns true, and
        /// <paramref name="code"/> contains the parsed invited code.
        /// </summary>
        public static bool TryParse(string inviteCode, out GuildInviteCode code)
        {
            const int Base32Length = 12;
            char[] cleanedCode = new char[Base32Length];
            int cursor = 0;

            foreach (char inputChar in inviteCode)
            {
                char c = inputChar;

                // trim and remove any extra markers, spacers.
                if (c == ' ' || c == '.' || c == '-')
                    continue;

                // To consistent case (upper)
                c = char.ToUpper(c, CultureInfo.InvariantCulture);

                // fix similar-looking chars and do 8 <-> L, and 9 <-> 6 mapping
                if (c == '1' || c == 'L')
                    c = 'I';
                else if (c == '0')
                    c = 'O';
                else if (c == '8')
                    c = 'L';
                else if (c == '6')
                    c = 'G';
                else if (c == '9')
                    c = '6';

                // append or die

                if (cursor >= Base32Length)
                {
                    code = default;
                    return false;
                }

                cleanedCode[cursor++] = c;
            }

            if (cursor != Base32Length)
            {
                code = default;
                return false;
            }

            // decode to 7 octets
            byte[] bytes = Base32Codec.TryDecodeToBytes(new string(cleanedCode), padding: false);
            if (bytes == null)
            {
                code = default;
                return false;
            }

            // Check CRC
            byte crc = Crc8.ComputeCrc8(bytes, 0, 6, Crc8.Polynomial.SMBus);
            if (bytes[6] != (crc ^ MetaplayCore.Options.GuildInviteCodeSalt))
            {
                code = default;
                return false;
            }

            code = FromBytes(bytes);
            return true;
        }

        public static bool operator ==(GuildInviteCode a, GuildInviteCode b) => a.Raw == b.Raw;
        public static bool operator !=(GuildInviteCode a, GuildInviteCode b) => a.Raw != b.Raw;

        public bool Equals(GuildInviteCode other) => this == other;

        public override bool Equals(object obj) => (obj is GuildInviteCode other) ? (this == other) : false;

        public int CompareTo(GuildInviteCode other) => Raw.CompareTo(other.Raw);

        public override int GetHashCode() => Raw.GetHashCode();
    }

    public class GuildInviteCodeConverter : JsonConverter //<GuildInviteCode>
    {
        public override bool CanConvert(Type type) => type == typeof(GuildInviteCode);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            string asString = serializer.Deserialize<string>(reader);
            if (!GuildInviteCode.TryParse(asString, out GuildInviteCode code))
                throw new FormatException($"cannot parse {asString} as GuildInviteCode");
            return code;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, ((GuildInviteCode)value).ToString());
        }
    }
}

#endif
