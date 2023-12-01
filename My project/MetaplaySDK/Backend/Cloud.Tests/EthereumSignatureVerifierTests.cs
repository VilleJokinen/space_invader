// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Web3;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    class EthereumSignatureVerifierTests
    {
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a08faedb05f094acf6cdd9f6a4bde14196a384d317bc89cd9a4cf4e6e6d645fc61c", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "1234", "0xb7396aa75d6e00f1d3cece3dee613e24a6aca29801aae1ed657b7349f7e44a5c1d2d8a577eb71039cd8654b28aed4ddf9034c7b9adb1c000b4c3228003cd458a1b", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "foorbar", "0xcefabdf546f24b91eb2d22de666fc488e865777f6ef5861c4742768a4476d06456370f9e20ce161202c8a912f482bbfe96d592067a01dd8fed446ddd0e19be2b1b", 3)]
        public void TestSuccess(string signerAddressStr, string message, string signatureString, int chainId)
        {
            EthereumAddress signerAddress = EthereumAddress.FromString(signerAddressStr);
            EthereumSignatureVerifier.ValidatePersonalSignature(signerAddress, message, signatureString, chainId);
        }

        // Too low R, S
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "0000000000000000000000000000000000000000000000000000000000000000" + "08faedb05f094acf6cdd9f6a4bde14196a384d317bc89cd9a4cf4e6e6d645fc6" + "1c", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a" + "0000000000000000000000000000000000000000000000000000000000000000" + "1c", 3)]
        // Too high R
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141" + "08faedb05f094acf6cdd9f6a4bde14196a384d317bc89cd9a4cf4e6e6d645fc6" + "1c", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364142" + "08faedb05f094acf6cdd9f6a4bde14196a384d317bc89cd9a4cf4e6e6d645fc6" + "1c", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff" + "08faedb05f094acf6cdd9f6a4bde14196a384d317bc89cd9a4cf4e6e6d645fc6" + "1c", 3)]
        // Too high R
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a" + "7fffffffffffffffffffffffffffffff5d576e7357a4501ddfe92f46681b20a1" + "1c", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a" + "7fffffffffffffffffffffffffffffff5d576e7357a4501ddfe92f46681b20a2" + "1c", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a" + "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff" + "1c", 3)]
        // Invalid V
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a" + "7fffffffffffffffffffffffffffffff5d576e7357a4501ddfe92f46681b20a1" + "1d", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a" + "7fffffffffffffffffffffffffffffff5d576e7357a4501ddfe92f46681b20a2" + "00", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a" + "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff" + "ff", 3)]
        public void TestInvalid(string signerAddressStr, string message, string signatureString, int chainId)
        {
            EthereumAddress signerAddress = EthereumAddress.FromString(signerAddressStr);
            Assert.Throws<EthereumSignatureVerifier.MalformedSignatureException>(() => EthereumSignatureVerifier.ValidatePersonalSignature(signerAddress, message, signatureString, chainId));
        }

        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "0000000000000000000000000000000000000000000000000000000000000001" + "08faedb05f094acf6cdd9f6a4bde14196a384d317bc89cd9a4cf4e6e6d645fc6" + "1c", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a" + "0000000000000000000000000000000000000000000000000000000000000001" + "1c", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364140" + "08faedb05f094acf6cdd9f6a4bde14196a384d317bc89cd9a4cf4e6e6d645fc6" + "1c", 3)]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234", "123", "0x" + "516016fea6ddd2f2f2862dd951c7feef59a6475569a7e350cfa1162ef8b2e73a" + "7fffffffffffffffffffffffffffffff5d576e7357a4501ddfe92f46681b20a0" + "1c", 3)]
        public void TestFailure(string signerAddressStr, string message, string signatureString, int chainId)
        {
            EthereumAddress signerAddress = EthereumAddress.FromString(signerAddressStr);
            Exception ex = Assert.Catch(() => EthereumSignatureVerifier.ValidatePersonalSignature(signerAddress, message, signatureString, chainId));
            // \note: cannot check just SignatureMismatchException as some signatures might not resolve to a public key.
            Assert.IsTrue(ex is EthereumSignatureVerifier.SignatureValidationException);
        }
    }
}
