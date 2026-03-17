#if UNITY_EDITOR
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace aki_lua87.AvatarUtils
{
    public class AvatarModifyProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 0;

        public bool OnPreprocessAvatar(GameObject avatar)
        {
            var mods = avatar.GetComponentsInChildren<AvatarModify>(true);
            Debug.Log($"[AAU] OnPreprocessAvatar: {mods.Length} 個の AvatarModify を検出");

            foreach (var m in mods)
            {
                if (m == null)
                {
                    Debug.LogWarning("[AAU] null コンポーネントをスキップ");
                    continue;
                }

                Debug.Log($"[AAU] Apply() 呼び出し: {m.GetType().Name} (GameObject: {m.gameObject.name})");

                try
                {
                    m.Apply(avatar);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[AAU] {m.GetType().Name}.Apply() で例外発生:\n{e}");
                }

                // Apply後もコンポーネントが残っている場合は削除する
                // (DestroyOnUpload のように Apply 内で GameObject ごと消した場合は null になる)
                if (m != null)
                    Object.DestroyImmediate(m);
            }

            return true;
        }
    }
}
#endif
