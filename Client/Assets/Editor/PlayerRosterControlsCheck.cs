using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WordCraft.Sim;
using WordCraft.Net;

namespace WordCraft.View
{
    public static class PlayerRosterControlsCheck
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        public static void Run(Action<bool, string> check)
        {
            foreach (Faction faction in Enum.GetValues(typeof(Faction)))
            {
                var found = new HashSet<int>();
                int pages = CommandCard.PageCount(CardKind.BuildMenu, faction, Role.Worker, 0);
                for (int page = 0; page < pages; page++)
                {
                    CardSlot[] card = CommandCard.BuildMenu(faction, page);
                    check(card.Length == CommandCard.Keys.Length, "build card key count");
                    check(card[11].More == (pages > 1), "build More cell missing");
                    foreach (CardSlot slot in card)
                        if (slot.Type == CommandType.Build)
                            check(found.Add(Command.RosterArg(slot.Produce, slot.Slot)), "duplicate building page entry");
                }
                foreach (Role role in Enum.GetValues(typeof(Role)))
                    if (FactionData.IsBuilding(role))
                        for (int slot = 0; slot < FactionData.SlotCount(faction, role); slot++)
                            if (FactionData.Has(faction, role, slot))
                                check(found.Contains(Command.RosterArg(role, slot)), "unreachable building " + faction + role + slot);
            }
            for (int i = 0; i < FactionData.GrowthTargetCount; i++)
            {
                var row = FactionData.GrowthTarget(i);
                var list = CommandCard.GrowthEntries(row.Faction, row.Cost.FromRole, row.Cost.FromSlot);
                check(list.Exists(x => x.Produce == row.Role && x.Slot == row.Slot), "missing growth target");
                foreach (var entry in list)
                {
                    var cost = FactionData.Growth(row.Faction, entry.Produce, entry.Slot);
                    check(cost.FromRole == row.Cost.FromRole && cost.FromSlot == row.Cost.FromSlot, "growth source mismatch");
                }
            }
            var go = new GameObject("player roster checks");
            var runner = go.AddComponent<MatchRunner>();
            var selection = go.AddComponent<Selection>();
            var orders = go.AddComponent<Orders>();
            var camera = go.AddComponent<CameraRig>();
            try
            {
                runner.StartSolo(Faction.TreeSpirits); runner.Session.Update(0);
                check((bool)Call(camera, "SyncMatchHome", runner), "new match camera did not home");
                Vector2 home = camera.Frame;
                bool atBase = false;
                for (int i = 0; i < runner.World.EntityCount; i++)
                {
                    Entity e = runner.World.GetEntity(i);
                    if (e.Owner == 0 && e.Role == Role.Base && e.Alive)
                        atBase |= home == MatchRunner.ToView(e.Position);
                }
                check(atBase, "new match camera did not center local base");
                Vector2 pan = home + new Vector2(3, 4);
                camera.CenterOn(pan);
                check(!(bool)Call(camera, "SyncMatchHome", runner) &&
                    (Vector2)typeof(CameraRig).GetField("frame", Private).GetValue(camera) == pan,
                    "camera reset interrupted player pan in same world");
                typeof(Selection).GetField("runner", Private).SetValue(selection, runner);
                typeof(Orders).GetField("runner", Private).SetValue(orders, runner);
                typeof(Orders).GetField("selection", Private).SetValue(orders, selection);
                int worker = -1, building = -1;
                for (int i = 0; i < runner.World.EntityCount; i++)
                {
                    Entity e = runner.World.GetEntity(i);
                    if (e.Owner != 0) continue;
                    if (e.Kind == EntityKind.Worker) worker = i;
                    if (e.Kind == EntityKind.Building) building = i;
                }
                selection.SelectOnly(worker);
                CardSlot[] unitCard = orders.Card();
                check(unitCard[6].Type == CommandType.Grow && unitCard[8].Type == CommandType.Build, "Grow overwrote Build or has no hotkey");
                orders.Run(unitCard[6]);
                check(orders.GrowMenuOpen, "Grow did not open");
                CardSlot[] growth = orders.Card();
                check(Array.Exists(growth, x => x.Type == CommandType.Grow && x.Locked), "missing growth requirement lock");
                Call(orders, "Cancel");
                check(!orders.GrowMenuOpen, "cancel left growth menu open");
                orders.Run(unitCard[8]);
                check(orders.BuildMenuOpen, "Build did not open");
                if (orders.Card()[11].More) orders.Run(orders.Card()[11]);
                check((int)typeof(Orders).GetProperty("Page", Private).GetValue(orders) == 1, "build paging did not advance");
                var target = CommandCard.BuildEntries(Faction.TreeSpirits).Find(x => x.Slot > 0);
                orders.Run(target);
                check(orders.Placing == target.Produce && orders.PlacingSlot == target.Slot, "placement lost building entry");
                Call(orders, "Fire", new Vector2(20, 20));
                var pending = (List<Command>)typeof(LockstepSession).GetField("pending", Private).GetValue(runner.Session);
                check(pending.Count > 0 && pending[pending.Count - 1].Arg == Command.RosterArg(target.Produce, target.Slot), "Build wire argument lost slot");
                var grown = CommandCard.GrowthEntries(Faction.TreeSpirits, Role.Worker, 0)[0];
                orders.Run(grown);
                check(pending[pending.Count - 1].Type == CommandType.Grow && pending[pending.Count - 1].EntityId == worker && pending[pending.Count - 1].Arg == Command.RosterArg(grown.Produce, grown.Slot), "Grow wire argument/source incorrect");
                selection.SelectOnly(building);
                Call(selection, "ApplyClick", building, true, false);
                check(selection.Selected.Count == 1 && selection.Selected[0] == building, "building double click cleared selection");
                selection.SelectOnly(worker); orders.Card(); orders.Run(unitCard[8]);
                runner.StartSolo(Faction.TreeSpirits); runner.Session.Update(0);
                check((bool)Call(camera, "SyncMatchHome", runner) && camera.Frame == home,
                    "restarted match did not return camera home");
                orders.Card();
                check(!orders.BuildMenuOpen && !orders.GrowMenuOpen && (int)typeof(Orders).GetProperty("Page", Private).GetValue(orders) == 0 && orders.Pending == CommandType.None, "new world preserved old menu state");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
