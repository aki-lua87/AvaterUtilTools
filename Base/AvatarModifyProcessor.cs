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
            }

            return true;
        }
    }
}
