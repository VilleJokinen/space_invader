// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.IO;
using System;
using System.Globalization;
using System.Text;
using System.Collections.Generic;

namespace Metaplay.Cloud.ProcFs
{
    /// <summary>
    /// Reader for category-columnar procfs files such as /proc/net/snmp
    /// </summary>
    class ProcFsNetstatReader : IDisposable
    {
        readonly string _path;
        readonly Stream _file;
        readonly Dictionary<(string, string), long> _values;

        public ProcFsNetstatReader(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _values = new Dictionary<(string, string), long>();

            UpdateFromProcFs();
        }

        public ProcFsNetstatReader(Stream stream)
        {
            _path = "anon";
            _file = stream ?? throw new ArgumentNullException(nameof(stream));
            _values = new Dictionary<(string, string), long>();

            UpdateFromProcFs();
        }

        /// <summary>
        /// Reads the most recent values from the procfs file.
        /// </summary>
        public void UpdateFromProcFs()
        {
            _values.Clear();

            try
            {
                _file.Seek(0, SeekOrigin.Begin);
                _file.Flush(); // clear internal read buffer

                using (StreamReader reader = new StreamReader(_file, encoding: Encoding.ASCII, leaveOpen: true))
                {
                    for (;;)
                    {
                        string headerLine = reader.ReadLine();
                        if (headerLine == null)
                            break;

                        string fieldLine = reader.ReadLine();
                        if (fieldLine == null)
                            return;

                        string[] headerEntries = headerLine.Split(' ');
                        string[] fieldEntries = fieldLine.Split(' ');

                        if (headerEntries.Length == 0 || fieldEntries.Length == 0 || headerEntries.Length != fieldEntries.Length || headerEntries[0] != fieldEntries[0])
                            return;
                        if (!headerEntries[0].EndsWith(':') || headerEntries[0].Length <= 1)
                            return;

                        string categoryName = headerEntries[0].Substring(0, headerEntries[0].Length - 1);
                        for (int ndx = 1; ndx < fieldEntries.Length; ++ndx)
                        {
                            string entryName = headerEntries[ndx];
                            string entryValue = fieldEntries[ndx];

                            if (!long.TryParse(entryValue, System.Globalization.NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long entryValueLong))
                                return;

                            _values.Add((categoryName, entryName), entryValueLong);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Error("Failed to read {Path}: {Error}", _path, ex);
            }
        }

        /// <summary>
        /// Retrieves the most recently read value.
        /// </summary>
        public bool TryGetValue(string category, string entry, out long value)
        {
            return _values.TryGetValue((category, entry), out value);
        }

        public void Dispose()
        {
            _file.Dispose();
        }
    }

    /// <summary>
    /// Reader for key-value procfs files such as /proc/net/snmp6
    /// </summary>
    class ProcFsSnmp6Reader : IDisposable
    {
        readonly string _path;
        readonly Stream _file;
        readonly Dictionary<string, long> _values;

        public ProcFsSnmp6Reader(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _values = new Dictionary<string, long>();

            UpdateFromProcFs();
        }

        public ProcFsSnmp6Reader(Stream stream)
        {
            _path = "anon";
            _file = stream ?? throw new ArgumentNullException(nameof(stream));
            _values = new Dictionary<string, long>();

            UpdateFromProcFs();
        }

        /// <summary>
        /// Reads the most recent values from the procfs file.
        /// </summary>
        public void UpdateFromProcFs()
        {
            _values.Clear();

            try
            {
                _file.Seek(0, SeekOrigin.Begin);
                _file.Flush(); // clear internal read buffer

                using (StreamReader reader = new StreamReader(_file, encoding: Encoding.ASCII, leaveOpen: true))
                {
                    for (;;)
                    {
                        string fieldLine = reader.ReadLine();
                        if (fieldLine == null)
                            return;

                        string[] fieldEntries = fieldLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (fieldEntries.Length != 2)
                            return;

                        string entryName = fieldEntries[0];
                        string entryValue = fieldEntries[1];

                        if (!long.TryParse(entryValue, System.Globalization.NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long entryValueLong))
                            return;

                        _values.Add(entryName, entryValueLong);
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Error("Failed to read {Path}: {Error}", _path, ex);
            }
        }

        /// <summary>
        /// Retrieves the most recently read value.
        /// </summary>
        public bool TryGetValue(string entry, out long value)
        {
            return _values.TryGetValue(entry, out value);
        }

        public void Dispose()
        {
            _file.Dispose();
        }
    }
}
