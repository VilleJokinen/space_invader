// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Metaplay.Server.Web3
{
    public readonly struct ImmutableXLoginChallenge
    {
        public readonly string Message;
        public readonly string Description;
        public readonly EthereumAddress EthAddress;
        public readonly EntityId PlayerId;
        public readonly MetaTime Timestamp;

        ImmutableXLoginChallenge(string message, string description, EthereumAddress ethAddress, EntityId playerId, MetaTime timestamp)
        {
            Message = message;
            Description = description;
            EthAddress = ethAddress;
            Timestamp = timestamp;
            PlayerId = playerId;
        }

        public static ImmutableXLoginChallenge Create(Web3Options web3Options, EnvironmentOptions environmentOpts, MetaTime issuedAt, EntityId playerId, EthereumAddress ethAddress, StarkPublicKey imxAddress)
        {
            // HMAC over input values. This allows untrusted client to send back the values without ability to forgery.
            string signature = GetHmacString(
                value: $"sub:{playerId};eth:{ethAddress.GetAddressString()};imx:{imxAddress.GetPublicKeyString()};t:{issuedAt.ToDateTime().ToString(format: "o", CultureInfo.InvariantCulture)}",
                hmacSecret: $"{web3Options.ImmutableXPlayerAuthenticationChallengeHmacSecret}-{environmentOpts.Environment}");

            OrderedDictionary<string, string> fields = new OrderedDictionary<string, string>()
            {
                { "{ProductName}", web3Options.ImmutableXPlayerAuthenticationProductName },
                { "{PlayerId}", playerId.ToString() },
                { "{EthAccount}", ethAddress.GetAddressString() },
                { "{ImxAccount}", imxAddress.GetPublicKeyString() },
                { "{Timestamp}", issuedAt.ToDateTime().ToString(format: "o", CultureInfo.InvariantCulture) },
                { "{Signature}", signature },
            };

            string signedMessage = Format(web3Options.ImmutableXPlayerAuthenticationMessageTemplate, fields);
            string description = Format(web3Options.ImmutableXPlayerAuthenticationDescriptionTemplate, fields);
            return new ImmutableXLoginChallenge(signedMessage, description, ethAddress, playerId, issuedAt);
        }

        static string Format(string template, OrderedDictionary<string, string> fields)
        {
            string result = template;
            foreach ((string fieldName, string fieldValue) in fields)
                result = result.Replace(fieldName, fieldValue);
            return result;
        }

        static string GetHmacString(string value, string hmacSecret)
        {
            byte[] key = Encoding.UTF8.GetBytes(hmacSecret);
            byte[] content = Encoding.UTF8.GetBytes(value);
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                byte[] hash = hmac.ComputeHash(content);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }

        internal static void ValidateMessageTemplate(string template)
        {
            // Must contain technical data to make sure the signature covers them.
            string[] requiredFields = new string[]
            {
                "{Signature}"
            };
            foreach (string field in requiredFields)
            {
                if (!template.Contains(field))
                    throw new InvalidOperationException($"Message template is missing {field}.");
            }
        }

        internal static void ValidateDescriptionTemplate(string template)
        {
            // No requirements
        }
    }
}
