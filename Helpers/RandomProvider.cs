using System;
using System.Security.Cryptography;

namespace GPM_driver.Helpers;

public static class RandomProvider
{
    private static int NextSeed() => RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);

    public static Random Shared => Random.Shared;

    public static Random CreateThreadSafeRandom() => new Random(NextSeed());

    public static int Next(int minValue, int maxValue) => Random.Shared.Next(minValue, maxValue);

    public static double NextDouble() => Random.Shared.NextDouble();

    public static TimeSpan NextDelay(TimeSpan min, TimeSpan max)
    {
        if (min > max) throw new ArgumentException("min cannot be greater than max.");
        var range = max - min;
        if (range <= TimeSpan.Zero) return min;
        var fraction = Random.Shared.NextDouble();
        return min + TimeSpan.FromMilliseconds(range.TotalMilliseconds * fraction);
    }
}
