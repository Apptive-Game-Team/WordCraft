namespace WordCraft.Sim
{
    /// <summary>
    /// 상태 이상과 오라, docs/TECH-TREE.md 묶음 5. Two numbers are state, and
    /// only two: how long a body stays slowed and how long it keeps burning,
    /// both on the body itself. Every aura is a question asked at the moment a
    /// system needs the number, of the bodies standing around the one it is
    /// asking about, the way 일제 사격 is.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>What a body that burns takes on a burning tick. Fixed damage: armor and levels do not touch it.</summary>
        public const int BurnDamage = 1;

        /// <summary>Every how many ticks of burn the body takes BurnDamage.</summary>
        public const int BurnPeriod = 10;

        /// <summary>
        /// Puts a status clock at the longer of what is left and the new
        /// duration. Never a sum: a second hit renews the status and does not
        /// make it last twice as long.
        /// </summary>
        private static void Refresh(ref int left, int ticks)
        {
            if (left < ticks) left = ticks;
        }

        /// <summary>
        /// 휘감기 and 화염탄: what a landed hit leaves on a target that survived
        /// it. 휘감기 takes ground bodies only, so a flier or a building shot by
        /// a 덩쿨 정령 walks on; 화염탄 sets anything it hits alight. Both refresh
        /// to the longer clock rather than adding.
        /// </summary>
        private void ApplyHitStatus(Entity a, ref Entity t)
        {
            int slow = ActiveAbility(a, AbilityKind.SlowOnHit);
            if (slow >= 0)
            {
                AbilityRow row = FactionData.AbilityRowAt(slow);
                if (AuraTargets(row.Targets, t)) Refresh(ref t.SlowTicksLeft, AbilityTicks(a.Owner, row));
            }
            int burn = ActiveAbility(a, AbilityKind.BurnOnHit);
            if (burn >= 0) Refresh(ref t.BurnTicksLeft, AbilityTicks(a.Owner, FactionData.AbilityRowAt(burn)));
        }

        /// <summary>
        /// How far this body walks this tick: SpeedFor, halved while it is
        /// slowed by 휘감기 or stands in a hostile 덩굴 지대 (the two together
        /// still halve it once), and never past MaxMoveSpeed. Asked by
        /// MoveSystem after CollectAuraSources.
        /// </summary>
        private Fix MoveSpeedFor(Entity e)
        {
            Fix speed = SpeedFor(e);
            if (e.SlowTicksLeft > 0 || InHostileAura(AbilityKind.SlowAura, e)) speed = speed * SlowFactor;
            return speed > MaxMoveSpeed ? MaxMoveSpeed : speed;
        }

        /// <summary>
        /// Runs right after CombatSystem and before FleeSystem, by ascending
        /// entity id. First the clocks every body carries: a tick off 휘감기, a
        /// tick off the burn and, when what is left lands on a multiple of
        /// BurnPeriod, the burn's damage.
        /// </summary>
        private void StatusSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive) continue;
                if (e.SlowTicksLeft == 0 && e.BurnTicksLeft == 0) continue;

                if (e.SlowTicksLeft > 0) e.SlowTicksLeft--;
                if (e.BurnTicksLeft > 0)
                {
                    e.BurnTicksLeft--;
                    if (e.BurnTicksLeft % BurnPeriod == 0)
                    {
                        e.Hp -= BurnDamage;
                        if (e.Hp <= 0) Kill(ref e);
                    }
                }
                entities[i] = e;
            }
        }

        /// <summary>
        /// Scenario setup only. Never call this from a system. Lets a harness put
        /// two worlds one status clock apart, which no order can do without
        /// also moving the hp the hit took.
        /// </summary>
        public void SetStatusForScenario(int id, int slowTicksLeft, int burnTicksLeft)
        {
            Entity e = entities[id];
            e.SlowTicksLeft = slowTicksLeft;
            e.BurnTicksLeft = burnTicksLeft;
            entities[id] = e;
        }
    }
}
