using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #191's checks: a building under construction keeps the damage it
    /// takes. The ramp used to rewrite hp from progress every tick, so a site
    /// shot at stood back at its ramp value on the next tick and could not be
    /// razed until it was finished.
    ///
    /// Four questions. An undamaged site ends on exactly MaxHp, on the tick
    /// its clock runs out and not before. A site under fire from twelve ranged
    /// units dies before it finishes, through Kill like any body. Twelve rather
    /// than two: the ramp climbs 2 hp a tick on any faction's base, and
    /// two ranged units deal under 1 hp a tick, so two can only raze a site in
    /// its first tick, which the old ramp allowed too. A site that
    /// took d damage and was then left alone ends on MaxHp - d. And the ramp is
    /// the same on two worlds, hash for hash, damaged or not.
    /// </summary>
    internal static class ConstructionChecks
    {
        private const ulong Seed = 0xC0FFEE;

        private static readonly Faction Owner = Faction.TreeSpirits;

        public static void Check()
        {
            AnUndamagedSiteFinishesOnExactlyMaxHp();
            ASiteUnderFireDiesBeforeItFinishes();
            ADamagedSiteFinishesAtMaxHpMinusTheDamage();
            TheRampIsTheSameOnTwoWorlds();
        }

        private static World Fixture(out int site)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Owner);
            world.SetPeerFaction(1, Owner);
            site = world.SpawnBuilding(0, Role.Base, At(30, 30), complete: false);
            return world;
        }

        private static int Clock => FactionData.BuildTicks(Owner, Role.Base, 0);

        private static void Idle(World world, int ticks)
        {
            var none = new List<Command>();
            for (int t = 0; t < ticks; t++) world.Step(none);
        }

        /// <summary>
        /// Climbs from 1 hp, never drops, ends on MaxHp on the tick BuildTicksLeft
        /// reaches 0 and stays there.
        /// </summary>
        private static void AnUndamagedSiteFinishesOnExactlyMaxHp()
        {
            World world = Fixture(out int site);
            int max = world.GetEntity(site).MaxHp;
            Check(world.GetEntity(site).Hp == 1, "a new site does not start at 1 hp");
            int last = 1;
            for (int t = 1; t <= Clock; t++)
            {
                Idle(world, 1);
                Entity e = world.GetEntity(site);
                Check(e.Hp >= last, "an undamaged site lost hp on tick " + t);
                Check(e.Hp <= max, "a site went past MaxHp on tick " + t);
                Check((e.BuildTicksLeft == 0) == (t == Clock), "the clock ran out on tick " + t + " of " + Clock);
                Check((e.Hp == max) == (t == Clock), "an undamaged site stood at " + e.Hp + " of " + max + " on tick " + t + " of " + Clock);
                last = e.Hp;
            }
            Idle(world, 20);
            Check(world.GetEntity(site).Hp == max, "a finished site did not hold MaxHp");
        }

        /// <summary>
        /// The n-th of twelve ranged units of the other peer, one arriving each
        /// tick from tick 20. Staggered on purpose: they all start with a cooldown of 0, so
        /// twelve spawned together fire one volley and the first volley kills a
        /// site whatever the ramp does. Spread over twelve ticks their cooldowns
        /// stay apart, and what is left is a steady hurt of about 4 hp a tick
        /// against a ramp of 2, which only a site that keeps its damage can lose.
        /// </summary>
        private static void Besiege(World world, int n)
        {
            world.SpawnUnit(1, Role.Ranged, At(28 + n % 6, 33 + n / 6));
        }

        private const int Besiegers = 12;
        private const int BesiegeFrom = 20;

        private static void ASiteUnderFireDiesBeforeItFinishes()
        {
            World world = Fixture(out int site);
            int diedOn = -1;
            var none = new List<Command>();
            for (int t = 1; t <= Clock + 50 && diedOn < 0; t++)
            {
                int n = t - BesiegeFrom;
                if (n >= 0 && n < Besiegers) Besiege(world, n);
                world.Step(none);
                if (!world.GetEntity(site).Alive) diedOn = t;
            }
            Check(diedOn > 0, "a site under fire from " + Besiegers + " ranged units was never razed");
            Check(diedOn < Clock, "the site died on tick " + diedOn + ", not before its clock of " + Clock);
            Check(diedOn > BesiegeFrom + 1, "the site died on tick " + diedOn + " to the first volley, which the old ramp lets die too");
            Entity e = world.GetEntity(site);
            Check(e.Hp <= 0, "a razed site still has " + e.Hp + " hp");
        }

        /// <summary>
        /// One ranged unit arrives once the site has climbed to a few hundred hp,
        /// shoots for twenty ticks and is then ordered out of range. Arriving late
        /// is what lets the site survive: the ramp adds MaxHp / BuildTicks a tick
        /// and one shooter takes a fraction of that, so it is only a site at its
        /// first few hp that dies to it. d is read off the site tick by tick as
        /// what the ramp added minus what the site actually gained, so it is the
        /// damage that landed and not a number this check computes a second time.
        /// </summary>
        private static void ADamagedSiteFinishesAtMaxHpMinusTheDamage()
        {
            const int Arrive = 40, Leave = 60;
            World world = Fixture(out int site);
            int max = world.GetEntity(site).MaxHp;
            int total = Clock;

            int shooter = -1;
            int damage = 0;
            int hp = world.GetEntity(site).Hp;
            var none = new List<Command>();
            for (int t = 1; t <= total + 10; t++)
            {
                if (t == Arrive) shooter = world.SpawnUnit(1, Role.Ranged, At(30, 33));
                var order = t == Leave
                    ? new List<Command> { new Command(0, 1, 0, CommandType.Move, shooter, At(60, 60)) }
                    : none;
                world.Step(order);
                Entity e = world.GetEntity(site);
                if (t <= total)
                {
                    int climb = max - 1;
                    int step = (climb * t) / total - (climb * (t - 1)) / total;
                    damage += hp + step - e.Hp;
                }
                hp = e.Hp;
            }
            Entity done = world.GetEntity(site);
            Check(done.Alive, "the site did not survive a single shooter that arrived at tick " + Arrive);
            Check(damage > 0, "the shooter never hurt the site, so the check proves nothing");
            Check(damage < max, "the shooter took more than the whole site");
            Check(done.BuildTicksLeft == 0, "the site never finished");
            Check(done.Hp == max - damage,
                "a site that took " + damage + " damage finished at " + done.Hp + " of " + max + ", not " + (max - damage));
        }

        private static void TheRampIsTheSameOnTwoWorlds()
        {
            for (int variant = 0; variant < 2; variant++)
            {
                World a = Fixture(out _);
                World b = Fixture(out _);
                for (int t = 0; t < Clock + 20; t++)
                {
                    if (variant == 1 && t >= BesiegeFrom && t - BesiegeFrom < Besiegers)
                    {
                        Besiege(a, t - BesiegeFrom);
                        Besiege(b, t - BesiegeFrom);
                    }
                    Idle(a, 1);
                    Idle(b, 1);
                    Check(a.Hash() == b.Hash(), (variant == 0 ? "undamaged" : "besieged") + " site hash drift at tick " + t);
                }
            }
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
