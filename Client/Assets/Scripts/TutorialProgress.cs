using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>Read-only, per-match tutorial evidence. Observe after every simulation tick.</summary>
    public sealed class TutorialProgress
    {
        public const int StepCount = 8;
        private readonly World source;
        private readonly int peer;
        private readonly int openingCount;
        private readonly int[] openingUpgrades;
        private readonly bool[] done = new bool[StepCount];
        private Entity[] previous;
        private int bank;
        private int observedTick;
        public int Step { get; private set; }
        public bool Completed => Step == StepCount;
        private static readonly string[] titles = {
            "일꾼 선택", "마나 채집", "풀씨 둥지 건설", "병력 생산",
            "덩쿨 정령으로 성장", "광란의 토템 건설", "기술 연구", "첫 전투", "실습 완료" };
        private static readonly string[] instructions = {
            "기지 주변의 풀씨 정령을 클릭하세요. 목표 보기로 위치를 찾을 수 있습니다.",
            "일꾼으로 마나 광맥을 우클릭하세요. 운반을 마치면 완료됩니다. 일꾼은 생명의 나무에서 U로 생산합니다. 본진을 선택해 광맥을 우클릭하면 새 일꾼도 자동 채집합니다.",
            "일꾼의 건설(V) 메뉴에서 풀씨 둥지를 골라 빈 땅에 놓으세요. 건설이 끝날 때까지 기다리세요.",
            "풀씨 둥지를 선택하고 잎날 정령을 생산하세요. 대기열의 생산이 완료되어야 합니다.",
            "풀씨 정령 한 기를 선택하고 성장(H) 메뉴에서 덩쿨 정령을 선택하세요. 다른 일꾼은 채집을 계속하게 두세요.",
            "건설 메뉴의 더 보기(M)를 사용하세요. 광란의 토템을 건설하세요. 풀씨 둥지가 완공되어 있어야 합니다.",
            "완성된 광란의 토템을 선택하고 연구를 하나 시작하세요. 마나를 모으고 연구 완료를 기다리세요.",
            "잎날 정령 5기 이상을 모아 더블클릭으로 함께 선택하세요. 목표 보기를 누른 뒤 A와 화면 중앙 클릭으로 공격 이동하세요. 적은 반격하므로 소수만 보내지 마세요.",
            "선택, 채집, 건설, 생산, 성장, 연구와 전투를 모두 익혔습니다. 이제 AI와의 솔로 대전에 도전하세요." };
        public string Title => titles[Step];
        public string Instructions => instructions[Step];

        public TutorialProgress(World world, int localPeer)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (localPeer < 0 || localPeer >= World.MaxPeers) throw new ArgumentOutOfRangeException(nameof(localPeer));
            source = world;
            peer = localPeer;
            openingCount = world.EntityCount;
            openingUpgrades = new int[FactionData.UpgradeCount(world.FactionOf(peer))];
            for (int i = 0; i < openingUpgrades.Length; i++) openingUpgrades[i] = world.UpgradeLevel(peer, i);
            Snapshot(world);
        }

        public void Observe(World world, IReadOnlyList<int> selection)
        {
            if (!ReferenceEquals(world, source)) return;
            if (selection != null)
                foreach (int id in selection)
                    if (id >= 0 && id < world.EntityCount)
                    {
                        Entity e = world.GetEntity(id);
                        if (e.Alive && e.Owner == peer && e.Kind == EntityKind.Worker) done[0] = true;
                    }
            if (world.Tick != observedTick)
            {
                for (int id = 0; id < world.EntityCount; id++)
                {
                    Entity e = world.GetEntity(id);
                    Entity old = id < previous.Length ? previous[id] : default;
                    if (old.Alive && old.Owner >= 0 && old.Owner != peer && old.Kind == EntityKind.Unit && !e.Alive)
                        done[7] = true;
                    if (!e.Alive || e.Owner != peer) continue;
                    // A deposit has both a carried-load transition and actual income.
                    // Selection, a gather order, a refund or a bank grant alone cannot pass.
                    if (e.Kind == EntityKind.Worker && old.CarryAmount > 0 && e.CarryAmount == 0 &&
                        e.GatherNodeId >= 0 && !World.IsGrowing(e) && world.GetResources(peer) > bank)
                        done[1] = true;
                    if (e.Kind == EntityKind.Building && e.BuildTicksLeft == 0 && id >= openingCount)
                    {
                        if (e.Role == Role.Production && e.Slot == 0) done[2] = true;
                        if (e.Role == Role.Tech && e.Slot == 2) done[5] = true;
                    }
                    if (e.Kind == EntityKind.Unit && id >= openingCount &&
                        FactionData.Production(world.FactionOf(peer), e.Role, e.Slot).Produced) done[3] = true;
                    if (e.Kind == EntityKind.Unit && e.Role == Role.Ranged && e.Slot == 1 &&
                        old.MorphTicksLeft > 0 && old.MorphRole == Role.Ranged && old.MorphSlot == 1 && !World.IsGrowing(e))
                        done[4] = true;
                }
                for (int i = 0; i < openingUpgrades.Length; i++)
                    if (world.UpgradeLevel(peer, i) > openingUpgrades[i]) done[6] = true;
                Snapshot(world);
            }
            while (Step < StepCount && done[Step]) Step++;
        }

        private void Snapshot(World world)
        {
            previous = new Entity[world.EntityCount];
            for (int i = 0; i < previous.Length; i++) previous[i] = world.GetEntity(i);
            bank = world.GetResources(peer);
            observedTick = world.Tick;
        }

        public int FocusEntityId(World world)
        {
            if (!ReferenceEquals(world, source) || Completed) return -1;
            if (Step == 1)
            {
                int worker = -1;
                for (int id = 0; id < world.EntityCount; id++)
                {
                    Entity e = world.GetEntity(id);
                    if (e.Alive && e.Owner == peer && e.Kind == EntityKind.Worker) { worker = id; break; }
                }
                if (worker < 0) return -1;
                int nearest = -1;
                Fix distance = Fix.Zero;
                FixVec2 origin = world.GetEntity(worker).Position;
                for (int id = 0; id < world.EntityCount; id++)
                {
                    Entity e = world.GetEntity(id);
                    if (!e.Alive || e.Kind != EntityKind.ResourceNode || e.Resource <= 0) continue;
                    Fix candidate = (e.Position - origin).SqrMagnitude;
                    if (nearest < 0 || candidate < distance) { nearest = id; distance = candidate; }
                }
                return nearest;
            }
            if (Step == 7)
            {
                int weakest = -1;
                for (int id = 0; id < world.EntityCount; id++)
                {
                    Entity e = world.GetEntity(id);
                    if (e.Alive && e.Owner >= 0 && e.Owner != peer && e.Kind == EntityKind.Unit &&
                        (weakest < 0 || e.Hp < world.GetEntity(weakest).Hp)) weakest = id;
                }
                return weakest;
            }
            for (int id = 0; id < world.EntityCount; id++)
            {
                Entity e = world.GetEntity(id);
                if (!e.Alive) continue;
                if (e.Owner != peer) continue;
                if ((Step <= 2 || Step == 4 || Step == 5) && e.Kind == EntityKind.Worker && !World.IsGrowing(e)) return id;
                if (Step == 3 && e.Role == Role.Production && e.Slot == 0 && e.BuildTicksLeft == 0) return id;
                if (Step == 6 && e.Role == Role.Tech && e.Slot == 2 && e.BuildTicksLeft == 0) return id;
            }
            return -1;
        }
    }
}
