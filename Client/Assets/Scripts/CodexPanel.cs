using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>Read-only roster reference. Never creates or mutates a simulation.</summary>
    public sealed class CodexPanel : MonoBehaviour
    {
        public static CodexPanel Instance { get; private set; }
        public static bool IsOpen { get; private set; }
        public Faction SelectedFaction { get; private set; }
        public Role SelectedRole { get; private set; } = Role.Worker;
        public int SelectedSlot { get; private set; }
        public string Search { get; set; } = "";
        public int Filter { get; set; } // 0 all, 1 units, 2 buildings
        private Vector2 outerScroll, listScroll, detailScroll;
        private GUIStyle body, title, item;
        private readonly List<(Role Role, int Slot)> entries = new List<(Role, int)>();
        private static readonly string[] Factions = { "세계수 정령", "지옥불 군단", "물 슬라임", "돌 골렘 부족", "차원 유랑종", "인간 마법 문명" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance == null) new GameObject("WordCraft Codex").AddComponent<CodexPanel>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        private void OnDestroy() { if (Instance == this) { Instance = null; IsOpen = false; } }
        public static void Toggle() { Boot(); IsOpen = !IsOpen; }
        public static void Close() => IsOpen = false;
        public static bool Covers(Vector2 inputScreen) => IsOpen;
        public void Select(Faction faction, Role role, int slot)
        {
            if (!FactionData.Has(faction, role, slot)) return;
            SelectedFaction = faction; SelectedRole = role; SelectedSlot = slot;
            detailScroll = Vector2.zero;
        }
        public int VisibleCount()
        {
            entries.Clear();
            for (int r = (int)Role.Base; r <= (int)Role.Tech; r++)
            {
                Role role = (Role)r;
                bool building = FactionData.IsBuilding(role);
                if ((Filter == 1 && building) || (Filter == 2 && !building)) continue;
                for (int slot = 0; slot < FactionData.SlotCount(SelectedFaction, role); slot++)
                {
                    if (!FactionData.Has(SelectedFaction, role, slot)) continue;
                    string name = FactionData.Name(SelectedFaction, role, slot);
                    if (!string.IsNullOrWhiteSpace(Search) && name.IndexOf(Search.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                    entries.Add((role, slot));
                }
            }
            return entries.Count;
        }
        private static Texture Portrait(Faction faction, Role role, int slot) => CardPortrait.For(faction,
            new CardSlot { Type = FactionData.IsBuilding(role) ? CommandType.Build : CommandType.Produce, Produce = role, Slot = slot });

        private void Styles()
        {
            if (body != null) return;
            body = new GUIStyle(UiStyle.Toned(UiStyle.Ink)) { fontSize = 15, wordWrap = true, alignment = TextAnchor.UpperLeft };
            title = new GUIStyle(body) { fontSize = 21, fontStyle = FontStyle.Bold };
            item = new GUIStyle(body) { alignment = TextAnchor.MiddleLeft };
        }
        private void OnGUI()
        {
            if (!IsOpen) return;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            { Close(); Event.current.Use(); return; }
            Styles();
            int depth = GUI.depth;
            GUI.depth = -100;
            UiStyle.PanelBox(new Rect(0, 0, Screen.width, Screen.height), UiStyle.Panel);
            GUILayout.BeginArea(new Rect(12, 12, Mathf.Max(100, Screen.width - 24), Mathf.Max(100, Screen.height - 24)));
            outerScroll = GUILayout.BeginScrollView(outerScroll);
            GUILayout.BeginHorizontal();
            GUILayout.Label("유닛 · 건물 도감", title);
            if (GUILayout.Button("닫기 · Esc", GUILayout.Width(105), GUILayout.Height(32))) Close();
            GUILayout.EndHorizontal();
            GUILayout.Label("온라인 경기는 도감을 열어도 계속 진행됩니다.", body);
            // Two rows keep all six faction buttons reachable on small windows.
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 3; col++)
                {
                    int f = row * 3 + col;
                    if (GUILayout.Toggle((int)SelectedFaction == f, Factions[f], "Button", GUILayout.Height(32)) && (int)SelectedFaction != f)
                    {
                        SelectedFaction = (Faction)f; SelectedRole = Role.Worker; SelectedSlot = 0;
                        listScroll = detailScroll = Vector2.zero;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("이름 검색", body, GUILayout.Width(70));
            Search = GUILayout.TextField(Search ?? "", GUILayout.Height(28));
            GUILayout.EndHorizontal();
            Filter = GUILayout.Toolbar(Filter, new[] { "전체", "유닛", "건물" }, GUILayout.Height(28));
            int count = VisibleCount();
            bool selectionVisible = entries.Exists(e => e.Role == SelectedRole && e.Slot == SelectedSlot);
            if (!selectionVisible && count > 0) Select(SelectedFaction, entries[0].Role, entries[0].Slot);
            GUILayout.Label(count + "개 · 기본 능력치와 조건 (연구 효과 제외)", body);
            bool narrow = Screen.width < 700;
            if (!narrow) GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(narrow ? GUILayout.ExpandWidth(true) : GUILayout.Width(Mathf.Min(330, Screen.width * 0.34f)));
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(narrow ? 220 : Mathf.Max(220, Screen.height - 235)));
            foreach (var entry in entries)
            {
                Rect row = GUILayoutUtility.GetRect(100, 61, GUILayout.ExpandWidth(true));
                bool selected = SelectedRole == entry.Role && SelectedSlot == entry.Slot;
                if (UiStyle.Button(row, "", selected)) Select(SelectedFaction, entry.Role, entry.Slot);
                Texture image = Portrait(SelectedFaction, entry.Role, entry.Slot);
                if (image != null) GUI.DrawTexture(new Rect(row.x + 4, row.y + 3, 54, 54), image, ScaleMode.ScaleToFit, true);
                item.normal.textColor = selected ? UiStyle.OnAccent : UiStyle.Ink;
                GUI.Label(new Rect(row.x + 64, row.y + 4, row.width - 68, row.height - 8), FactionData.Name(SelectedFaction, entry.Role, entry.Slot), item);
            }
            if (count == 0) GUILayout.Label("검색 결과가 없습니다.", body);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            detailScroll = GUILayout.BeginScrollView(detailScroll, GUILayout.Height(narrow ? 490 : Mathf.Max(220, Screen.height - 235)));
            GUILayout.Label(count > 0 ? FactionData.Name(SelectedFaction, SelectedRole, SelectedSlot) : "검색 결과가 없습니다.", title);
            Texture portrait = count > 0 ? Portrait(SelectedFaction, SelectedRole, SelectedSlot) : null;
            Rect picture = GUILayoutUtility.GetRect(180, 220, GUILayout.ExpandWidth(true));
            if (portrait != null) GUI.DrawTexture(picture, portrait, ScaleMode.ScaleToFit, true);
            else if (count > 0) GUI.Label(picture, "그림 준비 중", body);
            if (count > 0)
            {
                GUILayout.Label("도감 그림 · 전장 3D 모델과 표현이 다를 수 있습니다.", body);
                GUILayout.Label(Details(SelectedFaction, SelectedRole, SelectedSlot), body);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            if (!narrow) GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.depth = depth;
        }

        private static string Seconds(int ticks) => (ticks / (double)World.TicksPerSecond).ToString("0.#", CultureInfo.InvariantCulture) + "초";
        public static string Details(Faction faction, Role role, int slot)
        {
            if (!FactionData.Has(faction, role, slot)) return "";
            UnitStats stats = FactionData.Stats(faction, role, slot);
            bool building = FactionData.IsBuilding(role);
            var text = new StringBuilder(building ? "건물" : stats.Air ? "비행 유닛" : "지상 유닛");
            text.Append(" · 기본 정보\n체력 ").Append(stats.Hp).Append(" · 공격 ").Append(stats.Damage);
            if (stats.HasWeapon) text.Append(" · 사거리 ").Append((stats.Range.Raw / (double)Fix.One).ToString("0.#", CultureInfo.InvariantCulture));
            if (!building && !stats.HasWeapon) text.Append("\n일반 공격 없음 · 고유 기술은 아래 참조");
            if (role == Role.Worker) text.Append("\n자원 채집과 건설을 담당합니다.");
            if (building)
                text.Append("\n건설: 마나 ").Append(FactionData.BuildCost(faction, role, slot)).Append(" · ").Append(Seconds(FactionData.BuildTicks(faction, role, slot)));
            else
            {
                ProductionCost price = FactionData.Production(faction, role, slot);
                if (price.Produced)
                {
                    text.Append("\n생산: 마나 ").Append(price.Resources).Append(" · ").Append(Seconds(price.Ticks)).Append(" · 인구 1");
                    text.Append("\n생산 건물: ");
                    for (int i = 0; i < FactionData.MaxProducers; i++)
                    {
                        var source = FactionData.ProducedAt(faction, role, slot, i);
                        if (source.Role == Role.None) break;
                        if (i > 0) text.Append(" / ");
                        text.Append(FactionData.Name(faction, source.Role, source.Slot));
                    }
                }
                GrowthCost growth = FactionData.Growth(faction, role, slot);
                if (growth.Grown)
                    text.Append("\n성장: ").Append(FactionData.Name(faction, growth.FromRole, growth.FromSlot))
                        .Append(" → 이 유닛\n마나 ").Append(growth.Resources).Append(" · ").Append(Seconds(growth.Ticks)).Append(" · 추가 인구 없음");
                if (!price.Produced && !growth.Grown) text.Append("\n일반 생산 불가 · 종족 고유 방식으로 획득");
            }
            text.Append("\n필요 단계: T").Append(FactionData.Tier(faction, role, slot));
            var needs = FactionData.Prerequisite(faction, role, slot);
            if (needs.Role != Role.None) text.Append("\n선행 건물: ").Append(FactionData.Name(faction, needs.Role, needs.Slot));
            for (int i = 0; i < FactionData.AbilityCount(faction); i++)
            {
                AbilityRow ability = FactionData.Ability(faction, i);
                if (ability.Role != role || ability.Slot != slot) continue;
                text.Append("\n능력: ").Append(ability.Name);
                if (ability.Unlock >= 0) text.Append(" (해금: ").Append(FactionData.Upgrade(faction, ability.Unlock).Name).Append(")");
                if (ability.Cooldown > 0) text.Append(" · 재사용 ").Append(Seconds(ability.Cooldown));
            }
            if (building)
            {
                // Include workers too; ProductionMenu intentionally lists military roles only.
                for (int r = (int)Role.Worker; r <= (int)Role.Signature; r++)
                {
                    Role made = (Role)r;
                    if (FactionData.IsBuilding(made)) continue;
                    for (int s = 0; s < FactionData.SlotCount(faction, made); s++)
                        if (FactionData.IsProducedAt(faction, made, s, role, slot))
                            text.Append("\n생산 가능: ").Append(FactionData.Name(faction, made, s));
                }
                foreach (ResearchMenu.Entry research in ResearchMenu.For(faction, role, slot))
                    text.Append("\n연구: ").Append(research.Name);
            }
            return text.ToString();
        }
    }
}
