// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Metaplay.Cloud.Persistence
{
    /// <summary>
    /// Marks a persisted item as not defining a primary key. Useful when the item doesn't have a unique identity,
    /// such as string search suffix tables.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class NoPrimaryKeyAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a persisted item as not using partitioning. Note that this is not scalable, so should only be used for
    /// small amounts of infrequently accessed data.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class NonPartitionedAttribute : Attribute
    {
    }

    /// <summary>
    /// Mark a member of a persisted item as the key used for partitioning (sharding) of the item in the database.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class PartitionKeyAttribute : Attribute
    {
    }

    /// <summary>
    /// Mark an abstract persisted base class as something that should be replaced with the concrete type when
    /// executing database queries on.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class MetaPersistedVirtualItemAttribute : Attribute
    {
    }

    /// <summary>
    /// Base interface for objects which are persisted into database.
    /// Note that partitioned items must declare one of their members with the <see cref="PartitionKeyAttribute"/> attribute.
    /// The value of said member controls which database shard the item is actually stored on.
    /// </summary>
    public interface IPersistedItem
    {
    }

    /// <summary>
    /// Represents a persisted entity (eg, Player or Alliance).
    /// </summary>
    public interface IPersistedEntity : IPersistedItem
    {
        string      EntityId        { get; set; }
        DateTime    PersistedAt     { get; set; }

        /// <summary>
        /// <c>null</c> if entity state has not yet been initialized.
        /// </summary>
        byte[]      Payload         { get; set; }

        /// <summary>
        /// Schema version for Payload.
        /// </summary>
        int         SchemaVersion   { get; set; }

        /// <summary>
        /// Is this a final persisted version (warn if resuming from non-final).
        /// </summary>
        bool        IsFinal         { get; set; }
    }

    /// <summary>
    /// Iterator for scanning through all persisted entities of a given type in the database in a paged manner.
    /// </summary>
    public class PagedIterator
    {
        public readonly int     ShardIndex          = 0;
        public readonly string  StartKeyExclusive   = "";
        public readonly bool    IsFinished          = false;

        public static readonly PagedIterator Start  = new PagedIterator();
        public static readonly PagedIterator End    = new PagedIterator(shardIndex: -1, startKeyExclusive: null, isFinished: true);

        PagedIterator() { }
        public PagedIterator(int shardIndex, string startKeyExclusive, bool isFinished) { ShardIndex = shardIndex; StartKeyExclusive = startKeyExclusive; IsFinished = isFinished; }
    }

    /// <summary>
    /// Result from a paged database query. Contains a collection of items and an iterator that can be used to query
    /// the next page of items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class PagedQueryResult<T>
    {
        public readonly PagedIterator   Iterator;
        public readonly List<T>         Items;

        public PagedQueryResult(PagedIterator iterator, List<T> items) { Iterator = iterator; Items = items; }
    }
}
