// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Runtime.Serialization;

namespace Metaplay.Core.MultiplayerEntity
{
    /// <summary>
    /// Default implementation of <see cref="IModelRuntimeData" /> for <see cref="MultiplayerModelBase{TModel}"/>
    /// </summary>
    public class MultiplayerModelRuntimeDataBase<TModel> : IModelRuntimeData<TModel>
        where TModel : IMultiplayerModel
    {
        readonly ISharedGameConfig  _gameConfig;
        readonly int                _logicVersion;
        readonly LogChannel         _log;

        public MultiplayerModelRuntimeDataBase(TModel instance)
        {
            _gameConfig     = instance.GameConfig;
            _logicVersion   = instance.LogicVersion;
            _log            = instance.Log;
        }

        public virtual void CopyResolversTo(TModel instance)
        {
            instance.GameConfig = _gameConfig;
            instance.LogicVersion = _logicVersion;
        }

        public virtual void CopySideEffectListenersTo(TModel instance)
        {
            instance.Log = _log;
        }
    }

    /// <summary>
    /// Default base class for models implementing <see cref="IMultiplayerModel{TModel}"/>. Inheriting this class
    /// for Multiplayer Entity Models is not required but is recommended.
    /// </summary>
    [MetaReservedMembers(200, 300)]
    public abstract class MultiplayerModelBase<TModel> : IMultiplayerModel<TModel>
        where TModel : MultiplayerModelBase<TModel>
    {
        [IgnoreDataMember] public int                   LogicVersion    { get; set; }
        [IgnoreDataMember] public ISharedGameConfig     GameConfig      { get; set; }
        [IgnoreDataMember] public LogChannel            Log             { get; set; } = LogChannel.Empty;

        [MetaMember(200), Transient] public MetaTime    TimeAtFirstTick { get; private set; }
        [MetaMember(201), Transient] public int         CurrentTick     { get; private set; }
        [MetaMember(202)] public EntityId               EntityId        { get; set; }
        [MetaMember(203)] public MetaTime               CreatedAt       { get; set; }

        [IgnoreDataMember] public abstract int TicksPerSecond { get; }

        public MetaTime CurrentTime => ModelUtil.TimeAtTick(CurrentTick, TimeAtFirstTick, TicksPerSecond);

        IGameConfigDataResolver IModel.GetDataResolver() => GameConfig;
        public virtual IModelRuntimeData<TModel> GetRuntimeData() => new MultiplayerModelRuntimeDataBase<TModel>((TModel)this);

        public void Tick(IChecksumContext checksumCtx)
        {
            CurrentTick += 1;
            OnTick();
        }

        /// <summary>
        /// Ticks the model state one tick forward. That is, progress time by 1sec/<see cref="IMultiplayerModel.TicksPerSecond"/>.
        ///
        /// This method is called <i>after</i> increasing <see cref="IMultiplayerModel.CurrentTick"/> and <see cref="IMultiplayerModel.CurrentTime"/>, i.e. the
        /// time in the method is the current time, but any game logic since the last time have not yet
        /// been executed (and is executed in this method).
        /// </summary>
        public abstract void OnTick();
        public abstract void OnFastForwardTime(MetaDuration elapsedTime);

        public virtual string GetDisplayNameForDashboard() => EntityId.ToString();
        public virtual MultiplayerMemberPrivateStateBase GetMemberPrivateState(EntityId memberId) => null;

        void IMultiplayerModel.ResetTime(MetaTime timeAtFirstTick)
        {
            TimeAtFirstTick = timeAtFirstTick;
            CurrentTick = 0;
        }
    }
}
