// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.Checksum
{
    public static class Crc8
    {
        public enum Polynomial : byte
        {
            SMBus = 0x07,
        }

        public static byte ComputeCrc8(byte[] data, Polynomial polynomial)
        {
            return ComputeCrc8(data, 0, data.Length, polynomial);
        }

        public static byte ComputeCrc8(byte[] data, int startIndex, int length, Polynomial polynomial)
        {
            // Naive, but compact implementation
            byte crc = 0;
            for (int ndx = startIndex; ndx < startIndex + length; ++ndx)
            {
                byte b = data[ndx];
                crc ^= b;

                for (int bs = 0; bs < 8; ++bs)
                {
                    bool sign = (crc & 0x80) != 0;
                    crc = (byte)(crc << 1);
                    if (sign)
                        crc ^= (byte)polynomial;
                }
            }
            return crc;
        }
    }
}
