using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #174's checks on what the view reads out of the simulation's upgrade
    /// research. Program.cs carries only the one line that calls Check(), the same
    /// split ProductionMenuChecks and the other per-issue files use.
    ///
    /// ResearchMenu is the Unity-free half of the research card, so the questions
    /// a graphics device would otherwise be the only judge of run here. Does every
    /// faction's list fit the card's pages. Does a cell say the level and the price
    /// the simulation then actually takes from the bank. Does the progress meter
    /// run from empty to full over the row's own ticks and fall back to nothing when
    /// the research ends or is cancelled. Does the +attack/+armor readout move
    /// with the levels. CommandCard, Orders and Hud stay untested here for the
    /// reason they do in ProductionMenuChecks: they are Unity plumbing over this.
    ///
    /// Each case is asserted against the simulation running the command, not
    /// against a second typing of its rule, or this file would only prove that it
    /// agrees with itself.
    /// </summary>
    internal static class ResearchMenuChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            EveryFactionListsItsRowsInOrder();
            NoFactionNeedsTooManyPages();
            EachResearchBuildingListsItsOwnRows();
            CellsSayTheLevelAndThePriceTheSimulationTakes();
            ProgressRunsFromEmptyToFull();
            AResearchCancelledByTheCardIsRefundedAndStops();
            TheBonusReadoutFollowsTheLevels();
            CaptureProgressDividesByTheResearchedClock();
            TheDrawnArrivalRadiusIsTheResearchedOne();
        }

        /// <summary>
        /// One entry per row of the table, by ascending index, carrying the table's
        /// own name and top level. The Index is the Arg of the Research the cell
        /// sends, so an entry out of place is a button that buys the wrong row.
        /// </summary>
        private static void EveryFactionListsItsRowsInOrder()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                List<ResearchMenu.Entry> entries = ResearchMenu.For(faction);
                Check(entries.Count == FactionData.UpgradeCount(faction),
                    faction + " lists " + entries.Count + " research cells for " +
                    FactionData.UpgradeCount(faction) + " rows");
                for (int i = 0; i < entries.Count; i++)
                {
                    UpgradeRow row = FactionData.Upgrade(faction, i);
                    Check(entries[i].Index == i, faction + " cell " + i + " buys row " + entries[i].Index);
                    Check(entries[i].Name == row.Name && entries[i].MaxLevel == row.MaxLevel,
                        faction + " cell " + i + " says " + entries[i].Name + " x" + entries[i].MaxLevel +
                        ", the table says " + row.Name + " x" + row.MaxLevel);
                }
            }
        }

        /// <summary>
        /// A research building's card lists the rows researched in it and no
        /// other, since docs/TECH-TREE.md 묶음 3 (#180) put rows on second and
        /// third research buildings: every row is on exactly one building's list,
        /// the building its ResearchAt names, and that list fits one page. Since
        /// MaxUpgrades went to 9 (#197) the whole table of 세계수 정령 and 차원
        /// 유랑종 no longer fits one page, and no card draws the whole table: each
        /// research building draws its own list, which is what has to fit.
        /// </summary>
        private static void EachResearchBuildingListsItsOwnRows()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                int listed = 0;
                for (int s = 0; s < FactionData.SlotCount(faction, Role.Tech); s++)
                {
                    List<ResearchMenu.Entry> entries = ResearchMenu.For(faction, Role.Tech, s);
                    Check(ProductionMenu.Pages(entries.Count) == 1,
                        faction + " Tech " + s + "'s " + entries.Count + " research rows do not fit one page");
                    foreach (ResearchMenu.Entry entry in entries)
                    {
                        Check(FactionData.Upgrade(faction, entry.Index).ResearchAt == (Role.Tech, s),
                            faction + " Tech " + s + " lists " + entry.Name + ", which is researched elsewhere");
                        listed++;
                    }
                }
                Check(listed == ResearchMenu.For(faction).Count,
                    faction + " research buildings list " + listed + " rows of " + ResearchMenu.For(faction).Count);
            }
        }

        /// <summary>
        /// The research list pages with the production list's arithmetic, and a
        /// list that would need more than ProductionMenu.MaxPages is a failure
        /// here for the reason it is there: the card is due a redesign.
        /// </summary>
        private static void NoFactionNeedsTooManyPages()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                int count = ResearchMenu.For(faction).Count;
                int pages = ProductionMenu.Pages(count);
                Check(pages <= ProductionMenu.MaxPages,
                    faction + " needs " + count + " research cells, which is " + pages +
                    " pages of " + ProductionMenu.PageSize + "; the card pages at most " + ProductionMenu.MaxPages);
            }
        }

        /// <summary>
        /// Row 0 at every level: the label names the level and quotes the price of
        /// the next one, the order then takes exactly that price from the bank, and
        /// the last level's cell reads maxed instead of quoting a price.
        /// </summary>
        private static void CellsSayTheLevelAndThePriceTheSimulationTakes()
        {
            World world = TechWorld(Faction.Humans, out int tech);
            ResearchMenu.Entry entry = ResearchMenu.For(Faction.Humans)[0];

            for (int level = 0; level < entry.MaxLevel; level++)
            {
                string label = ResearchMenu.Label(world, 0, entry);
                Check(!ResearchMenu.Maxed(world, 0, entry), "level " + level + " is already maxed");

                string[] lines = label.Split('\n');
                Check(lines.Length == 2, "the cell is " + lines.Length + " lines, not a name and a price");
                Check(lines[0] == entry.Name + " Lv " + level + "/" + entry.MaxLevel,
                    "level " + level + " reads '" + lines[0] + "'");

                int banked = world.GetResources(0);
                world.Step(Research(0, 10 + level, tech, entry.Index));
                Check(lines[1] == (banked - world.GetResources(0)).ToString(),
                    "the cell quotes " + lines[1] + " and the order took " + (banked - world.GetResources(0)));

                Run(world, FactionData.Upgrade(Faction.Humans, entry.Index).Ticks);
                Check(world.UpgradeLevel(0, entry.Index) == level + 1, "level " + (level + 1) + " never landed");
            }

            Check(ResearchMenu.Maxed(world, 0, entry), "three levels bought and the cell is not maxed");
            string last = ResearchMenu.Label(world, 0, entry);
            Check(last == entry.Name + " Lv " + entry.MaxLevel + "/" + entry.MaxLevel + "\nmax",
                "the maxed cell reads '" + last.Replace("\n", " / ") + "'");

            // The maxed cell is dead in the view, and the simulation refuses it
            // too, so a key that got past the card would still change nothing.
            int banked3 = world.GetResources(0);
            world.Step(Research(0, 50, tech, entry.Index));
            Check(world.GetResources(0) == banked3 && world.GetEntity(tech).ResearchIndex < 0,
                "the simulation took a fourth level of a row with three");

            // Another row of the same building is untouched by row 0's levels.
            ResearchMenu.Entry armor = ResearchMenu.For(Faction.Humans)[1];
            Check(!ResearchMenu.Maxed(world, 0, armor), "maxing 공격력 maxed 방어력");
            Check(ResearchMenu.Label(world, 0, armor).StartsWith(armor.Name + " Lv 0/"), "방어력 moved with 공격력");
        }

        /// <summary>
        /// -1 for a building that is not researching, then a meter that never goes
        /// backwards, ends at the row's own ticks, and is -1 again on the tick the
        /// level lands. The name is the row's, and null when idle.
        /// </summary>
        private static void ProgressRunsFromEmptyToFull()
        {
            World world = TechWorld(Faction.RockGolems, out int tech);
            Check(ResearchMenu.Progress(world, world.GetEntity(tech)) < 0f, "an idle building shows a meter");
            Check(ResearchMenu.Researching(world, world.GetEntity(tech)) == null, "an idle building names a research");

            UpgradeRow row = FactionData.Upgrade(Faction.RockGolems, 1);
            world.Step(Research(0, 1, tech, 1));
            Check(ResearchMenu.Researching(world, world.GetEntity(tech)) == row.Name,
                "the building is researching " + row.Name + " but says '" +
                ResearchMenu.Researching(world, world.GetEntity(tech)) + "'");

            float last = -1f;
            int steps = 1;
            while (world.GetEntity(tech).ResearchIndex >= 0)
            {
                Entity b = world.GetEntity(tech);
                float now = ResearchMenu.Progress(world, b);
                Check(now >= 0f && now < 1f, "the meter reads " + now + " with " + b.ResearchTicksLeft + " ticks left");
                Check(now >= last, "the meter went backwards: " + last + " then " + now);
                Check(Math.Abs(now - (1f - b.ResearchTicksLeft / (float)row.Ticks)) < 1e-6f,
                    "the meter is not 1 - left / ticks");
                last = now;
                world.Step(Idle);
                Check(++steps <= row.Ticks + 1, "the research ran past its " + row.Ticks + " ticks");
            }
            Check(last > 0.9f, "the meter had only reached " + last + " on the last tick");
            Check(ResearchMenu.Progress(world, world.GetEntity(tech)) < 0f, "a finished research still shows a meter");
        }

        /// <summary>
        /// Cancel on a researching 기술 건물 is the card's own first cell sending
        /// CancelProduction, and the simulation already treats it as a research
        /// cancel (Sim/Research.cs TryCancelResearch). Pinned here from the view's
        /// side: after it the meter is gone, the name is gone, and the bank holds
        /// what it held before the order.
        /// </summary>
        private static void AResearchCancelledByTheCardIsRefundedAndStops()
        {
            World world = TechWorld(Faction.Hellfire, out int tech);
            int before = world.GetResources(0);
            world.Step(Research(0, 1, tech, 0));
            Run(world, 40);
            Check(ResearchMenu.Progress(world, world.GetEntity(tech)) > 0f, "the research never showed progress");
            Check(world.GetResources(0) < before, "the research was free");

            world.Step(One(0, 2, CommandType.CancelProduction, tech, 0));
            Entity b = world.GetEntity(tech);
            Check(ResearchMenu.Progress(world, b) < 0f && ResearchMenu.Researching(world, b) == null,
                "a cancelled research still shows");
            Check(world.GetResources(0) == before, "the cancel refunded " + (world.GetResources(0) - before) + " short of the price");
            Check(world.UpgradeLevel(0, 0) == 0, "a cancelled research landed a level");
        }

        /// <summary>
        /// "+attack/+armor", read off the levels. Starts at +0/+0, and each
        /// researched level moves only its own half.
        /// </summary>
        private static void TheBonusReadoutFollowsTheLevels()
        {
            World world = TechWorld(Faction.TreeSpirits, out int tech);
            Check(ResearchMenu.Bonus(world, 0) == "+0/+0" && !ResearchMenu.HasBonus(world, 0),
                "an unresearched peer reads " + ResearchMenu.Bonus(world, 0));

            ResearchToCompletion(world, tech, 0);
            ResearchToCompletion(world, tech, 0);
            Check(ResearchMenu.Bonus(world, 0) == "+2/+0", "two attack levels read " + ResearchMenu.Bonus(world, 0));
            ResearchToCompletion(world, tech, 1);
            Check(ResearchMenu.Bonus(world, 0) == "+2/+1" && ResearchMenu.HasBonus(world, 0),
                "two attack and one armor read " + ResearchMenu.Bonus(world, 0));
            Check(ResearchMenu.Bonus(world, 1) == "+0/+0", "the other peer's readout moved with this peer's levels");
        }

        // ---------------------------------------------------------------------
        // The numbers the view reads off the world

        /// <summary>
        /// The row of this faction's table with this effect, found by searching
        /// rather than by index so a table reordered later does not make this file
        /// research the wrong row.
        /// </summary>
        private static int RowOf(Faction faction, UpgradeEffect effect)
        {
            for (int i = 0; i < FactionData.UpgradeCount(faction); i++)
            {
                if (FactionData.Upgrade(faction, i).Effect == effect) return i;
            }
            throw new Exception(faction + " has no " + effect + " row");
        }

        /// <summary>
        /// A capture's bar, with and without 포획 가속. Both are run through a
        /// real capture and read at every tick. Before the research the world-aware
        /// reading is the old one, tick for tick. After it, the bar starts near
        /// empty and ends near full, which the old reading divided by 60 cannot do
        /// on a clock that starts at 40: it begins a third of the way across.
        /// </summary>
        private static void CaptureProgressDividesByTheResearchedClock()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool researched = pass == 1;
                World world = TechWorld(Faction.Humans, out int tech);
                if (researched) ResearchToCompletion(world, tech, RowOf(Faction.Humans, UpgradeEffect.CaptureTicks));
                int worker = world.SpawnWorker(0, At(30, 30));
                int rock = world.SpawnNeutralRock(At(31, 30));
                world.Step(One(0, 50, CommandType.Capture, worker, rock));

                float first = -1f, last = -1f;
                for (int t = 0; t < 200 && world.GetEntity(worker).CaptureTargetId >= 0; t++)
                {
                    Entity w = world.GetEntity(worker);
                    float now = CaptureOrder.Progress(world, w);
                    Check(now >= last, "the capture bar went backwards: " + last + " then " + now);
                    if (!researched)
                    {
                        Check(now == CaptureOrder.Progress(w),
                            "an unresearched peer's bar differs from the base reading at " + w.CaptureTicksLeft + " left");
                    }
                    if (now > 0f && first < 0f) first = now;
                    if (now > 0f) last = now;
                    world.Step(Idle);
                }

                string which = researched ? "a researched" : "an unresearched";
                Check(first > 0f && first < 0.1f, which + " capture's bar started at " + first + ", not near empty");
                Check(last > 0.9f && last <= 1f, which + " capture's bar ended at " + last + ", not near full");
            }
        }

        /// <summary>
        /// The ring a 굴절 기둥 is drawn with for a peer that has researched 도착
        /// 반경, on both sides of its edge, the pair ArrivalOrderChecks pins the
        /// base radius with. A ring drawn at the base radius fails the first half:
        /// it would promise less than the simulation arrives at.
        /// </summary>
        private static void TheDrawnArrivalRadiusIsTheResearchedOne()
        {
            World world = TechWorld(Faction.Driftworlds, out int tech);
            world.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), true);
            int pillar = world.EntityCount - 1;
            FixVec2 anchor = world.GetEntity(pillar).Position;

            Check(ArrivalOrder.RadiusFor(world, 0) == World.ArrivalRadius &&
                  ArrivalOrder.RadiusFor(world, 0) == ArrivalOrder.Radius,
                "an unresearched peer's ring is not the base radius");

            ResearchToCompletion(world, tech, RowOf(Faction.Driftworlds, UpgradeEffect.ArrivalRadius));
            Fix radius = ArrivalOrder.RadiusFor(world, 0);
            Check(radius == World.ArrivalRadiusResearched && radius > ArrivalOrder.Radius,
                "the researched ring is " + radius.ToInt() + " cells, not " + World.ArrivalRadiusResearched.ToInt());

            Check(world.ArrivalValid(0, new FixVec2(anchor.X + radius, anchor.Y)),
                "a point on the researched ring is refused by the simulation");
            Check(!world.ArrivalValid(0, new FixVec2(anchor.X + radius + Fix.Ratio(1, 16), anchor.Y)),
                "a point a sixteenth of a cell outside the researched ring is still arrived at");
        }

        // ---------------------------------------------------------------------

        private static readonly List<Command> Idle = new List<Command>();

        /// <summary>
        /// Peer 0 of the named faction with a finished 기술 건물 and a deep bank.
        /// Peer 1 is another faction and owns nothing.
        /// </summary>
        private static World TechWorld(Faction faction, out int tech)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, faction);
            world.SetPeerFaction(1, faction == Faction.Hellfire ? Faction.TreeSpirits : Faction.Hellfire);
            world.GrantResources(0, 10000);
            world.GrantResources(1, 10000);
            tech = world.SpawnBuilding(0, Role.Tech, At(20, 20), complete: true);
            return world;
        }

        /// <summary>Starts one level of this row and steps until it lands.</summary>
        private static void ResearchToCompletion(World world, int tech, int index)
        {
            int level = world.UpgradeLevel(0, index);
            world.Step(Research(0, 100 + world.Tick, tech, index));
            Check(world.GetEntity(tech).ResearchIndex == index, "research of row " + index + " was refused");
            Run(world, FactionData.Upgrade(world.FactionOf(0), index).Ticks);
            Check(world.UpgradeLevel(0, index) == level + 1, "research of row " + index + " never landed");
        }

        private static List<Command> Research(int peer, int seq, int building, int index) =>
            One(peer, seq, CommandType.Research, building, index);

        private static List<Command> One(int peer, int seq, CommandType type, int entity, int arg) =>
            new List<Command> { new Command(0, peer, seq, type, entity, FixVec2.Zero, arg) };

        private static void Run(World world, int ticks)
        {
            for (int t = 0; t < ticks; t++) world.Step(Idle);
        }

        private static FixVec2 At(int x, int y)
        {
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(x) + half, Fix.FromInt(y) + half);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
