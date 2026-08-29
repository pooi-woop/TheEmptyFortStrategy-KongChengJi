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

        public GameComponent_KongChengJi(Game game) { }

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

            ls.Label("保护期内" + "每天" + "被发现的概率（百分比）: " + settings.discoverChancePercent.ToString("0.##") + "%");
            settings.discoverChancePercent = ls.Slider(settings.discoverChancePercent, 0f, 100f);

            ls.Gap(12f);
            ls.Label("被发现时减少的关系量: " + settings.goodwillLoss);
            settings.goodwillLoss = (int)ls.Slider(settings.goodwillLoss, 1, 100);

            ls.Gap(16f);
            ls.Label("说明：默认再次点击纪念碑的“空城计”按钮即可关闭本模式。");
            ls.End();
        }

        public override string SettingsCategory() => "空城计 (The Empty Fort Strategy)";
    }

    public class KongChengJiSettings : ModSettings
    {
        public float discoverChancePercent = 1f;
        public int goodwillLoss = 25;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref discoverChancePercent, "discoverChancePercent", 1f);
            Scribe_Values.Look(ref goodwillLoss, "goodwillLoss", 25);
        }
    }
}