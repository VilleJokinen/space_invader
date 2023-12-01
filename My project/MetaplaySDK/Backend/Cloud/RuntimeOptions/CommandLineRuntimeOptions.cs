// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Cloud.RuntimeOptions
{
    /// <summary>
    /// The command line represented as a list of RuntimeOptions definitions.
    /// </summary>
    public class CommandLineRuntimeOptions
    {
        public class ParseError : Exception
        {
            public ParseError(string message) : base(message)
            {
            }
        }

        readonly OrderedDictionary<string, string> _fields;

        public IReadOnlyDictionary<string, string> Definitions => _fields;
        public IConfigurationSource ConfigurationSource => new FixedFieldsConfigurationSource(_fields);

        CommandLineRuntimeOptions(OrderedDictionary<string, string> fields)
        {
            _fields = fields;
        }

        /// <summary>
        /// Parse the command line and return CommandLineRuntimeOptions form the contents.
        ///
        /// <para>
        /// Only four formats are allowed. Any other format is a parse failure and will throw a <see cref="ParseError"/>:
        /// </para>
        /// <para>
        /// A value can be specified as <c>--SectionName:KeyName=Value</c> or <c>--SectionName:KeyName Value</c>.
        /// In both cases, <c>SectionName</c>, <c>KeyName</c> and <c>Value</c> must be non-empty.
        /// </para>
        /// <para>
        /// A value can also be specified as <c>-ShortName=Value</c> or <c>-ShortName Value</c>. In both cases <c>ShortName</c>,
        /// <c>KeyName</c> and <c>Value</c> must be non-empty, and <c>ShortName</c> must be specified in <paramref name="shortToLongFormMappings"/>.
        /// Note that there is no "flag" format and the Value must always be specified, even for boolean values.
        /// </para>
        /// </summary>
        public static CommandLineRuntimeOptions Parse(string[] commandLine, Dictionary<string, string> shortToLongFormMappings)
        {
            if (commandLine == null)
                throw new ArgumentNullException(nameof(commandLine));
            if (shortToLongFormMappings == null)
                throw new ArgumentNullException(nameof(shortToLongFormMappings));

            OrderedDictionary<string, string>   resultFields        = new OrderedDictionary<string, string>();
            OrderedDictionary<string, string>   fieldSourceTokens   = new OrderedDictionary<string, string>();
            OrderedDictionary<string, string>   sectionSourceTokens = new OrderedDictionary<string, string>();
            int                                 cursor              = 0;

            for (;;)
            {
                if (cursor >= commandLine.Length)
                    break;

                string token = commandLine[cursor];
                cursor++;

                // Is long or short form
                string withoutPrefix;
                bool isShortForm;
                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    withoutPrefix = token.Substring(2);
                    isShortForm = false;
                }
                else if (token.StartsWith("-", StringComparison.Ordinal))
                {
                    withoutPrefix = token.Substring(1);
                    isShortForm = true;
                }
                else
                    throw new ParseError($"Expected an option name in long or short form (example: --SectionName:KeyName or -ShortName). Found \"{token}\"");

                // Is inline form (--A:B=C) or not (--A:B C)
                int separatorNdx = withoutPrefix.IndexOf('=', StringComparison.Ordinal);
                string fieldName;
                string fieldValue;
                if (separatorNdx != -1)
                {
                    // inline
                    fieldName = withoutPrefix.Substring(0, separatorNdx);
                    fieldValue = withoutPrefix.Substring(separatorNdx + 1);
                }
                else
                {
                    // Not inline
                    // \note: if the value is missing, the value is set to null. This is checked later
                    fieldName = withoutPrefix;
                    if (cursor == commandLine.Length)
                        fieldValue = null;
                    else
                    {
                        fieldValue = commandLine[cursor];
                        cursor++;
                    }
                }

                // Validate fieldName
                if (string.IsNullOrWhiteSpace(fieldName))
                    throw new ParseError($"Invalid field name while processing \"{token}\"");

                if (isShortForm)
                {
                    if (!shortToLongFormMappings.ContainsKey(fieldName))
                    {
                        // A weird long/short hybrid?
                        if (fieldName.Contains(':', StringComparison.Ordinal))
                            throw new ParseError($"Unrecognized command line short form \"{token}\". Did you mean with long form with \"--\" prefix, i.e. \"-{token}\"");

                        // case sensitivity issue?
                        string caseMismatchingMatch = shortToLongFormMappings.Keys.Where(key => string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                        if (caseMismatchingMatch != null)
                            throw new ParseError($"Unrecognized command line argument \"{token}\". Did you mean \"-{caseMismatchingMatch}\". Arguments are Case Sensitive.");

                        throw new ParseError($"Unrecognized command line argument \"{token}\"");
                    }
                    fieldName = shortToLongFormMappings[fieldName];
                }
                else
                {
                    int sectionFieldSeparatorNdx = fieldName.IndexOf(':', StringComparison.Ordinal);
                    if (sectionFieldSeparatorNdx == -1 && shortToLongFormMappings.ContainsKey(fieldName))
                        throw new ParseError($"Invalid syntax for long form argument \"{token}\". Did you mean with short form, i.e. \"{token.Substring(1)}\"");

                    string caseMismatchingMatch = shortToLongFormMappings.Keys.Where(key => string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    if (caseMismatchingMatch != null)
                        throw new ParseError($"Invalid syntax for long form argument \"{token}\". Did you mean \"-{caseMismatchingMatch}\". Note that arguments are case sensitive.");

                    if (sectionFieldSeparatorNdx < 1 || sectionFieldSeparatorNdx >= fieldName.Length - 1)
                        throw new ParseError($"Invalid syntax for long form argument. Expected separate section and key name (example: --SectionName:KeyName), got {fieldName} while processing \"{token}\"");
                }

                // Validate fieldValue
                if (fieldValue == null)
                    throw new ParseError($"Expected a value following the key \"{token}\"");

                string preexistingField = resultFields.Keys.Where(key => string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                if (preexistingField != null)
                {
                    // Due to aliasing/mapping, it can be hard to figure out the ovelap. Print original token
                    throw new ParseError($"Value {fieldName} was specified multiple times in the command line while processing \"{token}\". Previous definition was at \"{fieldSourceTokens[preexistingField]}\"");
                }

                // Validate section name
                string sectionName = fieldName.Substring(0, fieldName.IndexOf(':'));
                if (!sectionSourceTokens.ContainsKey(sectionName))
                {
                    string conflictingSectionName = sectionSourceTokens.Keys.Where(key => string.Equals(key, sectionName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    if (conflictingSectionName != null)
                    {
                        // Like previously, due to aliasing/mapping, it can be hard to figure out the ovelap. Print original token
                        throw new ParseError($"Name conflict. Section name in \"{token}\" is \"{sectionName}\". A conflicting section name \"{conflictingSectionName}\" was used previously in \"{sectionSourceTokens[conflictingSectionName]}\". Check UPPER- and lowercase characters.");
                    }
                    sectionSourceTokens.Add(sectionName, token);
                }

                resultFields.Add(fieldName, fieldValue);
                fieldSourceTokens.Add(fieldName, token);
            }

            return new CommandLineRuntimeOptions(resultFields);
        }
    }
}
