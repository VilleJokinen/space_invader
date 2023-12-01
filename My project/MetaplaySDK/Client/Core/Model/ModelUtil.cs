// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.Model
{
    /// <summary>
    /// Convenience tools for managing Models.
    /// </summary>
    public static class ModelUtil
    {
        /// <summary>
        /// Executes a single Tick on the Model.
        /// </summary>
        public static MetaActionResult RunTick<TModel, TChecksumCtx>(TModel model, TChecksumCtx ctx)
            where TModel : IModel
            where TChecksumCtx : IChecksumContext
        {
            // \todo Try-catch, returning an error result on exception.
            model.Tick(ctx);
            return MetaActionResult.Success;
        }

        /// <summary>
        /// Executes the Action on the Model and returns the action result.
        /// </summary>
        public static MetaActionResult RunAction<TModel, TAction, TChecksumCtx>(TModel model, TAction action, TChecksumCtx ctx)
            where TModel : IModel
            where TAction : ModelAction
            where TChecksumCtx : IChecksumContext
        {
            // \todo Try-catch, returning an error result on exception.
            return action.InvokeExecute(model, commit: true);
        }

        /// <summary>
        /// Dry-Runs the Action on the Model and returns the action result. Dry-running means the action preconditions are checked
        /// but not actual changes are made. This can be used to determine if an action would execute successfully without executing it.
        /// </summary>
        public static MetaActionResult DryRunAction<TModel, TAction>(TModel model, TAction action)
            where TModel : IModel
            where TAction : ModelAction
        {
            // \todo Try-catch, returning an error result on exception.
            return action.InvokeExecute(model, commit: false);
        }

        /// <summary>
        /// Returns the number of whole ticks at a <paramref name="time"/> if a Model started ticking at <paramref name="startTime"/> at the rate of <paramref name="ticksPerSecond"/>.
        /// </summary>
        public static long TotalNumTicksElapsedAt(MetaTime time, MetaTime startTime, int ticksPerSecond)
        {
            return FloorTicksPerDuration(time - startTime, ticksPerSecond);
        }

        /// <summary>
        /// Returns the time when tick <paramref name="tick"/> starts and the tick operation is issued, given the model started ticking at <paramref name="timeAtFirstTick"/> at the rate of <paramref name="ticksPerSecond"/>.
        /// </summary>
        public static MetaTime TimeAtTick(long tick, MetaTime timeAtFirstTick, int ticksPerSecond)
        {
            return timeAtFirstTick + MetaDuration.FromMilliseconds(tick * 1000 / ticksPerSecond);
        }

        /// <summary>
        /// The number of whole ticks in <paramref name="duration"/>, given that the model ticks at the rate of <paramref name="ticksPerSecond"/>.
        /// </summary>
        public static long FloorTicksPerDuration(MetaDuration duration, int ticksPerSecond)
        {
            return duration.Milliseconds * ticksPerSecond / 1000;
        }
    }
}
