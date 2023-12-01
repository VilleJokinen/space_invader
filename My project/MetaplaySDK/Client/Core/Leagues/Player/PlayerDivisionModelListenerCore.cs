// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.League.Player
{
    /// <summary>
    /// Internal SDK listener hooks.
    /// </summary>
    public interface IPlayerDivisionModelClientListenerCore : IDivisionModelClientListenerCore
    {
    }

    /// <summary>
    /// Empty implementation of <see cref="IPlayerDivisionModelClientListenerCore"/>.
    /// </summary>
    public class EmptyPlayerDivisionModelClientListenerCore : IPlayerDivisionModelClientListenerCore
    {
        public static readonly EmptyPlayerDivisionModelClientListenerCore Instance = new EmptyPlayerDivisionModelClientListenerCore();

        /// <inheritdoc />
        public void OnSeasonConcluded() { }
    }

    /// <summary>
    /// Internal SDK listener hooks.
    /// </summary>
    public interface IPlayerDivisionModelServerListenerCore : IDivisionModelServerListenerCore
    {
    }

    /// <summary>
    /// Empty implementation of <see cref="IPlayerDivisionModelServerListenerCore"/>.
    /// </summary>
    public class EmptyPlayerDivisionModelServerListenerCore : IPlayerDivisionModelServerListenerCore
    {
        public static readonly EmptyPlayerDivisionModelServerListenerCore Instance = new EmptyPlayerDivisionModelServerListenerCore();

        /// <inheritdoc />
        public void OnSeasonDebugConcluded() { }

        /// <inheritdoc />
        public void OnSeasonDebugEnded() { }
    }
}
