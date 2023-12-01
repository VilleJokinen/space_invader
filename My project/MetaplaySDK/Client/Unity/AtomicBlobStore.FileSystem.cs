// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !UNITY_WEBGL || UNITY_EDITOR
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL" (regarding blocking file IO). False positive, this is non-WebGL.

using Metaplay.Core;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using System.IO;

namespace Metaplay.Unity
{
    public static class AtomicBlobStore
    {
        public static void Initialize()
        {
            // empty
        }

        /// <summary>
        /// Reads a blob from path written with <see cref="TryWriteBlob"/>. On
        /// failure returns null.
        ///
        /// <para>
        /// Performed dance (with filename = credstore.dat): <br/>
        /// (read credstore.dat) success -> done <br/>
        /// (read credstore.dat.old) success -> done <br/>
        /// else -> fail <br/>
        /// </para>
        /// </summary>
        public static byte[] TryReadBlob(string path)
        {
            // Lock to prevent file-read-locks from preventing ongoing write or move
            using (FileAccessLock fsLock = FileAccessLock.AcquireSync(path))
            {
                byte[] envelopePayload;

                if (TryReadEnvelopeFromFile(path, out envelopePayload))
                    return envelopePayload;
                if (TryReadEnvelopeFromFile(path + ".old", out envelopePayload))
                    return envelopePayload;

                return null;
            }
        }

        /// <summary>
        /// Writes blob to the path such that reading it with <see cref="TryReadBlob(string)"/>
        /// always results either an old or the new version completely.
        ///
        /// <para>
        /// Performed dance (with filename = credstore.dat): <br/>
        /// (write credstore.dat.new) <br/>
        /// If credstore.dat exists: <br/>
        /// -- (rm credstore.dat.old) <br/>
        /// -- (move credstore.dat to credstore.dat.old) <br/>
        /// (move credstore.dat.new to credstore.dat) <br/>
        /// (rm credstore.dat.old) <br/>
        /// </para>
        /// </summary>
        public static bool TryWriteBlob(string path, byte[] blob)
        {
            using (FileAccessLock fsLock = FileAccessLock.AcquireSync(path))
            {
                if (!TryWriteEnvelope(path + ".new", blob))
                    return false;

                if (File.Exists(path))
                {
                    if (File.Exists(path + ".old"))
                    {
                        try
                        {
                            File.Delete(path + ".old");
                        }
                        catch
                        {
                            // if we cannot delete backup, we cannot guarantee atomic write
                            return false;
                        }
                    }

                    try
                    {
                        File.Move(path, path + ".old");
                    }
                    catch
                    {
                        // if we cannot update backup, we cannot guarantee atomic write
                        return false;
                    }
                }

                try
                {
                    File.Move(path + ".new", path);
                }
                catch
                {
                    return false;
                }

                try
                {
                    File.Delete(path + ".old");
                }
                catch
                {
                    // we could not clean up. The main file is in place, so this is ok
                }

                return true;
            }
        }

        /// <summary>
        /// Removes the blob and its backups.
        /// </summary>
        public static bool TryDeleteBlob(string path)
        {
            using (FileAccessLock fsLock = FileAccessLock.AcquireSync(path))
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        return false;
                    }
                }

                if (File.Exists(path + ".old"))
                {
                    try
                    {
                        File.Delete(path + ".old");
                    }
                    catch
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        static bool TryWriteEnvelope(string path, byte[] envelopePayload)
        {
            byte[] contents = WrapEnvelope(payload: envelopePayload);
            try
            {
                File.WriteAllBytes(path, contents);
            }
            catch
            {
                return false;
            }

            return true;
        }

        static bool TryReadEnvelopeFromFile(string path, out byte[] envelopePayload)
        {
            byte[] contents;

            try
            {
                contents = File.ReadAllBytes(path);
            }
            catch
            {
                envelopePayload = null;
                return false;
            }

            if (TryUnwrapEnvelope(contents, out byte[] payload))
            {
                envelopePayload = payload;
                return true;
            }
            else
            {
                envelopePayload = null;
                return false;
            }
        }

        static byte[] WrapEnvelope(byte[] payload)
        {
            // format:
            // HEAD: 4 bytes
            // version: 4 bytes
            // length: 4 bytes
            // payload: length bytes
            // CSUM: 4 bytes
            // TAIL: 4 bytes

            uint csum = MurmurHash.MurmurHash2(payload);
            uint version = 1;

            using (SegmentedIOBuffer buffer = new SegmentedIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer))
                {
                    writer.WriteBytes(new byte[] { (byte)'H', (byte)'E', (byte)'A', (byte)'D' }, 0, 4);
                    writer.WriteUInt32(version);
                    writer.WriteUInt32((uint)payload.Length);
                    writer.WriteBytes(payload, 0, payload.Length);
                    writer.WriteUInt32(csum);
                    writer.WriteBytes(new byte[] { (byte)'T', (byte)'A', (byte)'I', (byte)'L' }, 0, 4);
                }

                return buffer.ToArray();
            }
        }

        static bool TryUnwrapEnvelope(byte[] envelope, out byte[] payload)
        {
            try
            {
                using (IOReader reader = new IOReader(envelope))
                {
                    byte[] head = new byte[4];
                    reader.ReadBytes(head);
                    if (head[0] != (byte)'H' ||
                        head[1] != (byte)'E' ||
                        head[2] != (byte)'A' ||
                        head[3] != (byte)'D')
                    {
                        throw new IODecodingError();
                    }

                    uint version = reader.ReadUInt32();
                    if (version != 1)
                        throw new IODecodingError();

                    uint payloadLength = reader.ReadUInt32();

                    byte[] uncheckedPayload = new byte[payloadLength];
                    reader.ReadBytes(uncheckedPayload);

                    uint csum = reader.ReadUInt32();
                    if (csum != MurmurHash.MurmurHash2(uncheckedPayload))
                        throw new IODecodingError();

                    byte[] tail = new byte[4];
                    reader.ReadBytes(tail);
                    if (tail[0] != (byte)'T' ||
                        tail[1] != (byte)'A' ||
                        tail[2] != (byte)'I' ||
                        tail[3] != (byte)'L')
                    {
                        throw new IODecodingError();
                    }

                    if (reader.Offset != envelope.Length)
                        throw new IODecodingError();

                    payload = uncheckedPayload;
                    return true;
                }
            }
            catch(IODecodingError)
            {
                payload = null;
                return false;
            }
        }
    }
}

#pragma warning restore MP_WGL_00
#endif
