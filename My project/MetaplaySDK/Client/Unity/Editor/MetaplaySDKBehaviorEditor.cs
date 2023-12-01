// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Actions;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.GuildDiscovery;
#endif
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Localization;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using Metaplay.Unity.Localization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Metaplay.Unity
{
    [CustomEditor(typeof(MetaplaySDKBehavior), isFallback = true)]
    public class MetaplaySDKBehaviorEditor : Editor
    {
        public void OnEnable()
        {
            MetaplaySDKBehavior sdk = (MetaplaySDKBehavior)target;
            sdk.EditorHookOnLocalizationUpdatedEvent += EditorHookOnLocalizationEvent;

            #if !METAPLAY_DISABLE_GUILDS
            if (MetaplayCore.Options.FeatureFlags.EnableGuilds)
            {
                EditorApplication.update += UpdateGuildHooks;
                UpdateGuildHooks();
            }
            #endif
        }

        public void OnDisable()
        {
            MetaplaySDKBehavior sdk = (MetaplaySDKBehavior)target;
            sdk.EditorHookOnLocalizationUpdatedEvent -= EditorHookOnLocalizationEvent;
            #if !METAPLAY_DISABLE_GUILDS
            if (MetaplayCore.Options.FeatureFlags.EnableGuilds)
                EditorApplication.update -= UpdateGuildHooks;
            #endif
        }

        void EditorHookOnLocalizationEvent()
        {
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            ConnectionGUI();
            LanguageGUI();
            OnInspectorGUIExperiments();
            #if !METAPLAY_DISABLE_GUILDS
            if (MetaplayCore.Options.FeatureFlags.EnableGuilds)
                OnInspectorGUIGuilds();
            #endif
            IAPSubscriptionsGUI();
            ActivablesGUI();
            MetaOffersGUI();
        }

        void ConnectionGUI()
        {
            MetaplaySDKBehavior sdk = (MetaplaySDKBehavior)target;

            // Connection
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);

            MetaplayConnection conn = MetaplaySDK.Connection;
            ConnectionState connState = conn?.State;

            SelectableField("Status", GetConnectionStatus(connState));

            // Connection health
            string healthText = "n/a";
            if (connState is ConnectionStates.Connected connected)
                healthText = connected.IsHealthy ? "Healthy" : "Unhealthy";

            SelectableField("Health", healthText);

            // Simulated Connection quality
            if (IsOffline())
            {
                sdk._simulatedLinkQuality = MetaplaySDKBehavior.LinkQualitySetting.Perfect;
                EditorGUILayout.Popup("Quality simulation", 0, new string[] { "Not supported in Offline mode. Perfect link"});
            }
            else
                sdk._simulatedLinkQuality = (MetaplaySDKBehavior.LinkQualitySetting)EditorGUILayout.Popup("Quality simulation", (int)sdk._simulatedLinkQuality, new string[] { "Perfect link", "Spotty link", "No incoming data (silent network error)", "No network (active refusal)" });

            sdk._appearAsPlatform = (ClientPlatform)EditorGUILayout.EnumPopup("Appear as client platform", sdk._appearAsPlatform);
        }

        static void SelectableField(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        string GetConnectionStatus(ConnectionState connState)
        {
            if (connState == null)
                return "n/a";

            switch (connState.Status)
            {
                case ConnectionStatus.Connected:
                    return "Connected";

                case ConnectionStatus.Connecting:
                    return "Connecting";

                case ConnectionStatus.Error:
                    return "Error";

                case ConnectionStatus.NotConnected:
                    return "Not Connected";

                default:
                    return "Unknown";
            }
        }

        void LanguageGUI()
        {
            EditorGUILayout.LabelField("Language", EditorStyles.boldLabel);

            if (!MetaplayCore.IsInitialized)
            {
                EditorGUILayout.LabelField("(Game is not running)");
                return;
            }
            else if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
            {
                EditorGUILayout.LabelField("(Localization is not enabled)");
                return;
            }

            SelectableField("Active Language", MetaplaySDK.ActiveLanguage?.LanguageId.ToString() ?? "None");
            SelectableField("Active Version", MetaplaySDK.ActiveLanguage?.Version.ToString() ?? "-");

            GUILayout.Label("Languages:");
            if (MetaplaySDK.LocalizationManager != null)
            {
                foreach ((LanguageId language, ContentHash builtinLocalizationVersion) in BuiltinLanguageRepository.GetBuiltinLanguages())
                {
                    List<string> tags = new List<string>();
                    tags.Add("builtin");

                    ContentHash[] cachedVersions = MetaplaySDK.LocalizationManager.DownloadCache.EditorTryGetCachedVersions(language);
                    ContentHash serverLocalizationVersion = MetaplaySDK.LocalizationManager.EditorTryGetLocalizationServerVersion(language);
                    ContentHash bestLocalizationVersion = (serverLocalizationVersion != ContentHash.None) ? serverLocalizationVersion : builtinLocalizationVersion;
                    bool canUpdateToServerVersion = serverLocalizationVersion != ContentHash.None && !cachedVersions.Contains(serverLocalizationVersion) && serverLocalizationVersion != builtinLocalizationVersion;

                    if (cachedVersions.Length > 0)
                        tags.Add("cached");
                    if (serverLocalizationVersion != ContentHash.None)
                        tags.Add("cdn");
                    if (canUpdateToServerVersion)
                        tags.Add("update");

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(language.ToString(), GUILayout.Width(150));
                    GUILayout.Label("[" + string.Join(",", tags) + "]");

                    GUI.enabled = (language != MetaplaySDK.ActiveLanguage.LanguageId) || (bestLocalizationVersion != MetaplaySDK.ActiveLanguage.Version);
                    if (GUILayout.Button("Use", GUILayout.Width(100)))
                    {
                        MetaplaySDK.LocalizationManager.SetCurrentLanguage(language, onCompleted: () => {});
                    }
                    GUI.enabled = true;

                    GUI.enabled = canUpdateToServerVersion;
                    if (GUILayout.Button("Fetch", GUILayout.Width(100)))
                    {
                        _ = MetaplaySDK.LocalizationManager.FetchLanguageAsync(language, serverLocalizationVersion, MetaplaySDK.CdnAddress, numFetchAttempts: 3, fetchTimeout: MetaDuration.FromSeconds(10), CancellationToken.None);
                    }
                    GUI.enabled = true;

                    GUILayout.EndHorizontal();
                }
            }
        }

        void OnInspectorGUIExperiments()
        {
            EditorGUILayout.LabelField("Player Experiments", EditorStyles.boldLabel);
            if ((MetaplaySDK.ActiveExperiments?.Count ?? 0) == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("(Not in any Experiment)");
                GUILayout.EndHorizontal();
            }
            else
            {
                var colw = GUILayout.Width(Screen.width * 0.20f);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Experiment", colw);
                GUILayout.Label("Variant", colw);
                GUILayout.Label("Analytics Id", colw);
                GUILayout.EndHorizontal();

                foreach ((PlayerExperimentId experimentId, ExperimentMembershipStatus status) in MetaplaySDK.ActiveExperiments)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(experimentId.ToString(), colw);
                    GUILayout.Label(status.VariantId == null ? "<control>" : status.VariantId.ToString(), colw);
                    GUILayout.Label($"({status.ExperimentAnalyticsId}={status.VariantAnalyticsId})", colw);
                    GUILayout.EndHorizontal();
                }
            }
        }

        bool IsOffline()
        {
            return MetaplaySDK.Connection?.Endpoint?.IsOfflineMode ?? true;
        }

        #region Guilds
        #if !METAPLAY_DISABLE_GUILDS

        GuildClient _lastGuildClient;

        bool _showGuildGroup = true;
        Vector2 _guildContentScroll;

        bool _showGuildDiscoveryGroup = true;
        bool _discoveringGuilds;
        List<GuildDiscoveryInfoBase> _discoveredGuilds;

        bool _showGuildSearchGroup = true;
        bool _searchingGuilds;
        string _searchString = "name";
        List<GuildDiscoveryInfoBase> _searchedGuilds;

        class ViewedGuildUIState
        {
            public bool showGroup;
            public Vector2 contentScroll;
        }
        bool _showGuildViewsGroup = true;
        Dictionary<int, ViewedGuildUIState> _viewedGuildUIStates = new Dictionary<int, ViewedGuildUIState>();

        bool _showGuildInviteGroup = true;
        string _inviteCode = "";
        GuildInviteInfo _inviteInfo = null;
        bool _inviteInspectOngoing = false;

        GuildClient GetGuildClient()
        {
            return GuildClient.EditorHookCurrent;
        }

        void UpdateGuildHooks()
        {
            GuildClient newClient = GetGuildClient();
            if (_lastGuildClient != newClient)
            {
                _lastGuildClient = newClient;
                if (_lastGuildClient != null)
                {
                    _lastGuildClient.PhaseChanged += () => Repaint();
                    _lastGuildClient.ActiveGuildUpdated += () => Repaint();
                }
            }
        }

        void OnInspectorGUIGuilds()
        {
            EditorGUILayout.Space(30);
            EditorGUILayout.LabelField("Guilds", EditorStyles.boldLabel);

            GuildClient guildClient = GetGuildClient();

            if (EditorGUILayout.BeginFoldoutHeaderGroup(_showGuildGroup, "Guild State"))
            {
                _showGuildGroup = true;

                GuildClientPhase phase = guildClient?.Phase ?? GuildClientPhase.NoSession;
                switch (phase)
                {
                    case GuildClientPhase.NoSession:
                    {
                        GUILayout.Label("NoSession");
                        break;
                    }

                    case GuildClientPhase.NoGuild:
                    {
                        GUILayout.Label("NoGuild");
                        if (GUILayout.Button("Create Guild"))
                            guildClient.BeginCreateGuild(null);
                        break;
                    }

                    case GuildClientPhase.GuildActive:
                    {
                        IGuildModelBase model = guildClient.GuildContext.CommittedModel;
                        if (!model.TryGetMember(guildClient.PlayerId, out GuildMemberBase currentPlayerMember))
                        {
                            GUILayout.Label("GuildActive -- ERROR: Player not Memer!");
                            break;
                        }

                        EditorGUILayout.BeginVertical();
                        GUILayout.Label($"GuildActive -- {model.GuildId}");
                        if (GUILayout.Button("Leave guild"))
                            guildClient.LeaveGuild();
                        if (GUILayout.Button("Kick random member"))
                            guildClient.GuildContext.EnqueueAction(new GuildMemberKick(targetPlayerId: RandomPCG.CreateNew().Choice(model.EnumerateMembers()), kickReasonOrNull: null));
                        if (GUILayout.Button("Promote/Demote random member"))
                        {
                            // try some random promotion/demotion until we find something that causes effects
                            RandomPCG rng = RandomPCG.CreateNew();
                            for (int i = 0; i < 30; ++i)
                            {
                                EntityId target = rng.Choice(model.EnumerateMembers());
                                GuildMemberRole role  = rng.Choice((GuildMemberRole[])System.Enum.GetValues(typeof(GuildMemberRole)));
                                if (!((IGuildModelBase)model).HasPermissionToChangeRoleTo(guildClient.PlayerId, target, role))
                                    continue;
                                OrderedDictionary<EntityId, GuildMemberRole> changes = ((IGuildModelBase)model).ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent.MemberEdit, target, role);
                                if (changes.Count == 0)
                                    continue;
                                guildClient.GuildContext.EnqueueAction(new GuildMemberEditRole(target, role, changes));
                                break;
                            }
                        }

                        GuildUIActions(guildClient, model);

                        GUILayout.Space(20);
                        string text = PrettyPrint.Verbose(model).ToString();
                        _guildContentScroll = EditorGUILayout.BeginScrollView(_guildContentScroll, GUILayout.Height(200));
                        Vector2 labelSize = GUI.skin.label.CalcSize(new GUIContent(text));
                        EditorGUILayout.SelectableLabel(text, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true), GUILayout.MinWidth(labelSize.x), GUILayout.MinHeight(labelSize.y));
                        EditorGUILayout.EndScrollView();

                        GUILayout.Label("Invites");
                        foreach ((int inviteId, GuildInviteState inviteState) in currentPlayerMember.Invites)
                        {
                            string expiresAt = "expires never";
                            if (inviteState.ExpiresAfter != null)
                            {
                                expiresAt = "expires in " + ((inviteState.CreatedAt + inviteState.ExpiresAfter.Value) - MetaTime.Now).ToString();
                            }

                            GUILayout.BeginHorizontal();

                            if (GUILayout.Button("-", GUILayout.Width(20)))
                                guildClient.RevokeGuildInvite(inviteId);

                            GUILayout.Label(inviteState.InviteCode.ToString(), GUILayout.Width(150));
                            GUILayout.Label(expiresAt, GUILayout.Width(150));
                            GUILayout.Label($"{inviteState.NumTimesUsed} / {inviteState.NumMaxUsages}", GUILayout.Width(150));
                            GUILayout.EndHorizontal();
                        }
                        if (GUILayout.Button("New Invite Code"))
                            _ = guildClient.BeginCreateGuildInviteCode(expirationDuration: MetaDuration.FromHours(1), usageLimit: 3);

                        EditorGUILayout.EndVertical();
                        break;
                    }

                    case GuildClientPhase.CreatingGuild:
                    case GuildClientPhase.JoiningGuild:
                    {
                        GUILayout.Label(phase.ToString());
                        break;
                    }
                }
            }
            else
            {
                _showGuildGroup = false;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Guild discovery
            if (EditorGUILayout.BeginFoldoutHeaderGroup(_showGuildDiscoveryGroup, "Guild Discovery"))
            {
                _showGuildDiscoveryGroup = true;

                if (guildClient != null)
                {
                    if (GUILayout.Button("Discover"))
                    {
                        _discoveredGuilds = null;
                        _discoveringGuilds = true;
                        guildClient.DiscoverGuilds((GuildDiscoveryResponse response) =>
                        {
                            _discoveringGuilds = false;
                            _discoveredGuilds = response.GuildInfos;
                            Repaint();
                        });
                    }

                    GUILayout.Label("Guilds:");
                    if (_discoveringGuilds)
                    {
                        GUILayout.Label("...");
                    }
                    else if (_discoveredGuilds != null)
                    {
                        foreach (var info in _discoveredGuilds)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(info.GuildId.ToString(), GUILayout.Width(150));
                            GUILayout.Label(info.DisplayName, GUILayout.Width(150));
                            GUILayout.Label($"{info.NumMembers} / {info.MaxNumMembers}", GUILayout.Width(150));
                            if (GUILayout.Button("Join"))
                                guildClient.BeginJoinGuild(info.GuildId);
                            if (GUILayout.Button("View"))
                                AddGuildView(guildClient, info.GuildId);
                            GUILayout.EndHorizontal();
                        }
                    }
                }
                else
                {
                    _discoveredGuilds = null;
                    _discoveringGuilds = true;
                }
            }
            else
            {
                _showGuildDiscoveryGroup = false;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Guild search
            if (EditorGUILayout.BeginFoldoutHeaderGroup(_showGuildSearchGroup, "Guild Search"))
            {
                _showGuildSearchGroup = true;

                if (guildClient != null)
                {
                    _searchString = GUILayout.TextField(_searchString);

                    if (GUILayout.Button("Search"))
                    {
                        DefaultGuildSearchParams searchParams = new DefaultGuildSearchParams() { SearchString = _searchString };

                        _searchedGuilds = null;
                        _searchingGuilds = true;
                        guildClient.SearchGuilds(searchParams, (GuildSearchResponse response) =>
                        {
                            _searchingGuilds = false;
                            if (response.IsError)
                                _searchedGuilds = null;
                            else
                                _searchedGuilds = response.GuildInfos;
                            Repaint();
                        });
                    }

                    GUILayout.Label("Guilds:");
                    if (_searchingGuilds)
                    {
                        GUILayout.Label("...");
                    }
                    else if (_searchedGuilds != null)
                    {
                        GUILayout.Label($"Found {_searchedGuilds.Count} guilds:");
                        foreach (var info in _searchedGuilds)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(info.GuildId.ToString(), GUILayout.Width(150));
                            GUILayout.Label(info.DisplayName, GUILayout.Width(150));
                            GUILayout.Label($"{info.NumMembers} / {info.MaxNumMembers}", GUILayout.Width(150));
                            if (GUILayout.Button("Join"))
                                _ = guildClient.BeginJoinGuild(info.GuildId);
                            if (GUILayout.Button("View"))
                                AddGuildView(guildClient, info.GuildId);
                            GUILayout.EndHorizontal();
                        }
                    }
                    else
                    {
                        GUILayout.Label($"Search failed");
                    }
                }
                else
                {
                    _searchedGuilds = null;
                    _searchingGuilds = true;
                }
            }
            else
            {
                _showGuildSearchGroup = false;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Guild views
            if (EditorGUILayout.BeginFoldoutHeaderGroup(_showGuildViewsGroup, "Guild Views"))
            {
                _showGuildViewsGroup = true;

                if (guildClient != null)
                {
                    GUILayout.Label($"Watching {guildClient.GuildViews.Count} guilds:");
                    foreach (var guildContext in new List<ForeignGuildContext>(guildClient.GuildViews))
                    {
                        ViewedGuildUIState uiState;
                        if (!_viewedGuildUIStates.TryGetValue(guildContext.ChannelId, out uiState))
                        {
                            uiState = new ViewedGuildUIState();
                            _viewedGuildUIStates[guildContext.ChannelId] = uiState;
                        }

                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button(uiState.showGroup ? "-" : "+", GUILayout.Width(20)))
                            uiState.showGroup = !uiState.showGroup;

                        GUILayout.Label(guildContext.Model.GuildId.ToString(), GUILayout.Width(150));
                        GUILayout.Label(guildContext.Model.DisplayName, GUILayout.Width(150));
                        GUILayout.Label($"{guildContext.Model.EnumerateMembers().Count()} / {guildContext.Model.MaxNumMembers}", GUILayout.Width(150));

                        if (GUILayout.Button("End view"))
                            guildClient.EndViewGuild(guildContext);

                        GUILayout.EndHorizontal();

                        if (uiState.showGroup)
                        {
                            string text = PrettyPrint.Verbose(guildContext.Model).ToString();
                            uiState.contentScroll = EditorGUILayout.BeginScrollView(uiState.contentScroll, GUILayout.Height(200));
                            Vector2 labelSize = GUI.skin.label.CalcSize(new GUIContent(text));
                            EditorGUILayout.SelectableLabel(text, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true), GUILayout.MinWidth(labelSize.x), GUILayout.MinHeight(labelSize.y));
                            EditorGUILayout.EndScrollView();
                        }
                    }
                }
            }
            else
            {
                _showGuildViewsGroup = false;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Guild invite code
            if (EditorGUILayout.BeginFoldoutHeaderGroup(_showGuildInviteGroup, "Guild Invite Codes"))
            {
                _showGuildInviteGroup = true;

                if (guildClient != null)
                {
                    GUILayout.Label($"Guild invite code:");
                    _inviteCode = GUILayout.TextField(_inviteCode);

                    GUI.enabled = GuildInviteCode.TryParse(_inviteCode, out GuildInviteCode inviteCode);
                    bool clickedOnUse = GUILayout.Button("Inspect Code");
                    GUI.enabled = true;

                    if (clickedOnUse)
                    {
                        _inviteInfo = null;
                        _inviteInspectOngoing = true;
                        guildClient.BeginInspectGuildInviteCode(inviteCode, (inviteInfo) =>
                        {
                            _inviteInfo = inviteInfo;
                            _inviteInspectOngoing = false;
                            Repaint();
                        });
                    }

                    if (_inviteInspectOngoing)
                    {
                        GUILayout.Label($"pending...");
                    }
                    else if (_inviteInfo == null)
                    {
                        GUILayout.Label($"no data");
                    }
                    else
                    {
                        string text = PrettyPrint.Verbose(_inviteInfo).ToString();
                        Vector2 labelSize = GUI.skin.label.CalcSize(new GUIContent(text));
                        EditorGUILayout.SelectableLabel(text, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true), GUILayout.MinWidth(labelSize.x), GUILayout.MinHeight(labelSize.y));

                        if (GUILayout.Button("Join Guild"))
                        {
                            _ = guildClient.BeginJoinGuildWithInviteCode(_inviteInfo.GuildId, _inviteInfo.InviteId, _inviteInfo.InviteCode);
                        }
                    }
                }
            }
            else
            {
                _showGuildInviteGroup = false;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void AddGuildView(GuildClient guildClient, EntityId guildId)
        {
            guildClient.BeginViewGuild(guildId, (ForeignGuildContext context) =>
            {
                Repaint();

                // viewing failed
                if (context == null)
                    return;

                // viewing succeeded
                // should we do something here?
            });
        }

        /// <summary>
        /// Game-specific guild actions.
        /// </summary>
        protected virtual void GuildUIActions(GuildClient guildClient, IGuildModelBase model)
        {
        }

        #endif
        #endregion

        #region IAP subscriptions

        bool _showIAPSubscriptions = false;

        void IAPSubscriptionsGUI()
        {
            IPlayerClientContext playerContext = MetaplaySDK.SessionContext?.PlayerContext;
            if (playerContext == null)
                return;
            IPlayerModelBase player = playerContext.Journal.StagedModel;
            if (player == null)
                return;

            EditorGUILayout.Space(30);
            EditorGUILayout.LabelField("IAP subscriptions", EditorStyles.boldLabel);
            _showIAPSubscriptions = EditorGUILayout.BeginFoldoutHeaderGroup(_showIAPSubscriptions, $"{player.IAPSubscriptions.Subscriptions.Count()} IAP subscriptions");
            if (_showIAPSubscriptions)
            {
                foreach ((InAppProductId productId, SubscriptionModel subscription) in player.IAPSubscriptions.Subscriptions)
                {
                    EditorGUILayout.LabelField($"Product {productId}", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField($"Active (with safety margin)? {player.CurrentTime < subscription.GetExpirationTime()}");
                    EditorGUILayout.LabelField($"Active (raw)? {player.CurrentTime < subscription.GetRawExpirationTime()}");
                    EditorGUILayout.LabelField($"Start: {subscription.GetStartTime()}");
                    EditorGUILayout.LabelField($"Expiration (with safety margin): {subscription.GetExpirationTime()}");
                    EditorGUILayout.LabelField($"Expiration (raw): {subscription.GetRawExpirationTime()}");
                    EditorGUILayout.LabelField($"Renewal status: {subscription.GetRenewalStatus()}");
                    EditorGUILayout.LabelField($"{subscription.SubscriptionInstances.Count} individual instances:");
                    EditorGUI.indentLevel++;
                    foreach ((string originalTransactionId, SubscriptionInstanceModel instance) in subscription.SubscriptionInstances)
                    {
                        EditorGUILayout.LabelField($"{Util.ShortenString(originalTransactionId, 30)}");
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField($"Disabled due to purchase reuse? {instance.DisabledDueToReuse}");
                        EditorGUILayout.LabelField($"Queried at {instance.StateQueriedAt}, state was {(instance.StateWasAvailableAtLastQuery ? "available" : "not available")}");
                        EditorGUILayout.LabelField($"Last known state (queried at {instance.LastKnownStateQueriedAt?.ToString() ?? "<none>"}):");
                        EditorGUI.indentLevel++;
                        if (instance.LastKnownState.HasValue)
                        {
                            SubscriptionInstanceState state = instance.LastKnownState.Value;
                            EditorGUILayout.LabelField($"Start: {state.StartTime}");
                            EditorGUILayout.LabelField($"Expiration: {state.ExpirationTime}");
                            EditorGUILayout.LabelField($"Renewal status: {state.RenewalStatus}");
                        }
                        else
                            EditorGUILayout.LabelField("<none>");
                        EditorGUI.indentLevel--;
                        EditorGUI.indentLevel--;
                    }
                    EditorGUI.indentLevel--;
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        #endregion

        #region Activables

        bool                                    _showAllDebugForcedActivablesGroup  = false;
        Dictionary<MetaActivableKindId, bool>   _showActivableKindGroup             = new Dictionary<MetaActivableKindId, bool>();

        void ActivablesGUI()
        {
            IPlayerClientContext playerContext = MetaplaySDK.SessionContext?.PlayerContext;
            if (playerContext == null)
                return;
            IPlayerModelBase player = playerContext.Journal.StagedModel;
            if (player == null)
                return;

            ISharedGameConfig gameConfig = player.GameConfig;

            EditorGUILayout.Space(30);
            EditorGUILayout.LabelField("Activables debug-forcing", EditorStyles.boldLabel);

            // List all debug-forced activables, and show a button for un-forcing them all.

            List<MetaActivableKey> debugForcedActivables = new List<MetaActivableKey>();
            foreach (MetaActivableRepository.KindSpec kind in MetaActivableRepository.Instance.AllKinds.Values)
            {
                IMetaActivableSet activableSet = MetaActivableUtil.GetPlayerActivableSetForKind(kind.Id, player);

                foreach (object activableId in MetaActivableUtil.GetActivableIdsOfKind(kind.Id, gameConfig))
                {
                    IMetaActivableConfigData    activableInfo       = MetaActivableUtil.GetActivableGameConfigData(new MetaActivableKey(kind.Id, activableId), gameConfig);
                    MetaActivableState          activableStateMaybe = activableSet.TryGetState(activableInfo);

                    if (activableStateMaybe?.Debug != null)
                        debugForcedActivables.Add(new MetaActivableKey(kind.Id, activableId));
                }
            }

            _showAllDebugForcedActivablesGroup = EditorGUILayout.BeginFoldoutHeaderGroup(_showAllDebugForcedActivablesGroup, $"There are {debugForcedActivables.Count} activables with a debug-forced phase");
            if (_showAllDebugForcedActivablesGroup)
            {
                if (GUILayout.Button($"Un-force all {debugForcedActivables.Count} debug-forced activables"))
                {
                    foreach (MetaActivableKey activableKey in debugForcedActivables)
                        playerContext.ExecuteAction(new PlayerDebugForceSetActivablePhase(activableKey.KindId, activableKey.ActivableId.ToString(), phase: null));
                }
                foreach (MetaActivableKey activableKey in debugForcedActivables)
                {
                    IMetaActivableConfigData activableInfo = MetaActivableUtil.GetActivableGameConfigData(activableKey, gameConfig);
                    EditorGUILayout.LabelField($"{activableInfo.DisplayName} ({activableKey})");
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // List all activables, grouped by kind, and show forcing controls for each activable.

            foreach (MetaActivableRepository.KindSpec kind in MetaActivableRepository.Instance.AllKinds.Values)
            {
                if (!_showActivableKindGroup.ContainsKey(kind.Id))
                    _showActivableKindGroup.Add(kind.Id, false);

                _showActivableKindGroup[kind.Id] = EditorGUILayout.BeginFoldoutHeaderGroup(_showActivableKindGroup[kind.Id], kind.DisplayName);
                if (_showActivableKindGroup[kind.Id])
                {
                    IMetaActivableSet activableSet = MetaActivableUtil.GetPlayerActivableSetForKind(kind.Id, player);

                    foreach (object activableId in MetaActivableUtil.GetActivableIdsOfKind(kind.Id, gameConfig))
                    {
                        IMetaActivableConfigData    activableInfo       = MetaActivableUtil.GetActivableGameConfigData(new MetaActivableKey(kind.Id, activableId), gameConfig);
                        MetaActivableState          activableStateMaybe = activableSet.TryGetState(activableInfo);

                        EditorGUILayout.LabelField($"{activableInfo.DisplayName} ({activableId})", EditorStyles.boldLabel);

                        MetaActivableState.DebugPhase? currentDebugPhase = activableStateMaybe?.Debug?.Phase;
                        if (!currentDebugPhase.HasValue)
                            EditorGUILayout.LabelField("No debug-forced phase");
                        else
                            EditorGUILayout.LabelField($"Debug-forced phase: {currentDebugPhase.Value}");

                        EditorGUILayout.LabelField("Debug-force a phase:");

                        float buttonWidth = 80;

                        GUILayout.BeginHorizontal();
                        foreach (MetaActivableState.DebugPhase debugPhase in EnumUtil.GetValues<MetaActivableState.DebugPhase>())
                        {
                            if (GUILayout.Button(debugPhase.ToString(), GUILayout.Width(buttonWidth)))
                                playerContext.ExecuteAction(new PlayerDebugForceSetActivablePhase(kind.Id, activableId.ToString(), debugPhase));
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("Random", GUILayout.Width(buttonWidth)))
                        {
                            IEnumerable<MetaActivableState.DebugPhase?> debugPhaseChoices   = EnumUtil.GetValues<MetaActivableState.DebugPhase>().Cast<MetaActivableState.DebugPhase?>().Append(null);
                            MetaActivableState.DebugPhase?              debugPhase          = RandomPCG.CreateNew().Choice(debugPhaseChoices);
                            playerContext.ExecuteAction(new PlayerDebugForceSetActivablePhase(kind.Id, activableId.ToString(), debugPhase));
                        }
                        if (GUILayout.Button("Unforce", GUILayout.Width(buttonWidth)))
                            playerContext.ExecuteAction(new PlayerDebugForceSetActivablePhase(kind.Id, activableId.ToString(), phase: null));
                        GUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }
        }

        #endregion

        #region MetaOffers

        bool _showOfferGroupsGroup = false;

        void MetaOffersGUI()
        {
            IPlayerClientContext playerContext = MetaplaySDK.SessionContext?.PlayerContext;
            if (playerContext == null)
                return;
            IPlayerModelBase player = playerContext.Journal.StagedModel;
            if (player == null)
                return;

            IMetaActivableSet<MetaOfferGroupId, MetaOfferGroupInfoBase, MetaOfferGroupModelBase> offerGroups = player.MetaOfferGroups;

            EditorGUILayout.Space(30);
            EditorGUILayout.LabelField("Offer Groups", EditorStyles.boldLabel);
            _showOfferGroupsGroup = EditorGUILayout.BeginFoldoutHeaderGroup(_showOfferGroupsGroup, $"{offerGroups.GetActiveStates(player).Count()} offer groups active");
            if (_showOfferGroupsGroup)
            {
                foreach (MetaOfferGroupInfoBase offerGroupInfo in player.GameConfig.MetaOfferGroups.Values)
                {
                    if (offerGroups.IsActive(offerGroupInfo.ActivableId, player))
                    {
                        MetaOfferGroupModelBase offerGroup = offerGroups.TryGetState(offerGroupInfo.ActivableId);

                        MetaDuration?   expiresIn       = offerGroup.LatestActivation.Value.EndAt - player.CurrentTime;
                        string          expirationStr    = expiresIn.HasValue ? $"in {expiresIn.Value}" : "never";
                        EditorGUILayout.LabelField($"Group '{offerGroupInfo.DisplayName}', with placement '{offerGroupInfo.Placement}', expires {expirationStr}", EditorStyles.boldLabel);

                        foreach (MetaOfferStatus offer in player.MetaOfferGroups.GetOffersInGroup(offerGroupInfo, player))
                        {
                            string statusString;
                            if (player.MetaOfferGroups.OfferIsPurchasable(offer))
                                statusString = "";
                            else if (offer.AnyPurchaseLimitReached)
                                statusString = "(SOLD OUT) ";
                            else if (!offer.IsActive)
                                statusString = "(CONDITIONS NOT FULFILLED) ";
                            else
                                statusString = "(UNAVAILABLE) ";

                            EditorGUILayout.LabelField($"{statusString}Offer '{offer.Info.DisplayName}'");
                            EditorGUI.indentLevel++;

                            EditorGUILayout.LabelField("Purchased:");
                            EditorGUI.indentLevel++;
                            EditorGUILayout.LabelField($"total by player: {offer.NumPurchasedByPlayer} / {offer.Info.MaxPurchasesPerPlayer?.ToString() ?? "infinite"}");
                            EditorGUILayout.LabelField($"total in this group: {offer.NumPurchasedInGroup} / {offer.Info.MaxPurchasesPerOfferGroup?.ToString() ?? "infinite"}");
                            EditorGUILayout.LabelField($"during this activation: {offer.NumPurchasedDuringActivation} / {offer.Info.MaxPurchasesPerActivation?.ToString() ?? "infinite"}");
                            EditorGUI.indentLevel--;

                            EditorGUILayout.LabelField("Rewards:");
                            EditorGUI.indentLevel++;
                            foreach (MetaPlayerRewardBase reward in offer.Info.Rewards)
                                EditorGUILayout.LabelField(PrettyPrint.Compact(reward).ToString());
                            EditorGUI.indentLevel--;

                            GUILayout.BeginHorizontal();
                            GUILayout.Space(EditorGUI.indentLevel * 10);
                            if (GUILayout.Button($"{statusString}Buy '{offer.Info.DisplayName}'"))
                                playerContext.ExecuteAction(new PlayerPreparePurchaseMetaOffer(offerGroupInfo, offer.Info, null));
                            GUILayout.EndHorizontal();

                            EditorGUI.indentLevel--;
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        #endregion
    }
}
