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

        /// <summary>
        /// Which entry of <see cref="Produce"/>'s roster list this cell orders.
        /// Only Produce reads it; Build still names entry 0 only (#111 opens that).
        /// Zero on every cell that is not a Produce button, which packs to the same
        /// argument a role-only command always sent.
        /// </summary>
        public int Slot;

        /// <summary>
        /// True on the one cell that names a point for the view instead of sending
        /// a command: 차원 유랑종's 도착 지점. Beside <see cref="Type"/> rather than
        /// a member of it, because CommandType is Sim's enum and this cell puts
        /// nothing on the wire — the point it names rides the next Produce's
        /// Target, which is the field Sim/World.cs already reads it out of.
        ///
        /// The build submenu is the precedent for a cell that does something the
        /// simulation never hears about (Orders.BuildMenuOpen). That one could
        /// borrow a Build with no role to say so; this one has no argument spare,
        /// so it says so in a field of its own.
        /// </summary>
        public bool Aim;
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
        //   cell 2  stand and shoot       Hold; on a building, the rest of row 0
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

        /// <summary>
        /// 징발 goes in cell 3 because cell 3 is where naming a body lives — it is
        /// Attack on the fighter card, and a Capture is the same gesture at the
        /// same key on the one card that has no Attack. The worker card had six
        /// free cells before this and has five after, so nothing was displaced and
        /// nothing had to be paged; the 3x3 is only tight on the produce card,
        /// where 차원 유랑종 already fills all six of ProductionMenu.Cells.
        ///
        /// The cell is here for every faction and drawn dead for five of them
        /// (Hud.Card asks CaptureOrder.Available). One layout per selection kind is
        /// what makes the position worth learning, and a cell that appears only
        /// when 인간 is picked would move Build under a different key depending on
        /// who the player chose.
        /// </summary>
        private static readonly CardSlot[] worker =
        {
            Cmd("Move", CommandType.Move), Cmd("Stop", CommandType.Stop), Empty,
            Cmd("Capture", CommandType.Capture), Empty, Empty,
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
        /// What this faction's buildings can produce, named and priced, one cell
        /// per roster entry rather than per role. Which entries that is comes
        /// from <see cref="ProductionMenu"/>, kept Unity-free so its one
        /// interesting question — does every faction's list still fit the card
        /// — runs headless in Replay/ProductionMenuChecks.cs. This method only
        /// turns that list into CardSlots and keys.
        ///
        /// Rows 1 and 2 (<see cref="ProductionMenu.Cells"/>, six) are the whole
        /// budget, not the bottom row alone. 차원 유랑종 fills all six today: three
        /// melee entries (its two temporary extinct-slime summons standing
        /// beside 화산편), two ranged (틈새 사수 and 멸종한 번개 슬라임), one
        /// signature — every other faction fits in three. A faction that grew a
        /// seventh producible entry would overflow this card; nothing on the
        /// roster does yet, and #114 leaves rebalancing or paging the card to
        /// whoever adds one.
        ///
        /// 도착 goes in cell 2 because it is the last free cell of row 0, and row 0
        /// is where a building's own settings live rather than what it makes. It
        /// sits beside Rally on purpose: the two are the same gesture — press,
        /// then point at the ground — and they name the two halves of the same
        /// journey, where a body appears and where it then walks to.
        ///
        /// Here for every faction and drawn dead for five, exactly as the 징발 cell
        /// is on the worker card (Hud.Card asks ArrivalOrder.Aims). One layout per
        /// selection kind is what makes a position worth learning, and a cell that
        /// appeared only for 차원 유랑종 would move nothing today and everything the
        /// day row 0 grows a fourth entry.
        /// </summary>
        public static CardSlot[] Building(Faction faction)
        {
            for (int i = 0; i < Cells; i++) building[i] = Empty;
            building[0] = Cmd("Rally", CommandType.SetRallyPoint);
            building[1] = Cmd("Cancel", CommandType.CancelProduction);
            // Type stays None: nothing leaves the client when this cell is pressed.
            building[2] = new CardSlot { Label = "Arrive", Aim = true };

            List<ProductionMenu.Entry> entries = ProductionMenu.For(faction);
            int cell = Cols; // rows 1-2: one cell per producible roster entry
            for (int i = 0; i < entries.Count && cell < Cells; i++)
            {
                ProductionMenu.Entry entry = entries[i];
                building[cell++] = new CardSlot
                {
                    Label = entry.Name + "\n" + entry.Resources,
                    Type = CommandType.Produce,
                    Produce = entry.Role,
                    Slot = entry.Slot,
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
            type == CommandType.SetRallyPoint || type == CommandType.Capture;

        private static CardSlot Cmd(string label, CommandType type) =>
            new CardSlot { Label = label, Type = type };

    }
}
