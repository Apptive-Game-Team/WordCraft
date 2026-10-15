namespace WordCraft.Sim
{
    /// <summary>
    /// 오토캐스트, docs/TECH-TREE.md 묶음 7: the abilities that go off on their
    /// own, and the clocks they run on. Two numbers on a body are state,
    /// AbilityCooldown and AbilityTicksLeft, and a body holds at most one
    /// ability that uses them; every other ability here is a question asked of
    /// the bodies standing around, the way an aura is.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// Runs after ExpirySystem and before CombatSystem. First every body, by
        /// ascending id: its cooldown comes down one tick.
        /// </summary>
        private void AbilitySystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.AbilityCooldown == 0) continue;
                e.AbilityCooldown--;
                entities[i] = e;
            }
        }

        /// <summary>
        /// Scenario setup only. Never call this from a system. Lets a harness put
        /// two worlds one ability clock apart, or a body in the middle of a roll,
        /// which no order can do.
        /// </summary>
        public void SetAbilityForScenario(int id, int cooldown, int ticksLeft)
        {
            Entity e = entities[id];
            e.AbilityCooldown = cooldown;
            e.AbilityTicksLeft = ticksLeft;
            entities[id] = e;
        }
    }
}
