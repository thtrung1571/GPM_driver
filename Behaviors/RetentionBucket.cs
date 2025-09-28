using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPM_driver.Behaviors
{
    internal enum RetentionBucket
    {
        Bounce,    // 0–10s
        Short,     // 15–45s
        Medium,    // 60–90s
        Long,      // 91–150s
        Deep       // 150–300s
    }

    internal static class RetentionBucketHelper
    {
        // weighted selection: Bounce 15%, Short 30%, Medium 25%, Long 20%, Deep 10%
        private static readonly (RetentionBucket bucket, double weight)[] _weights =
        {
            (RetentionBucket.Bounce, 0.15),
            (RetentionBucket.Short, 0.30),
            (RetentionBucket.Medium, 0.25),
            (RetentionBucket.Long, 0.20),
            (RetentionBucket.Deep, 0.10)
        };

        public static RetentionBucket GetRandomBucket(Random rng)
        {
            double roll = rng.NextDouble();
            double acc = 0;
            foreach (var w in _weights)
            {
                acc += w.weight;
                if (roll <= acc) return w.bucket;
            }
            return _weights[^1].bucket; // fallback
        }

        public static (int MinSeconds, int MaxSeconds) GetDurationRange(RetentionBucket bucket) =>
            bucket switch
            {
                RetentionBucket.Bounce => (0, 10),
                RetentionBucket.Short => (15, 45),
                RetentionBucket.Medium => (60, 90),
                RetentionBucket.Long => (91, 150),
                RetentionBucket.Deep => (150, 300),
                _ => (15, 45)
            };

        public static int PickDurationSeconds(Random rng, RetentionBucket bucket)
        {
            var (minS, maxS) = GetDurationRange(bucket);
            if (minS == maxS) return minS;
            return rng.Next(minS, maxS + 1);
        }
    }
}