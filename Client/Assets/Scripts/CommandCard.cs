using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Which card a selection shows. Fighters beat workers beat buildings.
    /// BuildMenu is not a selection at all: it is the worker card's submenu, and it
    /// ranks nowhere because Representative never returns it.
    /// </summary>
    public enum CardKind
    {
        None = 0,
        Fighter = 1,
        Worker = 2,
        Building = 3,
        BuildMenu = 4,
    }

    /// <summary>One cell of the card. Type None is an empty cell.</summary>
    public struct CardSlot
    {
        public string Label;
        public CommandType Type;

        /// <summary>
        /// Command argument. Produce reads it as the unit role, Build as the
        /// building role; everything else ignores it. Role.None on a Build is the
        /// cell that opens the submenu rather than placing anything.
        /// </summary>
        public Role Produce;
    }

    /// <summary>
    /// The command card layout, and the only place it is decided.
    ///
    /// Position is the shortcut. Cell n always carries Keys[n], the key is drawn
    /// on the button, and a command keeps its cell for a given selection kind, so
    /// the hand learns the position and stops looking at the screen. Retuning the
    /// feel means editing the two tables below and nothing else.
    /// </summary>
    public static class CommandCard
    {
        public const int Cols = 3;
        public const int Rows = 3;
        public const int Cells = Cols * Rows;

        /// <summary>
        /// Cell to key. Not QWE/ASD/ZXC, which would be the obvious block, because
        /// the camera keeps WASD (roadmap 3-5) and a pan key that also fires a
        /// command is worse than an unfamiliar block. RTY/FGH/VBN is the nearest
        /// contiguous 3x3 that leaves WASD alone.
        /// </summary>
        public static readonly KeyCode[] Keys =
        {
            KeyCode.R, KeyCode.T, KeyCode.Y,
            KeyCode.F, KeyCode.G, KeyCode.H,
            KeyCode.V, KeyCode.B, KeyCode.N,
        };

        // The column convention every card below obeys, so a cell means the same
        // thing whatever is selected:
        //   cell 0  point at the ground   Move, or where produced units walk to
        //   cell 1  cancel                Stop, or take one off the queue
        //   cell 2  stand and shoot
        //   cell 3  name a victim
        //   cell 4  walk and shoot
        //   cells 6-8 (bottom row)        what this thing makes

        /// <summary>Declared before the tables: static initializers run in textual order.</summary>
        private static readonly CardSlot Empty = new CardSlot();

        private static readonly CardSlot[] fighter =
        {
            Cmd("Move", CommandType.Move), Cmd("Stop", CommandType.Stop), Cmd("Hold", CommandType.HoldPosition),
            Cmd("Attack", CommandType.Attack), Cmd("A-Move", CommandType.AttackMove), Empty,
            Empty, Empty, Empty,
        };

        private static readonly CardSlot[] worker =
        {
            Cmd("Move", CommandType.Move), Cmd("Stop", CommandType.Stop), Empty,
            Empty, Empty, Empty,
            Cmd("Build", CommandType.Build), Empty, Empty,
        };

        /// <summary>
        /// What a Build may name, in the order the submenu lists them. Ascending
        /// Role, so a building keeps its cell whatever the faction and the hand
        /// learns one layout.
        /// </summary>
        private static readonly Role[] buildings =
        {
            Role.Base, Role.Production, Role.Defense, Role.Supply, Role.Tech
        };

        /// <summary>
        /// The build submenu. Filled when the menu opens, because which buildings a
        /// faction lists is roster data and Of() has only the kind to go on. One
        /// array, so the card the keys fire is the card the HUD draws.
        /// </summary>
        private static readonly CardSlot[] buildMenu = new CardSlot[Cells];

        /// <summary>What a building may produce, in the order the bottom row lists them.</summary>
        private static readonly Role[] producible = { Role.Melee, Role.Ranged, Role.Signature };

        /// <summary>
        /// Filled per faction, like the build submenu. A cell reading "Melee" says
        /// nothing about what walks out of the building; the roster name and the
        /// price do, and they are the two things a player decides on.
        /// </summary>
        private static readonly CardSlot[] building = new CardSlot[Cells];

        private static readonly CardSlot[] none = new CardSlot[Cells];

        /// <summary>
        /// The card for this selection kind, laid out for this faction. Always
        /// Cells long. The HUD and the keys both call this, so what a key fires is
        /// always what the button under it says.
        /// </summary>
        public static CardSlot[] Of(CardKind kind, Faction faction)
        {
            switch (kind)
            {
                case CardKind.Fighter: return fighter;
                case CardKind.Worker: return worker;
                case CardKind.Building: return Building(faction);
                case CardKind.BuildMenu: return buildMenu;
                default: return none;
            }
        }

        /// <summary>
        /// What this faction's buildings can produce, named and priced. Roles the
        /// faction does not field are left blank rather than shown and refused —
        /// the humans have no melee unit at all, by design.
        /// </summary>
        public static CardSlot[] Building(Faction faction)
        {
            for (int i = 0; i < Cells; i++) building[i] = Empty;
            building[0] = Cmd("Rally", CommandType.SetRallyPoint);
            building[1] = Cmd("Cancel", CommandType.CancelProduction);

            int cell = Cols * 2; // bottom row: what this thing makes
            for (int i = 0; i < producible.Length && cell < Cells; i++)
            {
                Role role = producible[i];
                if (!FactionData.Has(faction, role)) { cell++; continue; }
                building[cell++] = new CardSlot
                {
                    Label = FactionData.Name(faction, role) + "\n" + World.ProduceCost,
                    Type = CommandType.Produce,
                    Produce = role,
                };
            }
            return building;
        }

        /// <summary>What the card is for, drawn over it so the player is never guessing.</summary>
        public static string Title(CardKind kind)
        {
            switch (kind)
            {
                case CardKind.Fighter: return "orders";
                case CardKind.Worker: return "worker";
                case CardKind.Building: return "produce";
                case CardKind.BuildMenu: return "build  ·  esc to cancel";
                default: return "";
            }
        }

        /// <summary>
        /// Lays the submenu out for this faction and returns it. Bottom two rows,
        /// keeping the top row where the worker card's own commands live, so the
        /// submenu reads as an extension of the card rather than a new screen.
        /// </summary>
        // ponytail: every building the faction lists is shown live, whether or not
        // the peer can afford it or has the tier for it; the simulation refuses the
        // rest silently. Grey a cell against resources and TierOf the day players
        // start learning the menu by being refused.
        public static CardSlot[] BuildMenu(Faction faction)
        {
            for (int i = 0; i < Cells; i++) buildMenu[i] = Empty;

            int cell = Cols; // row 1, under the worker's Move and Stop
            for (int i = 0; i < buildings.Length && cell < Cells; i++)
            {
                Role role = buildings[i];
                if (!FactionData.Has(faction, role)) continue;
                buildMenu[cell++] = new CardSlot
                {
                    // The price on the button, because a build menu that hides what
                    // a thing costs is a menu the player has to learn by failing.
                    Label = role + "\n" + FactionData.BuildCost(role),
                    Type = CommandType.Build,
                    Produce = role,
                };
            }
            return buildMenu;
        }

        public static CardKind KindOf(Entity e)
        {
            switch (e.Kind)
            {
                case EntityKind.Unit: return CardKind.Fighter;
                case EntityKind.Worker: return CardKind.Worker;
                case EntityKind.Building: return CardKind.Building;
                default: return CardKind.None;
            }
        }

        /// <summary>
        /// The entity the panel speaks for: the highest-priority owned thing in the
        /// selection, earliest first within a kind. -1 when nothing qualifies.
        /// </summary>
        public static int Representative(World world, IReadOnlyList<int> selected, int localPeer)
        {
            int best = -1, bestRank = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                Entity e = world.GetEntity(selected[i]);
                if (!e.Alive || e.Owner != localPeer) continue;
                int rank = (int)KindOf(e);
                if (rank <= bestRank) continue;
                best = selected[i];
                bestRank = rank;
            }
            return best;
        }

        /// <summary>True when the command still needs a world click to name where or what.</summary>
        public static bool NeedsTarget(CommandType type) =>
            type == CommandType.Move || type == CommandType.Attack ||
            type == CommandType.AttackMove || type == CommandType.Build ||
            type == CommandType.SetRallyPoint;

        private static CardSlot Cmd(string label, CommandType type) =>
            new CardSlot { Label = label, Type = type };

    }
}
