using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;
using static WordCraft.Replay.AbilityChecks;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #210's checks on what a unit card says about abilities.
    /// Program.cs carries only the one line that calls Check(), the split the
    /// other per-issue menu checks use.
    ///
    /// AbilityMenu is the Unity-free half of the ability cells, so the questions
    /// a graphics device would otherwise be the only judge of run here. Does
    /// every faction's every body fit its card, and when a list would not, does
    /// the paging still put each entry on exactly one page. Does a cell say it
    /// is locked exactly when the simulation would refuse the Cast as locked.
    /// Does a cell say it is cooling exactly while the simulation refuses the
    /// Cast for the cooldown, and does the price on its label match what the
    /// Cast then takes from the bank. CommandCard, Orders and Hud stay untested
    /// here for the reason they do in ProductionMenuChecks: they are Unity
    /// plumbing over this.
    ///
    /// Each case is asserted against the simulation running the command, not
    /// against a second typing of its rule, or this file would only prove that it
    /// agrees with itself.
    /// </summary>
    internal static class AbilityMenuChecks
    {
        private static int seq = 9000;

        public static void Check()
        {
            EveryBodyFitsItsCard();
            EveryActiveIsOnACard();
            TheAbilityCellsAreFreeOnTheirCards();
            TheLongListsPageRatherThanOverflow();
            TheAutocastsAreTheDocumentedFour();
            ACellIsLockedExactlyWhenTheSimulationRefusesItAsLocked();
            ACellIsCoolingExactlyWhileTheSimulationRefusesItForTheCooldown();
            TheLabelPriceIsWhatTheCastTakes();
            AHoldingBodyIsTheOneARowNames();
        }

        // ---------------------------------------------------------------------
        // Layout

        /// <summary>The roles whose bodies walk onto a unit card: everything that is not a building.</summary>
        private static IEnumerable<(Faction Faction, Role Role, int Slot)> WalkingEntries()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                for (int r = 0; r < FactionData.RoleCount; r++)
                {
                    var role = (Role)r;
                    if (role == Role.None || FactionData.IsBuilding(role)) continue;
                    for (int s = 0; s < FactionData.SlotCount(faction, role); s++) yield return (faction, role, s);
                }
            }
        }

        private static EntityKind KindOf(Role role) => role == Role.Worker ? EntityKind.Worker : EntityKind.Unit;

        /// <summary>
        /// The rule #210 asks for: a body's actives never overflow the cells its
        /// card gives them without a More page, and a list that did need pages
        /// stays inside AbilityMenu.MaxPages. Asked of every walking entry of every
        /// faction, since the card shows the selected body's own.
        /// </summary>
        private static void EveryBodyFitsItsCard()
        {
            foreach (var entry in WalkingEntries())
            {
                int capacity = AbilityMenu.CellsOf(KindOf(entry.Role)).Length;
                int count = AbilityMenu.For(entry.Faction, entry.Role, entry.Slot).Count;
                int pages = AbilityMenu.Pages(count, capacity);
                string who = entry.Faction + " " + FactionData.Name(entry.Faction, entry.Role, entry.Slot);
                Check(pages <= AbilityMenu.MaxPages,
                    who + " holds " + count + " actives, which is " + pages + " pages of " + (capacity - 1) +
                    "; the card pages at most " + AbilityMenu.MaxPages);
                Check(count <= capacity || pages > 1,
                    who + " holds " + count + " actives in " + capacity + " cells and has no More");
            }
        }

        /// <summary>
        /// Every active of the table sits on the card of a body that walks: a row
        /// held by a building, or by an entry the roster does not field, would be
        /// a Cast no cell can send. The count is the table's own, not a typed ten.
        /// </summary>
        private static void EveryActiveIsOnACard()
        {
            int listed = 0;
            foreach (var entry in WalkingEntries())
            {
                listed += AbilityMenu.For(entry.Faction, entry.Role, entry.Slot).Count;
            }
            int actives = 0;
            for (int i = 0; i < FactionData.AbilityRowCount; i++)
            {
                if (FactionData.IsCast(FactionData.AbilityRowAt(i).Kind)) actives++;
            }
            Check(actives > 0, "the table holds no actives");
            Check(listed == actives, "the cards list " + listed + " actives and the table holds " + actives);
        }

        /// <summary>
        /// The cells #210 names are the free ones. The commands each unit card
        /// spends are written out here (CommandCard.fighter and .worker: Move,
        /// Stop, Hold, Attack, A-Move; Move, Stop, Capture, Build), so a command
        /// moved onto an ability cell fails here and not in a player's hands.
        /// Column 3 and row 2 first: the first three abilities keep U J M.
        /// </summary>
        private static void TheAbilityCellsAreFreeOnTheirCards()
        {
            CheckCells("fighter", AbilityMenu.FighterCells, new[] { 0, 1, 2, 4, 5 });
            CheckCells("worker", AbilityMenu.WorkerCells, new[] { 0, 1, 4, 8 });
            Check(AbilityMenu.FighterCells[0] == 3 && AbilityMenu.FighterCells[1] == 7 && AbilityMenu.FighterCells[2] == 11,
                "the first three ability cells are not the fourth column");
            Check(AbilityMenu.WorkerCells[0] == 3 && AbilityMenu.WorkerCells[1] == 7 && AbilityMenu.WorkerCells[2] == 11,
                "the first three worker ability cells are not the fourth column");
        }

        private static void CheckCells(string card, int[] cells, int[] spent)
        {
            var seen = new HashSet<int>();
            foreach (int cell in cells)
            {
                Check(cell >= 0 && cell < 12, card + " card gives abilities cell " + cell + ", which is off the 3x4 card");
                Check(seen.Add(cell), card + " card gives abilities cell " + cell + " twice");
                Check(Array.IndexOf(spent, cell) < 0, card + " card gives abilities cell " + cell + ", which holds a command");
            }
        }

        /// <summary>
        /// The paging arithmetic on lists no roster has yet: for every capacity
        /// the two cards have and every length up to the pages' ceiling, each
        /// entry is on exactly one page, a page never holds more than the cells
        /// minus the More, and the last page is the one that holds the end. A list
        /// that fits is one page with no More.
        /// </summary>
        private static void TheLongListsPageRatherThanOverflow()
        {
            foreach (int capacity in new[] { AbilityMenu.FighterCells.Length, AbilityMenu.WorkerCells.Length })
            {
                for (int count = 0; count <= (capacity - 1) * AbilityMenu.MaxPages; count++)
                {
                    int pages = AbilityMenu.Pages(count, capacity);
                    Check(pages >= 1, count + " actives in " + capacity + " cells take no page");
                    Check((count <= capacity) == (pages == 1),
                        count + " actives in " + capacity + " cells take " + pages + " pages");

                    var covered = new int[count];
                    for (int page = 0; page < pages; page++)
                    {
                        int shown = AbilityMenu.PageRange(count, capacity, page, out int first, out int length);
                        Check(shown == page, "page " + page + " of " + pages + " was drawn as page " + shown);
                        int room = pages == 1 ? capacity : capacity - 1;
                        Check(length <= room, "page " + page + " holds " + length + " in " + room + " cells");
                        Check(length > 0 || count == 0, "page " + page + " of " + pages + " is empty with " + count + " actives");
                        for (int i = first; i < first + length; i++) covered[i]++;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        Check(covered[i] == 1, "of " + count + " actives in " + capacity + " cells, entry " + i +
                            " is on " + covered[i] + " pages");
                    }

                    // A stale page, from a selection that has since changed, is clamped.
                    int clamped = AbilityMenu.PageRange(count, capacity, pages + 3, out _, out _);
                    Check(clamped == pages - 1, "a stale page 'pages + 3' clamped to " + clamped + " of " + pages);
                    int low = AbilityMenu.PageRange(count, capacity, -2, out _, out _);
                    Check(low == 0, "a negative page clamped to " + low);
                }
            }
        }

        // ---------------------------------------------------------------------
        // Autocasts

        /// <summary>
        /// The four rows docs/TECH-TREE.md types 오토캐스트, written out rather
        /// than read back: 질풍 (폭풍 기수), 굴러 들이받기 (구르는 바위), 잔해
        /// 수리 (바위 술사), 수리 (공방 기술자). They get a line in the info panel
        /// and no cell, so none may be a Cast, and every body of the table that
        /// holds one lists it.
        /// </summary>
        private static void TheAutocastsAreTheDocumentedFour()
        {
            var documented = new[]
            {
                (Faction.TreeSpirits, "질풍"),
                (Faction.RockGolems, "굴러 들이받기"),
                (Faction.RockGolems, "잔해 수리"),
                (Faction.Humans, "수리"),
            };

            int found = 0;
            for (int i = 0; i < FactionData.AbilityRowCount; i++)
            {
                AbilityRow row = FactionData.AbilityRowAt(i);
                if (!AbilityMenu.IsAutocast(row.Kind)) continue;
                found++;
                Check(Array.IndexOf(documented, (row.Faction, row.Name)) >= 0,
                    row.Faction + " " + row.Name + " is called an autocast and the document does not name it");
                Check(!FactionData.IsCast(row.Kind), row.Name + " is an autocast and an active");
                int listed = AbilityMenu.Autocasts(row.Faction, row.Role, row.Slot).Count;
                Check(listed >= 1, row.Faction + " " + row.Name + " is on no body's info panel");
            }
            Check(found == documented.Length, "the table holds " + found + " autocasts, the document " + documented.Length);

            foreach (var (faction, name) in documented)
            {
                bool present = false;
                for (int i = 0; i < FactionData.AbilityCount(faction); i++)
                {
                    AbilityRow row = FactionData.Ability(faction, i);
                    if (row.Name == name) present = AbilityMenu.IsAutocast(row.Kind);
                }
                Check(present, faction + " " + name + " is not an autocast to the view");
            }
        }

        // ---------------------------------------------------------------------
        // Cell state, against the simulation

        /// <summary>Where every caster stands, and the easy point beside it.</summary>
        private static readonly FixVec2 Origin = At(30, 30);

        private static FixVec2 PointFor(AbilityRow row)
        {
            if (row.CastTarget == CastTarget.NoTarget) return Origin;
            int cells = row.CastTarget == CastTarget.RemnantCell ? 4 : row.CastRange - 1;
            return Origin + new FixVec2(Fix.FromInt(cells), Fix.Zero);
        }

        private static int IndexOf(AbilityRow row)
        {
            for (int i = 0; i < FactionData.AbilityCount(row.Faction); i++)
            {
                if (FactionData.Ability(row.Faction, i).Name == row.Name) return i;
            }
            throw new Exception(row.Faction + " has no ability named " + row.Name);
        }

        private static IEnumerable<AbilityRow> Actives()
        {
            for (int i = 0; i < FactionData.AbilityRowCount; i++)
            {
                AbilityRow row = FactionData.AbilityRowAt(i);
                if (FactionData.IsCast(row.Kind)) yield return row;
            }
        }

        /// <summary>
        /// Peer 0 of the row's faction with the row's unlock researched (or not),
        /// a base and money, and the caster at Origin under Hold. Peer 1 is 세계수
        /// 정령 with nothing standing. The point's cell carries what a 간헐천 or
        /// a 깨우기 needs, for long enough that it outlasts the cooldown.
        /// </summary>
        private static World Ready(AbilityRow row, bool unlocked, out int caster)
        {
            World w = Two(row.Faction, Faction.TreeSpirits);
            if (row.Unlock >= 0 && unlocked) Research(w, 0, FactionData.Upgrade(row.Faction, row.Unlock).Effect);
            else
            {
                w.GrantResources(0, 10000);
                w.SpawnBuilding(0, Role.Base, At(3, 3), complete: true);
            }
            caster = row.Role == Role.Worker ? w.SpawnWorker(0, Origin) : w.SpawnUnit(0, row.Role, row.Slot, Origin);
            Step(w, new List<Command> { new Command(w.Tick, 0, seq++, CommandType.HoldPosition, caster, FixVec2.Zero) });

            int cell = World.CellOf(PointFor(row));
            if (row.CastTarget == CastTarget.WaterFieldCell) w.SetWaterFieldForScenario(cell, w.Tick + 5000);
            if (row.CastTarget == CastTarget.RemnantCell) w.SetRemnantForScenario(cell, w.Tick + 5000);
            return w;
        }

        private static string Refusal(World w, int caster, AbilityRow row) =>
            w.CastRefusal(0, caster, IndexOf(row), PointFor(row), out _);

        private static void CastNow(World w, int caster, AbilityRow row) =>
            Step(w, new List<Command> { new Command(w.Tick, 0, seq++, CommandType.Cast, caster, PointFor(row), IndexOf(row)) });

        /// <summary>
        /// The cell says locked exactly when the simulation's answer to the same
        /// Cast is "unlock", on every active, before and after the research. A
        /// row with no unlock is never locked. The positive control: the unlocked
        /// world's Cast is accepted, so a harness that locked everything cannot
        /// pass.
        /// </summary>
        private static void ACellIsLockedExactlyWhenTheSimulationRefusesItAsLocked()
        {
            int locked = 0;
            foreach (AbilityRow row in Actives())
            {
                World before = Ready(row, unlocked: false, out int caster);
                bool wants = row.Unlock >= 0;
                Check(AbilityMenu.Locked(before, 0, row) == wants,
                    row.Name + " reads locked=" + AbilityMenu.Locked(before, 0, row) + " before its research, expected " + wants);
                Check((Refusal(before, caster, row) == "unlock") == wants,
                    row.Name + " is refused as " + Refusal(before, caster, row) + " before its research");
                if (wants) locked++;

                World after = Ready(row, unlocked: true, out int ready);
                Check(!AbilityMenu.Locked(after, 0, row), row.Name + " reads locked after its research");
                Check(Refusal(after, ready, row) == null, row.Name + " is refused as " + Refusal(after, ready, row) + " after its research");
            }
            Check(locked > 0, "no active waits on a research, so the locked cell was never exercised");
        }

        /// <summary>
        /// The cooling meter is on exactly while the simulation refuses the same
        /// Cast for the cooldown (or, for 전개, for the transition clock). Run
        /// from the Cast that starts it to the tick the next one is let in: the
        /// meter starts above 0 and at most 1, never rises, and is 0 the first
        /// tick the refusal is no longer "cooldown". 굳기 ends its caster and
        /// waits on nothing, so its meter is 0 before the Cast and the body is
        /// gone after it.
        /// </summary>
        private static void ACellIsCoolingExactlyWhileTheSimulationRefusesItForTheCooldown()
        {
            int metered = 0;
            foreach (AbilityRow row in Actives())
            {
                World w = Ready(row, unlocked: true, out int caster);
                Check(AbilityMenu.Remaining(w.GetEntity(caster), row) == 0f, row.Name + " is cooling before its first Cast");

                CastNow(w, caster, row);
                if (row.Kind == AbilityKind.Harden)
                {
                    Check(!w.GetEntity(caster).Alive, "굳기 left its caster standing");
                    continue;
                }

                float last = 2f;
                int cooling = 0;
                int limit = Math.Max(row.Cooldown, row.Ticks) + 5;
                for (int t = 0; t < limit; t++)
                {
                    float remaining = AbilityMenu.Remaining(w.GetEntity(caster), row);
                    bool refused = Refusal(w, caster, row) == "cooldown";
                    Check((remaining > 0f) == refused,
                        row.Name + " tick +" + t + ": meter " + remaining + " but the simulation says " + Refusal(w, caster, row));
                    Check(remaining <= 1f && remaining <= last, row.Name + " tick +" + t + ": meter rose to " + remaining);
                    last = remaining;
                    if (remaining > 0f) cooling++;
                    Step(w);
                }
                Check(cooling > 0, row.Name + " never showed the meter after its Cast");
                Check(last == 0f, row.Name + " is still cooling " + limit + " ticks after its Cast");
                metered++;
            }
            Check(metered > 0, "no active was metered");
        }

        /// <summary>
        /// The second line of the label is what the Cast takes. 깨우기 is the one
        /// active with a price: 40 before its cost research and 25 after, and the
        /// bank falls by exactly the number the label shows. An active with no
        /// price has no second line. 전개 on a deployed body reads as taking it
        /// down.
        /// </summary>
        private static void TheLabelPriceIsWhatTheCastTakes()
        {
            int priced = 0;
            foreach (AbilityRow row in Actives())
            {
                foreach (bool boosted in new[] { false, true })
                {
                    if (boosted && (row.Boost < 0 || row.BoostedCost == 0)) continue;
                    World w = Ready(row, unlocked: true, out int caster);
                    if (boosted) Research(w, 0, FactionData.Upgrade(row.Faction, row.Boost).Effect);
                    string label = AbilityMenu.Label(w, 0, w.GetEntity(caster), row);
                    string[] lines = label.Split('\n');
                    Check(lines[0] == row.Name, row.Name + " label starts with '" + lines[0] + "'");

                    int charged;
                    if (row.Cost > 0)
                    {
                        Check(lines.Length == 2, row.Name + " costs " + row.Cost + " and its label has no price line");
                        Check(int.TryParse(lines[1], out charged), row.Name + " price line '" + lines[1] + "' is not a number");
                        // No worker is gathering in this world, so the bank moves by the price alone.
                        int before = w.GetResources(0);
                        CastNow(w, caster, row);
                        int after = w.GetResources(0);
                        Check(before - after == charged,
                            row.Name + " label says " + charged + " and the Cast took " + (before - after));
                        priced++;
                    }
                    else
                    {
                        Check(lines.Length == 1, row.Name + " costs nothing and its label has a price line '" + label + "'");
                    }
                }
            }
            Check(priced >= 2, "the price line was checked " + priced + " times; 깨우기 should be checked at both prices");

            AbilityRow deploy = default;
            foreach (AbilityRow row in Actives()) if (row.Kind == AbilityKind.Deploy) deploy = row;
            World d = Ready(deploy, unlocked: true, out int cannon);
            Check(AbilityMenu.Label(d, 0, d.GetEntity(cannon), deploy) == deploy.Name, "an undeployed cannon's cell is not named for deploying");
            CastNow(d, cannon, deploy);
            for (int t = 0; t < deploy.Ticks + 2; t++) Step(d);
            Check(d.GetEntity(cannon).Deployed, "the cannon never deployed");
            Check(AbilityMenu.Label(d, 0, d.GetEntity(cannon), deploy) == deploy.Name + " 해제",
                "a deployed cannon's cell is not named for packing up");
        }

        /// <summary>
        /// A Cast is sent to the bodies that hold the row: its Role and Slot, alive,
        /// not a building. The test is the first thing the simulation's own
        /// CastRefusal asks ("row"), so a body Holds accepts is one the simulation
        /// would not refuse for being the wrong body, and one it rejects is one it
        /// would. Asked of the caster and of every other walking entry of its faction.
        /// </summary>
        private static void AHoldingBodyIsTheOneARowNames()
        {
            foreach (AbilityRow row in Actives())
            {
                World w = Ready(row, unlocked: true, out int caster);
                Check(AbilityMenu.Holds(w.GetEntity(caster), row), row.Name + " does not name its own caster");

                // Every other walking entry of the faction: Holds says no, and so does the
                // simulation ("row"). Includes the entries that share the row's Role and
                // differ in Slot only, which is the test a Role-only Holds would fail.
                int strangers = 0;
                foreach (var entry in WalkingEntries())
                {
                    if (entry.Faction != row.Faction || (entry.Role == row.Role && entry.Slot == row.Slot)) continue;
                    int stranger = entry.Role == Role.Worker
                        ? w.SpawnWorker(0, At(33, 33))
                        : w.SpawnUnit(0, entry.Role, entry.Slot, At(33, 33));
                    Check(!AbilityMenu.Holds(w.GetEntity(stranger), row), row.Name + " names a " + entry.Role + " " + entry.Slot);
                    Check(w.CastRefusal(0, stranger, IndexOf(row), PointFor(row), out _) == "row",
                        row.Name + " is not refused as the wrong row for a " + entry.Role + " " + entry.Slot);
                    strangers++;
                }
                Check(strangers > 0, row.Faction + " fields one walking entry");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
