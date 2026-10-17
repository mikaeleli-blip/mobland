using System.Collections.Generic;

namespace Mobland.Core
{
    /// <summary>Deterministic RNG so a seed always produces the same city.</summary>
    public sealed class SeededRandom
    {
        readonly System.Random rng;

        public SeededRandom(int seed) { rng = new System.Random(seed); }

        public float Value => (float)rng.NextDouble();

        public float Range(float min, float max) => min + (max - min) * (float)rng.NextDouble();

        /// <summary>Integer in [min, max) like UnityEngine.Random.Range.</summary>
        public int Range(int min, int max) => max <= min ? min : rng.Next(min, max);

        /// <summary>Integer in [min, max].</summary>
        public int RangeInclusive(int min, int max) => max <= min ? min : rng.Next(min, max + 1);

        public bool Chance(float p) => rng.NextDouble() < p;

        public T Pick<T>(IList<T> list) => list[rng.Next(list.Count)];

        public int NextSeed() => rng.Next();

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
