// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static System.FormattableString;

namespace Metaplay.Core
{
    /// <summary>
    /// Mark a class as containing EntityKind registrations -- all 'static EntityKind' fields in the class are
    /// considered EntityKind definitions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class EntityKindRegistryAttribute : Attribute
    {
        public readonly int StartIndex;
        public readonly int EndIndex;

        public EntityKindRegistryAttribute(int startIndex, int endIndex)
        {
            MetaDebug.Assert(startIndex >= 0 && endIndex >= 0 && startIndex < endIndex, "Invalid range of values {0}..{1}", startIndex, endIndex);
            StartIndex = startIndex;
            EndIndex = endIndex;
        }
    }

    public class EntityKindRegistry
    {
        static EntityKindRegistry _instance = null;
        public static EntityKindRegistry Instance => _instance ?? throw new InvalidOperationException("EntityKindRegistry not yet initialized");

        public readonly Dictionary<string, EntityKind>  ByName  = new Dictionary<string, EntityKind>();
        public readonly Dictionary<EntityKind, string>  ByValue = new Dictionary<EntityKind, string>();

        EntityKindRegistry()
        {
            Dictionary<Type, EntityKindRegistryAttribute[]> typeToAttribs = new Dictionary<Type, EntityKindRegistryAttribute[]>();

            foreach (Type type in TypeScanner.GetClassesWithAttribute<EntityKindRegistryAttribute>())
            {
                // Check for range conflicts
                EntityKindRegistryAttribute[] attribs = type.GetCustomAttributes<EntityKindRegistryAttribute>().ToArray();
                for (int pairA = 0; pairA < attribs.Length; ++pairA)
                {
                    for (int pairB = pairA+1; pairB < attribs.Length; ++pairB)
                    {
                        if (DoesRangeOverlap(attribs[pairA], attribs[pairB]))
                            throw new InvalidOperationException($"Ranges for EntityKind registry {type.ToGenericTypeString()} ({attribs[pairA].StartIndex}..{attribs[pairA].EndIndex}) ({attribs[pairB].StartIndex}..{attribs[pairB].EndIndex}) overlap!");
                    }
                }
                foreach (EntityKindRegistryAttribute attrib in attribs)
                {
                    foreach ((Type otherType, EntityKindRegistryAttribute[] others) in typeToAttribs)
                    {
                        foreach (EntityKindRegistryAttribute other in others)
                        {
                            if (DoesRangeOverlap(attrib, other))
                                throw new InvalidOperationException($"Ranges for EntityKind registries {type.ToGenericTypeString()} ({attrib.StartIndex}..{attrib.EndIndex}) and {otherType.ToGenericTypeString()} ({other.StartIndex}..{other.EndIndex}) overlap!");
                        }
                    }
                }
                typeToAttribs.Add(type, attribs);

                // Iterate over all EntityKind registrations
                foreach (FieldInfo fi in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(fi => fi.FieldType == typeof(EntityKind)))
                {
                    if (!fi.IsInitOnly)
                        throw new InvalidOperationException($"{type.ToGenericTypeString()}.{fi.Name} must be 'static readonly'");

                    // Check that value is in range
                    EntityKind kind = (EntityKind)fi.GetValue(null);
                    bool inRange = false;
                    foreach (EntityKindRegistryAttribute attrib in attribs)
                    {
                        if (kind.Value >= attrib.StartIndex && kind.Value < attrib.EndIndex)
                            inRange = true;
                    }
                    if (!inRange)
                        throw new InvalidOperationException($"{type.ToGenericTypeString()}.{fi.Name} value ({kind.Value}) is out of range ({string.Join(", ", attribs.Select(attrib => $"{attrib.StartIndex}..{attrib.EndIndex})"))}).");

                    // Check for name conflicts
                    if (ByName.TryGetValue(fi.Name, out EntityKind _))
                        throw new InvalidOperationException($"Duplicate EntityKinds with name {fi.Name}");

                    // Check for value conflicts
                    if (ByValue.TryGetValue(kind, out string existingName))
                        throw new InvalidOperationException($"EntityKinds {fi.Name} and {existingName} have the same value {kind.Value}");

                    ByName.Add(fi.Name, kind);
                    ByValue.Add(kind, fi.Name);
                }
            }
        }

        public static void Initialize()
        {
            if (_instance != null)
                throw new InvalidOperationException($"Duplicate initialization of {nameof(EntityKindRegistry)}");

            _instance = new EntityKindRegistry();
        }

        static bool DoesRangeOverlap(EntityKindRegistryAttribute a, EntityKindRegistryAttribute b)
        {
            int start0  = a.StartIndex;
            int end0    = a.EndIndex;
            int start1  = b.StartIndex;
            int end1    = b.EndIndex;
            int w0 = end0 - start0;
            int w1 = end1 - start1;
            int mn = System.Math.Min(start0, start1);
            int mx = System.Math.Max(end0, end1);
            return (mx - mn) < (w0 + w1);
        }

        public static bool TryFromName(string str, out EntityKind kind) =>
            Instance.ByName.TryGetValue(str, out kind);

        public static EntityKind FromName(string str)
        {
            if (TryFromName(str, out EntityKind kind))
                return kind;
            else
                throw new InvalidOperationException($"No such EntityKind '{str}'");
        }

        public static string GetName(EntityKind kind)
        {
            if (Instance.ByValue.TryGetValue(kind, out string name))
                return name;
            else
                return Invariant($"Invalid#{kind.Value}");
        }

        /// <summary>
        /// Check whether the given EntityKind is a valid value found in the registry.
        /// <c>EntityKind.None</c> returns false.
        /// </summary>
        public static bool IsValid(EntityKind kind) =>
            Instance.ByValue.ContainsKey(kind);

        public static IEnumerable<EntityKind> AllValues =>
            Instance.ByValue.Keys;
    }
}
