using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;

namespace GPM_driver.Behaviors.Utils
{
    internal static class TimingDistributions
    {
        // Box-Muller for normal distribution (returns double)
        public static double NextNormal(Random rng, double mean = 0.0, double stdDev = 1.0)
        {
            // generate two uniform(0,1) numbers
            double u1 = 1.0 - rng.NextDouble(); // avoid 0
            double u2 = 1.0 - rng.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                                   Math.Sin(2.0 * Math.PI * u2); // random normal(0,1)
            return mean + stdDev * randStdNormal;
        }

        // Exponential distribution, lambda = rate
        public static double NextExponential(Random rng, double lambda = 1.0)
        {
            double u = rng.NextDouble();
            return -Math.Log(1 - u) / lambda;
        }

        // Helper that returns int milliseconds clipped to min/max
        public static int NormalMs(Random rng, int meanMs, int stdDevMs, int minMs = 50, int maxMs = 10000)
        {
            double val = NextNormal(rng, meanMs, stdDevMs);
            int v = (int)Math.Round(val);
            if (v < minMs) v = minMs;
            if (v > maxMs) v = maxMs;
            return v;
        }

        public static int ExponentialMs(Random rng, double lambda, int minMs = 50, int maxMs = 10000)
        {
            double val = NextExponential(rng, lambda) * 1000.0; // convert seconds to ms
            int v = (int)Math.Round(val);
            if (v < minMs) v = minMs;
            if (v > maxMs) v = maxMs;
            return v;
        }
    }
}