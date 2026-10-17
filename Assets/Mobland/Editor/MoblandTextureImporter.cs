using UnityEditor;
using UnityEngine;

namespace Mobland.EditorTools
{
    /// <summary>Import settings for generated textures, based on their suffix (_n normal, _m mask, _e emission).</summary>
    sealed class MoblandTextureImporter : AssetPostprocessor
    {
        public const string TexFolder = "Assets/Mobland/Resources/Mobland/Tex/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TexFolder)) return;
            var ti = (TextureImporter)assetImporter;
            Configure(ti, assetPath);
        }

        public static bool Configure(TextureImporter ti, string path)
        {
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            bool changed = false;

            void Set<T>(ref T field, T value, System.Action<T> apply)
            {
                if (!Equals(field, value)) { apply(value); changed = true; }
            }

            TextureImporterType type = file.EndsWith("_n") ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool srgb = !(file.EndsWith("_n") || file.EndsWith("_m"));
            bool alpha = file.EndsWith("_m") || file == "smoke";

            var t = ti.textureType; Set(ref t, type, v => ti.textureType = v);
            var s = ti.sRGBTexture; Set(ref s, srgb, v => ti.sRGBTexture = v);
            var w = ti.wrapMode; Set(ref w, file == "smoke" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat, v => ti.wrapMode = v);
            var a = ti.anisoLevel; Set(ref a, 8, v => ti.anisoLevel = v);
            var mm = ti.mipmapEnabled; Set(ref mm, true, v => ti.mipmapEnabled = v);
            var ms = ti.maxTextureSize; Set(ref ms, 2048, v => ti.maxTextureSize = v);
            var at = ti.alphaSource;
            Set(ref at, alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None, v => ti.alphaSource = v);
            var fm = ti.filterMode; Set(ref fm, FilterMode.Trilinear, v => ti.filterMode = v);
            return changed;
        }
    }
}
