using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    [CreateAssetMenu(menuName = "TapGJ/卡牌卡面配置", fileName = "BattleCardArt")]
    public sealed class BattleCardArt : ScriptableObject
    {
        [Header("拖入整张卡面即可统一替换；留空时显示对应元素占位")]
        [InspectorName("金")] public Sprite metal;
        [InspectorName("木")] public Sprite wood;
        [InspectorName("水")] public Sprite water;
        [InspectorName("火")] public Sprite fire;
        [InspectorName("土")] public Sprite earth;
        [InspectorName("融合")] public Sprite fusion;
        [InspectorName("分解")] public Sprite decompose;

        public Sprite GetFace(CardKind kind, Element element)
        {
            switch (kind)
            {
                case CardKind.ElementCard: return GetFace(element);
                case CardKind.FusionCard: return fusion;
                case CardKind.DecomposeCard: return decompose;
                default: return null;
            }
        }

        public Sprite GetFace(Element element)
        {
            switch (element)
            {
                case Element.Metal: return metal;
                case Element.Wood: return wood;
                case Element.Water: return water;
                case Element.Fire: return fire;
                case Element.Earth: return earth;
                default: return null;
            }
        }

#if UNITY_EDITOR
        void OnValidate() => UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }
}
