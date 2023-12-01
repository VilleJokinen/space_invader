// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Message
{
    /// <summary>
    /// Game-specific data delivered from server to client on session start.
    /// <para>
    /// Usage:
    /// <code>
    /// // Define payload
    /// [MetaSerializableDerived(1)
    /// class MySessionStartSuccessGamePayload : ISessionStartSuccessGamePayload
    /// {
    ///   [MetaMember(1)] public int MyCustomValue;
    /// }
    ///
    /// // Create data in session actor
    /// class SessionActor
    /// {
    ///    protected override ISessionStartSuccessGamePayload GameGetSessionStartPayload(SessionStartParams sessionStart) => new MySessionStartSuccessGamePayload( ... );
    /// }
    ///
    /// // On client, in SessionStartSuccess handler access the data
    /// void Handle(SessionStartSuccess success)
    /// {
    ///     MySessionStartSuccessGamePayload myPayload = (MySessionStartSuccessGamePayload)success.GamePayload;
    ///     Log.Debug("server supplied {Value}", myPayload.MyCustomValue);
    /// }
    /// </code>
    /// </para>
    /// </summary>
    [MetaSerializable]
    public interface ISessionStartSuccessGamePayload
    {
    }
}
