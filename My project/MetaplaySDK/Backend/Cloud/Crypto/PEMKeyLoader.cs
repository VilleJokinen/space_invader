// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Metaplay.Cloud.Crypto
{
    public static class PEMKeyLoader
    {
        // Encoded OID sequence for PKCS #1 rsaEncryption szOID_RSA_RSA = "1.2.840.113549.1.1.1"
        static readonly byte[] SeqOID = { 0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01, 0x05, 0x00 };

        private static bool ByteArrayEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int ndx = 0; ndx < a.Length; ndx++)
            {
                if (a[ndx] != b[ndx])
                    return false;
            }
            return true;
        }

        public static RSACryptoServiceProvider CryptoServiceProviderFromPublicKeyInfo(byte[] x509key)
        {
            if (x509key == null || x509key.Length == 0)
                return null;

            // Set up stream to read the asn.1 encoded SubjectPublicKeyInfo blob  ------
            using (MemoryStream mem = new MemoryStream(x509key))
            using (BinaryReader binr = new BinaryReader(mem))
            {
                ushort twobytes = binr.ReadUInt16();
                if (twobytes == 0x8130) // data read as little endian order (actual data order for Sequence is 30 81)
                    binr.ReadByte();
                else if (twobytes == 0x8230)
                    binr.ReadInt16();
                else
                    return null;

                // Check sequence OID
                byte[] seq = binr.ReadBytes(15);
                if (!ByteArrayEqual(seq, SeqOID))
                    return null;

                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8103) // data read as little endian order (actual data order for Bit String is 03 81)
                    binr.ReadByte();
                else if (twobytes == 0x8203)
                    binr.ReadInt16();
                else
                    return null;

                byte bt = binr.ReadByte();
                if (bt != 0x00)
                    return null;

                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8130) // data read as little endian order (actual data order for Sequence is 30 81)
                    binr.ReadByte();
                else if (twobytes == 0x8230)
                    binr.ReadInt16();
                else
                    return null;

                twobytes = binr.ReadUInt16();
                byte lowbyte = 0x00;
                byte highbyte = 0x00;

                if (twobytes == 0x8102) // data read as little endian order (actual data order for Integer is 02 81)
                    lowbyte = binr.ReadByte(); // read next bytes which is bytes in modulus
                else if (twobytes == 0x8202)
                {
                    highbyte = binr.ReadByte();
                    lowbyte = binr.ReadByte();
                }
                else
                    return null;
                byte[] modint = { lowbyte, highbyte, 0x00, 0x00 }; // reverse byte order since asn.1 key uses big endian order
                int modsize = BitConverter.ToInt32(modint, 0);

                int firstbyte = binr.PeekChar();
                if (firstbyte == 0x00)
                {
                    binr.ReadByte(); // skip this null byte
                    modsize -= 1; // reduce modulus buffer size by 1
                }

                // read the modulus bytes
                byte[] modulus = binr.ReadBytes(modsize);

                // expect an Integer for the exponent data
                if (binr.ReadByte() != 0x02)
                    return null;
                int expbytes = binr.ReadByte(); // should only need one byte for actual exponent data (for all useful values)
                byte[] exponent = binr.ReadBytes(expbytes);

                // Create RSACryptoServiceProvider instance and initialize with public key -----
                RSACryptoServiceProvider RSA = new RSACryptoServiceProvider();
                RSAParameters RSAKeyInfo = new RSAParameters();
                RSAKeyInfo.Modulus = modulus;
                RSAKeyInfo.Exponent = exponent;
                RSA.ImportParameters(RSAKeyInfo);

                return RSA;
            }
        }

        public static RSACryptoServiceProvider CryptoServiceProviderFromPublicKeyInfo(string base64EncodedKey)
        {
            return CryptoServiceProviderFromPublicKeyInfo(Convert.FromBase64String(base64EncodedKey));
        }

        public static string RemovePemHeaderFooter(string pem)
        {
            StringBuilder sb = new StringBuilder(pem);
            sb.Replace("-----BEGIN PUBLIC KEY-----", "");
            sb.Replace("-----END PUBLIC KEY-----", "");
            return sb.ToString();
        }

        public static byte[] X509KeyFromFile(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || !File.Exists(filename))
                return null;

            string str = RemovePemHeaderFooter(File.ReadAllText(filename));
            try
            {
                // assume base64 encoded
                return Convert.FromBase64String(str);
            }
            catch (FormatException)
            {
                // if not base64, fall back to binary
                return File.ReadAllBytes(filename);
            }
        }
    }
}
