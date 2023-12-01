// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Text;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Efficient appendable string with built-in support for indenting lines.
    /// Convenient for code generation.
    /// </summary>
    public class IndentedStringBuilder
    {
        bool            _outputDebugCode;   // Should debug code be outputted?
        StringBuilder   _sb;
        int             _level  = 0;

        public bool DebugEnabled => _outputDebugCode;

        public IndentedStringBuilder(bool outputDebugCode, int initialCapacity = 4096)
        {
            _outputDebugCode = outputDebugCode;
            _sb = new StringBuilder(initialCapacity);
        }

        public void AppendLine()
        {
            _sb.AppendLine();
        }

        public void AppendLine(string str)
        {
            for (int ndx = 0; ndx < _level; ndx++)
                _sb.Append("    ");

            _sb.AppendLine(str);
        }

        public void AppendDebugLine()
        {
            if (_outputDebugCode)
                AppendLine();
        }

        public void AppendDebugLine(string str)
        {
            if (_outputDebugCode)
                AppendLine(str);
        }

        public void Indent(string str)
        {
            AppendLine(str);
            _level++;
        }

        public void Unindent(string str)
        {
            _level--;
            AppendLine(str);
        }

        public void Indent() => _level++;
        public void Unindent() => _level--;

        public override string ToString() => _sb.ToString();
    }
}
