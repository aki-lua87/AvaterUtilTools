using UnityEngine;
using VRC.SDKBase;

namespace aki_lua87.AvatarUtils
{
    public abstract class AvatarModify : MonoBehaviour, IEditorOnly
    {
        public abstract void Apply(GameObject avatarRoot);
    }
}
