// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Metaplay.Core
{
    public class LogTemplateFormatter
    {
        static readonly Regex s_validIdentifierHead = new Regex("^[A-Za-z_][0-9A-Za-z_]*", RegexOptions.Compiled);

        // Converts valid identifiers in a message template to numeric indices. This
        // makes the format string suitable for plain String.Format().
        // @ and $ Property operators are not supported.
        private static string MessageTemplateToFormatString(string template)
        {
            StringBuilder dst = new StringBuilder(template.Length);
            int numNamedHoles = 0;
            int srcNdx = 0;

            while (true)
            {
                int nextOpenNdx = template.IndexOf('{', srcNdx);
                if (nextOpenNdx == -1)
                {
                    int remaining = template.Length - srcNdx;
                    dst.Append(template, srcNdx, remaining);
                    break;
                }

                dst.Append(template, srcNdx, nextOpenNdx + 1 - srcNdx);
                srcNdx = nextOpenNdx + 1;

                if (srcNdx == template.Length)
                    break;

                char peek = template[srcNdx];
                if (peek == '{')
                {
                    // escaped {, i.e. ".. {{". push to dst as is
                    dst.Append('{');
                    srcNdx++;
                    continue;
                }

                Match identifier = s_validIdentifierHead.Match(template, srcNdx, template.Length - srcNdx);
                if (!identifier.Success)
                {
                    // Not identifier. It is either numeric or illegal. In both cases String.Format will handle it.
                    continue;
                }

                int endOfItem = template.IndexOf('}', srcNdx);
                if (endOfItem == -1)
                {
                    // this is not a valid item. Keep as is, let underlying String.Format handle this.
                    continue;
                }

                dst.Append(numNamedHoles.ToString(CultureInfo.InvariantCulture));
                numNamedHoles += 1;

                srcNdx += identifier.Length;
                dst.Append(template, srcNdx, endOfItem - srcNdx + 1);
                srcNdx = endOfItem + 1;
            }
            return dst.ToString();
        }

        /// <summary>
        /// Renders a message template string with given args.
        ///
        /// See https://messagetemplates.org/ for format specification.
        /// </summary>
        /// <param name="messageTemplate">Format template string</param>
        /// <param name="args"></param>
        /// <returns>Formatted String</returns>
        public static string ToFlatString(string messageTemplate, params object[] args)
        {
            string formatString = MessageTemplateToFormatString(messageTemplate);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, formatString, args);
            }
            catch
            {
                // We cannot format. Do emergency formatting to try preserve information.
                string[] argStrings = new string[args.Length];
                for (int i = 0; i < args.Length; ++i)
                {
                    try
                    {
                        if (args[i] == null)
                            argStrings[i] = "null";
                        else
                            argStrings[i] = Util.ObjectToStringInvariant(args[i]);
                    }
                    catch
                    {
                        argStrings[i] = "<ToString() failed>";
                    }
                }
                return $"fmtError{{fmt: {messageTemplate}, args: {String.Join(",", argStrings)}}}";
            }
        }
    }
}
