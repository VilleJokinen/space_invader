// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Forms;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.DatabaseScan;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using static System.FormattableString;

namespace Metaplay.Server.NotificationCampaign
{
    /// <summary>
    /// Parameters of a notification campaign.
    /// </summary>
    [MetaSerializable]
    public class NotificationCampaignParams : IPlayerFilter
    {
        [MetaMember(1)]   public string                                                     Name                        { get; private set; }
        [MetaMember(2)]   public MetaTime                                                   TargetTime                  { get; private set; }
        #pragma warning disable CS0618
        [Obsolete($"Use {nameof(Content)} instead")]
        [MetaMember(3)]   public OrderedDictionary<LanguageId, NotificationLocalizationOld> LocalizationsOld            { get; private set; }
        #pragma warning restore CS0618
        [MetaMember(4)]   public List<EntityId>                                             TargetPlayers               { get; private set; }   // List of playerIds for targeted notifications
        [MetaMember(6)]   public PlayerCondition                                            TargetCondition             { get; private set; }   // Player targeting condition
        [MetaMember(7)]   public NotificationContent                                        Content                     { get; private set; }
        [MetaMember(20)]  public string                                                     FirebaseAnalyticsLabel      { get; private set; } // Can be null

        /// <summary>
        /// Used to restrict the database scan to a smaller range than full EntityId value range.
        /// Useful in a bot-heavy environment because their id distribution is very different from real players.
        ///
        /// If 0, full EntityId value range is used.
        /// </summary>
        [MetaMember(100)] public ulong                                                      DebugEntityIdValueUpperBound    { get; private set; } = 0;
        [MetaMember(101)] public bool                                                       DebugFakeNotificationMode       { get; private set; } = false;

        [MetaMember(5)] public List<PlayerSegmentId> LegacyTargetSegments { get; private set; }   // List of player segment ids for targeted notifications


        public bool Validate(out string error)
        {
            int targetAudienceLimit = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>().MaxTargetPlayersListSize;

            if (string.IsNullOrEmpty(Name))
                error = "Name is missing";
            else if (Content == null)
                error = "Content is missing";
            else if (!Content.ContainsLocalizationForLanguage(MetaplayCore.Options.DefaultLanguage))
                error = $"No localization specified for default language {MetaplayCore.Options.DefaultLanguage}";
            else if (!string.IsNullOrEmpty(FirebaseAnalyticsLabel) && !IsValidFirebaseAnalyticsLabel(FirebaseAnalyticsLabel))
                error = "Invalid Firebase analytics label";
            else if (TargetPlayers != null && TargetPlayers.Count > 0 && TargetPlayers.Count > targetAudienceLimit)
                error = Invariant($"Target players list size of {TargetPlayers.Count} exceeds maximum allowed size of {targetAudienceLimit} defined in {nameof(SystemOptions)}.{nameof(SystemOptions.MaxTargetPlayersListSize)}");
            else
            {
                error = default;
                return true;
            }

            return false;
        }

        // \todo [nuutti] Move to some utility
        private bool IsValidFirebaseAnalyticsLabel(string label)
        {
            if (label == null)
                return false;

            return FirebaseAnalyticsLabelRegex.IsMatch(label);
        }

        [JsonIgnore]
        public PlayerFilterCriteria PlayerFilter => new PlayerFilterCriteria(TargetPlayers, TargetCondition);

        private static Regex FirebaseAnalyticsLabelRegex = new Regex(@"^[a-zA-Z0-9-_.~%]{1,50}$");

        public NotificationCampaignParams(){ }
        public NotificationCampaignParams(string name, MetaTime targetTime, NotificationContent content, string firebaseAnalyticsLabel, ulong debugEntityIdValueUpperBound, bool debugFakeNotificationMode)
        {
            Name                            = name;
            TargetTime                      = targetTime;
            Content                         = content;
            FirebaseAnalyticsLabel          = firebaseAnalyticsLabel;
            DebugEntityIdValueUpperBound    = debugEntityIdValueUpperBound;
            DebugFakeNotificationMode       = debugFakeNotificationMode;
        }

