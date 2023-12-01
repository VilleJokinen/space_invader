// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    public class GameConfigValidationResult
    {
        List<GameConfigSourceMapping>                    _sourceMappingScope;
        string                                           _variantScope;
        readonly List<GameConfigValidationMessage>            _validationMessages = new List<GameConfigValidationMessage>();
        readonly List<Predicate<GameConfigValidationMessage>> _filters            = new List<Predicate<GameConfigValidationMessage>>();

        public IEnumerable<GameConfigValidationMessage> FilteredValidationMessages
        {
            get { return ValidationMessages.Where(log => _filters.All(filter => filter(log))); }
        }

        public IReadOnlyList<GameConfigValidationMessage> ValidationMessages => _validationMessages;

        public GameConfigValidationResult(string variantScope)
        {
            _variantScope = variantScope;
        }

        internal void ScopeTo(List<GameConfigSourceMapping> sourceMappings)
        {
            if (sourceMappings == null)
                return;

            _sourceMappingScope = sourceMappings;
        }

        /// <summary>
        /// Adds a filter to exclude known false positive/accepted warnings
        /// </summary>
        /// <param name="filter">Predicate to determine whether to include a log or not, returning <c>True</c> means it is included.</param>
        public void AddFilter(Predicate<GameConfigValidationMessage> filter)
        {
            _filters.Add(filter);
        }

        /// <summary>
        /// Logs a message to be added to the config build metadata.
        /// The <paramref name="sheetName"/>, <paramref name="configKey"/>, and <paramref name="columnHint"/> are used to try to additional debug information to the output,
        /// Therefore it is important that these match to the corresponding config Library, Row, and Column names.
        /// </summary>
        /// <param name="sheetName">The sheet name that this log occured in, most of the time this is equal to the library name.</param>
        /// <param name="configKey">The row that this log occured in.</param>
        /// <param name="columnHint">A hint for the user to investigate the log.</param>
        /// <param name="additionalMessageData">Additional data that is added to the serialized metadata, therefore this has to use the <see cref="MetaSerializableDerivedAttribute"/> attribute.</param>
        void WriteMessage(
            string sheetName,
            string configKey,
            string message,
            GameConfigLogLevel messageLevel,
            string columnHint = "",
            AdditionalMessageData additionalMessageData = null,
            [CallerFilePath] string sourcePath = "",
            [CallerMemberName] string sourceMember = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            // Resolve location in source & URL to it (if applicable)
            // \todo The sheetName here is generally really the name of the GameConfigLibrary member in SharedGameConfig (it happens to match usually but it's not correct in the general case)
            GameConfigSourceLocation sourceLocation = (sheetName != null && configKey != null) ? TryGetItemMemberSourceLocation(sheetName, configKey, columnHint) : null;
            string url = (sourceLocation != null) ? sourceLocation.SourceInfo.GetLocationUrl(sourceLocation) : null;

            _validationMessages.Add(new GameConfigValidationMessage(
                sheetName,
                configKey,
                message,
                columnHint,
                _variantScope,
                url,
                sourcePath,
                sourceMember,
                sourceLineNumber,
                messageLevel,
                additionalMessageData));
        }

        public void Verbose(
            string sheetName,
            string configKey,
            string message,
            string columnHint = "",
            AdditionalMessageData additionalMessageData = null,
            [CallerFilePath] string sourcePath = "",
            [CallerMemberName] string sourceMember = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            WriteMessage(
                sheetName,
                configKey,
                message,
                GameConfigLogLevel.Verbose,
                columnHint,
                additionalMessageData,
                sourcePath,
                sourceMember,
                sourceLineNumber);
        }

        public void Debug(
            string sheetName,
            string configKey,
            string message,
            string columnHint = "",
            AdditionalMessageData additionalMessageData = null,
            [CallerFilePath] string sourcePath = "",
            [CallerMemberName] string sourceMember = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            WriteMessage(
                sheetName,
                configKey,
                message,
                GameConfigLogLevel.Debug,
                columnHint,
                additionalMessageData,
                sourcePath,
                sourceMember,
                sourceLineNumber);
        }

        public void Info(
            string sheetName,
            string configKey,
            string message,
            string columnHint = "",
            AdditionalMessageData additionalMessageData = null,
            [CallerFilePath] string sourcePath = "",
            [CallerMemberName] string sourceMember = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            WriteMessage(
                sheetName,
                configKey,
                message,
                GameConfigLogLevel.Information,
                columnHint,
                additionalMessageData,
                sourcePath,
                sourceMember,
                sourceLineNumber);
        }

        public void Warning(
            string sheetName,
            string configKey,
            string message,
            string columnHint = "",
            AdditionalMessageData additionalMessageData = null,
            [CallerFilePath] string sourcePath = "",
            [CallerMemberName] string sourceMember = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            WriteMessage(
                sheetName,
                configKey,
                message,
                GameConfigLogLevel.Warning,
                columnHint,
                additionalMessageData,
                sourcePath,
                sourceMember,
                sourceLineNumber);
        }

        public void Error(
            string sheetName,
            string configKey,
            string message,
            string columnHint = "",
            AdditionalMessageData additionalMessageData = null,
            [CallerFilePath] string sourcePath = "",
            [CallerMemberName] string sourceMember = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            WriteMessage(
                sheetName,
                configKey,
                message,
                GameConfigLogLevel.Error,
                columnHint,
                additionalMessageData,
                sourcePath,
                sourceMember,
                sourceLineNumber);
        }

        GameConfigSourceLocation TryGetItemMemberSourceLocation(string sheetName, string configKeyStr, string memberPathHint)
        {
            if (_sourceMappingScope != null)
            {
                foreach (GameConfigSourceMapping sourceMapping in _sourceMappingScope)
                {
                    // \todo Fix assumption on source being a spreadsheet. Shouldn't be relying on sheet names in the first place. We can know
                    //       the library and item within the library, so should re-map back to source using the library and item's identity. We
                    //       cannot rely on item reference itself as we're validating variants of the items, not the original baseline item.
                    if (sourceMapping.SourceInfo is GameConfigSpreadsheetSourceInfo spreadsheetSource && sheetName == spreadsheetSource.GetSheetName())
                    {
                        if (sourceMapping.TryFindItemSource(configKeyStr, _variantScope, memberPathHint, out GameConfigSourceLocation itemOrMemberLocation))
                            return itemOrMemberLocation;
                    }
                }
            }

            return null;
        }

        internal void DisposeScope()
        {
            _sourceMappingScope = null;
        }
    }

    /// <summary>
    /// Summary of the BuildReport data, containing the amount of messages per LogLevel for both Build and Validation messages.
    /// This is used in the dashboard to indicate how many messages there are without parsing the whole log.
    /// </summary>
    [MetaSerializable]
    public class GameConfigBuildSummary
    {
        [MetaMember(1)] public OrderedDictionary<GameConfigLogLevel, int> BuildMessagesCount          { get; private set; }
        [MetaMember(2)] public OrderedDictionary<GameConfigLogLevel, int> ValidationMessagesCount     { get; private set; }
        [MetaMember(3)] public GameConfigLogLevel                         HighestMessageLevel         { get; private set; }
        [MetaMember(4)] public bool                                       IsBuildMessagesTrimmed      { get; private set; }
        [MetaMember(5)] public bool                                       IsValidationMessagesTrimmed { get; private set; }

        GameConfigBuildSummary()
        {
        }

        GameConfigBuildSummary(
            OrderedDictionary<GameConfigLogLevel, int> buildMessagesCount,
            OrderedDictionary<GameConfigLogLevel, int> validationMessagesCount,
            GameConfigLogLevel highestMessageLevel,
            bool isBuildMessagesTrimmed,
            bool isValidationMessagesTrimmed)
        {
            ValidationMessagesCount     = validationMessagesCount;
            BuildMessagesCount          = buildMessagesCount;
            HighestMessageLevel         = highestMessageLevel;
            IsValidationMessagesTrimmed = isValidationMessagesTrimmed;
            IsBuildMessagesTrimmed      = isBuildMessagesTrimmed;
        }

        public static GameConfigBuildSummary GenerateFromReport(GameConfigBuildReport report, int maxReportMessages)
        {
            OrderedDictionary<GameConfigLogLevel, int> buildLogLogLevelToCountMapping          = new OrderedDictionary<GameConfigLogLevel, int>();
            OrderedDictionary<GameConfigLogLevel, int> validationResultsLogLevelToCountMapping = new OrderedDictionary<GameConfigLogLevel, int>();
            GameConfigLogLevel[]                       logLevels                               = (GameConfigLogLevel[])Enum.GetValues(typeof(GameConfigLogLevel));

            foreach (GameConfigLogLevel gameConfigLogLevel in logLevels)
            {
                buildLogLogLevelToCountMapping.Add(gameConfigLogLevel, 0);
                validationResultsLogLevelToCountMapping.Add(gameConfigLogLevel, 0);
            }

            if (report.BuildMessages != null)
                foreach (GameConfigBuildMessage buildMessage in report.BuildMessages)
                    buildLogLogLevelToCountMapping[buildMessage.Level]++;

            if (report.ValidationMessages != null)
                foreach (GameConfigValidationMessage validationMessage in report.ValidationMessages)
                    validationResultsLogLevelToCountMapping[validationMessage.MessageLevel]++;

            return new GameConfigBuildSummary(
                buildLogLogLevelToCountMapping,
                validationResultsLogLevelToCountMapping,
                report.HighestMessageLevel,
                report.BuildMessages?.Length > maxReportMessages,
                report.ValidationMessages?.Length > maxReportMessages);
        }
    }

    /// <summary>
    /// Contains the build and (optional) validation message logs that occurred during the building of a game config.
    /// Retained with the final build as metadata in the archive, or contained in <see cref="GameConfigBuildFailed"/>
    /// in case of failure during the build.
    /// </summary>
    [MetaSerializable]
    public class GameConfigBuildReport
    {
        [MetaMember(1)] public GameConfigLogLevel HighestMessageLevel { get; private set; }
        [MetaMember(3), MaxCollectionSize(int.MaxValue)] public GameConfigBuildMessage[] BuildMessages { get; private set; } = new GameConfigBuildMessage[] { }; // ensure legacy game configs deserialize with non-null messages
        [MetaMember(2), MaxCollectionSize(int.MaxValue)] public GameConfigValidationMessage[] ValidationMessages { get; private set; }

        public GameConfigBuildReport(IEnumerable<GameConfigBuildMessage> buildMessages, List<GameConfigValidationResult> validationResults)
        {
            BuildMessages = buildMessages.ToArray();
            ValidationMessages = MergeValidationMessages(validationResults); // \todo move merging outside to keep this simple?
            HighestMessageLevel = GetHighestMessageLevel();
        }

        private GameConfigBuildReport(GameConfigLogLevel highestMessageLevel, GameConfigBuildMessage[] buildMessages, GameConfigValidationMessage[] validationMessages)
        {
            HighestMessageLevel = highestMessageLevel;
            BuildMessages       = buildMessages;
            ValidationMessages  = validationMessages;
        }

        private GameConfigBuildReport() { }

        public int GetMessageCountForLevel(GameConfigLogLevel level)
        {
            // \todo Replace to use the summary information instead of log when it's merged
            return BuildMessages.Count(msg => msg.Level == level) + ValidationMessages.Count(msg => msg.MessageLevel == level);
        }

        /// <summary>
        /// Creates a clone of this BuildReport with the amount of messages trimmed to <paramref name="maxReportMessages"/>
        /// and sorted by GameConfigLogLevel, this is to prevent trimming important errors/warnings
        /// </summary>
        public GameConfigBuildReport TrimAndClone(int maxReportMessages)
        {
            GameConfigValidationMessage[] trimmedValidationMessages = ValidationMessages.OrderByDescending(x => x.MessageLevel).Take(maxReportMessages).ToArray();
            GameConfigBuildMessage[]      trimmedBuildMessages      = BuildMessages.OrderByDescending(x => x.Level).Take(maxReportMessages).ToArray();
            return new GameConfigBuildReport(HighestMessageLevel, trimmedBuildMessages, trimmedValidationMessages);
        }

        GameConfigValidationMessage[] MergeValidationMessages(IEnumerable<GameConfigValidationResult> validationResults)
        {
            if (validationResults == null)
                return Array.Empty<GameConfigValidationMessage>();

            // Merge validation results of variants into a single list of messages
            OrderedDictionary<int, GameConfigValidationMessage> mergedLogs = new OrderedDictionary<int, GameConfigValidationMessage>();
            foreach (GameConfigValidationResult result in validationResults)
            {
                foreach (GameConfigValidationMessage msg in result.FilteredValidationMessages)
                {
                    int valueHashCode = msg.GetValueHashCode();
                    if (mergedLogs.TryGetValue(valueHashCode, out var source))
                        source.AddVariant(msg);
                    else
                        mergedLogs[valueHashCode] = msg;
                }
            }

            return mergedLogs.Values.ToArray();
        }

        GameConfigLogLevel GetHighestMessageLevel()
        {
            GameConfigLogLevel highestLevel = GameConfigLogLevel.NotSet;

            // Build messages
            foreach (GameConfigBuildMessage msg in BuildMessages)
                highestLevel = (msg.Level > highestLevel) ? msg.Level : highestLevel;

            // Validation messages
            foreach (GameConfigValidationMessage msg in ValidationMessages)
                highestLevel = (msg.MessageLevel > highestLevel) ? msg.MessageLevel : highestLevel;

            return highestLevel;
        }

        public string MessagesToString(int maxBuildLogMessages = -1, int maxValidationLogMessages = -1)
        {
            StringBuilder sb = new StringBuilder();

            // Build log
            sb.AppendLine("");
            int showBuildMessageCount = (maxBuildLogMessages < 0) ? BuildMessages.Length : System.Math.Min(maxBuildLogMessages, BuildMessages.Length);
            if (showBuildMessageCount == BuildMessages.Length)
                sb.AppendLine(Invariant($"BUILD LOG ({BuildMessages.Length} messages):"));
            else
                sb.AppendLine(Invariant($"BUILD LOG (showing {showBuildMessageCount} of {BuildMessages.Length} messages):"));
            foreach (GameConfigBuildMessage msg in BuildMessages.Take(showBuildMessageCount))
                sb.AppendLine(msg.ToString());

            // Validation log
            if (ValidationMessages != null && ValidationMessages.Length > 0)
            {
                sb.AppendLine("");
                int showValidationMessageCount = (maxValidationLogMessages < 0) ? ValidationMessages.Length : System.Math.Min(maxValidationLogMessages, ValidationMessages.Length);
                if (showValidationMessageCount == ValidationMessages.Length)
                    sb.AppendLine(Invariant($"VALIDATION LOG ({ValidationMessages.Length} messages):"));
                else
                    sb.AppendLine(Invariant($"VALIDATION LOG (showing {showValidationMessageCount} of {ValidationMessages.Length} messages):"));
                foreach (GameConfigValidationMessage msg in ValidationMessages.Take(showValidationMessageCount))
                    sb.AppendLine(msg.ToString());
            }

            return sb.ToString();
        }

        public void PrintToConsole()
        {
            // Log a single message using the right DebugLog method for the message's log level.
            static void DebugLogMessage<TMessage>(GameConfigLogLevel level, TMessage msg)
            {
                switch (level)
                {
                    case GameConfigLogLevel.NotSet:         DebugLog.Verbose(msg.ToString());       break;
                    case GameConfigLogLevel.Verbose:        DebugLog.Verbose(msg.ToString());       break;
                    case GameConfigLogLevel.Debug:          DebugLog.Debug(msg.ToString());         break;
                    case GameConfigLogLevel.Information:    DebugLog.Information(msg.ToString());   break;
                    case GameConfigLogLevel.Warning:        DebugLog.Warning(msg.ToString());       break;
                    case GameConfigLogLevel.Error:          DebugLog.Error(msg.ToString());         break;
                    default:
                        throw new InvalidOperationException($"Invalid GameConfigLogLevel {level}");
                }
            }

            // Print all build log messages
            foreach (GameConfigBuildMessage msg in BuildMessages)
                DebugLogMessage(msg.Level, msg);

            // Print all validation messages
            foreach (GameConfigValidationMessage msg in ValidationMessages)
                DebugLogMessage(msg.MessageLevel, msg);
        }
    }

    [MetaSerializable]
    public enum GameConfigLogLevel
    {
        NotSet      = 0,
        Verbose     = 1,
        Debug       = 2,
        Information = 3,
        Warning     = 4,
        Error       = 5,
    }

    [MetaSerializable]
    public abstract class AdditionalMessageData
    {
        public abstract bool ValueEquals(AdditionalMessageData other);
        public abstract int GetValueHashCode();
    }

    [MetaSerializable]
    public class GameConfigValidationMessage
    {
        [MetaMember(1)]  public string                      SheetName        { get; private set; }
        [MetaMember(2)]  public string                      ConfigKey        { get; private set; }
        [MetaMember(3)]  public string                      Message          { get; private set; }
        [MetaMember(4)]  public string                      ColumnHint       { get; private set; }
        [MetaMember(5)]  public List<string>                Variants         { get; private set; }
        [MetaMember(6)]  public string                      Url              { get; private set; }
        [MetaMember(7)]  public string                      SourcePath       { get; private set; }
        [MetaMember(8)]  public string                      SourceMember     { get; private set; }
        [MetaMember(9)]  public int                         SourceLineNumber { get; private set; }
        [MetaMember(10)] public GameConfigLogLevel          MessageLevel     { get; private set; }
        [MetaMember(11)] public int                         Count            { get; private set; }
        [MetaMember(12)] public AdditionalMessageData       AdditionalData   { get; private set; }


        private GameConfigValidationMessage() { }

        public GameConfigValidationMessage(
            string sheetName,
            string configKey,
            string message,
            string columnHint,
            string variant,
            string url,
            string sourcePath,
            string sourceMember,
            int sourceLineNumber,
            GameConfigLogLevel messageLevel,
            AdditionalMessageData additionalMessageData)
        {
            SheetName = sheetName;
            ConfigKey = configKey;
            Message   = message;

            Variants = new List<string>();
            Variants.Add(variant);

            Url              = url;
            SourcePath       = sourcePath;
            SourceMember     = sourceMember;
            SourceLineNumber = sourceLineNumber;
            MessageLevel     = messageLevel;
            ColumnHint       = columnHint;
            Count            = 1;

            AdditionalData = additionalMessageData;
        }

        public void AddVariant(GameConfigValidationMessage variantGameConfigMessage)
        {
            Variants.AddRange(variantGameConfigMessage.Variants);
            Count += variantGameConfigMessage.Variants.Count;
        }

        public override string ToString()
        {
            const string notAvailable = "N/A";
            var          configKey    = ConfigKey ?? notAvailable;
            var          column       = ColumnHint ?? notAvailable;
            var          library      = SheetName ?? notAvailable;
            var          message      = Message ?? "";
            return Invariant($"{ToLetter(MessageLevel)}: {library}/{configKey}/{column}: {message}{Environment.NewLine}Occurs {Count} times in experiments ({string.Join("), (", Variants)}){Environment.NewLine}{Url}");
        }

        static string ToLetter(GameConfigLogLevel messageLevel) => messageLevel switch
        {
            GameConfigLogLevel.Verbose     => "V",
            GameConfigLogLevel.Debug       => "D",
            GameConfigLogLevel.Information => "I",
            GameConfigLogLevel.Warning     => "W",
            GameConfigLogLevel.Error       => "E",
            _                              => "",
        };

        public int GetValueHashCode()
        {
            unchecked
            {
                int hashCode = SheetName?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (ConfigKey?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (Message?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (SourcePath?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (SourceMember?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ SourceLineNumber;
                hashCode = (hashCode * 397) ^ (int)MessageLevel;
                hashCode = (hashCode * 397) ^ (AdditionalData?.GetValueHashCode() ?? 0);
                return hashCode;
            }
        }
    }
}
