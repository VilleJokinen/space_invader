// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using System;

namespace Metaplay.Core.Message
{
    [AttributeUsage(AttributeTargets.Class)]
    public class RequestResponseAttribute : Attribute, ISerializableFlagsProvider
    {
        public MetaSerializableFlags ExtraFlags => MetaSerializableFlags.ImplicitMembers;
    }

    [MetaSerializable, RequestResponse, MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    public abstract class MetaRequest { }

    [MetaSerializable, RequestResponse, MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    public abstract class MetaResponse { }

    public static class RequestTypeCodes
    {
        public const int SocialAuthenticateRequest = 1;
        public const int SocialAuthenticateResponse = 2;
        public const int DevOverwritePlayerStateRequest = 3;
        public const int DevOverwritePlayerStateFailure = 4;
        public const int ImmutableXLoginChallengeRequest = 5;
        public const int ImmutableXLoginChallengeResponse = 6;
    };
}
