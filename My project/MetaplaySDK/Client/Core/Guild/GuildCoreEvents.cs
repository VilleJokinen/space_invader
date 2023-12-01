// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Analytics;
using Metaplay.Core.EventLog;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using static System.FormattableString;

namespace Metaplay.Core.Guild
{
    // Substitute event for when an event log entry payload fails to deserialize
    [AnalyticsEvent(AnalyticsEventCodesCore.GuildEventDeserializationFailureSubstitute, displayName: "<Failed to Deserialize Event!>", docString: AnalyticsEventDocsCore.GuildEventDeserializationFailureSubstitute, sendToAnalytics: false)]
    public class GuildEventDeserializationFailureSubstitute : GuildEventBase, IEntityEventPayloadDeserializationFailureSubstitute
    {
        [MetaMember(1)] public EntityEventDeserializationFailureInfo FailureInfo { get; private set; }

        public override string EventDescription => FailureInfo?.DescriptionForEvent ?? "Failure info not initialized.";

        public void Initialize(MetaMemberDeserializationFailureParams failureParams)
        {
            FailureInfo = new EntityEventDeserializationFailureInfo(failureParams);
        }
    }

    /// <summary>
    /// Info about a guild member to include in a guild event.
    /// </summary>
    [MetaSerializable]
    public struct GuildEventMemberInfo
    {
        [MetaMember(1)] public EntityId PlayerId            { get; private set; }
        [MetaMember(2)] public int      MemberInstanceId    { get; private set; }
        [MetaMember(3)] public string   DisplayName         { get; private set; }

        public GuildEventMemberInfo(EntityId playerId, int memberInstanceId, string displayName)
        {
            // \note Nulls are tolerated for defensiveness
            PlayerId = playerId;
            MemberInstanceId = memberInstanceId;
            DisplayName = displayName;
        }

        public static GuildEventMemberInfo Create(EntityId playerId, int memberInstanceId, GuildMemberPlayerDataBase playerData)
        {
            // \note Tolerate null playerData for defensiveness
            return new GuildEventMemberInfo(playerId, memberInstanceId, playerData?.DisplayName);
        }

        public static GuildEventMemberInfo Create(EntityId playerId, GuildMemberBase member)
        {
            // \note Tolerate null member for defensiveness
            return new GuildEventMemberInfo(playerId, member?.MemberInstanceId ?? 0, member?.DisplayName);
        }

        public override string ToString() => $"'{DisplayName}' ({PlayerId})";
    }

    /// <summary>
    /// Info about who invoked a guild event: a specific guild member, or an admin.
    /// </summary>
    [MetaSerializable]
    public struct GuildEventInvokerInfo
    {
        [MetaSerializable]
        public enum InvokerType
        {
            Member  = 0,
            Admin   = 1,
        }

        [MetaMember(1)] public InvokerType          Type    { get; private set; }
        [MetaMember(2)] public GuildEventMemberInfo Member  { get; private set; } // \note Only relevant when Type is Member
        // \note When Type is Admin, audit logs should contain more detailed information.

        GuildEventInvokerInfo(InvokerType type, GuildEventMemberInfo member)
        {
            Type = type;
            Member = member;
        }

        public static GuildEventInvokerInfo ForMember(GuildEventMemberInfo member) => new GuildEventInvokerInfo(InvokerType.Member, member: member);
        public static GuildEventInvokerInfo ForAdmin() => new GuildEventInvokerInfo(InvokerType.Admin, member: default);

        public override string ToString()
        {
            switch (Type)
            {
                case InvokerType.Member:    return $"Member {Member}";
                case InvokerType.Admin:     return "Admin";
                default: return $"<{Type}>";
            }
        }
    }

    /// <summary>
    /// Guild was created. At this point there are 0 members in the guild.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodesCore.GuildCreated, docString: AnalyticsEventDocsCore.GuildCreated)]
    public class GuildEventCreated : GuildEventBase
    {
        [MetaMember(1)] public string DisplayName   { get; private set; }
        [MetaMember(2)] public string Description   { get; private set; }

        public override string EventDescription => $"The guild '{DisplayName}' was created.";

        GuildEventCreated(){ }
        public GuildEventCreated(string displayName, string description)
        {
            DisplayName = displayName;
            Description = description;
        }
    }

    /// <summary>
    /// The initial member was added to the guild.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodesCore.GuildFounderJoined, docString: AnalyticsEventDocsCore.GuildFounderJoined)]
    public class GuildEventFounderJoined : GuildEventBase
    {
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(1)] public GuildEventMemberInfo FoundingMember { get; private set; }

        public override string EventDescription => $"Founding member {FoundingMember} joined the guild.";

