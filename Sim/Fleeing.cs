namespace WordCraft.Sim
{
    /// <summary>
    /// 차원 유랑종 경계 운반자 도주: the one body in the game that answers a fight
    /// by leaving it. docs/FACTION-MECHANICS.md gives it 비전투 and 도주 in the same
    /// row; #138 delivered the first half, and a body that is 비전투 and nothing
    /// else stands still and is killed, which is the opposite of the second.
    ///
    /// The whole mechanic is one sentence: while something that could shoot it
    /// stands within <see cref="AcquireRange"/>, an idle 경계 운반자 walks away from
    /// the nearest of them. One radius decides when the flight starts, how far it
    /// aims, and when it ends, so there is no second threshold for two peers to
    /// disagree over and no hysteresis rule to write down.
    ///
    /// Like the 통로 it belongs to, this decides on one tick and acts on another,
    /// and what follows is the same audit Driftworlds.cs writes: everything that
    /// can change between the two moments, and what answers it.
    ///
    /// The two moments are one system apart. FleeSystem decides and MoveSystem
    /// walks, and they are adjacent in Step's system order with nothing between
    /// them — nothing in that gap writes a position, kills a body, or takes an
    /// order. That adjacency is the answer to most of what follows, and it is a
    /// consequence of the system order rather than a rule written here, which is
    /// exactly why it is the same on both peers and why moving either system past
    /// the other would move it.
    ///
    /// The threat can die. Deaths happen in CombatSystem, which runs immediately
    /// before this one, so a body killed on tick T is not fled from on tick T. The
    /// carrier can die in the same pass, and the loop below skips it for the same
    /// reason. Both boundaries fall out of the order rather than out of a test.
    ///
    /// The threat can move — it is usually walking straight at the carrier. It
    /// moves in MoveSystem, which runs after this, so the positions read here are
    /// the ones both peers already hashed at the end of the previous tick. The
    /// flight is always one tick behind the chase, and it is one tick behind it on
    /// both peers alike. That is also why the direction is recomputed every tick
    /// instead of being frozen: a frozen direction would be a verdict about a
    /// moment, and the only field it could live in is a field nobody has to have.
    ///
    /// A threat can arrive rather than approach. ProductionSystem and
    /// WarlordSpawnSystem both run before combat and therefore before this, so a
    /// body that stood up on tick T is fled from on tick T — the same boundary an
    /// offspring's first shot already gets.
    ///
    /// An order can arrive. Apply runs first in the whole tick, so an order given
    /// on tick T has already ended the flight by the time this asks, and the walk
    /// it set is the walk MoveSystem performs. There is no tick in which a body is
    /// under an order and fleeing at once.
    ///
    /// Nothing else about the carrier can change at all, and it is worth saying
    /// rather than assuming. Owner moves only through 인간 징발, which transfers a
    /// neutral 꼬마돌 and nothing else; Role and Slot move only through 세계수 정령
    /// 성장, which is one faction's workers. Neither can reach a 차원 유랑종 body,
    /// so a 경계 운반자 is a 경계 운반자 for its whole life.
    ///
    /// And the thing this must not do is choose. It picks the nearest threat, and
    /// only to take a direction from it — there is no friendly anchor to run to, no
    /// nearest base, no safest corner. Each of those is a judgement that needs a
    /// tie-break, and the same sentence Driftworlds.cs writes about 가장 가까운
    /// applies here: a direction needs none. The nearest threat itself is a
    /// selection, so it breaks its tie the way AcquireTarget and FindNearestNode
    /// already do — strictly-less, which keeps the lowest entity id — and it is
    /// asked by an ascending scan for the same reason.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// True for a body 도주 belongs to: a living 경계 운반자 and nothing else.
        /// Read off faction, kind, role and entry rather than carried on the
        /// entity, for the reason IsWarlord and IsPassage are — none of the four
        /// ever changes for a given body, so making it state would add a hashed
        /// field that can only ever hold one value.
        ///
        /// Public because Replay asks it of a spawned body: 비전투 is only half of
        /// what the roster row promises, and the other half has to be answerable
        /// about a real body rather than about the table.
        /// </summary>
        public bool CanFlee(Entity e) =>
            e.Kind == EntityKind.Unit && e.Role == CarrierRole && e.Slot == CarrierSlot &&
            e.Owner >= 0 && e.Owner < MaxPeers && factions[e.Owner] == PassageFaction;

        /// <summary>
        /// Runs after combat and immediately before the mover, which is the whole
        /// of the audit above: a body killed this tick is not fled from, and the
        /// direction decided here is walked this tick with nothing in between.
        ///
        /// By ascending entity id, so two carriers deciding on the same tick decide
        /// in the same order on every peer. Nothing here reads another carrier, so
        /// the order cannot reach the result either; it is an ascending loop
        /// because every simulation loop is one.
        /// </summary>
        private void FleeSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || !CanFlee(e)) continue;

                // 어떤 이유로도 움직이지 않는다 is what Hold means, and 도주 is a
                // reason like any other. This is the one order that outranks the
                // flight for good rather than only while it runs, and it is the
                // player's way of saying stand there — skipped whole rather than
                // decided and then dropped by the mover, because Target and
                // Fleeing are both hashed and a held body must write neither.
                if (e.Mode == OrderMode.Hold) continue;

                int threat = NearestThreat(e);
                if (threat < 0)
                {
                    // 도주가 끝나는 곳. The walk is dropped rather than left
                    // standing, because the destination was never a place the
                    // carrier was going: it was a direction, and a direction that
                    // has stopped meaning anything is a unit walking to a corner
                    // of the map for a reason nobody can see. Only a body that was
                    // actually fleeing is halted, so a carrier standing still under
                    // its owner's last Move order is left exactly as it was found.
                    if (e.Fleeing)
                    {
                        e.Fleeing = false;
                        Halt(i, ref e);
                        entities[i] = e;
                    }
                    continue;
                }

                // 명령이 도주를 이긴다, for as long as the order is running. The
                // same rule CombatSystem already keeps about a chase: an automatic
                // behaviour never overrides what the player told the body to do.
                // A flight already under way is not an order and does not get the
                // protection, which is what lets the direction be recomputed.
                if (!e.Fleeing && !WalkIsOver(i, e)) continue;

                FixVec2 away = (e.Position - entities[threat].Position).Normalized();
                // Standing on the same point as its killer, there is no direction
                // to leave in. Normalized already answers zero rather than dividing
                // by it, so both peers get the same nothing; the body holds still
                // for this tick and the threat's own step gives it a direction back
                // on the next. Fleeing is deliberately left as it was: this is not
                // the flight ending, it is a tick with no answer.
                if (away.Equals(FixVec2.Zero)) continue;

                e.Fleeing = true;
                // Snapped to the cell centre, which buys two things for one line.
                // CellOf clamps, so the destination is always a cell on the map and
                // a cornered carrier aims at the edge rather than off it; and the
                // point only moves when the carrier crosses a cell boundary, so
                // Retarget's equality test skips the repath on most ticks.
                //
                // Pathfound like any other walk rather than run at as a straight
                // line. A pursuit may walk into a lake and stop, because a pursuit
                // that loses its quarry to the terrain has lost nothing; a flight
                // that does it has died. This is the one automatic walk in the
                // simulation whose whole purpose is to arrive somewhere.
                Retarget(i, ref e, CellCenter(CellOf(e.Position + away * AcquireRange)));
                entities[i] = e;
            }
        }

        /// <summary>
        /// Whether this body's walk is finished: it has run out of path and stands
        /// exactly on its destination. Exact, because MoveSystem lands a body on
        /// its goal rather than near it — the last step is clamped to the goal — so
        /// there is no epsilon here for two peers to round differently.
        ///
        /// Stricter than PathDone alone, which goes true on the last cell of the
        /// route while the body is still walking the remainder in a straight line.
        /// CombatSystem is content with the looser test because a chase that breaks
        /// off a little early costs a step; a flight that did it would overwrite a
        /// player's Move order a second before it landed.
        /// </summary>
        private bool WalkIsOver(int id, Entity e) => PathDone(id) && e.Position.Equals(e.Target);

        /// <summary>
        /// The nearest body that could shoot this one, inside AcquireRange, or -1.
        ///
        /// AcquireTarget read from the other side, and deliberately the same
        /// radius: the question 도주 asks is whether something is about to point a
        /// weapon at this body, and AcquireRange is already the one number in the
        /// simulation that answers it. A radius of its own would be a second number
        /// meaning the same thing, and the tick the two disagreed would be a body
        /// standing inside a weapon's reach because its own rule said it was safe.
        ///
        /// CanAttack and CanHit are asked of the threat rather than of the carrier,
        /// which is what keeps the rule about danger instead of about hostility. A
        /// worker cannot shoot, an unfinished turret cannot shoot yet, and a 대포
        /// cannot reach what flies — none of the three is a reason to run, and a
        /// carrier that fled from a worker would be a carrier no 차원 유랑종 player
        /// could ever park anywhere.
        ///
        /// Distance goes through the same SqrMagnitude comparison every other range
        /// question in the simulation asks, and takes no square root. A second
        /// distance calculation invented here would be a second rounding for two
        /// peers to disagree over.
        /// </summary>
        // ponytail: a linear scan per carrier per tick, which is the ceiling
        // AcquireTarget and VolleyBonus already live under. All three go to a grid
        // bucket broad phase on the same day.
        private int NearestThreat(Entity body)
        {
            int best = -1;
            Fix bestDist = Fix.Zero;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity t = entities[i];
                if (!t.Alive || t.Owner < 0 || t.Owner == body.Owner) continue;
                if (!CanAttack(t) || !CanHit(t, body)) continue;

                Fix d = (t.Position - body.Position).SqrMagnitude;
                if (d > AcquireRange * AcquireRange) continue;
                // Strictly-less: the first, lowest-id threat wins an exact tie,
                // exactly as it does in AcquireTarget and FindNearestNode.
                if (best < 0 || d < bestDist) { best = i; bestDist = d; }
            }
            return best;
        }
    }
}
