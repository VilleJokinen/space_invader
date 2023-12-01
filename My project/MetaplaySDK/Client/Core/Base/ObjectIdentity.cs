// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Runtime.CompilerServices;

namespace Metaplay.Core
{
    /// <summary>
    /// Wrapper that is Equal only to other wrappers with objects with the same identity. (Instead
    /// of structural equality).
    /// </summary>
    readonly struct ObjectIdentity : IEquatable<ObjectIdentity>
    {
        readonly object _obj;

        public ObjectIdentity(object obj)
        {
            _obj = obj;
        }

        public override bool Equals(object obj)
        {
            if (obj is ObjectIdentity other)
                return Equals(other);
            return false;
        }

        public override int GetHashCode()
        {
#pragma warning disable RS1024
            return RuntimeHelpers.GetHashCode(_obj);
#pragma warning restore RS1024
        }

        public override string ToString()
        {
            return _obj == null ? "<null>" : Util.ObjectToStringInvariant(_obj);
        }

        public bool Equals(ObjectIdentity other)
        {
            return Object.ReferenceEquals(_obj, other._obj);
        }
    }
}