        GuildEventFounderJoined(){ }
        public GuildEventFounderJoined(GuildEventMemberInfo foundingMember)
        {
            FoundingMember = foundingMember;
        }
    }

    /// <summary>
    /// A member (other than the initial member) joined the guild.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodesCore.GuildMemberJoined, docString: AnalyticsEventDocsCore.GuildMemberJoined)]
    public class GuildEventMemberJoined : GuildEventBase
    {
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(1)] public GuildEventMemberInfo JoiningMember { get; private set; }

        public override string EventDescription => $"Member {JoiningMember} joined the guild.";

        GuildEventMemberJoined(){ }
        public GuildEventMemberJoined(GuildEventMemberInfo joiningMember)
        {
            JoiningMember = joiningMember;
        }
    }

    /// <summary>
    /// A member left the guild (by their own request).
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodesCore.GuildMemberLeft, docString: AnalyticsEventDocsCore.GuildMemberLeft)]
    public class GuildEventMemberLeft : GuildEventBase
    {
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(1)] public GuildEventMemberInfo LeavingMember { get; private set; }

        public override string EventDescription => $"{LeavingMember} left the guild.";

        GuildEventMemberLeft(){ }
        public GuildEventMemberLeft(GuildEventMemberInfo leavingMember)
        {
            LeavingMember = leavingMember;
        }
    }

    /// <summary>
    /// A member was kicked by a fellow member or an admin.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodesCore.GuildMemberKicked, docString: AnalyticsEventDocsCore.GuildMemberKicked)]
    public class GuildEventMemberKicked : GuildEventBase
    {
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(1)] public GuildEventMemberInfo     KickedMember    { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(2)] public GuildEventInvokerInfo    KickInvoker     { get; private set; }

        public override string EventDescription => $"{KickedMember} was kicked by {KickInvoker}.";

        GuildEventMemberKicked(){ }
        public GuildEventMemberKicked(GuildEventMemberInfo kickedMember, GuildEventInvokerInfo kickInvoker)
        {
            KickedMember = kickedMember;
            KickInvoker = kickInvoker;
        }
    }

    /// <summary>
    /// A member was removed from the guild to repair an inconsistency between
    /// the player entity and the guild entity, where the player and guild
    /// disagree about the player's membership in the guild.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodesCore.GuildMemberRemovedDueToInconsistency, docString: AnalyticsEventDocsCore.GuildMemberRemovedDueToInconsistency)]
    public class GuildEventMemberRemovedDueToInconsistency : GuildEventBase
    {
        [MetaSerializable]
        public enum InconsistencyType
        {
            SubscribeAttemptMemberInstanceIdDiffers = 0,
            JoinAttemptAlreadyMember = 1,
            PeekKickedStateAttemptMemberInstanceIdDiffers = 2,
        }

        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(1)] public GuildEventMemberInfo RemovedMember   { get; private set; }
        [MetaMember(2)] public InconsistencyType    Type            { get; private set; }

        public override string EventDescription => $"{RemovedMember} was removed due to inconsistency: {Type}.";

        GuildEventMemberRemovedDueToInconsistency(){ }
        public GuildEventMemberRemovedDueToInconsistency(GuildEventMemberInfo removedMember, InconsistencyType type)
        {
            RemovedMember = removedMember;
            Type = type;
        }
    }

    /// <summary>
    /// The guild's name and/or description were changed by a member or an admin.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodesCore.GuildNameAndDescriptionChanged, docString: AnalyticsEventDocsCore.GuildNameAndDescriptionChanged)]
    public class GuildEventNameAndDescriptionChanged : GuildEventBase
    {
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(1)] public GuildEventInvokerInfo    Invoker         { get; private set; }
        [MetaMember(2)] public string                   OldName         { get; private set; }
        [MetaMember(3)] public string                   OldDescription  { get; private set; }
        [MetaMember(4)] public string                   NewName         { get; private set; }
        [MetaMember(5)] public string                   NewDescription  { get; private set; }

        public override string EventDescription => $"Name changed from '{OldName}' to '{NewName}' by {Invoker}.";

        GuildEventNameAndDescriptionChanged(){ }
        public GuildEventNameAndDescriptionChanged(GuildEventInvokerInfo invoker, string oldName, string oldDescription, string newName, string newDescription)
        {
            Invoker = invoker;
            OldName = oldName;
            OldDescription = oldDescription;
            NewName = newName;
            NewDescription = newDescription;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.GuildModelSchemaMigrated, displayName: "Model Schema Migrated", docString: AnalyticsEventDocsCore.GuildModelSchemaMigrated)]
    public class GuildEventModelSchemaMigrated : GuildEventBase
    {
        [MetaMember(1)] public int  FromVersion { get; private set; }
        [MetaMember(2)] public int  ToVersion   { get; private set; }

        public override string EventDescription => Invariant($"Model schema was migrated from v{FromVersion} to v{ToVersion}");

        GuildEventModelSchemaMigrated() { }
        public GuildEventModelSchemaMigrated(int fromVersion, int toVersion)
        {
            FromVersion = fromVersion;
            ToVersion = toVersion;
        }
    }


}

#endif
