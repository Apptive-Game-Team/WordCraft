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
        /// Produce reads it; Build still names entry 0 only (#111 opens that).
        /// Research reads it as something else entirely: the row of the faction's
        /// upgrade table, which is the whole Arg of that command and has no role
        /// beside it. Zero on every cell that is neither, which packs to the same
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

        /// <summary>
        /// True on the cell that turns the production list to its next page. Like
        /// <see cref="Aim"/> it puts nothing on the wire and has no command to
        /// borrow, so it says so in a field of its own; the page it turns is view
        /// state (Orders.Page).
        /// </summary>
        public bool More;

        /// <summary>
        /// True on a Research cell whose row the peer has bought to its last
        /// level. The cell stays on the card, drawn on a Void face like any other
        /// command that exists and cannot be used (Hud.Card), and Orders.Run
        /// refuses it so the key means what the button does. Only that: a row
        /// short of tier or resources is left live and the simulation refuses it,
        /// the rule the build menu follows.
        /// </summary>
        public bool Maxed;
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
        public const int Cols = 4;
        public const int Rows = 3;
        public const int Cells = Cols * Rows;

        /// <summary>
        /// Cell to key. Not QWE/ASD/ZXC, which would be the obvious block, because
        /// the camera keeps WASD (roadmap 3-5) and a pan key that also fires a
        /// command is worse than an unfamiliar block. RTYU/FGHJ/VBNM is the
        /// nearest contiguous 3x4 that leaves WASD alone.
        ///
        /// The fourth column was added on the right, so the three columns that
        /// were here before keep their keys: R T Y / F G H / V B N still sit
        /// where the hand learned them, and U J M are new. Every card below is
        /// written in rows of four to match.
        /// </summary>
        public static readonly KeyCode[] Keys =
        {
            KeyCode.R, KeyCode.T, KeyCode.Y, KeyCode.U,
            KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.J,
            KeyCode.V, KeyCode.B, KeyCode.N, KeyCode.M,
        };

        // The column convention every card below obeys, so a cell means the same
        // thing whatever is selected. Columns 0-2 are the old 3x3 and keep their
        // meaning; column 3 (cells 3, 7, 11) is new and stays empty on every card
        // except where a card says otherwise.
        //   cell 0  (R)  point at the ground   Move, or where produced units walk to
        //   cell 1  (T)  cancel                Stop, or take one off the queue
        //   cell 2  (Y)  stand and shoot       Hold; on a building, the rest of row 0
        //   cell 4  (F)  name a victim
        //   cell 5  (G)  walk and shoot
        //   cells 8-10 (V B N), and on a building all of rows 1 and 2
        //                what this thing makes

        /// <summary>Declared before the tables: static initializers run in textual order.</summary>
        private static readonly CardSlot Empty = new CardSlot();

        private static readonly CardSlot[] fighter =
        {
            Cmd("Move", CommandType.Move), Cmd("Stop", CommandType.Stop), Cmd("Hold", CommandType.HoldPosition), Empty,
            Cmd("Attack", CommandType.Attack), Cmd("A-Move", CommandType.AttackMove), Empty, Empty,
            Empty, Empty, Empty, Empty,
        };

        /// <summary>
        /// 징발 goes in cell 4 because cell 4 is where naming a body lives — it is
        /// Attack on the fighter card, and a Capture is the same gesture at the
        /// same key on the one card that has no Attack. The worker card had six
        /// free cells before this and has five after, so nothing was displaced and
        /// nothing had to be paged; the card is only tight on the produce card,
        /// where 차원 유랑종 already fills six of ProductionMenu.Cells.
        ///
        /// The cell is here for every faction and drawn dead for five of them
        /// (Hud.Card asks CaptureOrder.Available). One layout per selection kind is
        /// what makes the position worth learning, and a cell that appears only
        /// when 인간 is picked would move Build under a different key depending on
        /// who the player chose.
        /// </summary>
        private static readonly CardSlot[] worker =
        {
            Cmd("Move", CommandType.Move), Cmd("Stop", CommandType.Stop), Empty, Empty,
            Cmd("Capture", CommandType.Capture), Empty, Empty, Empty,
            Cmd("Build", CommandType.Build), Empty, Empty, Empty,
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
        /// Cells long. The HUD and the keys both get it through Orders.Card, so
        /// what a key fires is always what the button under it says. Page is the
        /// production or research list's page and means nothing to any other card.
        /// Lead and leadSlot are the role and entry of the entity the card speaks
        /// for, which only the building card reads: a Tech building lists research
        /// where every other building lists production, and each building entry
        /// lists only what it makes or researches.
        ///
        /// The world and the peer are here because a research cell says the level
        /// the peer has reached and the price of the next one, and those are
        /// simulation state. Both are unread by every other card, so a caller with
        /// no match yet may pass null for the world with CardKind.None.
        /// </summary>
        public static CardSlot[] Of(CardKind kind, World world, int peer, Role lead, int leadSlot, int page)
        {
            switch (kind)
            {
                case CardKind.Fighter: return fighter;
                case CardKind.Worker: return worker;
                case CardKind.Building: return Building(world, peer, lead, leadSlot, page);
                case CardKind.BuildMenu: return buildMenu;
                default: return none;
            }
        }

        /// <summary>
        /// What this building lists in rows 1 and 2, one cell per entry: for most
        /// buildings what this building entry produces, named and priced, and for
        /// a Tech building (<paramref name="lead"/>) the upgrades researched in
        /// that entry, with the level reached and the price of the next. Per
        /// building entry since docs/TECH-TREE.md 묶음 3 (#180) spread each
        /// faction's ten or more producible entries and up to eight research rows
        /// over two or three buildings. Which entries those are comes
        /// from <see cref="ProductionMenu"/> and <see cref="ResearchMenu"/>, kept
        /// Unity-free so their one interesting question — does every faction's
        /// list still fit the card — runs headless in Replay. This method only
        /// turns a list into CardSlots and keys.
        ///
        /// Row 0 is the building's own settings and is the same on every building.
        /// Cancel is in it for a Tech building too, and there it takes back the
        /// research that is running: the simulation reads CancelProduction on a
        /// building with an empty queue and a running research as that
        /// (Sim/Research.cs TryCancelResearch), so the cell needs no label or
        /// command of its own and keeps its key.
        ///
        /// Rows 1 and 2 (<see cref="ProductionMenu.Cells"/>, eight) are the whole
        /// budget, column 3 included: the card was widened to four columns so a
        /// roster past six entries still has a button each. Since 묶음 3 three
        /// buildings list more than eight entries or come close: 물 슬라임 거품
        /// 생성기 10, 차원 유랑종 통로 9, 돌 골렘 각성 바위 7.
        ///
        /// A list longer than eight is paged, and the research list pages the same
        /// way the production list does. Each page lists seven entries and cell 11
        /// (key M) becomes More, which turns to the next page and, on the last,
        /// back to the first. The page is the caller's: it is view state
        /// (Orders.Page), and turning it sends nothing to the simulation. Every
        /// entry is on exactly one page, in list order.
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
        public static CardSlot[] Building(World world, int peer, Role lead, int leadSlot, int page)
        {
            for (int i = 0; i < Cells; i++) building[i] = Empty;
            building[0] = Cmd("Rally", CommandType.SetRallyPoint);
            building[1] = Cmd("Cancel", CommandType.CancelProduction);
            // Type stays None: nothing leaves the client when this cell is pressed.
            building[2] = new CardSlot { Label = "Arrive", Aim = true };

            Faction faction = world.FactionOf(peer);
            int count, pages;
            int first, length;
            List<ProductionMenu.Entry> produced = null;
            List<ResearchMenu.Entry> researched = null;
            if (lead == Role.Tech)
            {
                researched = ResearchMenu.For(faction, lead, leadSlot);
                count = researched.Count;
            }
            else
            {
                produced = ProductionMenu.For(faction, lead, leadSlot);
                count = produced.Count;
            }
            pages = ProductionMenu.Pages(count);
            page = ProductionMenu.PageRange(count, page, out first, out length);

            int cell = Cols; // rows 1-2: one cell per entry
            for (int i = first; i < first + length; i++)
            {
                if (researched != null)
                {
                    ResearchMenu.Entry entry = researched[i];
                    building[cell++] = new CardSlot
                    {
                        Label = ResearchMenu.Label(world, peer, entry),
                        Type = CommandType.Research,
                        Slot = entry.Index,
                        Maxed = ResearchMenu.Maxed(world, peer, entry),
                    };
                    continue;
                }

                ProductionMenu.Entry made = produced[i];
                building[cell++] = new CardSlot
                {
                    Label = made.Name + "\n" + made.Resources,
                    Type = CommandType.Produce,
                    Produce = made.Role,
                    Slot = made.Slot,
                };
            }

            // The last cell of the card, whatever the page holds, so the key that
            // turns the page never moves. The label says where the list stands.
            if (pages > 1)
            {
                building[Cells - 1] = new CardSlot { Label = "More\n" + (page + 1) + "/" + pages, More = true };
            }
            return building;
        }

        /// <summary>
        /// How many pages the list this building shows takes: the faction's
        /// research for a Tech building, its production for any other. Orders reads
        /// it to know where More wraps; the card itself only needs the count to
        /// decide whether it has a More at all.
        /// </summary>
        public static int PageCount(Faction faction, Role lead, int leadSlot) =>
            ProductionMenu.Pages(lead == Role.Tech
                ? ResearchMenu.For(faction, lead, leadSlot).Count
                : ProductionMenu.For(faction, lead, leadSlot).Count);

        /// <summary>
        /// What the card is for, drawn over it so the player is never guessing. A
        /// Tech building reads "research" because that is the one thing its rows 1
        /// and 2 are for; every other building produces.
        /// </summary>
        public static string Title(CardKind kind, Role lead)
        {
            switch (kind)
            {
                case CardKind.Fighter: return "orders";
                case CardKind.Worker: return "worker";
                case CardKind.Building: return lead == Role.Tech ? "research" : "produce";
                case CardKind.BuildMenu: return "build  ·  esc to cancel";
                default: return "";
            }
        }

        /// <summary>
        /// Lays the submenu out for this faction and returns it. Bottom two rows,
        /// keeping the top row where the worker card's own commands live, so the
        /// submenu reads as an extension of the card rather than a new screen.
        ///
        /// Four to a row, so the five building roles take cells 4-8: four on row 1
        /// and the fifth at the start of row 2. Eight cells are free and five are
        /// used, which is why this menu has no page.
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
                    //
                    // The faction's own price, and entry 0's, which is the entry a
                    // Build names (see CardSlot.Slot). #147 gave the roster its
                    // first building prices that differ by faction — 굴절 기둥
                    // 70, 인간 대포 55, 인간 마법 탑 60 — so the shared row this
                    // read before is now a number the 차원 유랑종 and 인간 players
                    // are never charged. The faction-free FactionData.BuildCost is
                    // gone rather than merely unused here, so the next cell that
                    // wants a price cannot reach for it by accident.
                    Label = role + "\n" + FactionData.BuildCost(faction, role),
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
