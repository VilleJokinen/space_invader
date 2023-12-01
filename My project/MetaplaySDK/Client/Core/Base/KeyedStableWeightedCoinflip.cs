// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core
{
    /// <summary>
    /// Implements a coinflip, what is
    /// * Keyed, i.e. a set of independent stable functions are implemented, and the desired function is chosen with a key.
    ///   (Comparable to the Key in a Cryptosystem tuple which is used in choosing the encryption function). <br/>
    /// * Stable, i.e. results are stateless and deterministic (pure) and do not change results based on for example supplied weight. <br/>
    /// * Coinflip, i.e. results in a boolean value. <br/>
    /// * Weighted, i.e. distribution can be controlled. <br/>
    /// </summary>
    public static class KeyedStableWeightedCoinflip
    {
        /// <summary>
        /// Flips a coin with the probability of <paramref name="trueWeightPermille"/> per 1000 of it being true.
        /// Key is given in two parts: <paramref name="key"/> which symbolically represents the coin flip function and
        /// <paramref name="rollId"/> which symbolically represents the roll index in that sequence. These are given
        /// separately to avoid accidental correlation in key derivation on callsite. Changing <paramref name="trueWeightPermille"/>
        /// does not affect the roll sequence. In particular, any roll with (key, rollId) which was true for a given probability
        /// will be true for any larger probability. And any false roll will be false with any smaller probability.
        /// </summary>
        public static bool FlipACoin(uint key, uint rollId, int trueWeightPermille = 500)
        {
            uint k;
            k = key ^ (rollId * 2654435761u);
            k ^= k >> 16;
            k *= 0x5bd1e995;
            k ^= k >> 16;
            k *= 0x5bd1e995;
            return (k % 1000u) < trueWeightPermille;
        }
    }
}
