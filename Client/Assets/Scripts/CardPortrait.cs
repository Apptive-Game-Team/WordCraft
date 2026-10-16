using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static class CardPortrait
    {
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D For(Faction faction, CardSlot slot)
        {
            if (slot.Produce == Role.None ||
                (slot.Type != CommandType.Produce && slot.Type != CommandType.Build && slot.Type != CommandType.Grow))
                return null;
            string name = FactionData.Sprite(faction, slot.Produce, slot.Slot);
            if (string.IsNullOrEmpty(name)) return null;
            if (!cache.TryGetValue(name, out Texture2D texture))
            {
                texture = Resources.Load<Texture2D>(MatchView.SpriteFolder + name);
                cache[name] = texture;
            }
            return texture;
        }

        public static string CostLabel(CardSlot slot)
        {
            string label = slot.Label ?? "";
            int split = label.LastIndexOf('\n');
            return split < 0 ? "" : label.Substring(split + 1);
        }
    }
}
