using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace aki_lua87.AvatarUtils
{
    [AddComponentMenu("aki_lua87/AAU/ColorMaskTextureGenerator")]
    public class ColorMaskTextureGenerator : AvatarModify
    {
        public Texture2D SourceTexture;
        public Color TargetColor = Color.white;
        [Range(0f, 1f)] public float Tolerance = 0.1f;
        public bool MatchIsWhite = true;
        public bool UseGradient = false;

        public Material TargetMaterial;
        public string TargetPropertyName = "_MainTex";

        /// <summary>
        /// テクスチャプロパティに対応するliltoonの有効化トグルを強制的にONにする。
        /// </summary>
        public bool ForceEnableTextureFeature = false;

        private const float ColorNorm = 1.7320508f;

        // liltoonのテクスチャプロパティ → 有効化に必要な (プロパティ名, 値) ペアのリスト
        // プロパティ名はltspass_opaque.shader の実測値に基づく
        private static readonly Dictionary<string, (string prop, float value)[]> LilToonFeatureEnableMap =
            new Dictionary<string, (string, float)[]>
            {
                { "_EmissionMap",       new[] { ("_UseEmission",    1f) } },
                { "_Emission2ndMap",    new[] { ("_UseEmission2nd", 1f) } },
                { "_NormalMap",         new[] { ("_UseBumpMap",     1f) } },
                { "_NormalMap2nd",      new[] { ("_UseBump2ndMap",  1f) } },
                { "_MatCapTex",         new[] { ("_UseMatCap",      1f) } },
                { "_MatCap2ndTex",      new[] { ("_UseMatCap2nd",   1f) } },
                { "_RimColorTex",       new[] { ("_UseRim",         1f) } },
                { "_MainTex2nd",        new[] { ("_UseMain2ndTex",  1f) } },
                { "_MainTex3rd",        new[] { ("_UseMain3rdTex",  1f) } },
                // 影カラーテクスチャ: 影自体のON + カラータイプをテクスチャ(1)に設定
                { "_ShadowColorTex",    new[] { ("_UseShadow", 1f), ("_ShadowColorType", 1f) } },
            };

        public override void Apply(GameObject avatarRoot)
        {
            if (SourceTexture == null || TargetMaterial == null) return;

            // マスクテクスチャを生成してマテリアルを複製（インメモリ、アセット保存不要）
            var mask = GenerateMaskTexture();
            var matClone = Object.Instantiate(TargetMaterial);
            matClone.name = TargetMaterial.name + "_AAU_Override";
            matClone.SetTexture(TargetPropertyName, mask);
            if (ForceEnableTextureFeature)
                TryForceEnableFeature(matClone);

#if UNITY_EDITOR
            // アセットパスによる一致判定（NDMF クローン対策）
            string targetAssetPath = AssetDatabase.GetAssetPath(TargetMaterial);
#endif

            int replacedCount = 0;
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;

                    bool isMatch = mats[i] == TargetMaterial;
#if UNITY_EDITOR
                    if (!isMatch && !string.IsNullOrEmpty(targetAssetPath))
                        isMatch = AssetDatabase.GetAssetPath(mats[i]) == targetAssetPath;
#endif
                    if (isMatch)
                    {
                        mats[i] = matClone;
                        changed = true;
                        replacedCount++;
                    }
                }
                if (changed) renderer.sharedMaterials = mats;
            }

            if (replacedCount > 0)
                Debug.Log($"[AAU] '{TargetMaterial.name}' の '{TargetPropertyName}' を {replacedCount} スロットで上書きしました。", this);
        }

        private void TryForceEnableFeature(Material mat) => ApplyForceEnableToMaterial(mat, TargetPropertyName);


        /// <summary>
        /// 指定テクスチャプロパティに対応する有効化トグルをマテリアルに設定する（エディターからも呼び出し可）。
        /// </summary>
        public static void ApplyForceEnableToMaterial(Material mat, string texPropName)
        {
            if (!LilToonFeatureEnableMap.TryGetValue(texPropName, out var entries)) return;
            foreach (var (prop, value) in entries)
            {
                if (mat.HasProperty(prop))
                    mat.SetFloat(prop, value);
            }
        }

        public Texture2D GenerateMaskTexture()
        {
            int w = SourceTexture.width;
            int h = SourceTexture.height;
            var mask = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] src = ReadPixelsSafe(SourceTexture, w, h);
            Color[] dst = new Color[src.Length];

            for (int i = 0; i < src.Length; i++)
            {
                float d = ColorDist(src[i], TargetColor);
                float val = CalcMaskValue(d);
                if (!MatchIsWhite) val = 1f - val;
                dst[i] = new Color(val, val, val, 1f);
            }

            mask.SetPixels(dst);
            mask.Apply();
            return mask;
        }

        /// <summary>
        /// Read/Write 不要でテクスチャのピクセルを取得する。
        /// Graphics.Blit で RenderTexture にコピーしてから ReadPixels する。
        /// </summary>
        public static Color[] ReadPixelsSafe(Texture2D tex, int targetW, int targetH)
        {
            var rt = RenderTexture.GetTemporary(targetW, targetH, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Graphics.Blit(tex, rt);

            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;

            var tmp = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
            tmp.Apply();

            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = tmp.GetPixels();
            Object.DestroyImmediate(tmp);
            return pixels;
        }

        /// <summary>
        /// 指定テクスチャプロパティに対応するliltoonの有効化プロパティ一覧を返す。
        /// 対応がない場合は false を返す。
        /// </summary>
        public static bool TryGetFeatureEnableInfo(string texPropName, out (string prop, float value)[] entries)
        {
            return LilToonFeatureEnableMap.TryGetValue(texPropName, out entries);
        }

        private float CalcMaskValue(float dist)
        {
            if (UseGradient)
            {
                float t = Tolerance > 0f
                    ? Mathf.Clamp01(1f - dist / Tolerance)
                    : (dist < 0.0001f ? 1f : 0f);
                return Mathf.SmoothStep(0f, 1f, t);
            }
            return dist <= Tolerance ? 1f : 0f;
        }

        private static float ColorDist(Color a, Color b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db) / ColorNorm;
        }
    }
}
