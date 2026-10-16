using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static class AttackInputChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run(Action<bool, string> check)
        {
            MatchRunner savedRunner = MatchRunner.Instance;
            Selection savedSelection = Selection.Instance;
            Orders savedOrders = Orders.Instance;
            var root = new GameObject("attack input check");
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
                selection.SelectOnly(3);
                CardSlot attack = orders.Card()[4];
                Require(attack.Type == CommandType.Attack && CommandCard.Shortcut(attack, 4) == KeyCode.A,
                    "fighter attack card is not bound to A");
                CardSlot produce = CommandCard.Building(runner.World, 0, Role.Production, 0, 0)[4];
                Require(produce.Type == CommandType.Produce && CommandCard.Shortcut(produce, 4) == KeyCode.F,
                    "attack shortcut displaced building production F");

                var pending = (List<Command>)typeof(LockstepSession).GetField("pending", Private).GetValue(runner.Session);
                Vector2 ground = new Vector2(20, 10);
                Require(runner.EntityAt(ground, 1.1f, false) < 0, "ground fixture overlaps an entity");
                FireAndCheck(orders, pending, attack, ground, CommandType.AttackMove, 0, MatchRunner.ToSim(ground));
                FireAndCheck(orders, pending, attack, runner.DrawPosition(9), CommandType.Attack, 9, FixVec2.Zero);
                Vector2 friendly = runner.DrawPosition(1);
                FireAndCheck(orders, pending, attack, friendly, CommandType.AttackMove, 0, MatchRunner.ToSim(friendly));
                Vector2 resource = runner.DrawPosition(5);
                FireAndCheck(orders, pending, attack, resource, CommandType.AttackMove, 0, MatchRunner.ToSim(resource));

                int beforeCancel = pending.Count;
                orders.Run(attack);
                typeof(Orders).GetMethod("Cancel", Private).Invoke(orders, null);
                Require(orders.Pending == CommandType.None && pending.Count == beforeCancel,
                    "cancelling attack aim emitted a command or kept it armed");
                check(true, "A arms attack; hostile click attacks; ground/friendly/resource clicks attack-move; building F and aim cancellation remain intact");
            }
            catch (Exception error) { check(false, "attack input: " + error); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                RestoreInstance(typeof(MatchRunner), savedRunner);
                RestoreInstance(typeof(Selection), savedSelection);
                RestoreInstance(typeof(Orders), savedOrders);
            }
        }

        private static void FireAndCheck(Orders orders, List<Command> pending, CardSlot attack,
            Vector2 point, CommandType expected, int arg, FixVec2 target)
        {
            int before = pending.Count;
            orders.Run(attack);
            Require(orders.Pending == CommandType.Attack && pending.Count == before, "A issued before the target click");
            typeof(Orders).GetMethod("Fire", Private).Invoke(orders, new object[] { point });
            Require(pending.Count == before + 1, "target click did not emit exactly one command");
            Command command = pending[pending.Count - 1];
            Require(command.PeerId == 0 && command.EntityId == 3 && command.Type == expected &&
                command.Arg == arg && command.Target.Equals(target), "incorrect " + expected + " command target or argument");
            Require(orders.Pending == CommandType.None, "target click did not disarm attack");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        private static void RestoreInstance(Type type, object value) =>
            type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetSetMethod(true).Invoke(null, new[] { value });
    }
}
