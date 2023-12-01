// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// A basic PlayerCondition class implementing the condition for a player segment.
    /// I.e., the condition which determines whether a player belongs to a segment.
    ///
    /// This type of PlayerCondition represents conditions on player properties
    /// (such as number of gems, account age, or last known country) as well
    /// as references to other segments.
    ///
    /// A segment can also use a custom game-specific PlayerCondition type.
    /// This type is provided out-of-the-box for convenience.
    ///
    /// In the future this will likely get replaced by a more general and flexible
    /// out-of-the-box condition/expression evaluation system.
    /// </summary>
    /// <remarks>
    /// Specifically, this implements the following form of condition:
    ///   Props AND RequireAny AND RequireAll
    /// Where:
    /// - Props is the logical AND of all the PropertyRequirements
    /// - RequireAny is the logical OR of all the segments in RequireAnySegment, except
    ///   as a special case RequireAny is true if RequireAnySegment is empty or null
    /// - RequireAll is the logical AND of all the segments in RequireAllSegments
    /// </remarks>
    [MetaSerializableDerived(1000)]
    public class PlayerSegmentBasicCondition : PlayerCondition
    {
        [MetaMember(1)] public List<PlayerPropertyRequirement>  PropertyRequirements    { get; private set; }
        [MetaMember(2)] public List<PlayerSegmentId>            RequireAnySegment       { get; private set; }
        [MetaMember(3)] public List<PlayerSegmentId>            RequireAllSegments      { get; private set; }

        /// <summary>
        /// This gets called after the class is deserialized from JSON, and is used to validate the segment data. It is
        /// used specifically by the various API controllers (broadcast, experiment, etc.) that allow the user to send
        /// segment based targeting information to ensure that the data is correctly formatted.
        /// </summary>
        [OnDeserialized]
        void OnDeserialized(StreamingContext context)
        {
            if ((RequireAnySegment != null && RequireAnySegment.Any(id => id == null)) ||
                (RequireAllSegments != null && RequireAllSegments.Any(id => id == null)))
            {
                throw new Exception("Segment lists cannot contain nulls");
            }
        }

        public override IEnumerable<PlayerSegmentId> GetSegmentReferences()
        {
            IEnumerable<PlayerSegmentId> requireAny = RequireAnySegment     ?? Enumerable.Empty<PlayerSegmentId>();
            IEnumerable<PlayerSegmentId> requireAll = RequireAllSegments    ?? Enumerable.Empty<PlayerSegmentId>();

            return requireAny.Concat(requireAll);
        }

        PlayerSegmentBasicCondition(){ }
        public PlayerSegmentBasicCondition(List<PlayerPropertyRequirement> propertyRequirements, List<PlayerSegmentId> requireAnySegment, List<PlayerSegmentId> requireAllSegments)
        {
            PropertyRequirements = propertyRequirements;
            RequireAnySegment = requireAnySegment;
            RequireAllSegments = requireAllSegments;
        }

        public override bool MatchesPlayer(IPlayerModelBase player)
        {
            return PropertyRequirementsMatch(player)
                && AnySegmentRequirementMatches(player)
                && AllSegmentsRequirementMatches(player);
        }

        IEnumerable<string> DescribeParts()
        {
            if (RequireAnySegment != null && RequireAnySegment.Count > 0)
                yield return string.Join(" OR ", RequireAnySegment);
            if (RequireAllSegments != null && RequireAllSegments.Count > 0)
                yield return string.Join(" AND ", RequireAllSegments);
            if (PropertyRequirements != null)
                yield return string.Join(" AND ", PropertyRequirements);
        }

        public string Describe()
        {
            List<string> parts = DescribeParts().ToList();
            if (!parts.Any())
                return "all players";
            if (parts.Count == 1)
                return parts[0];
            return string.Join(" AND ", parts.Select(x => $"({x})"));
        }

        bool PropertyRequirementsMatch(IPlayerModelBase player)
        {
            // All requirements in PropertyRequirements (if non-null) must match.
            if (PropertyRequirements == null)
                return true;
            foreach (PlayerPropertyRequirement req in PropertyRequirements)
            {
                if (!req.MatchesPlayer(player))
                    return false;
            }
            return true;
        }

        bool AnySegmentRequirementMatches(IPlayerModelBase player)
        {
            // RequireAnySegment must be null or empty, or at least one segment in it must match.
            if (RequireAnySegment == null)
                return true;
            if (RequireAnySegment.Count == 0)
                return true;
            foreach (PlayerSegmentId segId in RequireAnySegment)
            {
                if (player.GameConfig.PlayerSegments.TryGetValue(segId, out PlayerSegmentInfoBase segment) && segment.MatchesPlayer(player))
                    return true;
            }
            return false;
        }

        bool AllSegmentsRequirementMatches(IPlayerModelBase player)
        {
            // All segments in RequireAllSegments (if non-null) must match.
            if (RequireAllSegments == null)
                return true;
            foreach (PlayerSegmentId segId in RequireAllSegments)
            {
                bool matchesSegment = player.GameConfig.PlayerSegments.TryGetValue(segId, out PlayerSegmentInfoBase segment) && segment.MatchesPlayer(player);
                if (!matchesSegment)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Base class for representing a row in a player segment config sheet.
    /// This is meant to be read from a config sheet, and then converted to a
    /// <see cref="PlayerSegmentInfoBase"/>-deriving config item, using <see cref="ToConfigData"/>.
    ///
    /// Meant for segments using the <see cref="PlayerSegmentBasicCondition"/> condition type specifically.
    /// </summary>
    public abstract class PlayerSegmentBasicInfoSourceItemBase<TPlayerSegmentInfo> :
        IGameConfigSourceItem<PlayerSegmentId, TPlayerSegmentInfo>,
        IMetaIntegrationConstructible<PlayerSegmentBasicInfoSourceItemBase<TPlayerSegmentInfo>>
        where TPlayerSegmentInfo : PlayerSegmentInfoBase, IGameConfigData<PlayerSegmentId>
    {
        public PlayerSegmentId        SegmentId          { get; private set; }
        public string                 DisplayName        { get; private set; }
        public string                 Description        { get; private set; }
        public List<PlayerPropertyId> PropId             { get; private set; } = new List<PlayerPropertyId>();
        public List<string>           PropMin            { get; private set; } = new List<string>();
        public List<string>           PropMax            { get; private set; } = new List<string>();
        public List<PlayerSegmentId>  RequireAnySegment  { get; private set; }
        public List<PlayerSegmentId>  RequireAllSegments { get; private set; }

        public PlayerSegmentId ConfigKey => SegmentId;

        public TPlayerSegmentInfo ToConfigData()
        {
            return CreateSegmentInfo(SegmentId, ConstructPlayerCondition(), DisplayName, Description);
        }

        /// <summary>
        /// Implement this in your derived class - this should create the <see cref="PlayerSegmentInfoBase"/>-derived
        /// config data item from the given parameters.
        /// </summary>
        protected abstract TPlayerSegmentInfo CreateSegmentInfo(PlayerSegmentId segmentId, PlayerSegmentBasicCondition playerCondition, string displayName, string description);

        protected virtual PlayerPropertyRequirement ParsePlayerPropertyRequirement(PlayerPropertyId id, string min, string max)
        {
            return PlayerPropertyRequirement.ParseFromStrings(id, min, max);
        }

        PlayerSegmentBasicCondition ConstructPlayerCondition()
        {
            return new PlayerSegmentBasicCondition(
                propertyRequirements:   ConstructPropertyRequirements(),
                requireAnySegment:      RequireAnySegment,
                requireAllSegments:     RequireAllSegments);
        }

        List<PlayerPropertyRequirement> ConstructPropertyRequirements()
        {
            int numProps = PropId.Count;

            if (PropMin.Count > numProps)
                throw new InvalidOperationException($"{SegmentId}: {nameof(PropMin)} has more entries than {nameof(PropId)}: {PropMin.Count} vs {numProps}");
            if (PropMax.Count > numProps)
                throw new InvalidOperationException($"{SegmentId}: {nameof(PropMax)} has more entries than {nameof(PropId)}: {PropMax.Count} vs {numProps}");

            List<PlayerPropertyRequirement> propertyRequirements = new List<PlayerPropertyRequirement>();
            for (int i = 0; i < numProps; i++)
            {
                PlayerPropertyRequirement requirement;
                try
                {
                    requirement = ParsePlayerPropertyRequirement(PropId[i], PropMin.ElementAtOrDefault(i), PropMax.ElementAtOrDefault(i));
                }
                catch (Exception ex)
                {
                    throw new ParseError($"Failed to parse {nameof(PlayerPropertyRequirement)} (on property {PropId[i]}) for segment {SegmentId}", ex);
                }

                propertyRequirements.Add(requirement);
            }

            return propertyRequirements;
        }
    }

    public class DefaultPlayerSegmentBasicInfoSourceItem : PlayerSegmentBasicInfoSourceItemBase<DefaultPlayerSegmentInfo>
    {
        protected override DefaultPlayerSegmentInfo CreateSegmentInfo(PlayerSegmentId segmentId, PlayerSegmentBasicCondition playerCondition, string displayName, string description)
        {
            return new DefaultPlayerSegmentInfo(segmentId, playerCondition, displayName, description);
        }
    }
}
