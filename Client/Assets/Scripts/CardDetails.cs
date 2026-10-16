using System.Globalization;
using System.Text;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>Read-only roster details shared by command-card hover surfaces.</summary>
    public static class CardDetails
    {
        public static string Text(World world, int peer, CardSlot slot)
        {
            string fallback = string.IsNullOrEmpty(slot.Description) ? slot.Label : slot.Description;
            if (world == null || peer < 0 || peer >= World.MaxPeers || slot.Produce == Role.None ||
                (slot.Type != CommandType.Produce && slot.Type != CommandType.Build && slot.Type != CommandType.Grow))
                return fallback ?? "";
            Faction faction = world.FactionOf(peer);
            Role role = slot.Produce;
            int index = slot.Slot;
            if (!FactionData.Has(faction, role, index)) return fallback ?? "";

            int cost, ticks;
            string action;
            bool produce = slot.Type == CommandType.Produce;
            bool grow = slot.Type == CommandType.Grow;
            if (produce)
            {
                ProductionCost price = FactionData.Production(faction, role, index);
                if (!price.Produced) return fallback ?? "";
                cost = price.Resources; ticks = price.Ticks; action = "생산";
            }
            else if (grow)
            {
                GrowthCost price = FactionData.Growth(faction, role, index);
                if (!price.Grown) return fallback ?? "";
                cost = price.Resources; ticks = world.GrowthTicksFor(peer, role, index); action = "성장";
            }
            else
            {
                cost = FactionData.BuildCost(faction, role, index);
                ticks = FactionData.BuildTicks(faction, role, index); action = "건설";
            }
            var text = new StringBuilder(FactionData.Name(faction, role, index));
            UnitStats stats = FactionData.Stats(faction, role, index);
            text.Append(" · ").Append(RoleName(role, stats)).Append(" · ").Append(action);
            text.Append("\n").Append(Purpose(role, stats));
            text.Append("\n기본: 체력 ").Append(stats.Hp).Append(" · 공격 ").Append(stats.Damage);
            if (stats.HasWeapon)
                text.Append(" · 사거리 ").Append((stats.Range.Raw / (double)Fix.One).ToString("0.#", CultureInfo.InvariantCulture));
            text.Append("\n마나 ").Append(cost).Append(" · ")
                .Append((ticks / (float)World.TicksPerSecond).ToString("0.#", CultureInfo.InvariantCulture)).Append("초");
            if (produce) text.Append(" · 인구 1");
            else if (grow) text.Append(" · 추가 인구 없음");
            else if (role == Role.Base) text.Append(" · 인구 한도 +").Append(World.PopulationPerBase);
            else if (role == Role.Supply) text.Append(" · 인구 한도 +").Append(World.PopulationPerSupply);

            int tier = FactionData.Tier(faction, role, index);
            text.Append("\n필요 단계 T").Append(tier);
            if (world.TierOf(peer) < tier) text.Append(" (미달)");
            var needs = FactionData.Prerequisite(faction, role, index);
            if (needs.Role != Role.None)
                text.Append(" · ").Append(FactionData.Name(faction, needs.Role, needs.Slot))
                    .Append(world.HasStanding(peer, needs.Role, needs.Slot) ? " (완공)" : " (완공 필요)");
            if (grow)
            {
                GrowthCost source = FactionData.Growth(faction, role, index);
                text.Append("\n").Append(FactionData.Name(faction, source.FromRole, source.FromSlot)).Append(" 1기가 성장 · 비용 환불 없음");
            }
            else if (produce)
            {
                text.Append("\n생산 건물: ");
                bool first = true;
                for (int i = 0; i < FactionData.MaxProducers; i++)
                {
                    var producer = FactionData.ProducedAt(faction, role, index, i);
                    if (producer.Role == Role.None) break;
                    if (!first) text.Append(" / ");
                    text.Append(FactionData.Name(faction, producer.Role, producer.Slot));
                    first = false;
                }
                text.Append(" · 완공 필요");
                if (world.GetPopulation(peer) >= world.PopulationCap(peer)) text.Append("\n인구 한도 도달");
            }
            else text.Append("\n일꾼과 건설 가능한 빈 공간 필요");
            if (world.GetResources(peer) < cost) text.Append("\n마나 부족: ").Append(cost - world.GetResources(peer)).Append(" 추가 필요");
            return text.ToString();
        }

        private static string Purpose(Role role, UnitStats stats)
        {
            switch (role)
            {
                case Role.Worker: return "자원을 채집하고 건물을 짓습니다.";
                case Role.Base: return "일꾼을 생산하고 인구 한도를 늘립니다.";
                case Role.Production: return "병력을 생산합니다.";
                case Role.Defense: return "적의 접근을 막는 방어 시설입니다.";
                case Role.Supply: return "더 많은 병력을 위한 인구 한도를 제공합니다.";
                case Role.Tech: return "새 기술과 업그레이드를 연구합니다.";
                default:
                    return (stats.Air ? "비행" : "지상") + (stats.HasWeapon
                        ? (stats.HitsAir ? " · 공중 공격 가능" : " · 지상 공격")
                        : " · 비전투 지원 유닛");
            }
        }

        private static string RoleName(Role role, UnitStats stats)
        {
            switch (role)
            {
                case Role.Worker: return "일꾼";
                case Role.Melee:
                case Role.Ranged: return !stats.HasWeapon ? "지원 유닛" : MatchView.Throws(stats) ? "원거리 유닛" : "근접 유닛";
                case Role.Signature: return "특수 유닛";
                case Role.Base: return "본진";
                case Role.Production: return "생산 건물";
                case Role.Defense: return "방어 건물";
                case Role.Supply: return "인구 지원 건물";
                case Role.Tech: return "기술 건물";
                default: return "";
            }
        }
    }
}
