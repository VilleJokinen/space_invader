// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for server-sent-events.
    /// This is slightly ghetto test code. In reality, it should be vent driven rather than polling for events.
    /// The task handling inside Get() should probably be implemented differently too, eg: one thread for all client
    /// instead of one per client. We may even want to switch to Web Sockets instead so that the client can subscribe
    /// to individual topics.
    /// </summary>
    public class SseController : GameAdminApiController
    {
        public SseController(ILogger<SseController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        // <summary>
        // Start receiving events.
        // Usage:  GET /api/sse
        // Test:   curl -N http://localhost:5550/api/sse
        // </summary>
        [HttpGet("sse")]
        [RequirePermission(MetaplayPermissions.ApiGeneralView)]
        public async Task Get()
        {
            HttpContext.Items.Add("_MetaplayLongRunningQuery", new object());

            // Reply with headers immediately.
            Response.Headers.Add("Content-Type", "text/event-stream");
            await Response.Body.FlushAsync();

            // Do long poll.
            IHttpBodyControlFeature bodyControl = HttpContext.Features.Get<IHttpBodyControlFeature>();
            if (bodyControl != null)
            {
                bodyControl.AllowSynchronousIO = true;

                // Events that we want to check for
                List<SseEventChecker> eventCheckers = new List<SseEventChecker>
                {
                    new ActiveGameConfigSseEventChecker(),
                    new RuntimeOptionsSseEventChecker(),
                };

                // Enter a loop, checking for and sending events every few seconds until the client disconnects
                const int PollIntervalInSeconds  = 5;
                const int KeepAliveTimeInSeconds = 30;
                int TimeSinceLastSendInSeconds   = 0;
                while (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    // Check for events from any of the event checkers
                    bool eventSent = false;
                    foreach (SseEventChecker eventChecker in eventCheckers)
                    {
                        SseEvent newEvent = eventChecker.CheckForEvent();
                        if (newEvent != null)
                        {
                            await Response.WriteAsync($"data: {newEvent.AsJson()}\r\r");
                            eventSent = true;
                        }
                    }

                    // If no events have been sent for a period of time then we need to send a keep-alive
                    if (!eventSent && TimeSinceLastSendInSeconds >= KeepAliveTimeInSeconds)
                    {
                        await Response.WriteAsync("\r\r");
                        eventSent = true;
                    }

                    if (eventSent)
                    {
                        await Response.Body.FlushAsync();
                        TimeSinceLastSendInSeconds = 0;
                    }
                    else
                    {
                        TimeSinceLastSendInSeconds += PollIntervalInSeconds;
                    }

                    await Task.Delay(PollIntervalInSeconds * 1000);
                }
            }
            else
            {
                throw new MetaplayHttpException(500, "Could not send SSE.", "Failed to get access to body control feature.");
            }
        }

        /// <summary>
        /// Encapsulates an event that can be sent to the client. Contains a message type and some optional opaque data
        /// </summary>
        class SseEvent
        {
            object Event = null;
            public SseEvent(string name, object data = null)
            {
                Event = new
                {
                    name = name,
                    data = data
                };
            }
            public string AsJson()
            {
                return JsonSerialization.SerializeToString(Event);
            }
        }

        /// <summary>
        /// Abstract base class for checking for events that might get sent out over SSE
        /// </summary>
        abstract class SseEventChecker
        {
            public abstract SseEvent CheckForEvent();
        }

        /// <summary>
        /// Event checker for changes in the active game config version
        /// </summary>
        class ActiveGameConfigSseEventChecker : SseEventChecker
        {
            MetaGuid _activeVersion;

            public ActiveGameConfigSseEventChecker()
            {
               _activeVersion = GetActiveVersionOrNone();
            }
            public override SseEvent CheckForEvent()
            {
                MetaGuid newActiveVersion = GetActiveVersionOrNone();
                if (_activeVersion != newActiveVersion)
                {
                    _activeVersion = newActiveVersion;
                    return new SseEvent("activeGameConfigChanged", null);
                }
                return null;
            }

            MetaGuid GetActiveVersionOrNone()
            {
                return GlobalStateProxyActor.ActiveGameConfig.Get()?.BaselineStaticGameConfigId ?? MetaGuid.None;
            }
        }

        /// <summary>
        /// Event checker for changes in the runtime options
        /// </summary>
        class RuntimeOptionsSseEventChecker : SseEventChecker
        {
            MetaTime _lastUpdateTime;
            public RuntimeOptionsSseEventChecker()
            {
                _lastUpdateTime = RuntimeOptionsRegistry.Instance._lastUpdateTime;
            }
            public override SseEvent CheckForEvent()
            {
                MetaTime newGenerationCount = RuntimeOptionsRegistry.Instance._lastUpdateTime;
                if (_lastUpdateTime != newGenerationCount)
                {
                    _lastUpdateTime = newGenerationCount;
                    return new SseEvent("runtimeOptionsChanged", null);
                }
                return null;
            }
        }
    }
}
