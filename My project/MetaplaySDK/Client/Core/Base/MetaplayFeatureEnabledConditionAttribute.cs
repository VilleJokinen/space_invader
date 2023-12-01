// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Metaplay.Core
{
    /// <summary>
    /// A generic way to communicate that a property is associated with a Metaplay feature
    /// that can be enabled or disabled. The semantics of what makes a feature enabled is
    /// defined by inherited classes and the semantics of what this means for a given property
    /// is context-dependent.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface  | AttributeTargets.Enum)]
    public abstract class MetaplayFeatureEnabledConditionAttribute : Attribute
    {
        public abstract bool IsEnabled { get; }

        public static bool IsEnabledWithTypeAndAncestorAttributes(IEnumerable<MetaplayFeatureEnabledConditionAttribute> attributes)
        {
            // If no feature flags => enabled
            // Otherwise if any feature is enabled => enabled
            // Otherwise if all declared features are disabled => not enabled

            if (attributes == null)
                return true;

            bool hasAnyFeatureRequirements = false;
            foreach (MetaplayFeatureEnabledConditionAttribute attribute in attributes)
            {
                hasAnyFeatureRequirements = true;
                if (attribute.IsEnabled)
                    return true;
            }

            if (!hasAnyFeatureRequirements)
                return true;
            return false;
        }
    }

    public static class MetaplayFeatureEnabledConditionAttributeExtensions
    {
        /// <summary>
        /// Checks for presence of <see cref="MetaplayFeatureEnabledConditionAttribute"/> and checks whether the feature is
        /// enabled or not. If no attribute is found this method returns true.
        /// </summary>
        public static bool IsMetaFeatureEnabled(this Type type)
        {
            return MetaplayFeatureEnabledConditionAttribute.IsEnabledWithTypeAndAncestorAttributes(type.GetCustomAttributes<MetaplayFeatureEnabledConditionAttribute>(inherit: true));
        }

        /// <inheritdoc cref="IsMetaFeatureEnabled(System.Type)"/>
        public static bool IsMetaFeatureEnabled(this MemberInfo member)
        {
            return MetaplayFeatureEnabledConditionAttribute.IsEnabledWithTypeAndAncestorAttributes(member.GetCustomAttributes<MetaplayFeatureEnabledConditionAttribute>(inherit: true));
        }
    }
}
