// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using NBitcoin.Secp256k1;
using Org.BouncyCastle.Crypto.Digests;
using System;
using System.Globalization;
using System.Text;

namespace Metaplay.Cloud.Web3
{
    public static class EthereumSignatureVerifier
    {
        static readonly byte[] _ethPersonalSignaturePrefix = Encoding.UTF8.GetBytes("\x0019Ethereum Signed Message:\n");
        static readonly byte[] _rEnd = Convert.FromHexString("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141");
        static readonly byte[] _sEnd = Convert.FromHexString("7fffffffffffffffffffffffffffffff5d576e7357a4501ddfe92f46681b20a1");

        public abstract class SignatureValidationException : Exception
        {
            public SignatureValidationException(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// Signature is not a well formed signature.
        /// </summary>
        public class MalformedSignatureException : SignatureValidationException
        {
            public MalformedSignatureException(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// Signature is not valid for the message.
        /// </summary>
        public class SignatureMismatchException : SignatureValidationException
        {
            public SignatureMismatchException(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// Verifies Ethereum signed personal message. If <paramref name="signatureString"/> is not a valid signature
        /// of <paramref name="signerAddress"/> for <paramref name="message"/>, throws <see cref="SignatureValidationException"/>.
        /// </summary>
        /// <param name="signatureString">Signature as hex string, with <c>0x</c> prefix</param>
        public static void ValidatePersonalSignature(EthereumAddress signerAddress, string message, string signatureString, int chainId)
        {
            // See https://ethereum.github.io/yellowpaper/paper.pdf Appendix F

            byte[] signature = HexSignatureToBytes(signatureString);
            byte[] hash = HashPersonalMessage(message);

            ReadOnlySpan<byte> r = signature.AsSpan(start: 0, length: 32);
            ReadOnlySpan<byte> s = signature.AsSpan(start: 32, length: 32);
            byte v = signature[64];

            // Check R
            if (!EcElementInValidRange(r, _rEnd))
                throw new MalformedSignatureException($"R not in valid range: {Convert.ToHexString(r)}");

            // Check S
            if (!EcElementInValidRange(s, _sEnd))
                throw new MalformedSignatureException($"S not in valid range: {Convert.ToHexString(s)}");

            // Check V
            int recid;
            if (v == 27 || v == 2 * chainId + 35)
                recid = 0;
            else if (v == 28 || v == 2 * chainId + 36)
                recid = 1;
            else
                throw new MalformedSignatureException($"V not in valid range: {(uint)v}. V must be one of 27, 28, 2b + 35, 2b + 36 where b is ChainID.");

            // Get sender:
            //  S(T) ≡ B96..255(KEC(ECDSARECOVER(h(T), v, r, s)

            SecpRecoverableECDSASignature sig;
            if (!SecpRecoverableECDSASignature.TryCreateFromCompact(signature.AsSpan(start: 0, length: 64), recid, out sig))
                throw new MalformedSignatureException("invalid signature. Could not parse compact signature.");

            if (!ECPubKey.TryRecover(Context.Instance, sig, hash, out ECPubKey pubKey))
                throw new MalformedSignatureException("invalid signature. Could not recover pub key.");

            byte[] senderPubKey = new byte[64];
            pubKey.Q.x.WriteToSpan(senderPubKey);
            pubKey.Q.y.WriteToSpan(senderPubKey.AsSpan().Slice(32));
            EthereumAddress senderAddress = EthereumAddress.FromPublicKey(senderPubKey);
            if (senderAddress != signerAddress)
                throw new SignatureMismatchException("signature does not match.");
        }

        /// <summary>
        /// Converts hex string into 65 element byte array.
        /// </summary>
        static byte[] HexSignatureToBytes(string signatureString)
        {
            if (signatureString.Substring(0, 2).ToLowerInvariant() != "0x")
                throw new FormatException("Signature must start with 0x");
            if (signatureString.Length != 132)
                throw new FormatException("Signature must be 132 characters long");
            return Convert.FromHexString(signatureString.AsSpan().Slice(2));
        }

        static byte[] HashPersonalMessage(string message)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] messageLenBytes = Encoding.UTF8.GetBytes(messageBytes.Length.ToString(CultureInfo.InvariantCulture));
            KeccakDigest digest = new KeccakDigest(bitLength: 256);
            digest.BlockUpdate(_ethPersonalSignaturePrefix, 0, _ethPersonalSignaturePrefix.Length);
            digest.BlockUpdate(messageLenBytes, 0, messageLenBytes.Length);
            digest.BlockUpdate(messageBytes, 0, messageBytes.Length);
            byte[] finalHash = new byte[32];
            digest.DoFinal(finalHash, 0);
            return finalHash;
        }

        /// <summary>
        /// Returns true if <c>0 &lt; <paramref name="element"/> &lt; <paramref name="upperLimit"/></c>
        /// </summary>
        static bool EcElementInValidRange(ReadOnlySpan<byte> element, ReadOnlySpan<byte> upperLimit)
        {
            if (element.SequenceCompareTo(upperLimit) >= 0)
                return false;

            Span<byte> zeros = stackalloc byte[32];
            zeros.Clear();

            if (element.SequenceCompareTo(zeros) <= 0)
                return false;

            return true;
        }
    }
}
