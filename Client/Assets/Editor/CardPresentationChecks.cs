using System;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static class CardPresentationChecks
    {
        public static void Run(Action<bool, string> check)
        {
            foreach (Faction faction in Enum.GetValues(typeof(Faction)))
            {
                World world = MatchScenario.Build(0xC0FFEE, faction, Faction.Hellfire);
                ulong before = world.Hash();
                foreach (Role role in Enum.GetValues(typeof(Role)))
                {
                    if (role == Role.None) continue;
                    for (int i = 0; i < FactionData.SlotCount(faction, role); i++)
                    {
                        if (!FactionData.Has(faction, role, i)) continue;
                        bool unit = role == Role.Worker || role == Role.Melee || role == Role.Ranged || role == Role.Signature;
                        var slot = new CardSlot { Type = unit ? CommandType.Produce : CommandType.Build, Produce = role, Slot = i };
                        string art = FactionData.Sprite(faction, role, i);
                        if (!string.IsNullOrEmpty(art))
                            check(CardPortrait.For(faction, slot) != null, "Missing card portrait: " + faction + "/" + role + "/" + i);
                        if (!unit || FactionData.Production(faction, role, i).Produced)
                        {
                            string detail = CardDetails.Text(world, 0, slot);
                            check(detail.Contains(FactionData.Name(faction, role, i)) && detail.Contains("마나") && detail.Contains("필요 단계"),
                                "Missing roster detail: " + faction + "/" + role + "/" + i);
                            slot.Locked = true;
                            check(CardDetails.Text(world, 0, slot) == detail, "Locked card lost its explanation");
                        }
                    }
                }
                check(world.Hash() == before, "Card presentation mutated " + faction + " simulation");
            }
            check(CardPortrait.For(Faction.TreeSpirits, new CardSlot { Type = CommandType.Build }) == null,
                "Build submenu opener must not look like a specific building");
            check(CardDetails.Text(null, 0, new CardSlot { Label = "Move" }) == "Move", "Command text fallback missing");
            check(CardDetails.Text(null, 0, new CardSlot()) == "", "Empty card has hover text");
            ModelPortraitChecks.Run(check);
        }
    }
}
