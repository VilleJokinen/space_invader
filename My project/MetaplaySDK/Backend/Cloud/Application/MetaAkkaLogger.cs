// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Serilog;
using System.Linq;

namespace Metaplay.Cloud
{
    /// <summary>
    /// Custom Akka.NET logger which forwards log events to Serilog.
    /// </summary>
    public class MetaAkkaLogger : MetaReceiveActor, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
    {
        // \note Akka's LogMessage<LogValues<..>> provides IEnumerable<> to args but Serilog only accepts arrays, so we must allocate a copy here.
        //       This should not be a problem as we don't expect many messages from Akka to be logged.

        static ILogger GetLogger(LogEvent ev)
        {
            return Serilog.Log.Logger
                .ForContext("Timestamp", ev.Timestamp)
                .ForContext("SourceContext", ev.LogSource ?? "#root");
        }

        static void ReceiveError(Error ev)
        {
            if (ev.Message is LogMessage msg)
                GetLogger(ev).Error(ev.Cause, msg.Format, msg.Parameters().ToArray());
            else
                GetLogger(ev).Error(ev.Cause, "{Message:l}", ev.Message);

            MetaLogger.IncLevelCounter(Core.LogLevel.Error);
        }

        static void ReceiveWarning(Warning ev)
        {
            if (ev.Message is LogMessage msg)
                GetLogger(ev).Warning(msg.Format, msg.Parameters().ToArray());
            else
                GetLogger(ev).Warning("{Message:l}", ev.Message);

            MetaLogger.IncLevelCounter(Core.LogLevel.Warning);
        }

        static void ReceiveInfo(Info ev)
        {
            if (ev.Message is LogMessage msg)
                GetLogger(ev).Information(msg.Format, msg.Parameters().ToArray());
            else
                GetLogger(ev).Information("{Message:l}", ev.Message);

            MetaLogger.IncLevelCounter(Core.LogLevel.Information);
        }

        static void ReceiveDebug(Debug ev)
        {
            if (ev.Message is LogMessage msg)
                GetLogger(ev).Debug(msg.Format, msg.Parameters().ToArray());
            else
                GetLogger(ev).Debug("{Message:l}", ev.Message);

            MetaLogger.IncLevelCounter(Core.LogLevel.Debug);
        }

        public MetaAkkaLogger()
        {
            Receive<Error>(ReceiveError);
            Receive<Warning>(ReceiveWarning);
            Receive<Info>(ReceiveInfo);
            Receive<Debug>(ReceiveDebug);

            Receive<InitializeLogger>(msg =>
            {
                Sender.Tell(new LoggerInitialized());
            });
        }
    }
}
