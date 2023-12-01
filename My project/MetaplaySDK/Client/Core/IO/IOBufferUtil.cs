// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.IO
{
    public static class IOBufferUtil
    {
        /// <summary>
        /// Returns true if the contents of the buffers are equal. The internal layout of the data does not affect the results.
        /// </summary>
        public static bool ContentsEqual(IOBuffer a, IOBuffer b)
        {
            a.BeginRead();
            try
            {
                b.BeginRead();
                try
                {
                    if (a.Count != b.Count)
                        return false;

                    int aSegmentNdx = 0;
                    int aSegmentOffset = 0;
                    int bSegmentNdx = 0;
                    int bSegmentOffset = 0;

                    for (;;)
                    {
                        // Reached the end. It is sufficient that one cursor reaches the end
                        // as we have already checked the total number of bytes are the same.
                        // Hence, if one cursor is not at the end, the remaining segments must
                        // be empty.
                        if (aSegmentNdx == a.NumSegments || bSegmentNdx == b.NumSegments)
                            return true;

                        IOBufferSegment aSegment = a.GetSegment(aSegmentNdx);
                        IOBufferSegment bSegment = b.GetSegment(bSegmentNdx);
                        int aRemaining = aSegment.Size - aSegmentOffset;
                        int bRemaining = bSegment.Size - bSegmentOffset;
                        int remaining = System.Math.Min(aRemaining, bRemaining);

                        if (!MemEqual(aSegment.Buffer, aSegmentOffset, bSegment.Buffer, bSegmentOffset, remaining))
                            return false;

                        aSegmentOffset += remaining;
                        bSegmentOffset += remaining;

                        if (aSegmentOffset == aSegment.Size)
                        {
                            aSegmentOffset = 0;
                            aSegmentNdx += 1;
                        }

                        if (bSegmentOffset == bSegment.Size)
                        {
                            bSegmentOffset = 0;
                            bSegmentNdx += 1;
                        }
                    }
                }
                finally
                {
                    b.EndRead();
                }
            }
            finally
            {
                a.EndRead();
            }
        }

        static bool MemEqual(byte[] a, int aOffset, byte[] b, int bOffset, int numBytes)
        {
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
            return a.AsSpan(aOffset, numBytes).SequenceEqual(b.AsSpan(bOffset, numBytes));
#else
            for (int ndx = 0; ndx < numBytes; ndx++)
            {
                if (a[aOffset + ndx] != b[bOffset + ndx])
                    return false;
            }
            return true;
#endif
        }

        /// <summary>
        /// Copies contents to a new array and returns it.
        /// </summary>
        public static byte[] ToArray(IOBuffer src)
        {
            byte[] dstArray = new byte[src.Count];
            CopyTo(src, dstArray, 0);
            return dstArray;
        }

        /// <summary>
        /// Appends the content of <paramref name="src"/> into <paramref name="dst"/>.
        /// </summary>
        public static void AppendTo(IOBuffer src, IOBuffer dst)
        {
            src.BeginRead();
            try
            {
                using (IOWriter writer = new IOWriter(dst, IOWriter.Mode.Append))
                {
                    for (int segmentNdx = 0; segmentNdx < src.NumSegments; ++segmentNdx)
                    {
                        IOBufferSegment segment = src.GetSegment(segmentNdx);
                        writer.WriteBytes(segment.Buffer, 0, segment.Size);
                    }
                }
            }
            finally
            {
                src.EndRead();
            }
        }

        /// <summary>
        /// Copies contents to the given array.
        /// </summary>
        public static void CopyTo(IOBuffer src, byte[] dstArray, int dstOffset)
        {
            if (dstOffset < 0 || dstOffset + src.Count > dstArray.Length)
                throw new ArgumentOutOfRangeException(nameof(dstOffset));

            src.BeginRead();

            for (int segmentNdx = 0; segmentNdx < src.NumSegments; ++segmentNdx)
            {
                IOBufferSegment segment = src.GetSegment(segmentNdx);
                int segmentSize = segment.Size;

                Buffer.BlockCopy(segment.Buffer, 0, dstArray, dstOffset, segmentSize);
                dstOffset += segmentSize;
            }
            src.EndRead();
        }
    }

    // Extensions for discoverability
    public static class IOBufferExtensions
    {
        /// <summary>
        /// Appends the content of this buffer into <paramref name="dst"/>.
        /// </summary>
        public static void AppendTo(this IOBuffer src, IOBuffer dst)
        {
            IOBufferUtil.AppendTo(src, dst);
        }

        /// <summary>
        /// Copies contents to a new array and returns it.
        /// </summary>
        public static byte[] ToArray(this IOBuffer src)
        {
            return IOBufferUtil.ToArray(src);
        }

        /// <inheritdoc cref="IOBufferUtil.CopyTo(IOBuffer, byte[], int)"/>
        public static void CopyTo(this IOBuffer src, byte[] dstArray, int dstOffset)
        {
            IOBufferUtil.CopyTo(src, dstArray, dstOffset);
        }
    }
}
