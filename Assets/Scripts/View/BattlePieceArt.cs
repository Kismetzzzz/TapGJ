using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    [CreateAssetMenu(menuName = "TapGJ/棋子美术配置", fileName = "BattlePieceArt")]
    public sealed class BattlePieceArt : ScriptableObject
    {
        [Header("五行棋子；留空时显示元素色块")]
        [InspectorName("金")] public Sprite metal;
        [InspectorName("木")] public Sprite wood;
        [InspectorName("水")] public Sprite water;
        [InspectorName("火")] public Sprite fire;
        [InspectorName("土")] public Sprite earth;

        [Header("复合元素棋子")]
        [InspectorName("烬（木＋火）")] public Sprite ember;
        [InspectorName("熔岩（火＋土）")] public Sprite lava;
        [InspectorName("矿晶（土＋金）")] public Sprite ore;
        [InspectorName("寒钢（金＋水）")] public Sprite coldSteel;
        [InspectorName("灵泉（水＋木）")] public Sprite spiritSpring;

        public Sprite GetSprite(Element element)
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

        public Sprite GetSprite(CompositeKind composite)
        {
            switch (composite)
            {
                case CompositeKind.Ember: return ember;
                case CompositeKind.Lava: return lava;
                case CompositeKind.Ore: return ore;
                case CompositeKind.ColdSteel: return coldSteel;
                case CompositeKind.SpiritSpring: return spiritSpring;
                default: return null;
            }
        }
    }
}
