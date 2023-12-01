// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System.Collections.Generic;

namespace Metaplay.Unity
{
    public sealed class MetaplayLogs
    {
        IMetaLogger                     _logger;
        LogLevel                        _defaultLogLevel    = LogLevel.Invalid; // Invalid marks uninitialized
        Dictionary<string, LogLevel>    _logLevelOverrides  = new Dictionary<string, LogLevel>();
        readonly List<LogChannel>       _channels           = new List<LogChannel>();

        internal readonly LogChannel    Message;
        internal readonly LogChannel    Metaplay; // like "Main" in Client
        internal readonly LogChannel    Network;
        internal readonly LogChannel    IAPFakeStore;
        internal readonly LogChannel    Config;
        internal readonly LogChannel    Incidents;
        internal readonly LogChannel    Localization;

        internal MetaplayLogs(IMetaLogger logger)
        {
            _logger = logger;
            Message = CreateChannel("message");
            Metaplay = CreateChannel("metaplay");
            Network = CreateChannel("network");
            IAPFakeStore = CreateChannel("iapFakeStore");
            Config = CreateChannel("config");
            Incidents = CreateChannel("incidents");
            Localization = CreateChannel("localization");
        }

        public LogChannel CreateChannel(string name)
        {
            LogLevel channelLevel = GetLevelForChannel(name);
            LogChannel channel = new LogChannel(name, _logger, new MetaLogLevelSwitch(channelLevel));
            _channels.Add(channel);
            return channel;
        }

        public void Initialize(LogLevel defaultLogLevel, Dictionary<string, LogLevel> logLevelOverrides)
        {
            _defaultLogLevel = defaultLogLevel;
            _logLevelOverrides = logLevelOverrides ?? new Dictionary<string, LogLevel>();

            // Update level switches of all existing log channels
            UpdateChannelLevelSwitches();
        }

        public void Reset()
        {
            _defaultLogLevel = LogLevel.Invalid; // marks uninitialized
            _logLevelOverrides = new Dictionary<string, LogLevel>();

            UpdateChannelLevelSwitches();
        }

        void UpdateChannelLevelSwitches()
        {
            foreach (LogChannel channel in _channels)
                channel.ChannelLevel.MinimumLevel = GetLevelForChannel(channel.Name);
        }

        LogLevel GetLevelForChannel(string name)
        {
            // GetValueOrDefault() not supported by old Unity
            if (_logLevelOverrides.TryGetValue(name, out LogLevel foundValue))
                return foundValue;
            else
                return _defaultLogLevel;
        }
    }
}
