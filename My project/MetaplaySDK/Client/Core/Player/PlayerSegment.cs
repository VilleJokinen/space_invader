// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Metaplay.Core.Player
{
    [MetaSerializable]
    public class PlayerSegmentId : StringId<PlayerSegmentId> { }

    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class PlayerSegmentInfoBase : IGameConfigData<PlayerSegmentId>
    {
        [MetaMember(100)] public PlayerSegmentId    SegmentId       { get; private set; }
        [MetaMember(103)] public PlayerCondition    PlayerCondition { get; private set; }
        [MetaMember(102)] public string             DisplayName     { get; private set; }
        [MetaMember(101)] public string             Description     { get; private set; }

        public PlayerSegmentId ConfigKey => SegmentId;

        public PlayerSegmentInfoBase(){ }
        public PlayerSegmentInfoBase(PlayerSegmentId segmentId, PlayerCondition playerCondition, string displayName, string description)
        {
            SegmentId       = segmentId ?? throw new ArgumentNullException(nameof(segmentId));
            PlayerCondition = playerCondition ?? throw new ArgumentNullException(nameof(playerCondition));
            DisplayName     = displayName;
            Description     = description;
        }

        /// <summary>
        /// Check that the given library doesn't contain invalid internal references.
        ///
        /// This will probably get replaced by more general reference-validation/cycle-detection
        /// in the future.
        /// See also the comment on <see cref="PlayerCondition.GetSegmentReferences"/>.
        /// </summary>
        public static void CheckInternalReferences(IGameConfigLibrary<PlayerSegmentId, PlayerSegmentInfoBase> infos)
        {
            HashSet<PlayerSegmentId> idsSoFar = new HashSet<PlayerSegmentId>();

            foreach (PlayerSegmentInfoBase info in infos.Values)
            {
                IEnumerable<PlayerSegmentId> segmentRefs = info.PlayerCondition.GetSegmentReferences();

                foreach (PlayerSegmentId segmentRef in segmentRefs)
                {
                    if (!infos.TryGetValue(segmentRef, out PlayerSegmentInfoBase _))
                        throw new InvalidOperationException($"Player segment {info.SegmentId} refers to nonexistent segment {segmentRef}");

                    if (!idsSoFar.Contains(segmentRef))
                    {
                        if (segmentRef == info.SegmentId)
                            throw new InvalidOperationException($"Player segment {info.SegmentId} refers to itself. An internal reference can only refer to a row that has been encountered before it in the sheet.");
                        else
                            throw new InvalidOperationException($"Player segment {info.SegmentId} refers to other segment {segmentRef} that comes later in the sheet. An internal reference can only refer to a row that has been encountered before it in the sheet.");
                    }
                }

                idsSoFar.Add(info.SegmentId);
            }
        }

        public virtual bool MatchesPlayer(IPlayerModelBase player)
        {
            return PlayerCondition.MatchesPlayer(player);
        }
    }

    // \todo [antti] #helloworld: add support for non-abstract base classes in serializer
    [MetaSerializableDerived(100)]
    public class DefaultPlayerSegmentInfo : PlayerSegmentInfoBase
    {
        public DefaultPlayerSegmentInfo(){ }
        public DefaultPlayerSegmentInfo(PlayerSegmentId segmentId, PlayerCondition playerCondition, string displayName, string description)
            : base(segmentId, playerCondition, displayName, description)
        {
        }

    }
}
