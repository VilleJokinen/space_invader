// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Config
{
    public class ConfigParserException : Exception
    {
        public ConfigParserException(string message, string url, Exception innerException) : base($"{message}{Environment.NewLine}{url}", innerException)
        {

        }

        public ConfigParserException(string message, SpreadsheetCell exceptionSource) : this(message, new List<SpreadsheetCell> {exceptionSource})
        {
        }

        public ConfigParserException(string message, SpreadsheetCell exceptionSource, Exception innerException) : base(ConstructDebugMessage(message, new List<SpreadsheetCell>() {exceptionSource}), innerException) { }
        public ConfigParserException(string message, List<SpreadsheetCell> exceptionSources, Exception innerException) : base(ConstructDebugMessage(message, exceptionSources), innerException) { }

        public ConfigParserException(string message, List<SpreadsheetCell> exceptionSources)
            : base(ConstructDebugMessage(message, exceptionSources)) { }

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        public ConfigParserException(string message, GameConfigHelper.RowWithVariantAssociation row)
            : base(ConstructDebugMessage(message, row)) { }

        public ConfigParserException(string message, GameConfigHelper.RowWithVariantAssociation row, Exception innerException)
            : base(ConstructDebugMessage(message, row), innerException) { }

        static string ConstructDebugMessage(string message, GameConfigHelper.RowWithVariantAssociation row)
        {
            return $"{message}{Environment.NewLine}Source Cells: {row.Row.FirstOrDefault(x => x.HasValues())}";
        }
#endif

        public ConfigParserException(string message, GameConfigSourceLocation sourceLocation)
            : base(ConstructDebugMessage(message, sourceLocation)) { }

        public ConfigParserException(string message, GameConfigSourceLocation sourceLocation, Exception innerException)
            : base(ConstructDebugMessage(message, sourceLocation), innerException) { }

        static string ConstructDebugMessage(string message, GameConfigSourceLocation sourceLocation)
        {
            // \todo Is this correct? Restore link to source
            return $"{message}{Environment.NewLine}Source Cells: {sourceLocation}";
            //return $"{message}{Environment.NewLine}Source Cells: {row.Row.FirstOrDefault(x => x.HasValues()).GetRowLink()}";
        }

        static string ConstructDebugMessage(string message, List<SpreadsheetCell> exceptionSources)
        {
            string sourceString = "Source cells:" + string.Join(", ", exceptionSources.Where(x => x.HasValues()));
            return $"{message}{Environment.NewLine}{sourceString}";
        }
    }
}
