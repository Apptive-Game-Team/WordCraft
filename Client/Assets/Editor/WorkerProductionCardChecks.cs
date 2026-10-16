using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static class WorkerProductionCardChecks
    {
        public static void Run(Action<bool, string> check)
        {
            try
            {
                foreach (Faction faction in Enum.GetValues(typeof(Faction)))
                {
                    World world = MatchScenario.Build(0xC0FFEE, faction, Faction.Hellfire);
                    CardSlot worker = CommandCard.Building(world, 0, Role.Base, 0, 0)[3];
                    Require(worker.Type == CommandType.Produce && worker.Produce == Role.Worker && worker.Slot == 0,
                        faction + " base has no worker button");
                    Require(!worker.Locked && !worker.Maxed && !string.IsNullOrEmpty(worker.Description),
                        faction + " worker button unusable or unexplained");
                    Require(world.CanProduce(0, 0, worker.Produce, worker.Slot), faction + " worker button names an illegal producer");
                    int before = world.EntityCount;
                    int bank = world.GetResources(0);
                    ProductionCost cost = FactionData.Production(faction, Role.Worker, 0);
                    world.Step(new[] { new Command(world.Tick, 0, 0, CommandType.Produce, 0, FixVec2.Zero,
                        Command.RosterArg(worker.Produce, worker.Slot)) });
                    Require(world.GetResources(0) == bank - cost.Resources && world.GetEntity(0).QueueCount == 1,
                        faction + " worker button did not queue a paid worker");
                    for (int tick = 1; tick < cost.Ticks; tick++) world.Step(Array.Empty<Command>());
                    Require(world.EntityCount > before && world.GetEntity(before).Alive &&
                        world.GetEntity(before).Kind == EntityKind.Worker && world.GetEntity(before).Owner == 0,
                        faction + " worker production did not complete");
                    foreach (Role role in Enum.GetValues(typeof(Role)))
                    {
                        if (!FactionData.IsBuilding(role)) continue;
                        for (int slot = 0; slot < FactionData.SlotCount(faction, role); slot++)
                        {
                            if (!FactionData.Has(faction, role, slot)) continue;
                            int pages = CommandCard.PageCount(CardKind.Building, faction, role, slot);
                            bool legal = FactionData.IsProducedAt(faction, Role.Worker, 0, role, slot);
                            for (int page = 0; page < pages; page++)
                            {
                                CardSlot shown = CommandCard.Building(world, 0, role, slot, page)[3];
                                Require((shown.Type == CommandType.Produce && shown.Produce == Role.Worker) == legal,
                                    faction + " worker key visibility disagrees with producer " + role + slot);
                            }
                        }
                    }
                }
                PlayerOrders();
                check(true, "all six base worker cards finish workers; player worker, ground/resource/enemy rally and aimed rally inputs emit correct session commands");
            }
            catch (Exception error) { check(false, "worker production card: " + error); }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static void PlayerOrders()
        {
            MatchRunner savedRunner = MatchRunner.Instance;
            Selection savedSelection = Selection.Instance;
            Orders savedOrders = Orders.Instance;
            var root = new GameObject("worker production input check");
            try
            {
                var runner = root.AddComponent<MatchRunner>();
                var selection = root.AddComponent<Selection>();
                var orders = root.AddComponent<Orders>();
                runner.StartSolo(Faction.TreeSpirits);
                runner.Session.Update(0);
                typeof(Selection).GetField("runner", Private).SetValue(selection, runner);
                typeof(Orders).GetField("runner", Private).SetValue(orders, runner);
                typeof(Orders).GetField("selection", Private).SetValue(orders, selection);
                selection.SelectOnly(0);
                var pending = (List<Command>)typeof(LockstepSession).GetField("pending", Private).GetValue(runner.Session);
                CardSlot worker = orders.Card()[3];
                int count = pending.Count;
                orders.Run(worker);
                CheckLast(pending, count, CommandType.Produce, 0, Command.RosterArg(Role.Worker, 0), FixVec2.Zero);

                Vector2 ground = new Vector2(20, 10);
                Require(runner.EntityAt(ground, 1.1f, false) < 0, "ground rally fixture overlaps an entity");
                Vector2 node = runner.DrawPosition(5);
                Vector2 enemy = runner.DrawPosition(9);
                count = pending.Count;
                orders.RightClick(node);
                CheckLast(pending, count, CommandType.SetRallyPoint, 0, 6, runner.World.GetEntity(5).Position);
                count = pending.Count;
                orders.RightClick(ground);
                CheckLast(pending, count, CommandType.SetRallyPoint, 0, 0, MatchRunner.ToSim(ground));
                count = pending.Count;
                orders.RightClick(enemy);
                CheckLast(pending, count, CommandType.SetRallyPoint, 0, 0, runner.World.GetEntity(9).Position);

                // The R card follows the same target encoding as right-click.
                foreach (Vector2 target in new[] { node, ground, enemy })
                {
                    count = pending.Count;
                    orders.Run(orders.Card()[0]);
                    Require(orders.Pending == CommandType.SetRallyPoint && pending.Count == count, "R did not arm rally without issuing early");
                    typeof(Orders).GetMethod("Fire", Private).Invoke(orders, new object[] { target });
                    bool resource = target == node;
                    CheckLast(pending, count, CommandType.SetRallyPoint, 0, resource ? 6 : 0, MatchRunner.ToSim(target));
                    Require(orders.Pending == CommandType.None, "aimed rally remained armed after click");
                }

                // Producer precedence must not steal existing worker and fighter orders.
                selection.SelectOnly(1);
                count = pending.Count;
                orders.RightClick(node);
                CheckLast(pending, count, CommandType.Gather, 1, 5, FixVec2.Zero);
                selection.SelectOnly(3);
                count = pending.Count;
                orders.RightClick(enemy);
                CheckLast(pending, count, CommandType.Attack, 3, 9, FixVec2.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                RestoreInstance(typeof(MatchRunner), savedRunner);
                RestoreInstance(typeof(Selection), savedSelection);
                RestoreInstance(typeof(Orders), savedOrders);
            }
        }

        private static void CheckLast(List<Command> pending, int before, CommandType type, int entity, int arg, FixVec2 target)
        {
            Require(pending.Count == before + 1, type + " did not issue exactly one command");
            Command command = pending[pending.Count - 1];
            Require(command.Type == type && command.EntityId == entity && command.Arg == arg && command.Target.Equals(target),
                type + " sent incorrect entity, target or argument");
        }

        private static void RestoreInstance(Type type, object value) =>
            type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetSetMethod(true).Invoke(null, new[] { value });
    }
}
