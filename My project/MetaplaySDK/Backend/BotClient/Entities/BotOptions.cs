// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Message;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.BotClient
{
    public class ServerGatewaySpec
    {
        public string   ServerHost;
        public int      ServerPort;
        public bool     EnableTls;

        public ServerGateway ToServerGateway() => new ServerGateway(ServerHost, ServerPort, EnableTls);
    }

    [RuntimeOptions("Bot", isStatic: true)]
    public class BotOptions : RuntimeOptionsBase
    {
        [MetaDescription("Server hostname to connect to.")]
        public string                   ServerHost              { get; private set; } = "localhost";
        [MetaDescription("Server port to connect to.")]
        public int                      ServerPort              { get; private set; } = 9339;
        [MetaDescription("Enables TLS for connections to the server.")]
        public bool                     EnableTls               { get; private set; } = false;
        [MetaDescription("Base URL to CDN (eg, https://<mygame>-<deploy>-assets.p1.metaplay.io/). Null means auto-resolve from ServerHost.")]
        public string                   CdnBaseUrl              { get; private set; } = null;
        [MetaDescription("Alternative gateways to the server in case a connection cannot be established to the ServerHost:ServerPort.")]
        public List<ServerGatewaySpec>  ServerBackupGateways    { get; private set; } = new List<ServerGatewaySpec>();

        [MetaDescription("The path where to cache GameConfig files retrieved from the CDN.")]
        public string       GameConfigCachePath                 { get; private set; } = "bin/GameConfigCache";
        [MetaDescription("Enables in-memory deduplication of game config content across different experiment specializations.")]
        public bool         EnableGameConfigInMemoryDeduplication { get; private set; } = true;

        [CommandLineAlias("-MaxBots")]
        [MetaDescription("Maximum number of bots to spawn.")]
        public int          MaxBots                             { get; set; } = 500;
        [CommandLineAlias("-MaxBotId")]
        [MetaDescription("Maximum bot id to use (defaults to 5 * MaxBots if left as 0).")]
        public long         MaxBotId                            { get; private set; } = 0; // Note: using long to allow scientific notation in json, which Helm uses for large values.
        [CommandLineAlias("-SpawnRate")]
        [MetaDescription("Number of new bots to spawn per second (until the maximum number of bots is reached).")]
        public float        SpawnRate                           { get; private set; } = 10.0f;
        [CommandLineAlias("-ExpectedSessionDuration")]
        [MetaDescription("Expected session duration for a session. Actual session lengths have some randomness applied.")]
        public TimeSpan     ExpectedSessionDuration             { get; private set; } = TimeSpan.FromMinutes(2);
        [MetaDescription("Force all bots to use the same PlayerId. Useful for testing multiple simultaneous logins for a single account.")]
        public bool         ForceConflictingPlayerId            { get; private set; } = false;

        [MetaDescription("Probability, per bot update tick, at which bot starts simulation of an app put to the background. This is either a long or short pause, see below.")]
        public float        PutOnBackgroundProbabilityPerTick   { get; private set; } = 0f;
        [MetaDescription("Length of a \"long\" pause when the app is put to simulated background. This should be long enough to cause a loss of a session.")]
        public TimeSpan     PutOnBackgroundLongPause            { get; private set; } = TimeSpan.FromSeconds(40);
        [MetaDescription("Length of a \"short\" pause the when app is put to simulated background. This should not result in the loss of a connection.")]
        public TimeSpan     PutOnBackgroundShortPause           { get; private set; } = TimeSpan.FromSeconds(2);
        [MetaDescription("Probability, per bot update tick, at which bot closes its message transport (in order to test session resumption).")]
        public float        DropConnectionProbabilityPerTick    { get; private set; } = 0f;

        [MetaDescription("Enables bots to produce dummy incident reports.")]
        public bool         EnableDummyIncidentReports          { get; private set; } = false;

        public override Task OnLoadedAsync()
        {
            // Resolve CDN base url (default to sensible value, if not specified)
            if (string.IsNullOrEmpty(CdnBaseUrl))
            {
                string serverHost = ServerHost;
                bool isLocalServer = serverHost == "127.0.0.1" || serverHost == "localhost";
                if (isLocalServer)
                    CdnBaseUrl = "http://127.0.0.1:5552/";
                else
                {
                    // Append '-assets' to part of host, eg, 'mygame-prod.p1.metaplay.io' -> 'https://mygame-prod-assets.p1.metaplay.io/'
                    string prefix = serverHost.Split('.')[0];
                    CdnBaseUrl = $"https://{prefix}-assets" + serverHost.Substring(prefix.Length) + "/";
                }
            }

            return Task.CompletedTask;
        }
    }
}
