// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.IO
{
    /// <summary>
    /// Low-level IOReader. Unlike IOReader, handles only byte ops.
    /// </summary>
    struct LowLevelIOReader : IDisposable
    {
        IOBuffer                      _buffer; // null if in direct mode
        internal ReadOnlyMemory<byte> _segmentData;
        internal int                  _segmentOffset;
        internal int                  _segmentEndOffset;
        int                           _segmentIndex;
        int                           _previousSegmentsTotalSize;

        internal LowLevelIOReader(
            IOBuffer buffer,
            ReadOnlyMemory<byte> segmentData,
            int segmentOffset,
            int segmentEndOffset,
            int segmentIndex,
            int previousSegmentsTotalSize)
        {
            _buffer                    = buffer;
            _segmentData               = segmentData;
            _segmentOffset             = segmentOffset;
            _segmentEndOffset          = segmentEndOffset;
            _segmentIndex              = segmentIndex;
            _previousSegmentsTotalSize = previousSegmentsTotalSize;
            IsDisposed                 = false;
        }

        public int  TotalOffset => _previousSegmentsTotalSize + _segmentOffset;
        public int  TotalLength => _buffer?.Count ?? _segmentEndOffset;
        public int  Remaining   => TotalLength - TotalOffset;
        public bool IsFinished  => (_segmentOffset == _segmentEndOffset) && ((_buffer == null) || _segmentIndex == _buffer.NumSegments - 1);
        public bool IsDisposed  { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            _buffer?.EndRead();
            _buffer                    = null;
            _segmentData               = default;
            _segmentOffset             = 0;
            _segmentEndOffset          = 0;
            _segmentIndex              = 0;
            _previousSegmentsTotalSize = 0;
            IsDisposed                 = true;
        }

        /// <summary>
        /// If there are successive segments, selects the next one.
        /// Otherwise returns false.
        /// </summary>
        internal bool NextSegment()
        {
            if (_buffer == null)
                return false;
            if (_segmentIndex == _buffer.NumSegments - 1)
                return false;

            _previousSegmentsTotalSize += _segmentOffset;
            _segmentIndex += 1;

            IOBufferSegment segment = _buffer.GetSegment(_segmentIndex);
            _segmentData = segment.Buffer;
            _segmentOffset = 0;
            _segmentEndOffset = segment.Size;
            return true;
        }

        void SeekToStart()
        {
            if (_buffer == null)
            {
                _segmentOffset = 0;
            }
            else
            {
                IOBufferSegment segment = _buffer.GetSegment(0);

                _segmentIndex               = 0;
                _segmentData                = segment.Buffer;
                _segmentOffset              = 0;
                _segmentEndOffset           = segment.Size;
                _previousSegmentsTotalSize  = 0;
            }
        }

        public void Seek(int targetOffset)
        {
            SeekToStart();
            SkipBytes(targetOffset);
        }

        void CheckIsDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(IOReader));
        }

        public void SkipBytes(int numBytes)
        {
            CheckIsDisposed();
            if (numBytes < 0)
                throw new ArgumentException($"Invalid numBytes value {numBytes}");

            while (numBytes > 0)
            {
                int spaceInSegment = _segmentEndOffset - _segmentOffset;
                if (spaceInSegment == 0)
                {
                    if (!NextSegment())
                        throw new IODecodingError($"SkipBytes(): going past end-of-buffer");
                }
                else
                {
                    int numToRead = System.Math.Min(numBytes, spaceInSegment);
                    _segmentOffset += numToRead;
                    numBytes -= numToRead;
                }
            }
        }

        public int ReadByte()
        {
            CheckIsDisposed();

            while (true)
            {
                int spaceInSegment = _segmentEndOffset - _segmentOffset;
                if (spaceInSegment == 0)
                {
                    if (!NextSegment())
                        throw new IODecodingError($"ReadByte(): reading past end-of-buffer (at offset {TotalOffset})");
                }
                else
                    break;
            }

            byte value;
            value = _segmentData.Span[_segmentOffset];
            _segmentOffset += 1;
            return value;
        }

        /// <summary>
        /// Reads bytes into destination until destination is completely filled or EOF
        /// is reached. Returns the number of bytes read.
        /// </summary>
        public int ReadSome(Span<byte> bytes)
        {
            CheckIsDisposed();

            int numOriginalBytes = bytes.Length;
            while (bytes.Length > 0)
            {
                int spaceInSegment = _segmentEndOffset - _segmentOffset;
                if (spaceInSegment == 0)
                {
                    if (!NextSegment())
                        break;
                }
                else
                {
                    int numToRead = System.Math.Min(bytes.Length, spaceInSegment);
                    _segmentData.Slice(_segmentOffset, numToRead).Span.CopyTo(bytes);
                    _segmentOffset += numToRead;
                    bytes = bytes.Slice(numToRead);
                }
            }
            return numOriginalBytes - bytes.Length;
        }

        /// <summary>
        /// Reads bytes into destination until destination is completely filled. If EOF is
        /// reached before the destination is full, throws IODecodingError.
        /// </summary>
        public void ReadAll(Span<byte> bytes)
        {
            int numRead = ReadSome(bytes);
            if (numRead != bytes.Length)
                throw new IODecodingError($"ReadAll(): tried to read {bytes.Length} but only got {numRead} bytes.");
        }

        /// <summary>
        /// Reads bytes into destination until destination is completely filled or EOF
        /// is reached. Returns the number of bytes read.
        /// </summary>
        public int ReadSome(byte[] outBytes, int outOffset, int numBytes)
        {
            return ReadSome(new Span<byte>(outBytes, outOffset, numBytes));
        }

        /// <summary>
        /// Reads bytes into destination until destination is completely filled. If EOF is
        /// reached before the destination is full, throws IODecodingError.
        /// </summary>
        public void ReadAll(byte[] outBytes, int outOffset, int numBytes)
        {
            int numRead = ReadSome(outBytes, outOffset, numBytes);
            if (numRead != numBytes)
                throw new IODecodingError($"ReadAll(): tried to read {numBytes} but only got {numRead} bytes.");
        }
    }
}
