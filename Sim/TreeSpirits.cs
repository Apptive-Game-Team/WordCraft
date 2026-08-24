namespace WordCraft.Sim
{
    /// <summary>
    /// 세계수 정령 성장: a 풀씨 정령 pays resources and, over a fixed number of
    /// ticks, stops being a worker. It comes back up a 덩쿨 정령 or a 고목 수호자
    /// on the same entity id, under the same owner, standing on the same cell.
    /// 전투 유닛 한 기는 언제나 일꾼 한 기의 상실이다: this faction turns bodies into
    /// an army rather than resources, so every fighter it fields is an economy it
    /// no longer has.
    ///
    /// This is the first rule in the simulation that changes what a body is.
    /// 징발 moved who owns one; growth moves its kind, its role, its entry, its hp,
    /// its ceiling and its speed, all under an owner that does not change. What
    /// follows is the audit that transfer needs, written the way the ownership one
    /// was: every system that reads Kind or Role, in the order Step runs them, and
    /// what each of them does on the tick the swap happens.
    ///
    /// Nothing needs a line of its own, and each for a reason worth stating.
    ///
    /// AiSystem reads both, and reads them fresh every think: AiCountWorkers and
    /// the worker loop key on Kind == Worker, AiIsFreeFighter on Kind == Unit and
    /// Role != Worker. A body that grows leaves the first set and joins the second
    /// on the same tick, which is what the mechanic means — the opponent's worker
    /// count drops by one and its squad count rises by one, both correctly, because
    /// neither number is carried anywhere. The ladder never orders a growth, so an
    /// AI 세계수 정령 plays without the mechanic rather than badly with it.
    ///
    /// Apply reads Kind in six places to refuse an order to the wrong sort of body.
    /// None of them can see a half-grown one: the gate at the top of Apply refuses
    /// every order naming a growing body before the switch is reached.
    ///
    /// GatherSystem keys on Kind == Worker and on GatherNodeId. A growth starts
    /// with ClearOrders, which drops the node, so the loop skips the body from the
    /// first tick of the growth to the last — 채집 불가, with nothing in GatherSystem
    /// that had to learn about growth. On the completion tick the same body is a
    /// Unit and could not re-enter the loop even holding a node.
    ///
    /// CaptureSystem keys on Kind == Worker and CaptureTargetId, and the same
    /// ClearOrders drops the capture. A 세계수 정령 cannot capture in the first
    /// place, so this is belt on top of braces, but the braces are the ones that
    /// matter: the two worker loops are mutually exclusive because every order
    /// routes through ClearOrders, and growth joins that set rather than sitting
    /// beside it.
    ///
    /// ConstructionSystem and ProductionSystem key on Kind == Building. A growing
    /// worker is a Worker and then a Unit, so neither ever sees it. PopulationCap
    /// and TierOf read Role off buildings only, for the same reason.
    ///
    /// WarlordSpawnSystem reads Kind and Role through IsWarlord, which also asks
    /// the faction. 세계수 정령 is not 지옥불, so this answers false before and
    /// after. The order matters anyway: growth runs before it, so a body that
    /// became a Signature this tick would be armed for this tick's spawn pass, and
    /// the faction test is what makes that a fact about 지옥불 rather than luck.
    ///
    /// CombatSystem is where the swap is visible. CanAttack asks Kind == Unit and
    /// Armed asks the roster row: a growing body is a Worker on the worker row,
    /// which carries no weapon, so it acquires nothing and fires nothing for every
    /// tick of the growth — 공격 불가, out of two facts already written rather than
    /// a third. On the completion tick it is a Unit on its new row, and it fights
    /// that tick, exactly as a 자손 emitted this tick fights this tick and a 징발
    /// that lands this tick shoots this tick. VolleyBonus reads Kind and Role to
    /// count 물 슬라임 archers and answers zero here for the same faction reason
    /// IsWarlord does. AcquireTarget reads Kind only to skip resource nodes: a
    /// growing worker is a legal target throughout, which is the whole of 성장 중
    /// 죽으면 그냥 죽는다.
    ///
    /// MoveSystem reads no Kind at all — it reads Speed, Mode and the path. Growth
    /// takes the walk away by calling Halt, which is what the capture does and for
    /// the same reason: the walk is what would have moved the body, so dropping it
    /// is the whole of 이동 불가 and MoveSystem needs to learn nothing. Nothing can
    /// give the body a new destination, because every path to SetDestination runs
    /// through Apply or through a system that has already skipped it.
    ///
    /// VictorySystem reads Kind and Role to find bases. A worker is not one and
    /// neither is what it becomes, so a growth cannot decide a match.
    ///
    /// Pathfinder's obstacle pass keys on Kind, and this is the one that would have
    /// bitten. It blocks a cell for a ResourceNode or a Building and for nothing
    /// else, so Worker and Unit are on the same side of it: a body crossing the
    /// swap does not move in or out of the blocked grid and no peer has to repath
    /// on the tick it changes. Had either kind blocked, every standing path in the
    /// match would have had to be recomputed on that tick, on both peers, in the
    /// same order.
    ///
    /// And the counter 징발 had to move by hand does not move here.
    /// CountsAgainstPopulation answers true for Unit and for Worker alike, so a
    /// body that was counted once when it entered the world is still counted once
    /// after the swap and is released once when it dies. A growth that touched
    /// population[] would be the defect, not the fix.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// The one faction that grows. Read off the roster's faction rather than
        /// carried anywhere, so nothing can disagree with it — and asked only
        /// through FactionData.Growth, which is keyed by faction already.
        /// </summary>
        public const Faction GrowthFaction = Faction.TreeSpirits;

        /// <summary>The slot 덩쿨 정령 occupies, and which entry of it.</summary>
        public const Role VineRole = Role.Ranged;

        /// <summary>
        /// Entry 1. Named rather than left to default for the reason
        /// WarlordOffspringSlot is: 번개 정령 holds entry 0 of the same slot and is
        /// a tier-3 unit, and nothing about a growth target would have said which
        /// of the two a worker had paid to become.
        /// </summary>
        public const int VineSlot = 1;

        /// <summary>The slot 고목 수호자 occupies, and which entry of it.</summary>
        public const Role GuardianRole = Role.Melee;

        /// <summary>
        /// Entry 1, for the same reason VineSlot is. 잎날 정령 holds entry 0 of the
        /// melee slot and is what a 풀씨 둥지 makes; entry 1 is what a worker
        /// becomes. Entry 0 of each combat slot is bought and entry 1 is grown, and
        /// that is the whole of 세계수 정령's roster shape.
        /// </summary>
        public const int GuardianSlot = 1;

        /// <summary>
        /// True while this body is mid-growth. The one predicate the rest of the
        /// simulation asks, and the tick clock is the whole of it: there is no
        /// second field that could disagree about whether a growth is running.
        ///
        /// Public because the harness asserts on it and, one day, the view draws
        /// it. Neither may write it.
        /// </summary>
        public static bool IsGrowing(Entity e) => e.MorphTicksLeft > 0;

        /// <summary>
        /// Every rule a Grow has to pass, and the only place they are written.
        /// Refused whole: a Grow that fails any of them spends nothing and starts
        /// nothing, because guessing what the player meant would have each peer
        /// guess for itself.
        ///
        /// The population cap is not consulted, for the reason a capture at the cap
        /// completes: growth adds no body. The peer had this one already and will
        /// have exactly it afterwards, so there is no supply to be over.
        ///
        /// Neither is the tech tier. A tier is what a production building opens,
        /// and 세계수 정령's answer to production is that it does not produce: the
        /// document prices growth in workers and ticks and says nothing about
        /// buildings, and a tier gate here would be a rule the document does not
        /// have and no player could see.
        /// </summary>
        private void TryGrow(int peer, int id, Role role, int slot)
        {
            Entity w = entities[id];
            // A worker, not an army. 일꾼이 자란다 is the mechanic, and refusing
            // rather than reinterpreting keeps a 고목 수호자 from being ordered to
            // grow into a second one.
            if (w.Kind != EntityKind.Worker) return;

            // The table is the faction test, the roster test and the price in one
            // question. Five factions out of six have no row here at all, and the
            // entry a 세계수 정령 player can name is the entry the table lists.
            GrowthCost cost = FactionData.Growth(factions[peer], role, slot);
            if (!cost.Grown) return;
            if (resources[peer] < cost.Resources) return;

            // Taken in full, up front, and never given back. That is what makes the
            // decision the mechanic is about a real one: a scouting mistake costs
            // the mana and the worker both.
            resources[peer] -= cost.Resources;

            // A growth is an order like any other, so it replaces the one running:
            // a worker mid-delivery drops its load and a worker mid-capture drops
            // its progress. It is the last order the body will ever take, because
            // Apply refuses every order naming a growing body from here on.
            ClearOrders(ref w);
            // 그동안 이동하지 않는다, dropped rather than expressed as a mode of its
            // own. See the audit above.
            Halt(id, ref w);

            w.MorphRole = role;
            w.MorphSlot = slot;
            w.MorphTicksLeft = cost.Ticks;
            entities[id] = w;
        }

        /// <summary>
        /// Runs beside the other two worker loops and immediately after them,
        /// because it is the worker's third loop and all three are mutually
        /// exclusive by construction: every order routes through ClearOrders, so a
        /// worker gathers, captures, or grows, and never two of them.
        ///
        /// Before combat, so a growth that completes on this tick fights on this
        /// tick, exactly as an offspring emitted this tick fights this tick. That
        /// also fixes the death boundary at one end: deaths happen in combat, which
        /// is after this, so a growth that has already finished is finished and one
        /// that has not is a worker when the shot lands.
        ///
        /// By ascending entity id, so two workers finishing on the same tick take
        /// their new rows in the same order on every peer.
        /// </summary>
        private void GrowthSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || !IsGrowing(e)) continue;

                GrowStep(ref e);
                entities[i] = e;
            }
        }

        /// <summary>
        /// One tick of growth: the hp ramp, and on the last tick the swap itself.
        ///
        /// 체력은 목표치까지 선형 증가한다: the ceiling is a straight line from the
        /// row the body is leaving to the row it is joining, taken as a fraction of
        /// the whole growth rather than as a step from wherever it currently is.
        /// The same shape ConstructionSystem's ramp has, and for the same reason —
        /// a step recomputed from the remainder each tick truncates downward every
        /// time and bends the line, arriving on the target by catching up at the
        /// end rather than by climbing evenly.
        ///
        /// Neither end of the line is carried as state. The row it is joining is
        /// MorphRole and MorphSlot; the row it is leaving is the one it is still
        /// standing on, read through the same accessor every other system reads a
        /// body's stats through. A field holding the hp it started with would be a
        /// fourth hashed number saying what the roster already says.
        ///
        /// The ceiling is what the line moves, and the damage rides along with it.
        /// That is the difference between a ramp and a recomputed hp, and it is the
        /// whole reason it is written as a delta: hp assigned from the formula each
        /// tick would undo every hit the body took, and 성장 중 죽으면 그냥 죽는다
        /// would be unreachable — a growing body would be immortal for the duration.
        /// </summary>
        private void GrowStep(ref Entity e)
        {
            UnitStats from = RosterStats(e);
            UnitStats s = FactionData.Stats(factions[e.Owner], e.MorphRole, e.MorphSlot);
            int total = FactionData.Growth(factions[e.Owner], e.MorphRole, e.MorphSlot).Ticks;

            e.MorphTicksLeft--;
            // Integer division, truncating toward zero, which C# defines and both
            // runtimes therefore do the same way. The last tick multiplies by a
            // remaining count of zero, so the line lands on the target exactly
            // rather than near it.
            int ceiling = s.Hp - ((s.Hp - from.Hp) * e.MorphTicksLeft) / total;
            e.Hp += ceiling - e.MaxHp;
            e.MaxHp = ceiling;
            // Both rows in the growth table stand above a worker, so the line only
            // ever climbs in play. The floor is what keeps a row that ever went the
            // other way from killing a body outside Kill, which is the one place a
            // body may stop existing.
            if (e.Hp < 1) e.Hp = 1;

            if (e.MorphTicksLeft > 0) return;

            // The swap. The same entity id, the same owner, the same cell: 새 개체가
            // 아니라 같은 엔티티의 종류가 바뀐다, so nothing is added and nothing is
            // killed, and every id any peer or any replay already holds still names
            // this body.
            e.Kind = EntityKind.Unit;
            e.Role = e.MorphRole;
            e.Slot = e.MorphSlot;
            // The ramp has already carried MaxHp to exactly this, and it is written
            // again so the completion says the whole of what a finished growth is
            // rather than leaving a third of it implied by arithmetic above.
            e.MaxHp = s.Hp;
            e.Speed = s.Speed;
            // Hp arrives through the ramp rather than being handed over here, which
            // is what makes the ramp mean anything: a body shot while it grew comes
            // up short, and one nobody touched comes up full. The clamp is for a
            // roster row that ever grew downward.
            if (e.Hp > e.MaxHp) e.Hp = e.MaxHp;

            // The worker's economy state goes with the worker. A Unit has no gather
            // loop to read any of it, and a carried load is not something a body
            // that is no longer a worker can deliver.
            e.CarryAmount = 0;
            e.DropOffId = -1;
            e.GatherTicksLeft = 0;

            ClearGrowth(ref e);
        }

        /// <summary>
        /// Puts a body back in the not-growing state. Static and by ref for the
        /// reason ClearCapture is: it is called from inside loops that hold an
        /// entity by value and write it back once.
        ///
        /// MorphRole and MorphSlot are cleared as well as the clock. Nothing reads
        /// them once MorphTicksLeft is zero, but they are hashed, and a finished
        /// growth that left them standing would put a body's whole history into the
        /// hash: two bodies identical in every way a system can see would hash apart
        /// because one of them used to be a worker.
        /// </summary>
        private static void ClearGrowth(ref Entity e)
        {
            e.MorphTicksLeft = 0;
            e.MorphRole = Role.None;
            e.MorphSlot = 0;
        }
    }
}
