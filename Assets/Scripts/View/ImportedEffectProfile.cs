using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    [CreateAssetMenu(menuName = "TapGJ/特效资源", fileName = "ImportedEffects")]
    public sealed class ImportedEffectProfile : ScriptableObject
    {
        public GameObject selection;
        public GameObject burst;
        public GameObject range;

        public static string GroupFor(CompositeKind kind)
        {
            switch (kind)
            {
                case CompositeKind.Ember: return "bofang_muhuo";
                case CompositeKind.Lava: return "bofang_huotu";
                case CompositeKind.Ore: return "bofang_tujin";
                case CompositeKind.ColdSteel: return "bofang_jinshui";
                case CompositeKind.SpiritSpring: return "bofang_shuimu";
                default: return null;
            }
        }
    }
}