        public void MigrateTargetSegments()
        {
            if (LegacyTargetSegments != null)
            {
                TargetCondition = new PlayerSegmentBasicCondition(propertyRequirements: null, requireAnySegment: LegacyTargetSegments, requireAllSegments: null);
                LegacyTargetSegments = null;
            }
        }

        public void MigrateContentLocalization()
        {
            #pragma warning disable CS0618
            if (LocalizationsOld != null)
            {
                List<(LanguageId, string)> titleLocalizations = new List<(LanguageId, string)>();
                List<(LanguageId, string)> bodyLocalizations  = new List<(LanguageId, string)>();

                foreach ((LanguageId languageId, NotificationLocalizationOld value) in LocalizationsOld)
                {
                    // Ensure that the default language is first in the LocalizedStrings
                    if (languageId == MetaplayCore.Options.DefaultLanguage)
                    {
                        titleLocalizations.Insert(0, (languageId, value.Title));
                        bodyLocalizations.Insert(0, (languageId, value.Body));
                    }
                    else
                    {
                        titleLocalizations.Add((languageId, value.Title));
                        bodyLocalizations.Add((languageId, value.Body));
                    }
                }

                LocalizedString localizedTitle = new LocalizedString(titleLocalizations);
                LocalizedString localizedBody  = new LocalizedString(bodyLocalizations);
                Content = new NotificationContent(localizedTitle, localizedBody);
                LocalizationsOld = null;
            }
            #pragma warning restore CS0618
        }
    }

    [MetaSerializable]
    public class NotificationContent
    {
        [MetaMember(1)] [MetaValidateRequired] public LocalizedString Title { get; private set; }
        [MetaMember(2)] [MetaValidateRequired] public LocalizedString Body  { get; private set; }

        public NotificationContent() { }

        public NotificationContent(LocalizedString title, LocalizedString body)
        {
            Title = title;
            Body = body;
        }

        public bool ContainsLocalizationForLanguage(LanguageId languageId)
        {
            if (Title.Localizations == null || !Title.Localizations.ContainsKey(languageId))
                return false;
            if (Body.Localizations == null || !Body.Localizations.ContainsKey(languageId))
                return false;
            return true;
        }
    }

    [Obsolete($"Use {nameof(NotificationContent)} instead")]
    [MetaSerializable]
    public class NotificationLocalizationOld
    {
        [MetaMember(1)] public string   Title   { get; private set; }
        [MetaMember(2)] public string   Body    { get; private set; }

        public NotificationLocalizationOld() { }
        public NotificationLocalizationOld(string title, string body) { Title = title; Body = body; }
    }

