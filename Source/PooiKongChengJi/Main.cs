using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.AI;
using Verse.AI.Group;

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
    ///
    /// 空城计-顶罪仪式：败露后4小时内举行仪式可阻止袭击，24小时内仍可减少关系惩罚。
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

    /// <summary>空城计的开关键 -- 补丁纪念碑的 Gizmo 列表，添加"空城计"切换按钮和顶罪仪式按钮。</summary>
    [HarmonyPatch(typeof(MonumentMarker), "GetGizmos")]
    public static class Patch_MonumentMarker_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, MonumentMarker __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            var comp = Current.Game?.GetComponent<GameComponent_KongChengJi>();
            if (comp == null)
            {
                yield break;
            }

            // ---- 空城计切换按钮（仅对未完工且已研究的纪念碑） ----
            if (__instance != null && __instance.Spawned && !__instance.AllDone && ResearchActive())
            {
                yield return new Command_Toggle
                {
                    icon = KongChengJiAssets.iconText,
                    defaultLabel = "KCJ_Gizmo_Label".Translate(),
                    defaultDesc = "KCJ_Gizmo_Desc".Translate(),
                    isActive = () => comp.IsKongChengJi(__instance),
                    toggleAction = () => comp.Toggle(__instance)
                };
            }

            // ---- 顶罪仪式按钮（被看穿后自动出现，无需文化配置） ----
            if (__instance != null && __instance.Spawned && __instance.questTags != null && __instance.questTags.Count > 0)
            {
                string questTag = __instance.questTags[0];
                ExposureEvent ev = comp.FindActiveExposureForQuestTag(questTag);
                if (ev != null)
                {
                    yield return new Command_Action
                    {
                        icon = KongChengJiAssets.iconText,
                        defaultLabel = "KCJ_BlameShift_Gizmo_Label".Translate(),
                        defaultDesc = "KCJ_BlameShift_Gizmo_Desc".Translate(),
                        action = () =>
                        {
                            Find.WindowStack.Add(new Dialog_BlameShiftConfirm(ev));
                        }
                    };
                }
            }

            // ---- 宣传仪式按钮（研究完成后自动出现，手动刷新纪念碑任务，无需文化配置） ----
            if (__instance != null && __instance.Spawned && ResearchActive())
            {
                bool onCooldown = comp.IsPropagandaOnCooldown;
                bool ceremonyRunning = comp.IsCeremonyRunning;
                string label = "KCJ_Propaganda_Gizmo_Label".Translate();
                string desc;
                string disabledReason = null;
                int cooldownDays = KongChengJiMod.settings?.propagandaCooldownDays ?? 5;

                if (ceremonyRunning)
                {
                    float progress = comp.activeCeremony?.Progress ?? 0f;
                    desc = "KCJ_Propaganda_Gizmo_Desc_Running".Translate((progress * 100f).ToString("F0"));
                    disabledReason = "KCJ_Propaganda_Gizmo_Desc_Running".Translate((progress * 100f).ToString("F0"));

                    yield return new Command_ActionWithProgressBar
                    {
                        icon = KongChengJiAssets.iconText,
                        defaultLabel = label,
                        defaultDesc = desc,
                        disabledReason = disabledReason,
                        Disabled = true,
                        progressGetter = () => comp.activeCeremony?.Progress ?? 0f,
                        action = () => { }
                    };
                }
                else if (onCooldown)
                {
                    int remaining = comp.PropagandaCooldownRemaining;
                    desc = "KCJ_Propaganda_Gizmo_Desc_Cooldown".Translate((remaining / 2500f).ToString("F1"), cooldownDays);
                    disabledReason = desc;

                    yield return new Command_Action
                    {
                        icon = KongChengJiAssets.iconText,
                        defaultLabel = label,
                        defaultDesc = desc,
                        disabledReason = disabledReason,
                        action = () => { }
                    };
                }
                else
                {
                    string durationStr = (comp.PropagandaDurationTicks / 2500f).ToString("F1");
                    desc = "KCJ_Propaganda_Gizmo_Desc".Translate(durationStr, cooldownDays);

                    yield return new Command_Action
                    {
                        icon = KongChengJiAssets.iconText,
                        defaultLabel = label,
                        defaultDesc = desc,
                        action = () =>
                        {
                            Find.WindowStack.Add(new Dialog_PropagandaCeremony(__instance, comp));
                        }
                    };
                }
            }
        }

        private static bool ResearchActive()
        {
            var proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("KCJ_EmptyCityStrategy");
            return proj != null && proj.IsFinished;
        }
    }

    /// <summary>顶罪仪式确认弹窗：从 Gizmo 触发，让玩家手动选择顶罪者。</summary>
    public class Dialog_BlameShiftConfirm : Window
    {
        private readonly ExposureEvent ev;
        private readonly GameComponent_KongChengJi comp;
        private readonly List<Pawn> candidates;
        private Pawn selectedPawn;
        private Vector2 infoScrollPosition;
        private Vector2 pawnScrollPosition;

        public override Vector2 InitialSize => new Vector2(760f, 560f);

        public Dialog_BlameShiftConfirm(ExposureEvent exposure)
        {
            ev = exposure;
            comp = Current.Game.GetComponent<GameComponent_KongChengJi>();
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;

            // 收集所有可选人员（奴隶优先显示，然后是殖民者）
            candidates = new List<Pawn>();
            if (Find.CurrentMap?.mapPawns != null)
            {
                foreach (Pawn pawn in Find.CurrentMap.mapPawns.FreeColonistsAndPrisonersSpawned)
                {
                    if (pawn != null && !pawn.Destroyed && (pawn.IsFreeColonist || pawn.IsSlave))
                    {
                        candidates.Add(pawn);
                    }
                }
            }
            candidates = candidates.OrderByDescending(p => p.IsSlave).ThenBy(p => p.LabelShort).ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            BlameShiftAction action = BlameShiftDialogUI.DrawContents(inRect, candidates, ref selectedPawn, ref infoScrollPosition, ref pawnScrollPosition);
            if (action == BlameShiftAction.Confirm)
            {
                comp.TriggerBlameShiftFromGizmo(ev, selectedPawn);
                Close();
            }
            else if (action == BlameShiftAction.Cancel)
            {
                Close();
            }
        }
    }

    /// <summary>顶罪仪式界面的操作结果。</summary>
    internal enum BlameShiftAction
    {
        None,
        Confirm,
        Cancel
    }

    /// <summary>
    /// 顶罪仪式共用 UI：左侧说明文字占 50%，右侧小人选择列表（左右布局，同原版文化仪式）。
    /// 返回是否点击了“确认/取消”。
    /// </summary>
    internal static class BlameShiftDialogUI
    {
        private const float TitleHeight = 30f;
        private const float EntryHeight = 40f;
        private const float IconSize = 30f;
        private const float BottomHeight = 36f;

        internal static BlameShiftAction DrawContents(Rect inRect, List<Pawn> candidates, ref Pawn selectedPawn, ref Vector2 infoScroll, ref Vector2 pawnScroll)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, TitleHeight), "KCJ_BlameShift_Confirm_Title".Translate());
            Text.Font = GameFont.Small;

            // 左右两栏：左栏说明文字占 50%，右栏选择小人
            float topY = inRect.y + TitleHeight + 10f;
            float contentBottom = inRect.yMax - BottomHeight - 10f;
            float contentH = contentBottom - topY;
            float gap = 16f;
            float leftW = (inRect.width - gap) * 0.5f;
            float rightX = inRect.x + leftW + gap;
            float rightW = inRect.width - leftW - gap;

            // ---------- 左栏：说明文字（可滚动） ----------
            string infoText = "KCJ_BlameShift_Confirm_Text".Translate();
            float infoTextHeight = Text.CalcHeight(infoText, leftW - 16f);
            Rect infoOutRect = new Rect(inRect.x, topY, leftW, contentH);
            Rect infoViewRect = new Rect(0f, 0f, infoOutRect.width - 16f, infoTextHeight);
            Widgets.BeginScrollView(infoOutRect, ref infoScroll, infoViewRect);
            Widgets.Label(new Rect(0f, 0f, infoViewRect.width, infoTextHeight), infoText);
            Widgets.EndScrollView();

            // ---------- 右栏：选择小人 ----------
            Widgets.Label(new Rect(rightX, topY, rightW, 22f), "KCJ_BlameShift_Select_Pawn".Translate());
            float listY = topY + 26f;
            float listHeight = contentBottom - listY;
            float totalHeight = candidates.Count * EntryHeight;

            Rect outRect = new Rect(rightX, listY, rightW, listHeight);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, totalHeight);
            Widgets.BeginScrollView(outRect, ref pawnScroll, viewRect);

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn pawn = candidates[i];
                Rect entryRect = new Rect(0f, i * EntryHeight, viewRect.width, EntryHeight - 2f);

                bool isSelected = pawn == selectedPawn;
                Widgets.DrawHighlightIfMouseover(entryRect);
                if (isSelected)
                {
                    Widgets.DrawBox(entryRect, 2);
                }

                // 小人图标
                Rect iconRect = new Rect(4f, i * EntryHeight + 5f, IconSize, IconSize);
                Widgets.ThingIcon(iconRect, pawn);

                string roleLabel = pawn.IsSlave ? "KCJ_Role_Intern".Translate() : "KCJ_Role_Employee".Translate();
                string label = pawn.NameShortColored + "  (" + roleLabel + ")";
                Widgets.Label(new Rect(iconRect.xMax + 6f, i * EntryHeight + 5f, viewRect.width - IconSize - 16f, EntryHeight - 6f), label);

                if (Widgets.ButtonInvisible(entryRect))
                {
                    selectedPawn = pawn;
                }
            }

            Widgets.EndScrollView();

            // ---------- 底部按钮 ----------
            float bY = inRect.yMax - BottomHeight;
            if (selectedPawn != null && Widgets.ButtonText(new Rect(inRect.x, bY, 140f, 30f), "KCJ_BlameShift_Confirm_Button".Translate()))
            {
                return BlameShiftAction.Confirm;
            }
            if (Widgets.ButtonText(new Rect(inRect.xMax - 140f, bY, 140f, 30f), "KCJ_Confirm_Cancel".Translate()))
            {
                return BlameShiftAction.Cancel;
            }
            return BlameShiftAction.None;
        }
    }

    /// <summary>
    /// 顶罪仪式-文化仪式路径选择弹窗：从仪式参与者中选择顶罪者。
    /// 用法：在 RitualOutcomeEffectWorker_KongChengJiBlameShift.Apply() 中打开，
    /// 玩家选择后由弹窗自行执行放逐和顶罪效果。
    /// </summary>
    public class Dialog_BlameShiftRitualSelection : Window
    {
        private readonly ExposureEvent ev;
        private readonly GameComponent_KongChengJi comp;
        private readonly List<Pawn> candidates;
        private Pawn selectedPawn;
        private Vector2 infoScrollPosition;
        private Vector2 pawnScrollPosition;

        public override Vector2 InitialSize => new Vector2(760f, 560f);

        public Dialog_BlameShiftRitualSelection(ExposureEvent exposure, List<Pawn> participants)
        {
            ev = exposure;
            comp = Current.Game.GetComponent<GameComponent_KongChengJi>();
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;

            // 从仪式参与者中筛选可顶罪者（奴隶优先显示，然后是殖民者）
            candidates = new List<Pawn>();
            if (participants != null)
            {
                foreach (Pawn pawn in participants)
                {
                    if (pawn != null && !pawn.Destroyed && (pawn.IsFreeColonist || pawn.IsSlave))
                    {
                        candidates.Add(pawn);
                    }
                }
            }
            candidates = candidates.OrderByDescending(p => p.IsSlave).ThenBy(p => p.LabelShort).ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            BlameShiftAction action = BlameShiftDialogUI.DrawContents(inRect, candidates, ref selectedPawn, ref infoScrollPosition, ref pawnScrollPosition);
            if (action == BlameShiftAction.Confirm)
            {
                // 弹窗自行执行放逐和顶罪效果
                string roleLabel = selectedPawn.IsSlave ? "KCJ_Role_Intern".Translate() : "KCJ_Role_Employee".Translate();
                string roleTooltip = selectedPawn.IsSlave
                    ? "KCJ_Role_Intern_Tooltip".Translate()
                    : "KCJ_Role_Employee_Tooltip".Translate();

                comp.BanishPawn(selectedPawn, roleLabel);
                comp.ApplyBlameShift(ev);

                Find.LetterStack.ReceiveLetter(
                    "KCJ_BlameShift_Detail_Title".Translate(),
                    "KCJ_BlameShift_Detail_Text".Translate(selectedPawn.NameShortColored, roleLabel, roleTooltip),
                    LetterDefOf.NeutralEvent,
                    selectedPawn);

                Close();
            }
            else if (action == BlameShiftAction.Cancel)
            {
                Close();
            }
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
    /// 空城计败露事件：记录败露时间、攻击派系、关系惩罚等。
    /// 用于延迟袭击逻辑和顶罪仪式。
    /// </summary>
    public class ExposureEvent : IExposable
    {
        public int exposureTick = -1;
        public bool attackHappened;
        public bool blameShifted;
        public string factionDefName;
        public int goodwillLoss;
        public string questTag;

        public void ExposeData()
        {
            Scribe_Values.Look(ref exposureTick, "exposureTick", -1);
            Scribe_Values.Look(ref attackHappened, "attackHappened", false);
            Scribe_Values.Look(ref blameShifted, "blameShifted", false);
            Scribe_Values.Look(ref factionDefName, "factionDefName", null);
            Scribe_Values.Look(ref goodwillLoss, "goodwillLoss", 25);
            Scribe_Values.Look(ref questTag, "questTag", null);
        }

        /// <summary>距败露至今经过的 ticks。</summary>
        public int ElapsedTicks => Find.TickManager.TicksGame - exposureTick;

        /// <summary>是否仍在24小时窗口内（顶罪仪式可用）。1 in-game 小时 = 2500 ticks。</summary>
        public bool IsWithinWindow => ElapsedTicks < 24 * 2500;

        /// <summary>是否已到触发袭击的时间（4小时后）。</summary>
        public bool IsAttackDue => !attackHappened && !blameShifted && ElapsedTicks >= 4 * 2500;
    }

    /// <summary>
    /// 宣传仪式运行状态：记录仪式进行中的各项数据。
    /// </summary>
    public class CeremonyState : IExposable
    {
        public int startTick = -1;
        public int durationTicks;
        public int monumentId = -1;
        public List<int> participantIds = new List<int>();
        public bool completed;
        public bool effectApplied;

        public void ExposeData()
        {
            Scribe_Values.Look(ref startTick, "startTick", -1);
            Scribe_Values.Look(ref durationTicks, "durationTicks", 0);
            Scribe_Values.Look(ref monumentId, "monumentId", -1);
            Scribe_Collections.Look(ref participantIds, "participantIds", LookMode.Value);
            Scribe_Values.Look(ref completed, "completed", false);
            Scribe_Values.Look(ref effectApplied, "effectApplied", false);
        }

        /// <summary>仪式已进行的 tick 数。</summary>
        public int ElapsedTicks => Find.TickManager.TicksGame - startTick;

        /// <summary>仪式进度 0~1。</summary>
        public float Progress => Mathf.Clamp01((float)ElapsedTicks / durationTicks);

        /// <summary>仪式是否已完成（经过时长后）。</summary>
        public bool IsFinished => startTick >= 0 && ElapsedTicks >= durationTicks;
    }

    /// <summary>
    /// GameComponent 由引擎自动实例化（无需手动注册），负责：
    ///   · 持久化每个纪念碑的"空城计"状态与原蓝图；
    ///   · 每日发现概率判定、关系惩罚与任务失败；
    ///   · 空城计败露事件的追踪、延迟袭击与顶罪仪式。
    /// </summary>
    public class GameComponent_KongChengJi : GameComponent
    {
        public List<MarkerState> states = new List<MarkerState>();
        public List<ExposureEvent> exposures = new List<ExposureEvent>();
        public bool researchCompletedQuestDispatched; // 研究完成时是否已触发纪念碑任务
        public int lastPropagandaTick = int.MinValue; // 上次宣传仪式触发时的 tick（初始化"从未触发"，开局即可用，避免一上来就进入冷却）

        public const int TicksPerDay = 60000;
        // 4小时 = 10000 ticks (1 in-game hour = 2500 ticks)
        public const int TicksAttackDelay = 4 * 2500; // 4 in-game hours
        public int PropagandaCooldownTicks => (KongChengJiMod.settings?.propagandaCooldownDays ?? 5) * 60000;

        public int PropagandaDurationTicks => Mathf.RoundToInt((KongChengJiMod.settings?.propagandaDurationHours ?? 2f) * 2500f);

        // ---------- 宣传仪式运行状态 ----------
        public CeremonyState activeCeremony;
        public Lord propagandaLord;

        public bool IsCeremonyRunning => activeCeremony != null && !activeCeremony.completed;

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
            Scribe_Collections.Look(ref exposures, "exposures", LookMode.Deep);
            if (exposures == null)
            {
                exposures = new List<ExposureEvent>();
            }
            Scribe_Values.Look(ref researchCompletedQuestDispatched, "researchCompletedQuestDispatched", false);
            Scribe_Values.Look(ref lastPropagandaTick, "lastPropagandaTick", int.MinValue);
            Scribe_Deep.Look(ref activeCeremony, "activeCeremony");
            Scribe_References.Look(ref propagandaLord, "propagandaLord");
        }

        /// <summary>宣传仪式是否在冷却中。</summary>
        public bool IsPropagandaOnCooldown
        {
            get
            {
                return Find.TickManager.TicksGame < lastPropagandaTick + PropagandaCooldownTicks;
            }
        }

        /// <summary>宣传仪式剩余冷却tick。</summary>
        public int PropagandaCooldownRemaining
        {
            get
            {
                int remaining = lastPropagandaTick + PropagandaCooldownTicks - Find.TickManager.TicksGame;
                return remaining > 0 ? remaining : 0;
            }
        }

        /// <summary>尝试刷新纪念碑任务，按质量缩放点数。</summary>
        public void TryManualPropaganda(float quality)
        {
            float pointsMult = 0.5f + quality * 0.5f; // 质量 0→0.5x, 1→1.0x
            KongChengJiHelpers.TryRefreshMonumentQuest(pointsMult);
        }

        /// <summary>开始宣传仪式。</summary>
        public void StartCeremony(MonumentMarker monument, List<Pawn> participants)
        {
            if (monument == null || !monument.Spawned)
            {
                return;
            }
            activeCeremony = new CeremonyState
            {
                startTick = Find.TickManager.TicksGame,
                durationTicks = PropagandaDurationTicks,
                monumentId = monument.thingIDNumber,
                participantIds = participants?.Select(p => p.thingIDNumber).ToList() ?? new List<int>()
            };

            // 仪式开始时设置冷却，无论结果好坏都防止连续进行
            lastPropagandaTick = Find.TickManager.TicksGame;

            // 创建 Lord 让参与者聚集在纪念碑周围（同原版仪式，小人可以吃饭）
            if (participants != null && participants.Count > 0)
            {
                Map map = monument.Map;
                if (map != null)
                {
                    propagandaLord = LordMaker.MakeNewLord(Faction.OfPlayer,
                        new LordJob_PropagandaCeremony(monument.Position, map), map, participants);
                }
            }

            // 发送通知信
            string durationStr = (PropagandaDurationTicks / 2500f).ToString("F1");
            Find.LetterStack.ReceiveLetter(
                "KCJ_Ceremony_Start_Title".Translate(),
                "KCJ_Ceremony_Start_Text".Translate(durationStr),
                LetterDefOf.PositiveEvent,
                monument);

            Messages.Message("KCJ_Ceremony_Start_Message".Translate(durationStr), monument, MessageTypeDefOf.PositiveEvent);
        }

        /// <summary>完成宣传仪式：计算质量并应用效果（同原版仪式，质量决定效果好坏，没有失败）。</summary>
        private void CompleteCeremony()
        {
            if (activeCeremony == null || activeCeremony.completed)
            {
                return;
            }
            activeCeremony.completed = true;

            // 查找纪念碑
            MonumentMarker monument = null;
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.State != QuestState.Ongoing) continue;
                MonumentMarker mm = GetMonumentMarker(q);
                if (mm != null && mm.thingIDNumber == activeCeremony.monumentId)
                {
                    monument = mm;
                    break;
                }
            }
            if (monument == null)
            {
                monument = Find.CurrentMap?.spawnedThings?.OfType<MonumentMarker>()
                    .FirstOrDefault(m => m.thingIDNumber == activeCeremony.monumentId);
            }

            // 计算质量
            float quality = CalculateCeremonyQuality(monument);

            // 按质量缩放任务点数，质量越高任务越好（同原版仪式逻辑）
            TryManualPropaganda(quality);

            // 根据质量决定信件的积极程度（同原版仪式）
            LetterDef letterDef = quality >= 0.6f ? LetterDefOf.PositiveEvent : LetterDefOf.NeutralEvent;
            string letterTitle = "KCJ_Ceremony_Complete_Title".Translate();
            string letterText = "KCJ_Ceremony_Complete_Text".Translate((quality * 100f).ToString("F0"));

            Find.LetterStack.ReceiveLetter(letterTitle, letterText, letterDef, monument);

            activeCeremony.effectApplied = true;
            activeCeremony = null;

            // 释放 Lord，参与者恢复正常工作
            if (propagandaLord != null)
            {
                propagandaLord.Cleanup();
                Find.CurrentMap?.lordManager?.RemoveLord(propagandaLord);
                propagandaLord = null;
            }
        }

        /// <summary>
        /// 计算宣传仪式质量（0~1），使用原版 RitualOutcomeEffectDef 的 comps 计算，
        /// 与原版文化仪式的质量算法完全一致。
        /// </summary>
        private float CalculateCeremonyQuality(MonumentMarker monument)
        {
            var def = DefDatabase<RitualOutcomeEffectDef>.GetNamedSilentFail("KCJ_MonumentRefreshEffect");
            if (def == null)
            {
                return 0.3f;
            }

            float quality = def.startingQuality;
            int participantCount = activeCeremony?.participantIds?.Count ?? 0;

            foreach (var comp in def.comps)
            {
                if (comp is RitualOutcomeComp_ParticipantCount pc && pc.curve != null)
                {
                    quality += pc.curve.Evaluate(participantCount);
                }
                else if (comp is RitualOutcomeComp_RoomStat rs && rs.curve != null)
                {
                    if (monument != null && monument.Spawned)
                    {
                        Room room = monument.GetRoom(RegionType.Set_Passable);
                        if (room != null)
                        {
                            float statValue = room.GetStat(rs.statDef);
                            quality += rs.curve.Evaluate(statValue);
                        }
                    }
                }
            }

            return Mathf.Clamp(quality, def.minQuality, def.maxQuality);
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
            Messages.Message("KCJ_Enabled_Message".Translate(), m, MessageTypeDefOf.NeutralEvent);
        }

        public void Disable(MonumentMarker m, MarkerState s)
        {
            if (m != null && s.originalSketch != null && m.Spawned)
            {
                m.sketch = s.originalSketch;
            }
            s.kongChengJi = false;
            SoundDefOf.Click.PlayOneShotOnCamera();
            Messages.Message("KCJ_Disabled_Message".Translate(), m, MessageTypeDefOf.NeutralEvent);
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
            // 宣传仪式 tick：检查仪式是否完成
            TickCeremony();

            // 安全清理：如果 Lord 存在但仪式已结束，清理 Lord
            if (propagandaLord != null && (activeCeremony == null || activeCeremony.completed))
            {
                try
                {
                    propagandaLord.Cleanup();
                    Find.CurrentMap?.lordManager?.RemoveLord(propagandaLord);
                }
                catch { }
                propagandaLord = null;
            }

            // 研究完成时下发双倍点数纪念碑任务
            if (!researchCompletedQuestDispatched)
            {
                var proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("KCJ_EmptyCityStrategy");
                if (proj != null && proj.IsFinished)
                {
                    researchCompletedQuestDispatched = true;
                    TryDispatchResearchMonumentQuest();
                }
            }

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

            // ---------- 宣传仪式 tick ----------
            TickCeremony();

            // ---------- 延迟袭击判定 ----------
            TickDelayedAttacks();
        }

        /// <summary>宣传仪式进度 tick：检查仪式是否到时间完成。</summary>
        private void TickCeremony()
        {
            if (activeCeremony == null || activeCeremony.completed || activeCeremony.startTick < 0)
            {
                return;
            }
            if (activeCeremony.IsFinished && !activeCeremony.effectApplied)
            {
                CompleteCeremony();
            }
        }

        /// <summary>
        /// 屏幕常驻全息 HUD：宣传仪式进行时在屏幕顶部居中绘制进度条，
        /// 即使未选中纪念碑也能看到仪式的实时进度与剩余时间。
        /// </summary>
        public override void GameComponentOnGUI()
        {
            if (activeCeremony == null || activeCeremony.completed || activeCeremony.startTick < 0)
            {
                return;
            }
            try
            {
                DrawCeremonyHUD();
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[KongChengJi] HUD draw error: " + ex, 910011);
            }
        }

        /// <summary>绘制宣传仪式全息进度 HUD（顶部居中、半透明暗蓝面板 + 青色进度条 + 剩余时间）。</summary>
        private void DrawCeremonyHUD()
        {
            CeremonyState cer = activeCeremony;
            float progress = Mathf.Clamp01(cer.Progress);

            // 剩余游戏内时间
            int remainingTicks = Mathf.Max(cer.durationTicks - cer.ElapsedTicks, 0);
            int remHours = remainingTicks / 2500;
            int remMinutes = Mathf.Max((remainingTicks % 2500) * 60 / 2500, 1);
            string timeStr = remHours > 0
                ? "KCJ_Time_HoursMinutes".Translate(remHours, remMinutes)
                : "KCJ_Time_Minutes".Translate(remMinutes);

            const float pad = 8f;
            const float titleH = 22f;
            const float barH = 16f;
            const float infoH = 20f;
            const float gap = 5f;
            const float w = 380f;
            float h = pad + titleH + gap + barH + gap + infoH + pad;
            float x = (UI.screenWidth - w) / 2f;
            float y = 8f;

            Rect panel = new Rect(x, y, w, h);

            // 全息面板背景（半透明暗蓝）
            GUI.color = new Color(0f, 0.16f, 0.24f, 0.55f);
            GUI.DrawTexture(panel, BaseContent.WhiteTex);

            // 标题
            GUI.color = new Color(0.6f, 0.95f, 1f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(panel.x + pad, panel.y + pad, panel.width - pad * 2f, titleH),
                "KCJ_HUD_Title".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // 进度条（底 + 填充 + 边框）
            Rect barBox = new Rect(panel.x + pad, panel.y + pad + titleH + gap, panel.width - pad * 2f, barH);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);          // 底色
            GUI.DrawTexture(barBox, BaseContent.WhiteTex);
            GUI.color = new Color(0.1f, 0.75f, 0.88f);        // 进度填充（全息青）
            GUI.DrawTexture(new Rect(barBox.x, barBox.y, barBox.width * progress, barBox.height), BaseContent.WhiteTex);
            GUI.color = new Color(0.3f, 0.85f, 1f);           // 进度条描边
            Widgets.DrawBox(barBox, 1);
            GUI.color = Color.white;

            // 百分比（左） + 剩余时间（右）
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(barBox.x, barBox.y + barBox.height + gap, barBox.width * 0.5f, infoH),
                (progress * 100f).ToString("F0") + "%");
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(barBox.x + barBox.width * 0.5f, barBox.y + barBox.height + gap, barBox.width * 0.5f, infoH),
                "KCJ_HUD_Remaining".Translate(timeStr));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // 全息外边框
            GUI.color = new Color(0.2f, 0.7f, 0.85f, 0.85f);
            Widgets.DrawBox(panel, 1);
            GUI.color = Color.white;
        }

        /// <summary>
        /// 检查所有败露事件，触发延迟袭击，并发送倒计时提醒。
        /// </summary>
        private void TickDelayedAttacks()
        {
            if (exposures == null || exposures.Count == 0)
            {
                return;
            }

            // 每 6000 ticks (~1 in-game hour) 发送一次倒计时提醒
            bool sendReminder = Find.TickManager.TicksGame % 6000 == 0;

            for (int i = exposures.Count - 1; i >= 0; i--)
            {
                ExposureEvent ev = exposures[i];
                if (ev.blameShifted)
                {
                    // 已顶罪，从列表中移除
                    exposures.RemoveAt(i);
                    continue;
                }

                if (ev.attackHappened)
                {
                    // 袭击已发生，保留记录直到超出24h窗口再清理
                    if (!ev.IsWithinWindow)
                    {
                        exposures.RemoveAt(i);
                    }
                    continue;
                }

                // 检查是否到袭击时间
                if (ev.IsAttackDue)
                {
                    TriggerDelayedAttack(ev);
                }
                else if (sendReminder)
                {
                    // 发送倒计时提醒
                    int remainingTicks = TicksAttackDelay - ev.ElapsedTicks;
                    if (remainingTicks > 0)
                    {
                        int remainingHours = remainingTicks / 2500;
                        int remainingMinutes = (remainingTicks % 2500) * 60 / 2500;
                        string timeStr;
                        if (remainingHours > 0)
                        {
                            timeStr = "KCJ_Time_HoursMinutes".Translate(remainingHours, remainingMinutes);
                        }
                        else
                        {
                            timeStr = "KCJ_Time_Minutes".Translate(remainingMinutes);
                        }
                        Messages.Message("KCJ_Warning_Message".Translate(timeStr),
                            MessageTypeDefOf.ThreatSmall);
                    }
                }
            }
        }

        /// <summary>
        /// 触发延迟惩罚：发送 MonumentDestroyed 信号，让原任务系统的失败惩罚机制自然触发。
        /// 如果原任务有袭击惩罚，才会触发袭击；否则仅结束任务。
        /// </summary>
        private void TriggerDelayedAttack(ExposureEvent ev)
        {
            ev.attackHappened = true;

            // 发送 MonumentDestroyed 信号，让原任务系统处理失败惩罚（包括袭击等）
            if (!string.IsNullOrEmpty(ev.questTag))
            {
                try
                {
                    QuestUtility.SendQuestTargetSignals(new List<string> { ev.questTag }, "MonumentDestroyed");
                }
                catch (System.Exception ex)
                {
                    Log.WarningOnce("[KongChengJi] failed to send delayed MonumentDestroyed signal: " + ex, 910007);
                }
            }

            // 发信通知玩家（信的内容会根据原任务是否有袭击而变化）
            Find.LetterStack.ReceiveLetter(
                "KCJ_Penalty_Letter_Title".Translate(),
                "KCJ_Penalty_Letter_Text".Translate(),
                LetterDefOf.ThreatBig,
                GlobalTargetInfo.Invalid);
        }

        /// <summary>统一的"空城计败露"处理：标记已发现 + 关系惩罚 + 延迟袭击 + 弹出窗口。</summary>
        private void FailKongChengJi(MarkerState s, MonumentMarker m, Quest q)
        {
            if (s.discovered)
            {
                return; // 防止同一琐坏重复触发
            }
            s.discovered = true;
            int loss = KongChengJiMod.settings?.goodwillLoss ?? 25;

            // 关系惩罚立即生效
            var reason = DefDatabase<HistoryEventDef>.GetNamedSilentFail("KCJ_EmptyCityExposed");
            string factionDefName = null;
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
                            if (factionDefName == null)
                            {
                                factionDefName = f.def.defName;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[KongChengJi] goodwill error: " + ex, 910003);
            }

            // 记录败露事件（用于延迟袭击和顶罪仪式）
            string questTag = null;
            if (m != null && m.questTags != null && m.questTags.Count > 0)
            {
                questTag = m.questTags[0];
            }
            ExposureEvent ev = new ExposureEvent
            {
                exposureTick = Find.TickManager.TicksGame,
                factionDefName = factionDefName,
                goodwillLoss = loss,
                questTag = questTag
            };
            exposures.Add(ev);

            // 任务不立即结束，保持活跃状态，等待4小时后发送 MonumentDestroyed 信号，
            // 让原任务系统的失败惩罚机制自然触发（如果原任务有袭击惩罚，才会触发袭击）

            // 黑白预览图
            if (KongChengJiAssets.previewTex != null)
            {
                Find.WindowStack.Add(new Dialog_KongChengJiExposed(null, loss));
            }
            Messages.Message("KCJ_Exposed_Message".Translate(q != null && q.name != null ? " " + q.name : ""),
                MessageTypeDefOf.ThreatBig);

            // 发送警告信
            Find.LetterStack.ReceiveLetter(
                "KCJ_Warning_Letter_Title".Translate(),
                "KCJ_Warning_Letter_Text".Translate(KongChengJiMod.settings?.blameShiftPercent ?? 70),
                LetterDefOf.ThreatBig,
                GlobalTargetInfo.Invalid);
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

        /// <summary>研究完成时下发一个双倍点数的纪念碑任务（建造并保护）。</summary>
        private void TryDispatchResearchMonumentQuest()
        {
            QuestScriptDef def = DefDatabase<QuestScriptDef>.GetNamedSilentFail("BuildMonument_TimeProtect");
            if (def == null)
            {
                return;
            }
            IIncidentTarget target = Find.CurrentMap != null ? (IIncidentTarget)Find.CurrentMap : (IIncidentTarget)Find.World;
            float points = target != null ? StorytellerUtility.DefaultThreatPointsNow(target) : 500f;
            if (points < 140f)
            {
                points = 500f;
            }
            points *= 2f; // 双倍点数
            try
            {
                Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(def, points);
                // 发送右侧信件通知（让玩家在右侧信件栏中看到研究完成触发的任务）
                Find.LetterStack.ReceiveLetter(
                    "KCJ_Research_Quest_Letter_Title".Translate(),
                    "KCJ_Research_Quest_Letter_Text".Translate(quest?.name ?? ""),
                    LetterDefOf.PositiveEvent,
                    GlobalTargetInfo.Invalid);
                Messages.Message("KCJ_Research_Quest_Message".Translate(), MessageTypeDefOf.PositiveEvent);
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[KongChengJi] failed to dispatch research monument quest: " + ex, 910009);
            }
        }

        /// <summary>
        /// 应用顶罪效果：撤销部分关系惩罚，标记袭击取消，并结束关联任务。
        /// </summary>
        public void ApplyBlameShift(ExposureEvent ev)
        {
            if (ev == null || ev.blameShifted)
            {
                return;
            }
            ev.blameShifted = true;

            float percent = (KongChengJiMod.settings?.blameShiftPercent ?? 70f) / 100f;
            int restoredGoodwill = Mathf.RoundToInt(ev.goodwillLoss * percent);

            // 恢复关系
            var reason = DefDatabase<HistoryEventDef>.GetNamedSilentFail("KCJ_EmptyCityExposed");
            try
            {
                if (!string.IsNullOrEmpty(ev.factionDefName))
                {
                    Faction faction = Find.FactionManager.AllFactionsListForReading
                        .FirstOrDefault(f => f.def.defName == ev.factionDefName && !f.IsPlayer);
                    if (faction != null)
                    {
                        faction.TryAffectGoodwillWith(Faction.OfPlayer, restoredGoodwill, true, true, reason);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[KongChengJi] blame shift goodwill error: " + ex, 910008);
            }

            // 结束关联任务（阻止原任务惩罚触发，避免任务永久处于"进行中"）
            EndQuestForExposure(ev);

            string attackStatus = ev.attackHappened
                ? "KCJ_Attack_Already_Happened".Translate()
                : "KCJ_Attack_Stopped".Translate();

            Find.LetterStack.ReceiveLetter(
                "KCJ_BlameShift_Letter_Title".Translate(),
                "KCJ_BlameShift_Letter_Text".Translate(restoredGoodwill, KongChengJiMod.settings?.blameShiftPercent ?? 70, attackStatus),
                LetterDefOf.PositiveEvent,
                GlobalTargetInfo.Invalid);
        }

        /// <summary>
        /// 查找与败露事件关联的任务并结束它（不发送 MonumentDestroyed 信号，从而避免袭击惩罚）。
        /// </summary>
        private void EndQuestForExposure(ExposureEvent ev)
        {
            if (string.IsNullOrEmpty(ev.questTag))
            {
                return;
            }
            try
            {
                List<Quest> quests = Find.QuestManager.QuestsListForReading;
                for (int i = 0; i < quests.Count; i++)
                {
                    Quest q = quests[i];
                    if (q == null || q.State != QuestState.Ongoing)
                    {
                        continue;
                    }
                    // 检查该任务的监视目标中是否有纪念碑，且其 questTags 匹配
                    foreach (GlobalTargetInfo t in q.QuestLookTargets)
                    {
                        Thing thing = t.Thing;
                        if (thing is MonumentMarker mm && mm.questTags != null && mm.questTags.Contains(ev.questTag))
                        {
                            q.End(QuestEndOutcome.Fail, false, false);
                            return;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[KongChengJi] failed to end quest for exposure: " + ex, 910010);
            }
        }

        /// <summary>
        /// 查找任意活跃败露事件（用于顶罪仪式 Gizmo）。
        /// </summary>
        public ExposureEvent FindActiveExposure()
        {
            if (exposures == null)
            {
                return null;
            }
            // 找最近一次未顶罪的败露事件（24h内）
            return exposures
                .Where(e => !e.blameShifted && e.IsWithinWindow)
                .OrderByDescending(e => e.exposureTick)
                .FirstOrDefault();
        }

        /// <summary>
        /// 按指定 questTag 查找活跃败露事件（用于顶罪仪式 Gizmo，精确匹配纪念碑）。
        /// </summary>
        public ExposureEvent FindActiveExposureForQuestTag(string questTag)
        {
            if (exposures == null || string.IsNullOrEmpty(questTag))
            {
                return null;
            }
            return exposures
                .Where(e => !e.blameShifted && e.IsWithinWindow && e.questTag == questTag)
                .OrderByDescending(e => e.exposureTick)
                .FirstOrDefault();
        }

        /// <summary>
        /// 从 Gizmo 直接触发顶罪仪式（无需文化仪式系统）。
        /// 放逐玩家指定的 pawn 并应用顶罪效果。
        /// </summary>
        public void TriggerBlameShiftFromGizmo(ExposureEvent ev, Pawn scapegoat)
        {
            if (ev == null || ev.blameShifted)
            {
                Messages.Message("KCJ_BlameShift_NoEvent".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (scapegoat == null || scapegoat.Destroyed)
            {
                Messages.Message("KCJ_BlameShift_NoParticipant".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            string roleLabel = scapegoat.IsSlave ? "KCJ_Role_Intern".Translate() : "KCJ_Role_Employee".Translate();
            string roleTooltip = scapegoat.IsSlave
                ? "KCJ_Role_Intern_Tooltip".Translate()
                : "KCJ_Role_Employee_Tooltip".Translate();

            // 放逐
            BanishPawn(scapegoat, roleLabel);

            // 应用顶罪效果
            ApplyBlameShift(ev);

            // 显示详情
            Find.LetterStack.ReceiveLetter(
                "KCJ_BlameShift_Detail_Title".Translate(),
                "KCJ_BlameShift_Detail_Text".Translate(scapegoat.NameShortColored, roleLabel, roleTooltip),
                LetterDefOf.NeutralEvent,
                scapegoat);
        }

        /// <summary>放逐一名人员。</summary>
        public void BanishPawn(Pawn pawn, string roleLabel)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }
            if (pawn.Spawned)
            {
                pawn.DeSpawn(DestroyMode.Vanish);
            }
            if (pawn.Faction == Faction.OfPlayer)
            {
                pawn.SetFactionDirect(null);
            }
            if (!Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            Messages.Message("KCJ_BlameShift_Banish".Translate(pawn.NameShortColored, roleLabel),
                MessageTypeDefOf.NegativeEvent);
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

        /// <summary>计算原始蓝图中所有被占用的格子。</summary>
        private static HashSet<IntVec3> BuildOccupiedCellSet(Sketch orig)
        {
            var set = new HashSet<IntVec3>();
            foreach (SketchEntity e in orig.Entities)
            {
                foreach (IntVec3 c in e.OccupiedRect)
                {
                    set.Add(c);
                }
            }
            return set;
        }

        /// <summary>判断一个实体是否位于"外墙"上：只要它的任意一个占据格有至少一个正交邻居不在纪念碑占地范围内。</summary>
        private static bool IsOnOuterPerimeter(HashSet<IntVec3> occupiedCells, SketchEntity e)
        {
            foreach (IntVec3 c in e.OccupiedRect)
            {
                if (!occupiedCells.Contains(new IntVec3(c.x - 1, c.y, c.z)) ||
                    !occupiedCells.Contains(new IntVec3(c.x + 1, c.y, c.z)) ||
                    !occupiedCells.Contains(new IntVec3(c.x, c.y, c.z - 1)) ||
                    !occupiedCells.Contains(new IntVec3(c.x, c.y, c.z + 1)))
                {
                    return true;
                }
            }
            return false;
        }

        private static Sketch BuildOuterWallSketch(Sketch orig)
        {
            var newSketch = new Sketch();
            if (orig == null)
            {
                return newSketch;
            }
            HashSet<IntVec3> occupiedCells = BuildOccupiedCellSet(orig);
            foreach (SketchEntity e in orig.Entities)
            {
                if (!(e is RimWorld.SketchBuildable))
                {
                    continue;
                }
                if (IsOnOuterPerimeter(occupiedCells, e))
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
            HashSet<IntVec3> occupiedCells = BuildOccupiedCellSet(orig);
            foreach (SketchEntity e in orig.Entities)
            {
                if (!(e is RimWorld.SketchBuildable sb) || IsOnOuterPerimeter(occupiedCells, e))
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
    }

    /// <summary>确认开启空城计的弹窗，顶部显示（彩色）预览图。</summary>
    public class Dialog_KongChengJiConfirm : Window
    {
        private readonly MonumentMarker marker;
        private readonly MarkerState state;
        private readonly GameComponent_KongChengJi comp;
        private Vector2 scrollPosition;

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
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "KCJ_Confirm_Title".Translate());
            Text.Font = GameFont.Small;

            Rect imgRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, 300f);
            if (KongChengJiAssets.previewTex != null)
            {
                Widgets.DrawTextureFitted(imgRect, KongChengJiAssets.previewTex, 1f);
            }

            float y = imgRect.yMax + 12f;
            float scrollAreaHeight = inRect.yMax - 40f - y - 8f;
            string text = "KCJ_Confirm_Text".Translate();
            float textHeight = Text.CalcHeight(text, inRect.width - 30f);

            Rect outRect = new Rect(inRect.x, y, inRect.width - 4f, scrollAreaHeight);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, textHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, textHeight), text);
            Widgets.EndScrollView();

            float bY = inRect.yMax - 40f;
            if (Widgets.ButtonText(new Rect(inRect.x, bY, 140f, 34f), "KCJ_Confirm_Button".Translate()))
            {
                comp.Enable(marker, state);
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.xMax - 140f, bY, 140f, 34f), "KCJ_Confirm_Cancel".Translate()))
            {
                Close();
            }
        }
    }

    /// <summary>任务失败时弹出的窗口：黑白预览图 + 说明（可滚动）。</summary>
    public class Dialog_KongChengJiExposed : Window
    {
        private readonly MonumentMarker marker;
        private readonly int goodWillLoss;
        private Vector2 scrollPosition;

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
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "KCJ_Exposed_Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect imgRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, 300f);
            Texture2D bw = KongChengJiAssets.PreviewBlackWhite;
            if (bw != null)
            {
                Widgets.DrawTextureFitted(imgRect, bw, 1f);
            }

            // 文本区域使用可滚动视图，避免提示内容溢出不可见
            float y = imgRect.yMax + 12f;
            float scrollAreaHeight = inRect.yMax - 40f - y - 8f;
            string text = "KCJ_Exposed_Text".Translate(goodWillLoss);
            float textHeight = Text.CalcHeight(text, inRect.width - 30f);

            Rect outRect = new Rect(inRect.x, y, inRect.width - 4f, scrollAreaHeight);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, textHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, textHeight), text);
            Widgets.EndScrollView();

            // 关闭按钮
            Rect bY = new Rect(inRect.x + inRect.width / 2f - 70f, inRect.yMax - 40f, 140f, 34f);
            if (Widgets.ButtonText(bY, "KCJ_Exposed_Button".Translate()))
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

            ls.Label("KCJ_Settings_ResearchCost".Translate(settings.researchCost));
            settings.researchCost = (int)ls.Slider(settings.researchCost, 0, 1000);

            ls.Gap(12f);
            ls.Label("KCJ_Settings_DiscoverChance".Translate(settings.discoverChancePercent.ToString("0.##")));
            settings.discoverChancePercent = ls.Slider(settings.discoverChancePercent, 0f, 100f);

            ls.Gap(12f);
            ls.Label("KCJ_Settings_GoodwillLoss".Translate(settings.goodwillLoss));
            settings.goodwillLoss = (int)ls.Slider(settings.goodwillLoss, 1, 100);

            ls.Gap(16f);
            ls.Label("KCJ_Settings_BlameShift_Title".Translate());
            ls.Gap(4f);
            ls.Label("KCJ_Settings_BlameShift_Percent".Translate(settings.blameShiftPercent.ToString("0.##")));
            settings.blameShiftPercent = ls.Slider(settings.blameShiftPercent, 0f, 100f);
            ls.Label("KCJ_Settings_BlameShift_Desc".Translate());

            ls.Gap(16f);
            ls.Label("KCJ_Settings_Boost_Title".Translate());
            ls.CheckboxLabeled("KCJ_Settings_Boost_Checkbox".Translate(), ref settings.boostMonumentQuests);
            if (settings.boostMonumentQuests)
            {
                ls.Label("KCJ_Settings_Boost_Multiplier".Translate(settings.boostMonumentMultiplier.ToString("0.##")));
                settings.boostMonumentMultiplier = ls.Slider(settings.boostMonumentMultiplier, 1f, 50f);
            }

            ls.Gap(16f);
            ls.Label("KCJ_Settings_Ritual_Title".Translate());
            ls.Label("KCJ_Settings_Ritual_Desc".Translate());
            ls.Gap(4f);
            settings.ritualMonumentChancePercent = ls.SliderLabeled(
                "KCJ_Settings_Ritual_Chance".Translate(settings.ritualMonumentChancePercent.ToString("0.##")),
                settings.ritualMonumentChancePercent, 0f, 100f,
                tooltip: "KCJ_Settings_Ritual_Tooltip".Translate());

            ls.Gap(16f);
            ls.Label("KCJ_Settings_Propaganda_Title".Translate());
            ls.Gap(4f);
            settings.propagandaDurationHours = ls.SliderLabeled(
                "KCJ_Settings_Propaganda_Duration".Translate(settings.propagandaDurationHours.ToString("F1")),
                settings.propagandaDurationHours, 0.5f, 12f,
                tooltip: "KCJ_Settings_Propaganda_Duration_Tooltip".Translate());
            ls.Label("KCJ_Settings_Propaganda_Cooldown".Translate(settings.propagandaCooldownDays));
            settings.propagandaCooldownDays = (int)ls.Slider(settings.propagandaCooldownDays, 1, 30);

            ls.Gap(16f);
            ls.Label("KCJ_Settings_Note".Translate());
            ls.End();

            // 设置改动后实时同步到对应 Def（研究点数 / 纪念碑权重），无需重启游戏
            KongChengJiHelpers.ApplyResearchCost();
            KongChengJiHelpers.ApplyMonumentBoost();
        }

        public override string SettingsCategory() => "KCJ_Settings_Category".Translate();
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

        // 空城计宣传仪式
        public float propagandaDurationHours = 2f;          // 仪式持续时长（游戏内小时，默认 2 小时 = 5000 ticks）
        public int propagandaCooldownDays = 5;               // 仪式冷却天数（默认 5 天）

        // 空城计-顶罪
        public float blameShiftPercent = 70f;   // 顶罪仪式撤销关系惩罚的比例（默认 70%）

        public override void ExposeData()
        {
            Scribe_Values.Look(ref discoverChancePercent, "discoverChancePercent", 1f);
            Scribe_Values.Look(ref goodwillLoss, "goodwillLoss", 25);
            Scribe_Values.Look(ref researchCost, "researchCost", 200);
            Scribe_Values.Look(ref boostMonumentQuests, "boostMonumentQuests", false);
            Scribe_Values.Look(ref boostMonumentMultiplier, "boostMonumentMultiplier", 10f);
            Scribe_Values.Look(ref ritualMonumentChancePercent, "ritualMonumentChancePercent", 100f);
            Scribe_Values.Look(ref propagandaDurationHours, "propagandaDurationHours", 2f);
            Scribe_Values.Look(ref propagandaCooldownDays, "propagandaCooldownDays", 5);
            Scribe_Values.Look(ref blameShiftPercent, "blameShiftPercent", 70f);
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
        public static void TryRefreshMonumentQuest(float pointsMult = 1f)
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
            points *= pointsMult;
            try
            {
                QuestUtility.GenerateQuestAndMakeAvailable(def, points);
                Messages.Message("KCJ_Ritual_Quest_Message".Translate(),
                    MessageTypeDefOf.PositiveEvent);
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[KongChengJi] failed to refresh monument quest: " + ex, 910006);
            }
        }
    }

    /// <summary>
    /// 带进度条的 Command_Action，仪式进行时在 Gizmo 底部显示进度条和百分比。
    /// </summary>
    public class Command_ActionWithProgressBar : Command_Action
    {
        public Func<float> progressGetter;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            GizmoResult result = base.GizmoOnGUI(topLeft, maxWidth, parms);

            if (Disabled && progressGetter != null)
            {
                float progress = Mathf.Clamp01(progressGetter());
                float width = GetWidth(maxWidth);
                float barHeight = 14f;
                Rect barRect = new Rect(topLeft.x + 4f, topLeft.y + 74f - barHeight - 2f, width - 8f, barHeight);

                // 背景
                GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
                GUI.DrawTexture(barRect, BaseContent.WhiteTex);
                // 进度条
                GUI.color = new Color(0.2f, 0.8f, 0.85f); // 同 RimWorld 1.6 Widgets.BarFullTexHor 颜色
                Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * progress, barRect.height);
                GUI.DrawTexture(fillRect, BaseContent.WhiteTex);
                // 边框
                GUI.color = Color.white;
                Widgets.DrawBox(barRect, 1);

                // 百分比文字
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.white;
                Widgets.Label(barRect, (progress * 100f).ToString("F0") + "%");
                Text.Anchor = TextAnchor.UpperLeft;
            }

            return result;
        }
    }

    /// <summary>
    /// 宣传仪式 LordToil：让参与者在纪念碑周围聚集（同原版集会/派对）。
    /// 使用 DutyDefOf.Gathering，小人会在纪念碑周围待着，可以吃饭、社交，
    /// 不会去做其他工作。
    /// </summary>
    public class LordToil_PropagandaCeremony : LordToil
    {
        public IntVec3 spot;

        public override ThinkTreeDutyHook VoluntaryJoinDutyHookFor(Pawn p)
        {
            return ThinkTreeDutyHook.MediumPriority;
        }

        public override void UpdateAllDuties()
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn != null && !pawn.Destroyed)
                {
                    // 与 Vanilla LordToil_Ritual 一致：让参与者聚集在纪念碑周围围观仪式，
                    // 并通过 spectateRect/spectateDistance 锚定在固定范围内，再强制打断他们手头的活。
                    PawnDuty duty = new PawnDuty(DutyDefOf.Spectate, spot);
                    duty.spectateRect = CellRect.CenteredOn(spot, 0);
                    duty.spectateDistance = new IntRange(2, 3);
                    duty.spectateRectAllowedSides = SpectateRectSide.All;
                    duty.spectateRectPreferredSide = SpectateRectSide.Down;

                    pawn.mindState.duty = duty;
                    pawn.mindState.priorityWork.ClearPrioritizedWorkAndJobQueue();
                    pawn.jobs?.CheckForJobOverride();
                }
            }
        }
    }

    /// <summary>
    /// 宣传仪式 LordJob：管理仪式期间参与者的行为。
    /// 当仪式开始时创建 Lord，参与者加入 Lord，仪式结束时释放 Lord。
    /// </summary>
    public class LordJob_PropagandaCeremony : LordJob
    {
        private IntVec3 spot;
        private int mapIndex;

        public LordJob_PropagandaCeremony() { }

        public LordJob_PropagandaCeremony(IntVec3 spot, Map map)
        {
            this.spot = spot;
            this.mapIndex = map.Index;
        }

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var toil = new LordToil_PropagandaCeremony { spot = spot };
            graph.AddToil(toil);
            return graph;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref spot, "spot");
            Scribe_Values.Look(ref mapIndex, "mapIndex");
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

    /// <summary>
    /// "空城计-顶罪"文化仪式效果的工作逻辑。
    /// 在仪式中放逐一名参与者（殖民者视为"正式员工"，奴隶视为"实习生"），
    /// 记为顶罪，撤销部分关系惩罚并阻止派系袭击。
    /// 由 Defs/RitualOutcomeEffects.xml 中的 KCJ_BlameShiftEffect 引用（workerClass）。
    /// </summary>
    public class RitualOutcomeEffectWorker_KongChengJiBlameShift : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_KongChengJiBlameShift(RitualOutcomeEffectDef def) : base(def)
        {
        }

        public override void Apply(float progress, Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual)
        {
            // 先跑原版逻辑（结果信、心情等）
            base.Apply(progress, totalPresence, jobRitual);

            if (!ModsConfig.IdeologyActive || Current.Game == null)
            {
                return;
            }

            // 查找活跃的败露事件
            var comp = Current.Game.GetComponent<GameComponent_KongChengJi>();
            if (comp == null)
            {
                return;
            }
            ExposureEvent ev = comp.FindActiveExposure();
            if (ev == null)
            {
                Messages.Message("KCJ_BlameShift_NoEvent".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // 收集参与者作为候选列表，打开选择弹窗让玩家手动选择顶罪者
            List<Pawn> participants = new List<Pawn>();
            if (totalPresence != null)
            {
                foreach (Pawn pawn in totalPresence.Keys)
                {
                    if (pawn != null && !pawn.Destroyed && (pawn.IsFreeColonist || pawn.IsSlave))
                    {
                        participants.Add(pawn);
                    }
                }
            }
            if (participants.Count == 0)
            {
                Messages.Message("KCJ_BlameShift_NoParticipant".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new Dialog_BlameShiftRitualSelection(ev, participants));
        }
    }

    // ====================================================================
    // 宣传仪式自动触发已移除：
    // 原先这里用反射把 Harmony Postfix 广播到所有
    // RitualOutcomeEffectWorker_FromQuality 子类（含血族/圣物等全部文化仪式），
    // 极易干扰非本模组的仪式并造成循环红字。
    // 现改为仅在文化编辑中把 KCJ_MonumentRefreshEffect 挂载到某场仪式上时，
    // 由 RitualOutcomeEffectWorker_KongChengJiMonument 触发刷新纪念碑任务，
    // 不再对全游戏仪式兜底处理。
    //
    // 宣传/顶罪仪式的核心逻辑（Dialog_PropagandaCeremony -> StartCeremony ->
    // CompleteCeremony -> TryManualPropaganda）不受影响，依旧可用。
    // ====================================================================

    /// <summary>
    /// 宣传仪式对话框：选择参与者，查看质量预览，然后开始仪式。
    /// </summary>
    public class Dialog_PropagandaCeremony : Window
    {
        private readonly MonumentMarker monument;
        private readonly GameComponent_KongChengJi comp;
        private readonly List<Pawn> allCandidates;
        private readonly HashSet<int> selectedIds = new HashSet<int>();
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(480f, 520f);

        public Dialog_PropagandaCeremony(MonumentMarker marker, GameComponent_KongChengJi component)
        {
            monument = marker;
            comp = component;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;

            // 收集所有可参与的小人（殖民者 + 奴隶）
            allCandidates = new List<Pawn>();
            if (Find.CurrentMap?.mapPawns != null)
            {
                foreach (Pawn pawn in Find.CurrentMap.mapPawns.FreeColonistsAndPrisonersSpawned)
                {
                    if (pawn != null && !pawn.Destroyed && (pawn.IsFreeColonist || pawn.IsSlave))
                    {
                        allCandidates.Add(pawn);
                    }
                }
            }
            allCandidates = allCandidates.OrderByDescending(p => p.IsSlave).ThenBy(p => p.LabelShort).ToList();

            // 默认选中所有
            foreach (Pawn p in allCandidates)
            {
                selectedIds.Add(p.thingIDNumber);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "KCJ_Ceremony_Dialog_Title".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 35f;

            // 质量预览
            string durationStr = (comp.PropagandaDurationTicks / 2500f).ToString("F1");
            string previewText = "KCJ_Ceremony_Dialog_Info".Translate(durationStr);
            float infoHeight = Text.CalcHeight(previewText, inRect.width - 20f);
            Widgets.Label(new Rect(inRect.x + 4f, y, inRect.width - 20f, infoHeight), previewText);
            y += infoHeight + 8f;

            // 参与人数预览
            int count = selectedIds.Count;
            float quality = CalculatePreviewQuality(count, monument);
            Widgets.Label(new Rect(inRect.x + 4f, y, inRect.width - 20f, 22f),
                "KCJ_Ceremony_Dialog_Quality".Translate(count, (quality * 100f).ToString("F0")));
            y += 26f;

            // 分隔线
            y += 4f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f), "KCJ_Ceremony_Dialog_Participants".Translate());
            y += 20f;

            // 参与者列表（可滚动）
            const float entryHeight = 40f;
            const float iconSize = 30f;
            float listHeight = inRect.yMax - 40f - y - 10f;
            float totalHeight = allCandidates.Count * entryHeight;

            Rect outRect = new Rect(inRect.x, y, inRect.width, listHeight);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, totalHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            for (int i = 0; i < allCandidates.Count; i++)
            {
                Pawn pawn = allCandidates[i];
                Rect entryRect = new Rect(0f, i * entryHeight, viewRect.width, entryHeight - 2f);

                Widgets.DrawHighlightIfMouseover(entryRect);

                // 小人图标
                Rect iconRect = new Rect(4f, i * entryHeight + 5f, iconSize, iconSize);
                Widgets.ThingIcon(iconRect, pawn);

                // 复选框（作为唯一切换入口，避免与整行按钮双重触发导致无法打钩）
                bool isSelected = selectedIds.Contains(pawn.thingIDNumber);
                Rect checkRect = new Rect(iconRect.xMax + 4f, i * entryHeight + 5f, 24f, entryHeight - 10f);
                bool modified = isSelected;
                Widgets.Checkbox(checkRect.position, ref modified, 24f);
                if (modified != isSelected)
                {
                    if (modified) selectedIds.Add(pawn.thingIDNumber);
                    else selectedIds.Remove(pawn.thingIDNumber);
                }

                string roleLabel = pawn.IsSlave ? "KCJ_Role_Intern".Translate() : "KCJ_Role_Employee".Translate();
                string label = pawn.NameShortColored + "  (" + roleLabel + ")";
                Widgets.Label(new Rect(checkRect.xMax + 4f, i * entryHeight + 5f, viewRect.width - checkRect.xMax - 4f, entryHeight - 6f), label);
            }

            Widgets.EndScrollView();

            // 按钮
            float bY = inRect.yMax - 40f;
            bool canStart = selectedIds.Count > 0;
            if (canStart && Widgets.ButtonText(new Rect(inRect.x, bY, 140f, 34f), "KCJ_Ceremony_Start_Button".Translate()))
            {
                List<Pawn> participants = allCandidates.Where(p => selectedIds.Contains(p.thingIDNumber)).ToList();
                comp.StartCeremony(monument, participants);
                Close();
            }
            if (!canStart)
            {
                Widgets.Label(new Rect(inRect.x, bY, 140f, 34f), "KCJ_Ceremony_NoParticipants".Translate());
            }
            if (Widgets.ButtonText(new Rect(inRect.xMax - 140f, bY, 140f, 34f), "KCJ_Confirm_Cancel".Translate()))
            {
                Close();
            }
        }

        private float CalculatePreviewQuality(int participantCount, MonumentMarker m)
        {
            var def = DefDatabase<RitualOutcomeEffectDef>.GetNamedSilentFail("KCJ_MonumentRefreshEffect");
            if (def == null)
            {
                return 0.3f;
            }

            float quality = def.startingQuality;

            foreach (var comp in def.comps)
            {
                if (comp is RitualOutcomeComp_ParticipantCount pc && pc.curve != null)
                {
                    quality += pc.curve.Evaluate(participantCount);
                }
                else if (comp is RitualOutcomeComp_RoomStat rs && rs.curve != null)
                {
                    if (m != null && m.Spawned)
                    {
                        Room room = m.GetRoom(RegionType.Set_Passable);
                        if (room != null)
                        {
                            float statValue = room.GetStat(rs.statDef);
                            quality += rs.curve.Evaluate(statValue);
                        }
                    }
                }
            }

            return Mathf.Clamp(quality, def.minQuality, def.maxQuality);
        }
    }
}