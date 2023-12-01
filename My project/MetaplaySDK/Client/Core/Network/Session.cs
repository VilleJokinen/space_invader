// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Session
{
    /// <summary>
    /// A token that is used to distinguish different sessions of a player.
    /// </summary>
    [MetaSerializable]
    public struct SessionToken
    {
        [MetaMember(1)] public ulong Value { get; private set; }

        public SessionToken(ulong value)
        {
            Value = value;
        }

        public static SessionToken CreateRandom()
        {
            // \todo [nuutti] This is modified copypaste from EntityId.CreateRandom

            // \note not guaranteed to be unique but it's pretty likely for our use case
            // \note guid provides much better randomness than simple Random.Next()
            ulong value = ((ulong)Guid.NewGuid().GetHashCode() << 32) + (ulong)Guid.NewGuid().GetHashCode();
            return new SessionToken(value);
        }

        public static bool operator ==(SessionToken a, SessionToken b) => a.Value == b.Value;
        public static bool operator !=(SessionToken a, SessionToken b) => !(a == b);

        public override bool Equals(object obj)
        {
            return (obj is SessionToken other) && this == other;
        }
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString()
        {
            return $"SessionToken({Value:X16})";
        }
    }

    /// <summary>
    /// Represents the session messaging state of a session participant (i.e. the client or the server).
    /// </summary>
    public class SessionParticipantState
    {
        public SessionToken         Token                       { get; }
        /// <summary>Number of payload messages sent to the other participant</summary>
        public int                  NumSent                     { get; set; } = 0;
        /// <summary>Payload messages that were not yet either acknowledged by the other participant or dropped by us due to queue limiting</summary>
        public Queue<MetaMessage>   RememberedSent              { get; set; } = new Queue<MetaMessage>();
        /// <summary>
        /// Number of payload messages acknowledged by the other participant. May differ from <c>NumSent - RememberedSent.Count</c>
        /// if messages were forgotten without acknowledgement due to queue limiting
        /// </summary>
        public int                  NumAcknowledgedSent         { get; set; } = 0;
        /// <summary>Number of payload messages received from the other participant</summary>
        public int                  NumReceived                 { get; set; } = 0;
        /// <summary>Number of received payload messages reported in the latest acknowledgement sent to the other participant</summary>
        public int                  AcknowledgedNumReceived     { get; set; } = 0;

        /// <summary>Checksum computed from forgotten sent messages</summary>
        /// <remarks>Checksumming is normally disabled</remarks>
        public uint                 ChecksumForForgottenSent    { get; set; } = 0;
        /// <summary>Checksum computed from received messages</summary>
        /// <remarks>Checksumming is normally disabled</remarks>
        public uint                 ChecksumForReceived         { get; set; } = 0;

        public SessionParticipantState(SessionToken token)
        {
            Token = token;
        }
    }

    /// <summary>
    /// Represents an acknowledgement by a session participant that it has received a certain number
    /// of session payload messages from the other session participant.
    /// This allows the sender to remove them from its queue.
    /// </summary>
    [MetaSerializable]
    public struct SessionAcknowledgement
    {
        [MetaMember(1)] public int  NumReceived         { get; private set; }
        /// <remarks>Checksumming is normally disabled</remarks>
        [MetaMember(2)] public uint ChecksumForReceived { get; private set; }

        public SessionAcknowledgement(int numReceived, uint checksumForReceived)
        {
            NumReceived         = numReceived;
            ChecksumForReceived = checksumForReceived;
        }

        public static SessionAcknowledgement FromParticipantState(SessionParticipantState state)
        {
            return new SessionAcknowledgement(state.NumReceived, state.ChecksumForReceived);
        }
    }

    /// <summary>
    /// Info needed to resume a session, sent by a session participant to the other.
    /// </summary>
    [MetaSerializable]
    public class SessionResumptionInfo
    {
        [MetaMember(1)] public SessionToken             Token           { get; private set; }
        [MetaMember(2)] public SessionAcknowledgement   Acknowledgement { get; private set; }

        public SessionResumptionInfo(){ }
        public SessionResumptionInfo(SessionToken token, SessionAcknowledgement acknowledgement)
        {
            Token           = token;
            Acknowledgement = acknowledgement;
        }

        public static SessionResumptionInfo FromParticipantState(SessionParticipantState state)
        {
            return new SessionResumptionInfo(state.Token, SessionAcknowledgement.FromParticipantState(state));
        }
    }

    /// <summary>
    /// Utilities for manipulating <see cref="SessionParticipantState"/>.
    /// </summary>
    public static class SessionUtil
    {
        /// <summary>Acknowledgements are sent every AcknowledgementMessageInterval received payload messages.</summary>
        /// \todo [nuutti] This might not be the ideal way of acknowledging? #session #sessionacknowledgements
        public const int        AcknowledgementMessageInterval  = 5;
        /// <summary>
        /// Whether message debugging checksums are enabled. They should normally be disabled
        /// (for performance reasons). They can be used for debugging session implementation itself.
        /// </summary>
        /// \todo [nuutti] Remove checksumming altogether when session feature is complete and debugged enough? #session
        static readonly bool    EnableMessagingChecksums        = false;

        /// <summary>
        /// Resume session if possible, mutating our session participant state <paramref name="our"/>,
        /// according to info in <paramref name="their"/> provided by the other session participant.
        /// If resuming session is not possible, an appropriate <see cref="ResumeResult.Failure"/> is returned
        /// and <paramref name="our"/> is not mutated.
        /// </summary>
        /// <param name="our">Our session participant state to be mutated upon successful session resumption, or null if we have no session</param>
        /// <param name="their">Description of the other participant's session state.</param>
        /// <returns>Description of resumption result</returns>
        public static ResumeResult HandleResume(SessionParticipantState our, SessionResumptionInfo their)
        {
            // Check: Must have existing session to resume
            if (our == null)
                return new ResumeResult.Failure.WeHaveNoSession{ };

            // Check: Token must match
            if (their.Token != our.Token)
                return new ResumeResult.Failure.TokenMismatch(ourToken: our.Token, theirToken: their.Token);

            // Check: Validate their acknowledgement.
            ValidateAckResult.Failure validateAckFailure = ValidateAck(our, their.Acknowledgement, out ValidateAckResult.Success validateAckSuccess);
            if (validateAckFailure != null)
                return new ResumeResult.Failure.ValidateAckFailure(validateAckFailure);

            // Check: Before applying the successful acknowledgement, also check that we remember enough sent messages so we can re-send them.
            {
                int numForgottenByUs = our.NumSent - our.RememberedSent.Count;
                if (their.Acknowledgement.NumReceived < numForgottenByUs)
                    return new ResumeResult.Failure.WeHaveForgottenTooMany(ourNumSent: our.NumSent, ourNumRememberedSent: our.RememberedSent.Count, theirNumReceived: their.Acknowledgement.NumReceived);
            }

            // All checks are now done, resumption will succeed.

            // Apply the acknowledgement.
            ApplyAck(our, validateAckSuccess);

            return new ResumeResult.Success(validateAckSuccess);
        }

        /// <summary>
        /// Mutate <paramref name="state"/>, reflecting sending an outgoing payload message.
        /// </summary>
        /// <param name="state">Our session state to mutate</param>
        /// <param name="payloadMessage">The message being sent</param>
        public static void HandleSendPayloadMessage(SessionParticipantState state, MetaMessage message)
        {
            MetaDebug.Assert(!(message is SessionControlMessage), "HandleSendPayloadMessage can only be used to send payload messages, but a control message was given: {MessageType}", message.GetType());

            state.RememberedSent.Enqueue(message);
            state.NumSent++;
        }

        /// <summary>
        /// Mutate <paramref name="state"/>, reflecting the reception of a message from the other session participant.
        /// The message may be either a control message that concerns the state of the session communication itself
        /// (such as an acknowledgement of messages received by the other participant) or a paylod message.
        /// </summary>
        /// <param name="state">Our session participant state to be mutated</param>
        /// <param name="message">The message received from the other session participant</param>
        /// <returns>Description of the result of receiving the message</returns>
        /// <remarks>
        /// Note that the result returned may represent an action that we should take as a response,
        /// in particular sending an acknowledgement. See <see cref="ReceiveResult.Type"/>.
        /// </remarks>
        /// \todo [nuutti] Handle sending acknowledgements some other way than as a response to receiving messages? #session #sessionacknowledgements
        public static ReceiveResult HandleReceive(SessionParticipantState state, MetaMessage message)
        {
            if (message is SessionControlMessage control)
            {
                switch (control)
                {
                    case SessionAcknowledgementMessage acknowledgement:
                    {
                        ValidateAckResult.Failure validateAckFailure = ValidateAck(state, acknowledgement.Acknowledgement, out ValidateAckResult.Success validateAckSuccess);
                        if (validateAckFailure != null)
                            return new ReceiveResult(ReceiveResult.ResultType.ReceivedFaultyAck, validAck: default, faultyAck: validateAckFailure, payloadMessage: default);

                        ApplyAck(state, validateAckSuccess);
                        return new ReceiveResult(ReceiveResult.ResultType.ReceivedValidAck, validAck: validateAckSuccess, faultyAck: default, payloadMessage: default);
                    }

                    default:
                        throw new ArgumentException($"Unknown {nameof(SessionControlMessage)}: {control}");
                }
            }
            else
            {
                ReceivePayloadMessageResult receivePayloadMessageResult = HandleReceivePayloadMessage(state, message);
                return new ReceiveResult(ReceiveResult.ResultType.ReceivedPayloadMessage, validAck: default, faultyAck: default, payloadMessage: receivePayloadMessageResult);
            }
        }

        /// <summary>
        /// Drop sent payload messages from the front of the <see cref="SessionParticipantState.RememberedSent"/>
        /// queue until its count is at most <paramref name="limit"/>.
        /// Note that this may result in failure to resume the session if the other participant does not receive
        /// the dropped messages (as we will be unable to send them at session resumption).
        /// </summary>
        /// <param name="state">Our state whose queue should be limited</param>
        /// <param name="limit">The desired count limit for the queue</param>
        /// <returns>The number of messages newly forgotten</returns>
        public static int LimitRememberedSentQueue(SessionParticipantState state, int limit)
        {
            MetaDebug.Assert(limit >= 0, "Limit cannot be negative");

            int numToForget = System.Math.Max(0, state.RememberedSent.Count - limit);

            for (int i = 0; i < numToForget; i++)
                ForgetSentMessage(state);

            return numToForget;
        }

        /// <summary>
        /// Validate an acknowledgement from the other participant.
        /// This does only does checks and does not modify the state.
        /// </summary>
        /// <param name="our">Our session participant state</param>
        /// <param name="their">The acknowledgement sent by the other participant</param>
        /// <param name="ackSuccess">the success result</param>
        /// <returns>Failure if any, otherwise <paramref name="ackSuccess"/> is set</returns>
        static ValidateAckResult.Failure ValidateAck(SessionParticipantState our, SessionAcknowledgement their, out ValidateAckResult.Success ackSuccess)
        {
            ackSuccess = default;

            // Check: They can't have received more messages than we've sent
            if (their.NumReceived > our.NumSent)
                return new ValidateAckResult.Failure.TheirNumReceivedTooHigh(theirNumReceived: their.NumReceived, ourNumSent: our.NumSent);

            // Check: They must have received at least as many messages as they previously reported
            if (their.NumReceived < our.NumAcknowledgedSent)
                return new ValidateAckResult.Failure.TheirNumReceivedTooLow(theirNumReceived: their.NumReceived, oldNumAcknowledgedByThem: our.NumAcknowledgedSent);

            int numForgottenByUs    = our.NumSent - our.RememberedSent.Count;
            // \note their.NumReceived may be lower than numForgottenByUs due to queue limiting
            int ourNumToNewlyForget = System.Math.Max(0, their.NumReceived - numForgottenByUs);

            // Check: their checksum of received messages must match our checksum of sent forgotten (after this ack) messages
            //        (if the acknowledgement is up to date with our queue)
            if (EnableMessagingChecksums)
            {
                // \note Cannot do checksum check if we have forgotten more than client has received, because
                //       if that is the case, then our checksum is more progressed than that sent by client,
                //       and we cannot roll ours back.
                if (numForgottenByUs <= their.NumReceived)
                {
                    uint ourChecksum = our.ChecksumForForgottenSent;

                    foreach (MetaMessage message in our.RememberedSent.Take(ourNumToNewlyForget))
                        ourChecksum = ComputeUpdatedMessagingChecksum(ourChecksum, message);

                    if (ourChecksum != their.ChecksumForReceived)
                        return new ValidateAckResult.Failure.ChecksumMismatch(ourChecksum: ourChecksum, theirChecksum: their.ChecksumForReceived);
                }
            }

            ackSuccess = new ValidateAckResult.Success(
                ourNumToNewlyForget:    ourNumToNewlyForget,
                theirNumReceived:       their.NumReceived);
            return null;
        }

        /// <summary>
        /// Apply an acknowledgement from the other participant, according to a successful validation of the acknowledgement.
        /// In particular, this will remove the acknowledged messages from our outgoing queue.
        /// </summary>
        /// <param name="our">Our state to mutate</param>
        /// <param name="success">A successful result from <see cref="ValidateAckResult"/></param>
        static void ApplyAck(SessionParticipantState our, ValidateAckResult.Success success)
        {
            // Forget newly-acknowledged still-remembered-by-us messages
            for (int i = 0; i < success.OurNumToNewlyForget; i++)
                ForgetSentMessage(our);

            // Remember their acknowledged count
            our.NumAcknowledgedSent = success.TheirNumReceived;
        }

        /// <summary>
        /// Mutate <paramref name="state"/>, reflecting the reception of a payload message from the
        /// other session participant.
        /// </summary>
        /// <param name="state">Our session participant state to be mutated</param>
        /// <param name="payloadMessage">The payload message received</param>
        /// <returns></returns>
        static ReceivePayloadMessageResult HandleReceivePayloadMessage(SessionParticipantState state, MetaMessage payloadMessage)
        {
            if (EnableMessagingChecksums)
                state.ChecksumForReceived = ComputeUpdatedMessagingChecksum(state.ChecksumForReceived, payloadMessage);

            int payloadMessageIndex = state.NumReceived;

            state.NumReceived++;

            // Acknowledgements are sent every N received payload messages.
            // \todo [nuutti] This might not be the ideal way of acknowledging? #session #sessionacknowledgements
            bool shouldSendAcknowledgement;
            if (state.NumReceived >= state.AcknowledgedNumReceived + AcknowledgementMessageInterval)
            {
                shouldSendAcknowledgement = true;
                state.AcknowledgedNumReceived = state.NumReceived;
            }
            else
                shouldSendAcknowledgement = false;

            return new ReceivePayloadMessageResult(payloadMessageIndex, shouldSendAcknowledgement);
        }

        static void ForgetSentMessage(SessionParticipantState state)
        {
            MetaMessage message = state.RememberedSent.Dequeue();

            if (EnableMessagingChecksums)
                state.ChecksumForForgottenSent = ComputeUpdatedMessagingChecksum(state.ChecksumForForgottenSent, message);
        }

        static uint ComputeUpdatedMessagingChecksum(uint oldChecksum, MetaMessage message)
        {
            MetaDebug.Assert(EnableMessagingChecksums, "ComputeUpdatedMessagingChecksum was called but EnableMessagingChecksums is false");

            // \note Very garbage-producing, should only be used in local development
            byte[] buffer = MetaSerialization.SerializeTagged(message, MetaSerializationFlags.SendOverNetwork, logicVersion: null); // \note Logic version omitted, this is just a dev feature
            return MurmurHash.MurmurHash2(buffer, oldChecksum);
        }

        /// <summary>Describes the result of a session resumption attempt</summary>
        public abstract class ResumeResult
        {
            public abstract class Failure : ResumeResult
            {
                public class WeHaveNoSession : Failure { }
                public class TokenMismatch : Failure
                {
                    public SessionToken OurToken    { get; }
                    public SessionToken TheirToken  { get; }
                    public TokenMismatch (SessionToken ourToken, SessionToken theirToken)
                    {
                        OurToken    = ourToken;
                        TheirToken  = theirToken;
                    }
                }
                public class ValidateAckFailure : Failure
                {
                    public ValidateAckResult.Failure Value { get; }
                    public ValidateAckFailure (ValidateAckResult.Failure value)
                    {
                        Value = value;
                    }
                }
                public class WeHaveForgottenTooMany : Failure
                {
                    public int OurNumSent           { get; }
                    public int OurNumRememberedSent { get; }
                    public int TheirNumReceived     { get; }
                    public WeHaveForgottenTooMany (int ourNumSent, int ourNumRememberedSent, int theirNumReceived)
                    {
                        OurNumSent              = ourNumSent;
                        OurNumRememberedSent    = ourNumRememberedSent;
                        TheirNumReceived        = theirNumReceived;
                    }
                }
            }

            public class Success : ResumeResult
            {
                public ValidateAckResult.Success ValidateAckSuccess { get; }
                public Success (ValidateAckResult.Success validateAckSuccess)
                {
                    ValidateAckSuccess = validateAckSuccess;
                }
            }
        }

        /// <summary>Describes the result of receiving a message (either control message or payload message) from the other session participant</summary>
        public struct ReceiveResult
        {
            public enum ResultType
            {
                ReceivedValidAck,
                ReceivedFaultyAck,
                ReceivedPayloadMessage,
            }
            public ResultType Type;
            public ValidateAckResult.Success ValidAck;
            public ValidateAckResult.Failure FaultyAck;
            public ReceivePayloadMessageResult PayloadMessage;

            public ReceiveResult(ResultType type, ValidateAckResult.Success validAck, ValidateAckResult.Failure faultyAck, ReceivePayloadMessageResult payloadMessage)
            {
                Type = type;
                ValidAck = validAck;
                FaultyAck = faultyAck;
                PayloadMessage = payloadMessage;
            }
        }

        /// <summary>Describes the result of handling an acknowledgement message received from the other session participant, or an acknowledgement associated with resuming a session</summary>
        public static class ValidateAckResult
        {
            public abstract class Failure
            {
                public class TheirNumReceivedTooHigh : Failure
                {
                    public int TheirNumReceived     { get; }
                    public int OurNumSent           { get; }
                    public TheirNumReceivedTooHigh (int theirNumReceived, int ourNumSent)
                    {
                        TheirNumReceived    = theirNumReceived;
                        OurNumSent          = ourNumSent;
                    }
                }
                public class TheirNumReceivedTooLow : Failure
                {
                    public int TheirNumReceived         { get; }
                    public int OldNumAcknowledgedByThem { get; }
                    public TheirNumReceivedTooLow (int theirNumReceived, int oldNumAcknowledgedByThem)
                    {
                        TheirNumReceived            = theirNumReceived;
                        OldNumAcknowledgedByThem    = oldNumAcknowledgedByThem;
                    }
                }
                public class ChecksumMismatch : Failure
                {
                    public uint OurChecksum     { get; }
                    public uint TheirChecksum   { get; }
                    public ChecksumMismatch (uint ourChecksum, uint theirChecksum)
                    {
                        OurChecksum     = ourChecksum;
                        TheirChecksum   = theirChecksum;
                    }
                }
            }

            public struct Success
            {
                public int  OurNumToNewlyForget { get; }
                public int  TheirNumReceived    { get; }

                public Success (int ourNumToNewlyForget, int theirNumReceived)
                {
                    OurNumToNewlyForget = ourNumToNewlyForget;
                    TheirNumReceived    = theirNumReceived;
                }
            }
        }

        /// <summary>Describes the result of handling the reception of a payload message from the other session participant</summary>
        public struct ReceivePayloadMessageResult
        {
            public int  PayloadMessageIndex         { get; }
            /// <summary>Whether we should send an acknowledgement to the other session participant</summary>
            public bool ShouldSendAcknowledgement   { get; }

            public ReceivePayloadMessageResult (int payloadMessageIndex, bool shouldSendAcknowledgement)
            {
                PayloadMessageIndex         = payloadMessageIndex;
                ShouldSendAcknowledgement   = shouldSendAcknowledgement;
            }
        }
    }
}
