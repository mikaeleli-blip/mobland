using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Mobland.Core
{
    /// <summary>A mesh exported from Blender (Tools/blender) - shared vertices + one index list per material slot.</summary>
    public sealed class ModelData
    {
        public string Name;
        public Vector3[] Positions;
        public Vector3[] Normals;
        public readonly List<(string slot, int[] indices)> Parts = new List<(string, int[])>();
        public Bounds Bounds;
    }

    /// <summary>Loads Resources/Mobland/Models/*.bytes (format 'MBM1', written by Tools/blender/mbl.py).</summary>
    public static class ModelLibrary
    {
        static readonly Dictionary<string, ModelData> cache = new Dictionary<string, ModelData>();

        public static ModelData Get(string name)
        {
            if (cache.TryGetValue(name, out var m)) return m;
            var asset = Resources.Load<TextAsset>("Mobland/Models/" + name);
            if (asset == null)
            {
                Debug.LogWarning($"[Mobland] Model '{name}' not found in Resources/Mobland/Models.");
                cache[name] = null;
                return null;
            }
            m = Parse(name, asset.bytes);
            cache[name] = m;
            return m;
        }

        static ModelData Parse(string name, byte[] bytes)
        {
            using (var br = new BinaryReader(new MemoryStream(bytes)))
            {
                var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (magic != "MBM1") throw new InvalidDataException($"{name}: bad model header");
                int vc = br.ReadInt32();
                var m = new ModelData { Name = name, Positions = new Vector3[vc], Normals = new Vector3[vc] };
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int i = 0; i < vc; i++)
                {
                    var p = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    m.Positions[i] = p;
                    m.Normals[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                m.Bounds = new Bounds((min + max) * 0.5f, max - min);
                int parts = br.ReadInt32();
                for (int k = 0; k < parts; k++)
                {
                    int len = br.ReadInt32();
                    string slot = Encoding.UTF8.GetString(br.ReadBytes(len));
                    int ic = br.ReadInt32();
                    var idx = new int[ic];
                    for (int i = 0; i < ic; i++) idx[i] = br.ReadInt32();
                    m.Parts.Add((slot, idx));
                }
                return m;
            }
        }
    }
}
