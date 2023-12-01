// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using static System.FormattableString;

namespace Metaplay.Core.Model
{
    /// <summary>
    /// Represents a position in a Journal timeline.
    ///
    /// <para>
    /// Position is stored in a three-level hierarchy (Tick . Operation . Step).
    /// Tick steps occur during Operation 0 (with steps 0...N)
    /// Actions and their steps occur on Operations [1...N],  (with steps 0...N).
    /// </para>
    /// <para>
    /// From the journal perspective, the steps are atomic and they cannot be observed as ongoing.
    /// The merely "happen" during the immeasurable time between <c>Step N</c> and <c>Step N+1</c>.
    /// Hence a <c>JournalPosition</c> represents both the time instant when <c>Step N</c> is first
    /// entered, and the timespan <c>[Step N, Step N+1[</c>.
    /// </para>
    /// <para>
    /// Note that a position in the journal does not necessarily match a start position
    /// of any action/tick placed on the journal. Each and every action/tick/step
    /// has a start position, but not all created positions have some action/tick/step
    /// on them.
    /// In particular the positions created with JournalPosition.After* and
    /// JournalPosition.Before* represent an abstract point in journal just
    /// before/after the desired point, and hence will not be equal the position
    /// of the concrete action/tick/step just before/after the desired point.
    /// </para>
    /// </summary>
    [System.Diagnostics.DebuggerDisplay("Tick={Tick} Op={Operation} Step={Step}")]
    public struct JournalPosition : IComparable<JournalPosition>, IEquatable<JournalPosition>
    {
        /// <summary>
        /// Origin. Most likely not a position on a timeline.
        /// </summary>
        public static JournalPosition Epoch => new JournalPosition(0, 0, 0);

        /// <summary>
        /// Tick number of the position.
        /// </summary>
        public readonly int Tick;

        /// <summary>
        /// Operation number of the position. 0 for Ticks(), 1+ for Actions.
        /// </summary>
        public readonly short Operation;

        /// <summary>
        /// Action step number of the position.
        ///
        /// <para>
        /// This represents the raising edge of the step, the the <c>JournalPosition</c> conceptually
        /// points to the time at the beginning of the <c>Step</c>.
        /// </para>
        /// </summary>
        public readonly short Step;

        private JournalPosition(int tick, int operation, int step)
        {
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick), "negative value. Such positions do not exist");
            if (operation < 0)
                throw new ArgumentOutOfRangeException(nameof(operation), "negative value. Such positions do not exist");
            if (step < 0)
                throw new ArgumentOutOfRangeException(nameof(step), "negative value. Such positions do not exist");
            if (operation > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(operation), "operation too large");
            if (step > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(step), "step too large");

