// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Metaplay.Server.MultiplayerEntity.InternalMessages
{
    //
    // This file contains Server-internal messages that are part of Metaplay core.
    //

    /// <summary>
    /// Request to fetch Entity State. Entity responds with <see cref="InternalEntityStateResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalEntityStateRequest, MessageDirection.ServerInternal)]
    public class InternalEntityStateRequest : MetaMessage
    {
        public static readonly InternalEntityStateRequest Instance = new InternalEntityStateRequest();
    }

    /// <summary>
    /// Response to <see cref="InternalEntityStateRequest"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalEntityStateResponse, MessageDirection.ServerInternal)]
    public class InternalEntityStateResponse : MetaMessage
    {
        /// <summary>
        /// Empty if there is no initialized model.
        /// </summary>
        public MetaSerialized<IModel>           Model                       { get; private set; }
        public int                              LogicVersion                { get; private set; }

        /// <summary>
        /// Id of the Static full game config
        /// </summary>
        public MetaGuid                         StaticGameConfigId          { get; private set; }

        /// <summary>
        /// Id of the Dynamic full game config
        /// </summary>
        public MetaGuid                         DynamicGameConfigId         { get; private set; }

        /// <summary>
        /// Specialization key for choosing the correct config variant combination. If <c>null</c>, then by convention no
        /// specialization is done.
        /// </summary>
        public GameConfigSpecializationKey?     SpecializationKey           { get; private set; }

        InternalEntityStateResponse() { }
        public InternalEntityStateResponse(MetaSerialized<IModel> model, int logicVersion, MetaGuid staticGameConfigId, MetaGuid dynamicGameConfigId, GameConfigSpecializationKey? specializationKey)
        {
            Model = model;
            LogicVersion = logicVersion;
            StaticGameConfigId = staticGameConfigId;
            DynamicGameConfigId = dynamicGameConfigId;
            SpecializationKey = specializationKey;
        }
    }

    /// <summary>
    /// Association reference with optional payload from a (source) entity to the "associated" entity. During session init, session subscribes into all associated entities of
    /// the player, proxying this data when it subscribes into each associated entity. This can be used to synchronize state between the entities.
    /// </summary>
    [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
    [MetaImplicitMembersRange(101, 200)]
    [MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    public abstract class AssociatedEntityRefBase
    {
        /// <summary>
        /// Default association ref with no userdata.
        /// </summary>
        [MetaSerializableDerived(101)]
        public sealed class Default : AssociatedEntityRefBase
        {
            public ClientSlot ClientSlot { get; private set; }
            public override ClientSlot GetClientSlot() => ClientSlot;

            Default() { }
            public Default(ClientSlot clientSlot, EntityId sourceEntity, EntityId associatedEntity) : base(sourceEntity, associatedEntity)
            {
                ClientSlot = clientSlot;
            }
        }

        public abstract ClientSlot GetClientSlot();

        /// <summary>
        /// Entity which declared this entity association.
        /// </summary>
        [MetaMember(101)] public EntityId SourceEntity;

        /// <summary>
        /// Entity which is associated to the source entity.
        /// </summary>
        [MetaMember(102)] public EntityId AssociatedEntity;

        protected AssociatedEntityRefBase() { }
        protected AssociatedEntityRefBase(EntityId sourceEntity, EntityId associatedEntity)
        {
            if (!sourceEntity.IsValid)
                throw new ArgumentException("Source entity ID must be valid", nameof(sourceEntity));
            if (!associatedEntity.IsValid)
                throw new ArgumentException("Associated entity ID must be valid", nameof(associatedEntity));
            SourceEntity = sourceEntity;
            AssociatedEntity = associatedEntity;
        }
    }

    /// <summary>
    /// Base class for Session->Entity subscription requests. This is an server internal message and implementation should set message
    /// direction to <see cref="MessageDirection.ServerInternal"/>.
    /// </summary>
    [MetaImplicitMembersRange(101, 200)]
    public abstract class InternalEntitySubscribeRequestBase : MetaMessage
    {
        /// <summary>
        /// Default implementation.
        /// </summary>
        [MetaMessage(MessageCodesCore.InternalEntitySubscribeRequestDefault, MessageDirection.ServerInternal)]
        public class Default : InternalEntitySubscribeRequestBase
        {
            Default() { }
            public Default(AssociatedEntityRefBase associationRef, int clientChannelId, SessionProtocol.SessionResourceProposal? resourceProposal, bool isDryRun, CompressionAlgorithmSet supportedArchiveCompressions) : base(associationRef, clientChannelId, resourceProposal, isDryRun, supportedArchiveCompressions)
            {
            }
        }

        public AssociatedEntityRefBase                  AssociationRef                  { get; private set; }
        public int                                      ClientChannelId                 { get; private set; }
        public SessionProtocol.SessionResourceProposal? ResourceProposal                { get; private set; }
        public bool                                     IsDryRun                        { get; private set; }
        public CompressionAlgorithmSet                  SupportedArchiveCompressions    { get; private set; }

        protected InternalEntitySubscribeRequestBase() { }
        protected InternalEntitySubscribeRequestBase(AssociatedEntityRefBase associationRef, int clientChannelId, SessionProtocol.SessionResourceProposal? resourceProposal, bool isDryRun, CompressionAlgorithmSet supportedArchiveCompressions)
        {
            AssociationRef = associationRef;
            ClientChannelId = clientChannelId;
            ResourceProposal = resourceProposal;
            IsDryRun = isDryRun;
            SupportedArchiveCompressions = supportedArchiveCompressions;
        }
    }

    /// <summary>
    /// Base class for successful Session->Entity subscription.
    /// </summary>
    [MetaImplicitMembersRange(101, 200)]
    public abstract class InternalEntitySubscribeResponseBase : MetaMessage
    {
        /// <summary>
        /// Default implementation.
        /// </summary>
        [MetaMessage(MessageCodesCore.InternalEntitySubscribeResponseDefault, MessageDirection.ServerInternal)]
        public class Default : InternalEntitySubscribeResponseBase
        {
            Default() { }
            public Default(EntityInitialState state, List<AssociatedEntityRefBase> associatedEntities) : base(state,  associatedEntities)
            {
            }
        }

        public EntityInitialState               State               { get; private set; }
        public List<AssociatedEntityRefBase>    AssociatedEntities  { get; private set; }

        protected InternalEntitySubscribeResponseBase() { }
        protected InternalEntitySubscribeResponseBase(EntityInitialState state, List<AssociatedEntityRefBase> associatedEntities)
        {
            State = state;
            AssociatedEntities = associatedEntities;
        }
    }

    /// <summary>
    /// Base class for Session->Entity subscription attempt refusal results. When emitted, this is delivered to the association source entity
    /// <see cref="PersistedMultiplayerEntityActorBase{TModel, TAction, TPersisted}.OnAssociatedEntityRefusalAsync(EntityId, AssociatedEntityRefBase, InternalEntitySubscribeRefusedBase)"/> handler
    /// which should handle the error. Common <see cref="Builtins"/> messages are provided for convenience.
    /// </summary>
    [MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    [MetaImplicitMembersRange(101, 200)]
    public abstract class InternalEntitySubscribeRefusedBase : EntityAskRefusal
    {
        /// <summary>
        /// Builtin refusal reasons.
        /// </summary>
        public static class Builtins
        {
            /// <summary>
            /// Refused due to resource correction.
            /// <para>This is handled in SessionActor automatically.</para>
            /// </summary>
            [MetaSerializableDerived(MessageCodesCore.InternalEntitySubscribeRefusedResourceCorrection)]
            public class ResourceCorrection : InternalEntitySubscribeRefusedBase
            {
                ResourceCorrection() { }
                public ResourceCorrection(SessionProtocol.SessionResourceCorrection resourceCorrection, List<AssociatedEntityRefBase> associatedEntities) : base(resourceCorrection, associatedEntities)
                {
                }
            }

            /// <summary>
            /// Refused due to successful dry run.
            /// <para>This is handled in SessionActor automatically.</para>
            /// </summary>
            [MetaSerializableDerived(MessageCodesCore.InternalEntitySubscribeRefusedDryRunSuccess)]
            public class DryRunSuccess : InternalEntitySubscribeRefusedBase
            {
                DryRunSuccess() { }
                public DryRunSuccess(List<AssociatedEntityRefBase> associatedEntities) : base(resourceCorrection: default, associatedEntities)
                {
                }
            }

            /// <summary>
            /// Refused due to a temporary error. Session should try immediately again.
            /// <para>This is handled in SessionActor automatically.</para>
            /// </summary>
            [MetaSerializableDerived(MessageCodesCore.InternalEntitySubscribeRefusedTryAgain)]
            public class TryAgain : InternalEntitySubscribeRefusedBase
            {
                public TryAgain() : base(resourceCorrection: default, associatedEntities: new List<AssociatedEntityRefBase>())
                {
                }
            }

            /// <summary>
            /// Refused due to source entity not being a a participant in the associated entity. Source entity
            /// should forget the associated entity and the session should try immediately again.
            /// </summary>
            [MetaSerializableDerived(MessageCodesCore.InternalEntitySubscribeRefusedNotAParticipant)]
            public class NotAParticipant : InternalEntitySubscribeRefusedBase
            {
                public NotAParticipant() : base(resourceCorrection: default, associatedEntities: new List<AssociatedEntityRefBase>())
                {
                }
            }
        }

        [MetaMember(101)] public SessionProtocol.SessionResourceCorrection  ResourceCorrection  { get; private set; }
        [MetaMember(102)] public List<AssociatedEntityRefBase>              AssociatedEntities  { get; private set; }

        public override string Message => $"Session subscribe refused with {GetType().ToGenericTypeString()}";

        protected InternalEntitySubscribeRefusedBase() { }
        protected InternalEntitySubscribeRefusedBase(SessionProtocol.SessionResourceCorrection resourceCorrection, List<AssociatedEntityRefBase> associatedEntities)
        {
            ResourceCorrection = resourceCorrection;
            AssociatedEntities = associatedEntities;
        }
    }

    /// <summary>
    /// Setup parameters to a Multiplayer Entity. Should contain whatever information is needed to create the Model state.
    /// </summary>
    [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
    [MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    public interface IMultiplayerEntitySetupParams
    {
    }

    /// <summary>
    /// Request to set up a an empty Multiplayer Entity with the given parameters. Responds with <see cref="InternalEntitySetupResponse"/>
    /// or fails with <see cref="InternalEntitySetupRefusal"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalEntitySetupRequest, MessageDirection.ServerInternal)]
    public class InternalEntitySetupRequest : MetaMessage
    {
        public IMultiplayerEntitySetupParams SetupParams { get; private set; }

        InternalEntitySetupRequest() { }
        public InternalEntitySetupRequest(IMultiplayerEntitySetupParams setupParams)
        {
            SetupParams = setupParams;
        }
    }

    /// <summary>
    /// Response to <see cref="InternalEntitySetupRequest"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalEntitySetupResponse, MessageDirection.ServerInternal)]
    public class InternalEntitySetupResponse : MetaMessage
    {
        public InternalEntitySetupResponse() { }
    }

    /// <summary>
    /// Failing response to <see cref="InternalEntitySetupRequest"/>.
    /// </summary>
    [MetaSerializableDerived(MessageCodesCore.InternalEntitySetupRefusal)]
    public class InternalEntitySetupRefusal : EntityAskRefusal
    {
        public InternalEntitySetupRefusal() { }

        public override string Message => "Entity setup refused";
    }

    /// <summary>
    /// Session -> Entity notification that the declared association data was refused by the associated entity. Entity should update its state to reflect
    /// the new state, unless the request is stale. Responded with <see cref="EntityAskOk"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalEntityAssociatedEntityRefusedRequest, MessageDirection.ServerInternal)]
    public class InternalEntityAssociatedEntityRefusedRequest : MetaMessage
    {
        /// <summary>
        /// The association this entity declared earlier.
        /// </summary>
        public AssociatedEntityRefBase              AssociationRef  { get; private set; }

        /// <summary>
        /// The refusal the association target entity returned for the given association data.
        /// </summary>
        public InternalEntitySubscribeRefusedBase   Refusal         { get; private set; }

        InternalEntityAssociatedEntityRefusedRequest() { }
        public InternalEntityAssociatedEntityRefusedRequest(AssociatedEntityRefBase associationRef, InternalEntitySubscribeRefusedBase refusal)
        {
            AssociationRef = associationRef;
            Refusal = refusal;
        }
    }
}
