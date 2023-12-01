// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using static System.FormattableString;

namespace Metaplay.Cloud.Sharding
{
    /// <summary>
    /// Identifies an <see cref="EntityShard"/> instance in the cluster. Identified by combination
    /// of <see cref="EntityKind"/> and integer <see cref="Value"/>.
    /// </summary>
    public struct EntityShardId : IEquatable<EntityShardId>
    {
        public EntityKind   Kind    { get; private set; }
        public int          Value   { get; private set; }

        public bool         IsValid => Kind != EntityKind.None;

        public EntityShardId(EntityKind kind, int value)
        {
            MetaDebug.Assert(kind != EntityKind.None || value == 0, "Value must be zero for EntityKind.None");

            Kind = kind;
            Value = value;
        }

        public static EntityShardId None => new EntityShardId(EntityKind.None, 0);

        public static bool operator ==(EntityShardId a, EntityShardId b) => (a.Kind == b.Kind) && (a.Value == b.Value);
        public static bool operator !=(EntityShardId a, EntityShardId b) => (a.Kind != b.Kind) || (a.Value != b.Value);

        public bool             Equals      (EntityShardId other) => this == other;
        public override bool    Equals      (object obj) => (obj is EntityShardId other) ? (this == other) : false;

        public bool             IsOfKind(EntityKind kind) => Kind == kind;

        public override int     GetHashCode() => Util.CombineHashCode(Kind.GetHashCode(), Value);

        public override string  ToString() => Invariant($"EntityShard.{Kind}#{Value}");
    }
}
