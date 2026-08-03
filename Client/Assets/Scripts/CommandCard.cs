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

        private static readonly CardSlot[] building =
        {
            Cmd("Rally", CommandType.SetRallyPoint), Cmd("Cancel", CommandType.CancelProduction), Empty,
            Empty, Empty, Empty,
            Make(Role.Melee), Make(Role.Ranged), Make(Role.Signature),
        };

        private static readonly CardSlot[] none = new CardSlot[Cells];

        /// <summary>The card for this selection kind. Always Cells long.</summary>
        public static CardSlot[] Of(CardKind kind)
        {
            switch (kind)
            {
                case CardKind.Fighter: return fighter;
                case CardKind.Worker: return worker;
                case CardKind.Building: return building;
                case CardKind.BuildMenu: return buildMenu;
                default: return none;
            }
        }

        /// <summary>
        /// Lays the submenu out for this faction and returns it. Bottom two rows,
        /// keeping the top row where the worker card's own commands live, so the
        /// submenu reads as an extension of the card rather than a new screen.
        /// </summary>
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

        private static CardSlot Make(Role role) =>
            new CardSlot { Label = role.ToString(), Type = CommandType.Produce, Produce = role };
    }
}
