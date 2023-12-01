// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core
{
    /// <summary>
    /// The context of the checksum evaluator during Action or Tick.
    /// </summary>
    public interface IChecksumContext
    {
        void Step(string name);
    }

    /// <summary>
    /// Dummy evaluator that does nothing. Useful for example in dry runs.
    /// </summary>
    public class NullChecksumEvaluator : IChecksumContext
    {
        public static readonly NullChecksumEvaluator Context = new NullChecksumEvaluator();

        void IChecksumContext.Step(string name)
        {
        }
    };
}
