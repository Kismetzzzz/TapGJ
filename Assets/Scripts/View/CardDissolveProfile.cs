using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace TapGJ.View
{
    [CreateAssetMenu(menuName = "TapGJ/卡牌消散资源", fileName = "CardDissolve")]
    public sealed class CardDissolveProfile : ScriptableObject
    {
        public GameObject prefab;
        public GameObject normalPrefab;
        public Material compositeMaterial;
        [Tooltip("原包相机的泛光配置；由接入层显式绑定，避免沿用预制体内失效的引用。")]
        public PostProcessProfile postProcessProfile;
        public PostProcessResources postProcessResources;
        [Tooltip("关闭时保留原包配色；开启时仅替换溶解边缘和火星颜色。")]
        public bool tintByElement;
    }
}
