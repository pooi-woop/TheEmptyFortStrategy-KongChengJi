using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PooiKongChengJi
{
    /// <summary>
    /// 空城计 (KongChengJi) —— 主逻辑
    /// =================================================
    /// 研究"空城计"后，选中纪念碑时会出现一个切换按钮：
    ///   · 开启：纪念碑蓝图只保留外墙（外围一圈），内部清空，只需砌好外墙即视为"完工"。
    ///   · 在以空城计模式完成建造后，进入保护期的每一天里都有（可调）概率被发现。
    ///   · 一旦被发现：任务直接判定失败、委托派系对玩家的关系降低（可调），并弹出黑白预览图。
    /// 关闭开关即可恢复完整蓝图。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class KongChengJiAssets
    {
        public static Texture2D iconText;
        public static Texture2D previewTex;
        public static Texture2D previewTexBlackWhite;

        static KongChengJiAssets()
        {
            // 注册所有 [HarmonyPatch]（纪念碑 Gizmo 切换按钮等），否则补丁永远不会生效
            new Harmony("pooiwoop.kongchengji").PatchAll(Assembly.GetExecutingAssembly());
            try
            {
                iconText = MakeIcon();
                // 运行时弹出窗使用的预览图（放在 mod 的 Textures/EmptyCity/ 下）
                previewTex = ContentFinder<Texture2D>.Get("EmptyCity/preview", false);
            }
            catch (System.Exception ex)
            {
                Log.ErrorOnce("[KongChengJi] loading preview texture failed: " + ex, 910001);
            }
        }

        /// <summary>黑白版预览图：为安全起见（ReadPixels 需在 GUI/渲染帧内），延迟到首次使用时生成。</summary>
        public static Texture2D PreviewBlackWhite
        {
            get
            {
                if (previewTexBlackWhite == null && previewTex != null)
                {
                    try
                    {
                        previewTexBlackWhite = ToGrayscale(previewTex);
                    }
                    catch (System.Exception ex)
                    {
                        Log.ErrorOnce("[KongChengJi] grayscale failed: " + ex, 910004);
                    }
                }
                return previewTexBlackWhite;
            }
        }

        /// <summary>画一个"空心方框"作为按钮图标（空城计主题）。</summary>
        private static Texture2D MakeIcon()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var cols = new Color[size * size];
            Color ring = new Color(0.55f, 0.75f, 0.95f, 1f); // 浅蓝
            int m = 10;       // 外框到边缘留白
            int thick = 12;   // 外框厚度
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool outer = x >= m && x < size - m && y >= m && y < size - m;
                    bool inner = x >= m + thick && x < size - m - thick && y >= m + thick && y < size - m - thick;
                    bool onRing = outer && !inner;
                    cols[y * size + x] = onRing ? ring : Color.clear;
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        /// <summary>借助 RenderTexture 读取任意可绘制纹理的像素，转成灰度图。</summary>
        private static Texture2D ToGrayscale(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pix = tmp.GetPixels();
            for (int i = 0; i < pix.Length; i++)
            {
                float g = pix[i].r * 0.30f + pix[i].g * 0.59f + pix[i].b * 0.11f;
                pix[i].r = g;
                pix[i].g = g;
                pix[i].b = g;
            }
            tmp.SetPixels(pix);
            tmp.Apply();
            return tmp;
        }
    }

    /// <summary>空城计的开关键 -- 补丁纪念碑的 Gizmo 列表，添加"空城计"切换按钮。</summary>
    [HarmonyPatch(typeof(MonumentMarker), "GetGizmos")]
    public static class Patch_MonumentMarker_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, MonumentMarker __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (__instance == null || !__instance.Spawned || __instance.AllDone)
            {
                yield break;
            }
            if (!ResearchActive())
            {
                yield break;
            }
            var comp = Current.Game?.GetComponent<GameComponent_KongChengJi>();
            if (comp == null)
            {
                yield break;
            }

            yield return new Command_Toggle
            {
                icon = KongChengJiAssets.iconText,
                defaultLabel = "空城计",
                defaultDesc = "开启后，纪念碑蓝图将只保留外墙（外围一圈），内部清空，只需砌好外墙即可视为完工。"
                             + "\n\n注意：以空城计模式建成后，在保护期的每一天都有概率被看穿，一旦被看穿，任务将判定失败，"
                             + "并与委托派系关系恶化。可再次点击关闭以恢复完整蓝图。",
                isActive = () => comp.IsKongChengJi(__instance),
                toggleAction = () => comp.Toggle(__instance)
            };
        }

        private static bool ResearchActive()
        {
            var proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("KCJ_EmptyCityStrategy");
            return proj != null && proj.IsFinished;
        }
    }

    /// <summary>一个纪念碑的持久化状态。以 thingIDNumber 为键。</summary>
    public class MarkerState : IExposable
    {
        public int id;
        public bool kongChengJi;
        public bool discovered;
        public int lastRollTick = -1;
        public bool isProtected; // 已进入保护期（纪念碑完工后），此状态下被拆除才触发失败
        public Sketch originalSketch;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref kongChengJi, "kongChengJi", false);
            Scribe_Values.Look(ref discovered, "discovered", false);
            Scribe_Values.Look(ref lastRollTick, "lastRollTick", -1);
            Scribe_Values.Look(ref isProtected, "isProtected", false);
            Scribe_Deep.Look(ref originalSketch, "originalSketch");
        }
    }

    /// <summary>
    /// GameComponent 由引擎自动实例化（无需手动注册），负责：
    ///   · 持久化每个纪念碑的"空城计"状态与原蓝图；
    ///   · 每日发现概率判定、关系惩罚与任务失败。
    /// </summary>
    public class GameComponent_KongChengJi : GameComponent
    {
        public List<MarkerState> states = new List<MarkerState>();

        public const int TicksPerDay = 60000;

        public GameComponent_KongChengJi(Game game)
        {
            KongChengJiHelpers.ApplyAll(); // 每次读档/开新游戏时同步研究点数与纪念碑权重设置
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref states, "states", LookMode.Deep);
            if (states == null)
            {
                states = new List<MarkerState>();
            }
        }

        private MarkerState StateFor(MonumentMarker m)
        {
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].id == m.thingIDNumber)
                {
                    return states[i];
                }
            }
            var s = new MarkerState { id = m.thingIDNumber };
            states.Add(s);
            return s;
        }

        public bool IsKongChengJi(MonumentMarker m) => StateFor(m).kongChengJi;

        public void Toggle(MonumentMarker m)
        {
            if (m == null || !m.Spawned)
            {
                return;
            }
            var s = StateFor(m);
            if (s.kongChengJi)
            {
                Disable(m, s);
            }
            else
            {
                // 开启时弹出带预览图的确认窗口
                Find.WindowStack.Add(new Dialog_KongChengJiConfirm(m, s));
            }
        }

        public void Enable(MonumentMarker m, MarkerState s)
        {
            if (m == null || !m.Spawned)
            {
                return;
            }
            if (s.originalSketch == null && m.sketch != null)
            {
                s.originalSketch = m.sketch.DeepCopy();
            }
            if (s.originalSketch != null)
            {
                m.sketch = BuildOuterWallSketch(s.originalSketch);
                DestroyInternalBuildableThings(m, s.originalSketch);
            }
            s.kongChengJi = true;
            s.discovered = false;
            s.lastRollTick = -1;
            s.isProtected = false;
            SoundDefOf.Click.PlayOneShotOnCamera();
            Messages.Message("空城计已开启：纪念碑只需建造外墙即可视为完工。", m, MessageTypeDefOf.NeutralEvent);
        }

        public void Disable(MonumentMarker m, MarkerState s)
        {
            if (m != null && s.originalSketch != null && m.Spawned)
            {
                m.sketch = s.originalSketch;
            }
            s.kongChengJi = false;
            SoundDefOf.Click.PlayOneShotOnCamera();
            Messages.Message("空城计已关闭：恢复完整蓝图。", m, MessageTypeDefOf.NeutralEvent);
        }

        // ---------- 每日发现判定 ----------

        public override void GameComponentTick()
        {
            if (Find.TickManager == null || Find.QuestManager == null)
            {
                return;
            }
            try
            {
                TickCore();
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[KongChengJi] tick error: " + ex, 910002);
            }
        }

        private void TickCore()
        {
            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            if (quests == null)
            {
                return;
            }

            HashSet<int> referencedIds = new HashSet<int>();
            foreach (Quest q in quests)
            {
                if (q == null || q.State != QuestState.Ongoing)
                {
                    continue;
                }
                // 不要求 Spawned：纪念碑被拆除后也要能找到它来判定失败
                MonumentMarker m = GetMonumentMarker(q);
                if (m == null)
                {
                    continue;
                }
                referencedIds.Add(m.thingIDNumber);

                MarkerState s = StateFor(m);
                if (!s.kongChengJi || s.discovered)
                {
                    continue;
                }

                if (!m.Spawned)
                {
                    // 空城计保护期内纪念碑被拆除 / 被撤销 -> 任务失败
                    if (s.isProtected)
                    {
                        FailKongChengJi(s, m, q);
                    }
                    continue;
                }

                if (m.complete)
                {
                    s.isProtected = true; // 完工，进入保护期
                    if (s.lastRollTick < 0)
                    {
                        s.lastRollTick = Find.TickManager.TicksGame;
                    }
                    if (Find.TickManager.TicksGame - s.lastRollTick >= TicksPerDay)
                    {
                        s.lastRollTick = Find.TickManager.TicksGame;
                        TryDiscover(s, m, q);
                    }
                }
            }

            // 清理：已发现 / 已关闭 / 不再被进行中任务引用的状态
            states.RemoveAll(s => s.discovered || !s.kongChengJi || !referencedIds.Contains(s.id));
        }

        /// <summary>统一的"空城计败露"处理：标记已发现 + 关系惩罚 + 任务失败 + 弹出窗口。</summary>
        private void FailKongChengJi(MarkerState s, MonumentMarker m, Quest q)
        {
            if (s.discovered)
            {
                return; // 防止同一琐坏重复触发
            }
            s.discovered = true;
            int loss = KongChengJiMod.settings?.goodwillLoss ?? 25;

            // 触发原版"纪念碑被毁"信号：让任务脚本中的原本失败惩罚（派系好感下降、
            // 失败信件、部分路线的袭击威胁等）一并发生，而不只是我们这边手动 End。
            if (m != null)
            {
                try
                {
                    if (m.questTags != null && m.questTags.Count > 0)
                    {
                        QuestUtility.SendQuestTargetSignals(m.questTags, "MonumentDestroyed", m.Named("SUBJECT"));
                    }
                }
                catch (System.Exception ex)
                {
                    Log.WarningOnce("[KongChengJi] failed to send MonumentDestroyed signal: " + ex, 910005);
                }
            }

            // 关系惩罚
            var reason = DefDatabase<HistoryEventDef>.GetNamedSilentFail("KCJ_EmptyCityExposed");
            try
            {
                IEnumerable<Faction> factions = q?.InvolvedFactions;
                if (factions != null)
                {
                    foreach (Faction f in factions)
                    {
                        if (f != null && f != Faction.OfPlayer && !f.temporary && !f.def.permanentEnemy)
                        {
                            f.TryAffectGoodwillWith(Faction.OfPlayer, -loss, true, true, reason, m);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[KongChengJi] goodwill error: " + ex, 910003);
            }

            // 任务失败
            if (q != null && q.State == QuestState.Ongoing)
            {
                q.End(QuestEndOutcome.Fail, true, true);
            }

            // 黑白预览图（纪念碑可能已被拆除，对话框不依赖它）
            if (KongChengJiAssets.previewTex != null)
            {
                Find.WindowStack.Add(new Dialog_KongChengJiExposed(null, loss));
            }
            Messages.Message("空城计败露！" + (q != null && q.name != null ? " " + q.name : "") + " 委托判定失败。",
                MessageTypeDefOf.ThreatBig);
        }

        private void TryDiscover(MarkerState s, MonumentMarker m, Quest q)
        {
            float chance = (KongChengJiMod.settings?.discoverChancePercent ?? 1f) / 100f;
            if (chance <= 0f)
            {
                return;
            }
            if (!Rand.Chance(chance))
            {
                return;
            }
            FailKongChengJi(s, m, q);
        }

        private static MonumentMarker GetMonumentMarker(Quest q)
        {
            foreach (GlobalTargetInfo t in q.QuestLookTargets)
            {
                Thing thing = t.Thing;
                if (thing is MonumentMarker mm)
                {
                    return mm;
                }
            }
            return null;
        }

        // ---------- 外墙提取工具 ----------

        private static Sketch BuildOuterWallSketch(Sketch orig)
        {
            var newSketch = new Sketch();
            if (orig == null)
            {
                return newSketch;
            }
            CellRect rect = orig.OccupiedRect;
            foreach (SketchEntity e in orig.Entities)
            {
                if (!(e is RimWorld.SketchBuildable))
                {
                    continue;
                }
                if (AllCellsOnPerimeter(rect, e))
                {
                    newSketch.Add(e, false);
                }
            }
            return newSketch;
        }

        /// <summary>删除原本位于外墙之外（内部）的、已放置的蓝图/框架，避免其被建出来变成"违建"。</summary>
        private static void DestroyInternalBuildableThings(MonumentMarker m, Sketch orig)
        {
            if (m == null || !m.Spawned || orig == null)
            {
                return;
            }
            CellRect rect = orig.OccupiedRect;
            foreach (SketchEntity e in orig.Entities)
            {
                if (!(e is RimWorld.SketchBuildable sb) || AllCellsOnPerimeter(rect, e))
                {
                    continue;
                }
                try
                {
                    Thing thing = sb.GetSpawnedBlueprintOrFrame(m.Position + sb.pos, m.Map);
                    if (thing != null && !thing.Destroyed)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
                catch
                {
                    // 单个实体失败不致命
                }
            }
        }

        private static bool AllCellsOnPerimeter(CellRect rect, SketchEntity e)
        {
            foreach (IntVec3 c in e.OccupiedRect)
            {
                if (!(c.x == rect.minX || c.x == rect.maxX || c.z == rect.minZ || c.z == rect.maxZ))
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>确认开启空城计的弹窗，顶部显示（彩色）预览图。</summary>
    public class Dialog_KongChengJiConfirm : Window
    {
        private readonly MonumentMarker marker;
        private readonly MarkerState state;
        private readonly GameComponent_KongChengJi comp;

        public override Vector2 InitialSize => new Vector2(480f, 620f);

        public Dialog_KongChengJiConfirm(MonumentMarker m, MarkerState s)
        {
            marker = m;
            state = s;
            comp = Current.Game.GetComponent<GameComponent_KongChengJi>();
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "空城计");
            Text.Font = GameFont.Small;

            Rect imgRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, 300f);
            if (KongChengJiAssets.previewTex != null)
            {
                Widgets.DrawTextureFitted(imgRect, KongChengJiAssets.previewTex, 1f);
            }

            float y = imgRect.yMax + 12f;
            Rect textRect = new Rect(inRect.x, y, inRect.width, 120f);
            string txt = "以此计开启后，纪念碑只需建造那堵严严实实的外墙即可" + "视为完工" + "——" +
                         "里面空得能跑马。\n\n" +
                         "但空城一计瞒不过天：建成后的保护期内，每天都有一定概率被" + "看穿" + "。" +
                         "一旦露馅，任务直接失败，并与委托方派系关系恶化。\n\n" +
                         "确认开启空城计模式？";
            Widgets.Label(textRect, txt);

            float bY = inRect.yMax - 40f;
            if (Widgets.ButtonText(new Rect(inRect.x, bY, 140f, 34f), "开启空城计"))
            {
                comp.Enable(marker, state);
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.xMax - 140f, bY, 140f, 34f), "取消"))
            {
                Close();
            }
        }
    }

    /// <summary>任务失败时弹出的窗口：黑白预览图 + 说明。</summary>
    public class Dialog_KongChengJiExposed : Window
    {
        private readonly MonumentMarker marker;
        private readonly int goodWillLoss;

        public override Vector2 InitialSize => new Vector2(480f, 620f);

        public Dialog_KongChengJiExposed(MonumentMarker m, int loss)
        {
            marker = m;
            goodWillLoss = loss;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            GUI.color = Color.red;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "空城计 · 败露");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect imgRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, 300f);
            Texture2D bw = KongChengJiAssets.PreviewBlackWhite;
            if (bw != null)
            {
                Widgets.DrawTextureFitted(imgRect, bw, 1f);
            }

            float y = imgRect.yMax + 12f;
            Rect textRect = new Rect(inRect.x, y, inRect.width, 130f);
            string txt = "虚虚实实，终被看穿。\n\n" +
                         "委托派系识破了这座空城，纪念碑任务" + "判定失败" + "。" +
                         "对方因此降下感观：与你的关系" + ("减少 " + goodWillLoss) + "。";
            Widgets.Label(textRect, txt);

            Rect bY = new Rect(inRect.x + inRect.width / 2f - 70f, inRect.yMax - 40f, 140f, 34f);
            if (Widgets.ButtonText(bY, "知道了"))
            {
                Close();
            }
        }
    }

    /// <summary>Mod 设置：发现概率与关系减少量。</summary>
    public class KongChengJiMod : Mod
    {
        public static KongChengJiSettings settings;

        public KongChengJiMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<KongChengJiSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(inRect);

            ls.Label("研究“空城计”所需点数（0 表示默认拥有）: " + settings.researchCost);
            settings.researchCost = (int)ls.Slider(settings.researchCost, 0, 1000);

            ls.Gap(12f);
            ls.Label("保护期内" + "每天" + "被发现的概率（百分比）: " + settings.discoverChancePercent.ToString("0.##") + "%");
            settings.discoverChancePercent = ls.Slider(settings.discoverChancePercent, 0f, 100f);

            ls.Gap(12f);
            ls.Label("被发现时减少的关系量: " + settings.goodwillLoss);
            settings.goodwillLoss = (int)ls.Slider(settings.goodwillLoss, 1, 100);

            ls.Gap(16f);
            // —— 纪念碑任务【平时】出现概率（与原版一致，默认 100%） ——
            ls.Label("纪念碑任务（平时）出现概率：");
            ls.CheckboxLabeled("提高纪念碑任务出现概率", ref settings.boostMonumentQuests);
            if (settings.boostMonumentQuests)
            {
                ls.Label("纪念碑任务出现概率放大倍数: " + settings.boostMonumentMultiplier.ToString("0.##") + "x");
                settings.boostMonumentMultiplier = ls.Slider(settings.boostMonumentMultiplier, 1f, 50f);
            }

            ls.Gap(16f);
            // —— 文化仪式效果「空城计-宣传」的触发概率（默认 100%，可选 0% ~ 100%） ——
            ls.Label("文化仪式效果「空城计-宣传」");
            ls.Label("需在“编辑文化”中把该效果选到某场仪式上；需 Ideology/文化 DLC。");
            ls.Gap(4f);
            settings.ritualMonumentChancePercent = ls.SliderLabeled(
                "仪式质量良好及以上时刷新纪念碑任务概率: " + settings.ritualMonumentChancePercent.ToString("0.##") + "%",
                settings.ritualMonumentChancePercent, 0f, 100f,
                tooltip: "「空城计-宣传」是一个文化仪式效果：把它选到某场仪式上后，若该仪式结束质量达到“良好及以上”，"
                         + "就有该概率刷新一座新的纪念碑（建造并保护）任务。\n\n"
                         + "这是“仪式效果触发”的概率，默认 100%，可在 0% ~ 100% 之间调节；0% 表示即使仪式成功也不刷新。\n\n"
                         + "它与上方“纪念碑任务（平时）出现概率”互不影响：上方那个调节的是平时委托方送任务的频率"
                         + "（原版默认 100%）；这个是仪式结束后额外再送一座纪念碑的机会。");

            ls.Gap(16f);
            ls.Label("说明：默认再次点击纪念碑的“空城计”按钮即可关闭本模式。");
            ls.End();

            // 设置改动后实时同步到对应 Def（研究点数 / 纪念碑权重），无需重启游戏
            KongChengJiHelpers.ApplyResearchCost();
            KongChengJiHelpers.ApplyMonumentBoost();
        }

        public override string SettingsCategory() => "空城计 (The Empty Fort Strategy)";
    }

    public class KongChengJiSettings : ModSettings
    {
        // 原有
        public float discoverChancePercent = 1f;   // 每日被发现概率（默认 1% 不变；0% = 永不被发现）
        public int goodwillLoss = 25;              // 被发现时关系惩罚

        // 研究点数（默认 200；0 = 默认拥有，无需研究）
        public int researchCost = 200;

        // 纪念碑任务出现概率加成（默认关闭）
        public bool boostMonumentQuests = false;
        public float boostMonumentMultiplier = 10f; // 权重放大倍数

        // 文化仪式加成（作为可选的"文化仪式效果"，默认开启、无需设置开关；概率可调）
        public float ritualMonumentChancePercent = 100f;   // 良好以上仪式刷新纪念碑任务概率（默认 100%）

        public override void ExposeData()
        {
            Scribe_Values.Look(ref discoverChancePercent, "discoverChancePercent", 1f);
            Scribe_Values.Look(ref goodwillLoss, "goodwillLoss", 25);
            Scribe_Values.Look(ref researchCost, "researchCost", 200);
            Scribe_Values.Look(ref boostMonumentQuests, "boostMonumentQuests", false);
            Scribe_Values.Look(ref boostMonumentMultiplier, "boostMonumentMultiplier", 10f);
            Scribe_Values.Look(ref ritualMonumentChancePercent, "ritualMonumentChancePercent", 100f);
        }
    }

    /// <summary>
    /// 空城计 mod 的辅助逻辑：
    ///   · 研究点数动态化（0 为默认拥有）；
    ///   · 纪念碑任务权重加成（提高出现概率）；
    ///   · 文化仪式良好以上刷新纪念碑任务（兼容无 Ideology DLC）。
    /// </summary>
    public static class KongChengJiHelpers
    {
        // 人群通用：仪式"良好及以上"对应的归一化质量阈值（0~1）。
        // 该值来自游戏内仪式结果质量（outcome quality float），0.6 视作"良好"。
        public const float RitualGoodQuality = 0.6f;

        // 记录纪念碑任务根节点（BuildMonument_*）的原始权重，用于还原。
        private static readonly Dictionary<string, float> monumentBaseWeights = new Dictionary<string, float>();

        /// <summary>在游戏开始读取存档时同步应用所有动态设置。</summary>
        public static void ApplyAll()
        {
            ApplyResearchCost();
            ApplyMonumentBoost();
        }

        /// <summary>把设置里的研究点数写入"空城计"研究项目；为 0 时 baseCost=0，研究默认已完成（默认拥有）。</summary>
        public static void ApplyResearchCost()
        {
            var proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("KCJ_EmptyCityStrategy");
            if (proj == null)
            {
                return;
            }
            int cost = KongChengJiMod.settings?.researchCost ?? 200;
            if (cost < 0)
            {
                cost = 0;
            }
            proj.baseCost = cost;
        }

        /// <summary>根据开关，把纪念碑任务根节点的 rootSelectionWeight 放大/还原，从而影响其出现概率。</summary>
        public static void ApplyMonumentBoost()
        {
            bool enabled = KongChengJiMod.settings?.boostMonumentQuests ?? false;
            float mult = KongChengJiMod.settings?.boostMonumentMultiplier ?? 10f;
            if (mult < 1f)
            {
                mult = 1f;
            }
            foreach (QuestScriptDef def in DefDatabase<QuestScriptDef>.AllDefsListForReading)
            {
                if (def == null || !def.IsRootRandomSelected || !def.defName.StartsWith("BuildMonument"))
                {
                    continue;
                }
                if (!monumentBaseWeights.TryGetValue(def.defName, out float baseWeight))
                {
                    baseWeight = def.rootSelectionWeight;
                    monumentBaseWeights[def.defName] = baseWeight;
                }
                def.rootSelectionWeight = enabled ? baseWeight * mult : baseWeight;
            }
        }

        /// <summary>文化仪式刷新：生成一个"建造并保护纪念碑"任务并立即对玩家可见。</summary>
        public static void TryRefreshMonumentQuest()
        {
            if (Current.Game == null)
            {
                return;
            }
            QuestScriptDef def = DefDatabase<QuestScriptDef>.GetNamedSilentFail("BuildMonument_TimeProtect");
            if (def == null)
            {
                return;
            }
            IIncidentTarget target = Find.CurrentMap != null ? (IIncidentTarget)Find.CurrentMap : (IIncidentTarget)Find.World;
            float points = target != null ? StorytellerUtility.DefaultThreatPointsNow(target) : 500f;
            if (points < 140f)
            {
                points = 500f; // TimeProtect 的最低点数要求是 140，兜底给一个合理值
            }
            try
            {
                QuestUtility.GenerateQuestAndMakeAvailable(def, points);
                Messages.Message("文化仪式大获成功，委托方再次送来了一座纪念碑的蓝图。",
                    MessageTypeDefOf.PositiveEvent);
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[KongChengJi] failed to refresh monument quest: " + ex, 910006);
            }
        }
    }

    /// <summary>
    /// "空城计-宣传"文化仪式效果的工作逻辑。
    /// 继承自 RitualOutcomeEffectWorker_FromQuality：当玩家在"编辑文化"把该效果选到某场仪式上时，
    /// 仪式结束时若质量达到"良好"及以上，则按设置中的概率刷新一个纪念碑任务。
    /// 由 Defs/RitualOutcomeEffects.xml 中的 KCJ_MonumentRefreshEffect 引用（workerClass）。
    /// </summary>
    public class RitualOutcomeEffectWorker_KongChengJiMonument : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_KongChengJiMonument(RitualOutcomeEffectDef def) : base(def)
        {
        }

        public override void Apply(float progress, Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual)
        {
            // 先跑原版"按质量出结果"的完整逻辑（结果信、心情、参与主体等），保证仪式本身表现不变
            base.Apply(progress, totalPresence, jobRitual);

            if (!ModsConfig.IdeologyActive || Current.Game == null)
            {
                return; // 没有 Ideology DLC 时不触发
            }
            KongChengJiSettings s = KongChengJiMod.settings;
            if (s == null || s.ritualMonumentChancePercent <= 0f)
            {
                return;
            }
            // 使用与游戏一致的真实质量（本类是 _FromQuality 子类，可直接调用受保护的 GetQuality）
            float quality = GetQuality(jobRitual, progress);
            if (quality < KongChengJiHelpers.RitualGoodQuality)
            {
                return; // 未达"良好"
            }
            float chance = Mathf.Min(s.ritualMonumentChancePercent, 100f);
            if (!Rand.Chance(chance / 100f))
            {
                return;
            }
            KongChengJiHelpers.TryRefreshMonumentQuest();
        }
    }
}