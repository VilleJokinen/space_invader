// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Forms;
using Metaplay.Core.InGameMail;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static System.FormattableString;

namespace Metaplay.Server
{
    [MetaSerializable]
    public abstract class BroadcastMessageContents
    {
        public abstract bool IsValid { get; }

        public abstract MetaInGameMail ConvertToPlayerMail(IPlayerModelBase player);
        public virtual bool IsContentReady()
        {
            return true;
        }
    }

    /// <summary>
    /// Information about a broadcast.
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(7, 8)] // NumGems, NumGold
    public class BroadcastMessageParams : IPlayerFilter
    {
        [MetaMember(1)]  public int                      Id                { get; set; }           // Unique identifier for broadcast
        [MetaMember(2)]  public string                   Name              { get; private set; }   // Developer-facing name (only used in dashboard)
        [MetaMember(3)]  public MetaTime                 StartAt           { get; private set; }   // Time when broadcasting should start
        [MetaMember(4)]  public MetaTime                 EndAt             { get; private set; }   // Time when broadcasting should end
        [MetaMember(5)]  public List<EntityId>           TargetPlayers     { get; private set; }   // List of playerIds for targeted broadcasts
        [MetaMember(12)] public PlayerCondition          TargetCondition   { get; private set; }   // Player targeting condition
        [MetaMember(11)] public BroadcastMessageContents Contents          { get; private set; }   // Contents of the broadcast used as template for instantiating in-game mail
        [MetaMember(13)] public PlayerTriggerCondition   TriggerCondition  { get; private set; }   // The triggering condition for a manually triggered broadcast

        [MetaMember(6), JsonIgnore] public OrderedDictionary<LanguageId, BroadcastLocalization?> LegacyLocalizations { get; private set; }   // Localizations of message to target languages
        [MetaMember(9), JsonIgnore] public List<MetaPlayerRewardBase> LegacyAttachments { get; private set; }  // Items/resources/whatnot attached to the broadcast message
        [MetaMember(10), JsonIgnore] public List<PlayerSegmentId> LegacyTargetSegments  { get; private set; }   // List of player segment ids for targeted broadcasts

        /// <summary>
        /// Checks that the contents are valid.
        /// </summary>
        public bool IsContentsValid => Contents != null && Contents.IsValid;

        /// <summary>
        /// Used to validate the input from API endpoints
        /// </summary>
        public bool Validate(out string error)
        {
            int targetAudienceLimit = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>().MaxTargetPlayersListSize;

            if (TargetPlayers != null && TargetPlayers.Count > 0 && TargetPlayers.Count > targetAudienceLimit)
                error = Invariant($"TargetPlayers list size of {TargetPlayers.Count} exceeds maximum allowed size of {targetAudienceLimit} defined in {nameof(SystemOptions)}.{nameof(SystemOptions.MaxTargetPlayersListSize)}");
            else if (TargetPlayers != null)
            {
                error = default;
                foreach (EntityId id in TargetPlayers)
                {
                    if (!id.IsOfKind(EntityKindCore.Player))
                    {
                        error = Invariant($"Entity ID {id} is not a player");
                        return false;
                    }
                }

                return true;
            }
            else
            {
                error = default;
                return true;
            }

            return false;
        }

        public BroadcastMessageParams() { }
        public BroadcastMessageParams(int id, string name, MetaTime startAt, MetaTime endAt, List<EntityId> targetPlayerIds)
        {
            Id              = id;
            Name            = name;
            StartAt         = startAt;
            EndAt           = endAt;
            TargetPlayers   = targetPlayerIds;
        }

        public bool IsActiveAt(MetaTime time) => (time >= StartAt) && (time < EndAt) && Contents.IsContentReady();

        public bool IsTargeted => (TargetPlayers != null && TargetPlayers.Count > 0) || TargetCondition != null;

        [JsonIgnore]
        public PlayerFilterCriteria PlayerFilter => new PlayerFilterCriteria(TargetPlayers, TargetCondition);

        public MetaInGameMail ConvertToPlayerMail(IPlayerModelBase player)
        {
            return Contents.ConvertToPlayerMail(player);
        }

        public void MigrateContents()
        {
            Contents = new GenericBroadcastMessageContents(
                new OrderedDictionary<LanguageId, BroadcastLocalization>(LegacyLocalizations.Select(x => KeyValuePair.Create(x.Key, x.Value.Value))),
                LegacyAttachments);
            LegacyAttachments = null;
            LegacyLocalizations = null;
        }
        public void MigrateTargetSegments()
        {
            if (LegacyTargetSegments != null)
            {
                TargetCondition = new PlayerSegmentBasicCondition(null, LegacyTargetSegments, null);
                LegacyTargetSegments = null;
            }
        }
    }

    /// <summary>
    /// Localization details for an individual broadcast.
    /// </summary>
    [MetaSerializable]
    public struct BroadcastLocalization
    {
        [MetaMember(1)] public string Title { get; private set; }
        [MetaMember(2)] public string Body { get; private set; }

        public BroadcastLocalization(string title, string body) { Title = title; Body = body; }
    }

    /// <summary>
    /// Broadcast message implementation compatible with MetaInGameGenericPlayerMail
    /// </summary>
    [MetaSerializableDerived(1)]
    [MetaReservedMembers(0, 100)]
    [MetaFormDeprecated]
    public class GenericBroadcastMessageContents : BroadcastMessageContents
    {
        [MetaMember(1)] public OrderedDictionary<LanguageId, BroadcastLocalization> Localizations { get; private set; }   // Localizations of message to target languages
        [MetaMember(2)] public List<MetaPlayerRewardBase> Attachments { get; private set; }   // Items/resources/whatnot attached to the broadcast message

        public override bool IsValid => Localizations != null;

        public GenericBroadcastMessageContents() { }

        public GenericBroadcastMessageContents(OrderedDictionary<LanguageId, BroadcastLocalization> localizations, List<MetaPlayerRewardBase> attachments)
        {

            Localizations = localizations;
            Attachments = attachments;
        }

        public override MetaInGameMail ConvertToPlayerMail(IPlayerModelBase player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            LanguageId language = player.Language;

            // Resolve translation
            if (!Localizations.TryGetValue(language, out BroadcastLocalization translation))
            {
                (language, translation) = Localizations.First();
            }

            return new SimplePlayerMail(language, translation.Title, translation.Body, Attachments, MetaGuid.New());
        }
    }

    /// <summary>
    /// Simple broadcast message contents implementation that forwards MetaInGameMail contents.
    /// </summary>
    [MetaSerializableDerived(100)]
    [MetaReservedMembers(0, 100)]
    public class MailBroadcastMessageContents : BroadcastMessageContents
    {
        [MetaMember(1)] public MetaInGameMail Contents { get; private set; }

        public override bool IsValid => Contents != null;

        public MailBroadcastMessageContents() { }

        public MailBroadcastMessageContents(MetaInGameMail contents)
        {
            Contents = contents;
        }

        public override MetaInGameMail ConvertToPlayerMail(IPlayerModelBase player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            MetaInGameMail mailCopy = MetaSerialization.CloneTagged(Contents, MetaSerializationFlags.IncludeAll, null, null);

            if (!mailCopy.LocalizeInClient)
            {
                foreach (MemberInfo localizableMember in mailCopy.GetType().EnumerateInstanceDataMembersInUnspecifiedOrder()
                    .Where(t => t.GetDataMemberType().ImplementsInterface<ILocalized>()))
                {
                    ILocalized localizable = (ILocalized)localizableMember.GetDataMemberGetValueOnDeclaringType().Invoke(mailCopy);
                    if (localizable != null)
                        localizable.Collapse(player.Language);
                }
            }
            return mailCopy;
        }
    }

    /// <summary>
    /// Collection of stats about a broadcast.
    /// </summary>
    [MetaSerializable]
    public class BroadcastMessageStats
    {
        [MetaMember(1)] public int                      ReceivedCount    { get; set; } = 0;     // Number of players who have received this broadcast

        public BroadcastMessageStats() { }
        public BroadcastMessageStats(int receivedCount)
        {
            ReceivedCount = receivedCount;
        }
    }

    /// <summary>
    /// A broadcast message.
    /// </summary>
    [MetaSerializable]
    public class BroadcastMessage
    {
        [MetaMember(1)] public BroadcastMessageParams   Params          { get; set; }           // How this broadcast looks to the player
        [MetaMember(2)] public BroadcastMessageStats    Stats           { get; set; }           // Stats about how the broadcast has been processed

        public BroadcastMessage() { }
        public BroadcastMessage(BroadcastMessageParams _params, BroadcastMessageStats stats)
        {
            Params = _params;
            Stats = stats;
        }
    }


    [MetaMessage(MessageCodesCore.AddBroadcastMessage, MessageDirection.ServerInternal)]
    public class AddBroadcastMessage : MetaMessage
    {
        public BroadcastMessageParams BroadcastParams { get; private set; }

        public AddBroadcastMessage() { }
        public AddBroadcastMessage(BroadcastMessageParams broadcastParams) { BroadcastParams = broadcastParams; }
    }

    [MetaMessage(MessageCodesCore.AddBroadcastMessageResponse, MessageDirection.ServerInternal)]
    public class AddBroadcastMessageResponse : MetaMessage
    {
        public bool   Success     { get; private set; }
        public int    BroadcastId { get; private set; }
        public string Error       { get; private set; }

        public AddBroadcastMessageResponse() { }
        public AddBroadcastMessageResponse(int id) { BroadcastId = id; }

        public static AddBroadcastMessageResponse Ok        (int id)       => new AddBroadcastMessageResponse{ Success = true, BroadcastId = id };
        public static AddBroadcastMessageResponse Failure   (string error) => new AddBroadcastMessageResponse{ Success = false, Error      = error };
    }

    [MetaMessage(MessageCodesCore.UpdateBroadcastMessage, MessageDirection.ServerInternal)]
    public class UpdateBroadcastMessage : MetaMessage
    {
        public BroadcastMessageParams BroadcastParams { get; private set; }

        public UpdateBroadcastMessage() { }
        public UpdateBroadcastMessage(BroadcastMessageParams broadcastParams) { BroadcastParams = broadcastParams; }
    }

    [MetaMessage(MessageCodesCore.DeleteBroadcastMessage, MessageDirection.ServerInternal)]
    public class DeleteBroadcastMessage : MetaMessage
    {
        public int BroadcastId { get; private set; }

        public DeleteBroadcastMessage() { }
        public DeleteBroadcastMessage(int broadcastId) { BroadcastId = broadcastId; }
    }

    /// <summary>
    /// Set of currently active broadcasts. Created by <see cref="GlobalStateProxyActor"/> for <see cref="PlayerActor"/>s
    /// to consume. The contents are read from multiple actors/threads simultaneously, so the contents are immutable in order
    /// to maintain thread safety. When the active set changes, a new instance of the class is created.
    /// </summary>
    public class ActiveBroadcastSet : IAtomicValue<ActiveBroadcastSet>
    {
        public readonly IReadOnlyList<BroadcastMessage> ActiveBroadcasts;
        public readonly IReadOnlyList<int>              BroadcastIds;

        public ActiveBroadcastSet()
        {
        }

        public ActiveBroadcastSet(MetaTime at, IEnumerable<BroadcastMessage> broadcasts)
        {
            ActiveBroadcasts    = broadcasts.Where(broadcast => broadcast.Params.IsActiveAt(at)).ToList();
            BroadcastIds        = broadcasts.Where(broadcast => /* \todo [petri] implement archive */ true).Select(broadcast => broadcast.Params.Id).ToList();
        }

        public bool Equals(ActiveBroadcastSet other)
        {
            if (other is null)
                return false;

            if (!ActiveBroadcasts.SequenceEqual(other.ActiveBroadcasts))
                return false;

            if (!BroadcastIds.SequenceEqual(other.BroadcastIds))
                return false;

            return true;
        }

        public override bool Equals(object obj) => obj is ActiveBroadcastSet other && Equals(other);
        public override int GetHashCode() => 0; // \todo Proper hash. Probably not much used for IAtomicValues though
    }
}
