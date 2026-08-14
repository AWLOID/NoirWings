using System.Security.Cryptography;

namespace IronBrew2;

/// <summary>
/// Supplies independent PRNG instances derived from one job seed.  This avoids
/// timestamp collisions from repeatedly constructing System.Random in a VM build.
/// </summary>
public static class RandomProvider
{
    private static readonly object Gate = new();
    private static Random _root = new(RandomNumberGenerator.GetInt32(1, int.MaxValue));

    public static int Configure(int? requestedSeed)
    {
        var seed = requestedSeed.GetValueOrDefault();
        if (seed <= 0)
        {
            seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        }

        lock (Gate)
        {
            _root = new Random(seed);
        }

        return seed;
    }

    public static Random Create()
    {
        lock (Gate)
        {
            return new Random(_root.Next(1, int.MaxValue));
        }
    }
}

