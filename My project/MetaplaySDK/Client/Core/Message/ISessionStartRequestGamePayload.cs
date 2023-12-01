// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Message
{
    /// <summary>
    /// Game-specific data from client delivered to player actor on session start.
    /// <para>
    /// Usage:
    /// <code>
    /// // Define payload
    /// [MetaSerializableDerived(1)
    /// class MySessionStartRequestGamePayload : ISessionStartRequestGamePayload
    /// {
    ///   [MetaMember(1)] public int MyCustomValue;
    /// }
    ///
    /// // In unity client connection delegate, supply the custom payload.
    /// // Remember to supply MyConnectionDelegate to `MetaplayClient.Init(.. MetaplayClientOptions() { ConnectionDelegate = new MyConnectionDelegate() } `.
    /// class MyConnectionDelegate : IMetaplayConnectionDelegate
    /// {
    ///   ISessionStartRequestGamePayload GetSessionStartRequestPayload() => new MySessionStartRequestGamePayload( ... )
    /// }
    ///
    /// // In bot client, supply the custom payload
    /// protected override ISessionStartRequestGamePayload CreateSessionStartRequestGamePayload() => new MySessionStartRequestGamePayload( ... )
    ///
    /// // Do something with the data on Player in session start
    /// PlayerActor.OnClientSessionHandshakeAsync(PlayerSessionParamsBase sessionParams)
    /// {
    ///     MySessionStartRequestGamePayload myPayload = (MySessionStartRequestGamePayload)sessionParams.GamePayload;
    ///     Log.Debug("client supplied {Value}", myPayload.MyCustomValue);
    /// }
    /// </code>
    /// </para>
    /// </summary>
    [MetaSerializable]
    public interface ISessionStartRequestGamePayload
    {
    }
}
