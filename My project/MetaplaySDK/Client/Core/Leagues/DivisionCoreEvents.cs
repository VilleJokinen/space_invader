// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.EventLog;
using Metaplay.Core.League.Player;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;

namespace Metaplay.Core.League
{
    /// <summary>
    /// Info about a division participant to include in a division event.
    /// </summary>
    [MetaSerializable]
    [LeaguesEnabledCondition]
    public struct DivisionEventParticipantInfo
    {
        [MetaMember(1)] public EntityId ParticipantId  { get; private set; }
        [MetaMember(2)] public string   DisplayName    { get; private set; }
        [MetaMember(3)] public int      ParticipantIdx { get; private set; }

        public DivisionEventParticipantInfo(int participantIdx, EntityId participantId, string displayName)
        {
            // \note Nulls are tolerated for defensiveness
            ParticipantIdx = participantIdx;
            ParticipantId = participantId;
            DisplayName   = displayName;
        }

        public override string ToString() => FormattableString.Invariant($"{ParticipantIdx}:'{DisplayName}' ({ParticipantId})");
    }

    /// <summary>
    /// Division was created. At this point there are 0 participants in the division.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodesCore.DivisionCreated, docString: AnalyticsEventDocsCore.DivisionCreated)]
    [LeaguesEnabledCondition]
    public class DivisionEventCreated : DivisionEventBase
    {
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(1)] public DivisionIndex DivisionIndex    { get; private set; }

        public override string EventDescription => $"The divison '{DivisionIndex}' was created.";

        DivisionEventCreated(){ }
        public DivisionEventCreated(DivisionIndex divisionIndex)
        {
            DivisionIndex = divisionIndex;
        }
    }

    /// <summary>
    /// A participant was added to the division.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodesCore.DivisionParticipantJoined, docString: AnalyticsEventDocsCore.DivisionParticipantJoined)]
    [LeaguesEnabledCondition]
    public class DivisionEventParticipantJoined : DivisionEventBase
    {
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(1)] public DivisionEventParticipantInfo ParticipantInfo { get; private set; }
        [MetaMember(2)] public bool SentByLeagueManager { get; private set; }

        public override string EventDescription => $"Participant {ParticipantInfo} joined the division.";

        DivisionEventParticipantJoined(){ }
        public DivisionEventParticipantJoined(DivisionEventParticipantInfo participant, bool sentByLeagueManager)
        {
            ParticipantInfo     = participant;
            SentByLeagueManager = sentByLeagueManager;
        }
    }
}

