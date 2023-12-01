// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Diagnostics;
using System.Linq;
using static System.FormattableString;

namespace Metaplay.Core
{
#if NETCOREAPP // cloud
    /// <summary>
    /// Super simple benchmarking class for ad-hoc tests.
    /// </summary>
    public class TinyBenchmark
    {
        public static void Execute(string name, int numRepeats, Action action)
        {
            const int NumIters = 51;
            TimeSpan[] iterElapsed = new TimeSpan[NumIters];

            // Prewarm
            action();

            // Execute operation NumIters times and get fastest result
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int iter = 0; iter < NumIters; iter++)
            {
                Stopwatch sw = Stopwatch.StartNew();

                for (int rep = 0; rep < numRepeats; rep++)
                    action();

                iterElapsed[iter] = sw.Elapsed;
            }
            long totalAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            // Take median time elapsed
            Array.Sort(iterElapsed);
            double elapsedMS = iterElapsed.Skip(NumIters / 4).Take(NumIters / 2).Average(elapsed => elapsed.TotalMilliseconds / numRepeats);
            long bytesAllocated = (long)((double)totalAllocated / (NumIters * numRepeats));
            Console.WriteLine("{0}: elapsed = {1}, allocated = {2} bytes", name, PrettyPrintElapsed(elapsedMS), bytesAllocated);
        }

        static string PrettyPrintElapsed(double elapsedMS)
        {
            if (elapsedMS >= 1000.0)
                return Invariant($"{elapsedMS / 1000.0:0.00}s");
            else if (elapsedMS >= 1.0)
                return Invariant($"{elapsedMS:0.00}ms");
            else
                return Invariant($"{elapsedMS * 1000.0:0.00}us");
        }
    }
#endif
}
