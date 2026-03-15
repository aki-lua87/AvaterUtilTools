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

            foreach (var m in mods)
            {
                m.Apply(avatar);
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