            Tick = tick;
            Operation = (short)operation;
            Step = (short)step;
        }

        /// <summary>
        /// Constructs JournalPosition from its components.
        /// </summary>
        /// <param name="tick">Tick number.</param>
        /// <param name="operation">Operation number of the position. 0 for Ticks(), 1+ for Actions.</param>
        /// <param name="step">Action step number of the position.</param>
        public static JournalPosition FromTickOperationStep(int tick, int operation, int step)
        {
            return new JournalPosition(tick, operation, step);
        }

        /// <summary>
        /// The position just after all Steps of a Tick, before any subsequent Action, or Tick.
        ///
        /// <para>
        /// Note that this is an abstract point in time and is not equal to the end time of the last concrete Tick step.
        /// </para>
        /// </summary>
        public static JournalPosition AfterTick(int tick)
        {
            return new JournalPosition(tick: tick, operation: 1, step: 0);
        }

        /// <summary>
        /// The position just after all Steps of a Tick, before any subsequent Action, or Tick. The given position must be a
        /// step of a Tick(), otherwise InvalidOperationException is thrown.
        ///
        /// <para>
        /// Note that this is an abstract point in time and is not equal to the end time of the last concrete Tick step.
        /// </para>
        /// </summary>
        public static JournalPosition AfterTick(JournalPosition position)
        {
            if (position.Operation != 0)
                throw new InvalidOperationException("AfterTick called for an action step, needs to be a tick step");
            return new JournalPosition(tick: position.Tick, operation: 1, step: 0);
        }

        /// <summary>
        /// The position just before the first Step of a Tick.
        /// </summary>
        public static JournalPosition BeforeTick(int tick)
        {
            return new JournalPosition(tick: tick, operation: 0, step: 0);
        }

        /// <summary>
        /// The start position of the next Action after the given position.
        /// </summary>
        public static JournalPosition NextTick(JournalPosition position)
        {
            return new JournalPosition(tick: position.Tick + 1, operation: 0, step: 0);
        }

        /// <summary>
        /// The position just after all Steps of an Action, before any subsequent Action, or Tick.
        ///
        /// <para>
        /// Note that this is an abstract point in time and is not equal to the end time of the last concrete Action step.
        /// </para>
        /// </summary>
        public static JournalPosition AfterAction(int tick, int action)
        {
            // \note: action is 0-based. Current Operation = action + 1, Next = action + 2
            return new JournalPosition(tick: tick, operation: action + 2, step: 0);
        }

        /// <summary>
        /// The position just after all Steps of an Action, before any subsequent Action, or Tick. The given position must be a
        /// step of an Action(), otherwise InvalidOperationException is thrown.
        ///
        /// <para>
        /// Note that this is an abstract point in time and is not equal to the end time of the last concrete Action step.
        /// </para>
        /// </summary>
        public static JournalPosition AfterAction(JournalPosition position)
        {
            if (position.Operation == 0)
                throw new InvalidOperationException("AfterAction called for a tick step, need to be an action step");
            return new JournalPosition(tick: position.Tick, operation: position.Operation + 1, step: 0);
        }

        /// <summary>
        /// The position just before the first Step of an Action.
        /// </summary>
        public static JournalPosition BeforeAction(int tick, int action)
        {
            // \note: action is 0-based. Current Operation = action + 1
            return new JournalPosition(tick: tick, operation: action + 1, step: 0);
        }

        /// <summary>
        /// The start position of the next Action after the given position.
        /// </summary>
        public static JournalPosition NextAction(JournalPosition position)
        {
            return new JournalPosition(tick: position.Tick, operation: position.Operation + 1, step: 0);
        }

        /// <summary>
        /// The position just after a Step.
        /// </summary>
        public static JournalPosition AfterStep(JournalPosition position)
        {
            return new JournalPosition(tick: position.Tick, operation: position.Operation, step: position.Step + 1);
        }

        /// <summary>
        /// The least position greater than the given position.
        /// </summary>
        public static JournalPosition NextAfter(JournalPosition position)
        {
            return AfterStep(position);
        }

        private ulong ToULong()
        {
            return (((ulong)(long)Tick) << 32) | (((ulong)(long)Operation) << 16) | ((ulong)(long)Step);
        }
        public static bool operator ==(JournalPosition v1, JournalPosition v2) { return v1.ToULong() == v2.ToULong(); }
        public static bool operator !=(JournalPosition v1, JournalPosition v2) { return v1.ToULong() != v2.ToULong(); }
        public static bool operator <(JournalPosition v1, JournalPosition v2) { return v1.ToULong() < v2.ToULong(); }
        public static bool operator <=(JournalPosition v1, JournalPosition v2) { return v1.ToULong() <= v2.ToULong(); }
        public static bool operator >(JournalPosition v1, JournalPosition v2) { return v1.ToULong() > v2.ToULong(); }
        public static bool operator >=(JournalPosition v1, JournalPosition v2) { return v1.ToULong() >= v2.ToULong(); }

        public override bool Equals(object obj) => (obj is JournalPosition position) && (position == this);
        public override int GetHashCode() => ToULong().GetHashCode();
        public int CompareTo(JournalPosition other) => ToULong().CompareTo(other.ToULong());
        public bool Equals(JournalPosition position) => (position == this);

        public override string ToString() => Invariant($"<Tick={Tick} Op={Operation} Step={Step}>");
    };
}