    /// <summary>
    /// Summary of a notification campaign. Sent to the Dashboard to display a list of notification campaigns
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(2, 3, 4)]
    public class NotificationCampaignSummary
    {
        [MetaMember(1)] public int                                                      Id                      { get; private set; }   // Unique identifier for notification campaign
        [MetaMember(7)] public NotificationCampaignParams                               Params                  { get; private set; }   // Parameters specified for campaign when it was created
        [MetaMember(5)] public NotificationCampaignPhase                                Phase                   { get; private set; }   // Phase of notification campaign
        [MetaMember(6)] public float                                                    ScannedRatioEstimate    { get; private set; }   // Estimate of what proportion of the database we've scanned so far

        public NotificationCampaignSummary() { }
        public NotificationCampaignSummary(int id, NotificationCampaignParams campaignParams, NotificationCampaignPhase phase, float scannedRatioEstimate)
        {
            Id = id;
            Params = campaignParams;
            Phase = phase;
            ScannedRatioEstimate = scannedRatioEstimate;
        }
    }

    [MetaSerializable]
    public enum NotificationCampaignPhase
    {
        Scheduled,
        Running,
        Sent,
        Cancelled,
        Cancelling,
        DidNotRun,
    }

    /// <summary>
    /// Statistics of a notification campaign
    /// </summary>
    [MetaSerializable]
    public class NotificationCampaignStatisticsInfo
    {
        [MetaMember(1)] public MetaTime                                 StartTime           { get; private set; }
        [MetaMember(2)] public MetaTime?                                StopTime            { get; private set; }
        [MetaMember(3)] public DatabaseScanStatistics                   ScanStats           { get; private set; }
        [MetaMember(4)] public NotificationCampaignProcessingStatistics NotificationStats   { get; private set; }

        public NotificationCampaignStatisticsInfo() { }
        public NotificationCampaignStatisticsInfo(MetaTime startTime, MetaTime? stopTime, DatabaseScanStatistics scanStats, NotificationCampaignProcessingStatistics notificationStats)
        {
            StartTime           = startTime;
            StopTime            = stopTime;
            ScanStats           = scanStats;
            NotificationStats   = notificationStats;
        }
    }

    /// <summary>
    /// Notification campaign's params, as well as its status and some other info.
    /// </summary>
    [MetaSerializable]
    public class NotificationCampaignInfo
    {
        [MetaMember(1)] public int                                  Id              { get; private set; }
        [MetaMember(2)] public NotificationCampaignParams           CampaignParams  { get; private set; }
        [MetaMember(3)] public NotificationCampaignPhase            CampaignPhase   { get; private set; }
        [MetaMember(4)] public NotificationCampaignStatisticsInfo   Stats           { get; private set; }

        public NotificationCampaignInfo() { }
        public NotificationCampaignInfo(int id, NotificationCampaignParams campaignParams, NotificationCampaignPhase campaignPhase, NotificationCampaignStatisticsInfo stats)
        {
            Id = id;
            CampaignParams = campaignParams;
            CampaignPhase = campaignPhase;
            Stats = stats;
        }
    }

    /// <summary>
    /// Request/response for retrieving all notification campaigns
    /// </summary>
    [MetaMessage(MessageCodesCore.ListNotificationCampaignsRequest, MessageDirection.ServerInternal)]
    public class ListNotificationCampaignsRequest : MetaMessage
    {
        public ListNotificationCampaignsRequest() { }
    }
    [MetaMessage(MessageCodesCore.ListNotificationCampaignsResponse, MessageDirection.ServerInternal)]
    public class ListNotificationCampaignsResponse : MetaMessage
    {
        public List<NotificationCampaignSummary> NotificationCampaigns { get; private set; }

        public ListNotificationCampaignsResponse() { }
        public ListNotificationCampaignsResponse(List<NotificationCampaignSummary> notificationCampaigns)
        {
            NotificationCampaigns = notificationCampaigns;
        }
    }

    /// <summary>
    /// Request/response for adding a notification campaign
    /// </summary>
    [MetaMessage(MessageCodesCore.AddNotificationCampaignRequest, MessageDirection.ServerInternal)]
    public class AddNotificationCampaignRequest : MetaMessage
    {
        public NotificationCampaignParams CampaignParams { get; private set; }

        public AddNotificationCampaignRequest() { }
        public AddNotificationCampaignRequest(NotificationCampaignParams campaignParams) { CampaignParams = campaignParams; }
    }
    [MetaMessage(MessageCodesCore.AddNotificationCampaignResponse, MessageDirection.ServerInternal)]
    public class AddNotificationCampaignResponse : MetaMessage
    {
        public bool     Success { get; private set; }
        public int      Id      { get; private set; } = -1;
        public string   Error   { get; private set; }

        public AddNotificationCampaignResponse() { }
        public static AddNotificationCampaignResponse Ok        (int id)       => new AddNotificationCampaignResponse{ Success = true, Id = id };
        public static AddNotificationCampaignResponse Failure   (string error) => new AddNotificationCampaignResponse{ Success = false, Error = error };
    }

    /// <summary>
    /// Request/response for retrieving a notification campaign
    /// </summary>
    [MetaMessage(MessageCodesCore.GetNotificationCampaignRequest, MessageDirection.ServerInternal)]
    public class GetNotificationCampaignRequest : MetaMessage
    {
        public int Id { get; private set; }

        public GetNotificationCampaignRequest() { }
        public GetNotificationCampaignRequest(int id) { Id = id; }
    }
    [MetaMessage(MessageCodesCore.GetNotificationCampaignResponse, MessageDirection.ServerInternal)]
    public class GetNotificationCampaignResponse : MetaMessage
    {
        public bool                     Success         { get; private set; }
        public NotificationCampaignInfo CampaignInfo    { get; private set; }
        public string                   Error           { get; private set; }

        public GetNotificationCampaignResponse() { }
        public static GetNotificationCampaignResponse Ok        (NotificationCampaignInfo campaignInfo) => new GetNotificationCampaignResponse{ Success = true, CampaignInfo = campaignInfo };
        public static GetNotificationCampaignResponse Failure   (string error)                          => new GetNotificationCampaignResponse{ Success = false, Error = error };
    }

    /// <summary>
    /// Request/response for updating a notification campaign
    /// </summary>
    [MetaMessage(MessageCodesCore.UpdateNotificationCampaignRequest, MessageDirection.ServerInternal)]
    public class UpdateNotificationCampaignRequest : MetaMessage
    {
        public int                          Id              { get; private set; }
        public NotificationCampaignParams   CampaignParams  { get; private set; }

        public UpdateNotificationCampaignRequest() { }
        public UpdateNotificationCampaignRequest(int id, NotificationCampaignParams campaignParams)
        {
            Id = id;
            CampaignParams = campaignParams;
        }
    }
    [MetaMessage(MessageCodesCore.UpdateNotificationCampaignResponse, MessageDirection.ServerInternal)]
    public class UpdateNotificationCampaignResponse : MetaMessage
    {
        public bool     Success { get; private set; }
        public string   Error   { get; private set; }

        public UpdateNotificationCampaignResponse() {  }
        public static UpdateNotificationCampaignResponse Ok        ()               => new UpdateNotificationCampaignResponse{ Success = true };
        public static UpdateNotificationCampaignResponse Failure   (string error)   => new UpdateNotificationCampaignResponse{ Success = false, Error = error };
    }

    /// <summary>
    /// Request/response to start cancelling a notification campaign that is already running
    /// </summary>
    /// <remarks>
    /// A successful response does not mean that the campaign has now been cancelled.
    /// It only means that cancellation has been started.
    /// </remarks>
    [MetaMessage(MessageCodesCore.BeginCancelNotificationCampaignRequest, MessageDirection.ServerInternal)]
    public class BeginCancelNotificationCampaignRequest : MetaMessage
    {
        public int Id { get; private set; }

        public BeginCancelNotificationCampaignRequest() { }
        public BeginCancelNotificationCampaignRequest(int id)
        {
            Id = id;
        }
    }
    [MetaMessage(MessageCodesCore.BeginCancelNotificationCampaignResponse, MessageDirection.ServerInternal)]
    public class BeginCancelNotificationCampaignResponse : MetaMessage
    {
        public bool Success { get; private set; }
        public string Error { get; private set; }

        public BeginCancelNotificationCampaignResponse() {  }
        public static BeginCancelNotificationCampaignResponse Ok        ()              => new BeginCancelNotificationCampaignResponse{ Success = true };
        public static BeginCancelNotificationCampaignResponse Failure   (string error)  => new BeginCancelNotificationCampaignResponse{ Success = false, Error = error };
    }

    /// <summary>
    /// Request/response for deleting a notification campaign
    /// </summary>
    [MetaMessage(MessageCodesCore.DeleteNotificationCampaignRequest, MessageDirection.ServerInternal)]
    public class DeleteNotificationCampaignRequest : MetaMessage
    {
        public int Id { get; private set; }

        public DeleteNotificationCampaignRequest() { }
        public DeleteNotificationCampaignRequest(int id) { Id = id; }
    }
    [MetaMessage(MessageCodesCore.DeleteNotificationCampaignResponse, MessageDirection.ServerInternal)]
    public class DeleteNotificationCampaignResponse : MetaMessage
    {
        public bool Success { get; private set; }
        public string Error { get; private set; }

        public DeleteNotificationCampaignResponse() {  }
        public static DeleteNotificationCampaignResponse Ok         ()              => new DeleteNotificationCampaignResponse{ Success = true };
        public static DeleteNotificationCampaignResponse Failure    (string error)  => new DeleteNotificationCampaignResponse{ Success = false, Error = error };
    }
}
