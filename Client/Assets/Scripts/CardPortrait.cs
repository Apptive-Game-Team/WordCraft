using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static class CardPortrait
    {
        public static Texture2D For(Faction faction, CardSlot slot)
        {
            if (slot.Produce == Role.None ||
                (slot.Type != CommandType.Produce && slot.Type != CommandType.Build && slot.Type != CommandType.Grow))
                return null;
            return ModelPortrait.Get(faction, slot.Produce, slot.Slot);
        }

        public static string CostLabel(CardSlot slot)
        {
            string label = slot.Label ?? "";
            int split = label.LastIndexOf('\n');
            return split < 0 ? "" : label.Substring(split + 1);
        }
    }
}
