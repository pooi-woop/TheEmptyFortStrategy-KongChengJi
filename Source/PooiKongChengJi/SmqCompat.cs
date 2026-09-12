using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PooiKongChengJi
{
    /// <summary>
    /// Simple Monument Quest (packageId: ZuoYao.SimpleMonumentQuest) 适配层。
    /// =================================================================
    /// SMQ 会把原版 MonumentMarker 的 thingClass 替换成子类 MonumentMarker_Simple，
    /// 并"完全重写" GetGizmos（不调用基类实现），因此：
    ///   1. 本 mod 对基类 MonumentMarker.GetGizmos 的后缀补丁在 SMQ 纪念碑上不会执行，
    ///      必须额外给 MonumentMarker_Simple.GetGizmos 打一个同样的后缀补丁，空城计按钮才会出现；
    ///   2. SMQ 按标记上的草图重新计算 1x1 单体纪念碑的材料需求（TryConvert 遍历 sketch.Buildables）。
    ///      因此空城计模式对 SMQ 纪念碑的处理与原版一致：开启时把草图替换为"只有外墙"的版本，
    ///      关闭时还原完整草图，并且每次切换后都强制 SMQ 按当前草图重算材料需求——
    ///      开关随时生效（含选料施工后），开=外墙价、关=全价。
    ///
    /// 为避免 SMQ 未安装时类型加载失败，这里不引用 SMQ 程序集，全部通过反射访问；
    /// 仅当检测到 SMQ 程序集已加载时才会应用补丁。
    /// </summary>
    public static class SmqCompat
    {
        private const string MarkerTypeName = "ZuoYao.SimpleMonumentQuest.MonumentMarker_Simple";

        private static bool? active;
        private static bool patched;

        private static Type markerType;
        private static FieldInfo fiConverted;

        /// <summary>SMQ 是否已启用（结果缓存）。
        /// 注意：不能用 ModLister.GetActiveModWithIdentifier 判断——ModsConfig.IsActive 依赖
        /// activeModsHashSet（存原始字符串、大小写敏感），若 ModsConfig.xml 里 packageId
        /// 大小写与注册的不一致，mod 内容会照常加载（加载路径大小写不敏感）但 Active 会误判
        /// 为 false。而 SMQ 的程序集只有在 mod 真正启用时才会加载，因此"类型已加载"才是唯一
        /// 可靠的信号。</summary>
        public static bool Active
        {
            get
            {
                if (active == null)
                {
                    EnsureTypeLoaded();
                    active = markerType != null;
                }
                return active.Value;
            }
        }

        /// <summary>该纪念碑是否是 SMQ 的 MonumentMarker_Simple。</summary>
        public static bool IsSmqMarker(MonumentMarker marker)
        {
            if (!Active || marker == null)
            {
                return false;
            }
            EnsureTypeLoaded();
            return markerType != null && markerType.IsInstanceOfType(marker);
        }

        /// <summary>SMQ 纪念碑是否已完成"选料转换"（材料需求已生成）。</summary>
        public static bool IsConverted(MonumentMarker marker)
        {
            if (!IsSmqMarker(marker))
            {
                return false;
            }
            EnsureTypeLoaded();
            if (markerType == null || fiConverted == null)
            {
                return false;
            }
            try
            {
                return fiConverted.GetValue(marker) is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>在空城计静态启动时调用：若检测到 SMQ（其程序集已加载），则给 SMQ 的方法打补丁。</summary>
        public static void ApplyPatches(Harmony harmony)
        {
            if (!Active || patched || harmony == null)
            {
                return;
            }
            patched = true;
            try
            {
                if (markerType == null)
                {
                    Log.Warning("[KongChengJi] SMQ detected but MonumentMarker_Simple not found; compatibility disabled.");
                    return;
                }

                var getGizmos = AccessTools.Method(markerType, "GetGizmos");
                if (getGizmos != null)
                {
                    harmony.Patch(getGizmos,
                        postfix: new HarmonyMethod(typeof(SmqCompat), nameof(GetGizmosPostfix)));
                    KongChengJiLog.Log("patched MonumentMarker_Simple.GetGizmos (postfix)");
                }
                else
                {
                    Log.Warning("[KongChengJi] MonumentMarker_Simple.GetGizmos not found!");
                }

                var tryConvert = AccessTools.Method(markerType, "TryConvert");
                if (tryConvert != null)
                {
                    harmony.Patch(tryConvert,
                        postfix: new HarmonyMethod(typeof(SmqCompat), nameof(TryConvertPostfix)));
                    KongChengJiLog.Log("patched MonumentMarker_Simple.TryConvert (postfix, diagnostics)");
                }
                else
                {
                    Log.Warning("[KongChengJi] MonumentMarker_Simple.TryConvert not found!");
                }

                Log.Message("[KongChengJi] Simple Monument Quest compatibility patched: gizmos + outer-wall sketch swap + live recompute.");
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[KongChengJi] failed to patch SMQ compatibility: " + ex, 910020);
            }
        }

        /// <summary>
        /// 让 SMQ 按当前草图重新计算材料需求（开关空城计替换草图之后调用）。
        /// 做法：ResetConstructionCosts 清空已算死的需求 -> 重新 TryConvert（保留玩家已选的材料组）
        /// -> 若之前已完成选料（converted）则恢复该标记，蓝图/框架的成本是实时读取 MaterialCosts 的，
        /// 因此重算立即生效。
        /// </summary>
        public static bool RecomputeRequirements(MonumentMarker marker)
        {
            if (!IsSmqMarker(marker))
            {
                return false;
            }
            EnsureTypeLoaded();
            try
            {
                bool wasConverted = IsConverted(marker);
                AccessTools.Method(markerType, "ResetConstructionCosts")?.Invoke(marker, null);
                // 未选料时必须清空建造组：ResetConstructionCosts 只清各组需求，不清组列表本身；
                // 而 TryConvert 见组已存在就跳过 CreateConstructionGroups，组里缓存的
                // stuffVolumeCount（选料面板显示的材料量）仍是旧草图的全价值，面板于是永远全价。
                // 清空后 TryConvert 会按当前（外墙/还原后）草图重建组，面板数字立即随开关刷新。
                // 已选料（converted）时不能清：重建会丢已选材料且 TryConvert 直接失败。
                if (!wasConverted)
                {
                    (AccessTools.Field(markerType, "constructionGroups")?.GetValue(marker)
                        as System.Collections.IList)?.Clear();
                }
                bool ok = AccessTools.Method(markerType, "TryConvert")?.Invoke(marker, new object[] { null }) is bool b && b;
                if (ok && wasConverted && fiConverted != null)
                {
                    fiConverted.SetValue(marker, true);
                }
                var groups = AccessTools.Field(markerType, "constructionGroups")?.GetValue(marker)
                    as System.Collections.IList;
                KongChengJiLog.Log("SMQ recompute: marker=" + marker.thingIDNumber
                    + " wasConverted=" + wasConverted
                    + " tryConvert=" + ok
                    + " groups=" + (groups != null ? groups.Count.ToString() : "?")
                    + " reqs=[" + DescribeRequirements(marker) + "]");
                return ok;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[KongChengJi] SMQ recompute failed: " + ex, 910022);
                return false;
            }
        }

        /// <summary>反射读取 SMQ 标记的 requirements / fixedRequirements，拼成可读文本（用于日志）。</summary>
        private static string DescribeRequirements(MonumentMarker marker)
        {
            var sb = new StringBuilder();
            if (markerType == null)
            {
                return "???";
            }
            foreach (string fieldName in new[] { "requirements", "fixedRequirements" })
            {
                var list = AccessTools.Field(markerType, fieldName)?.GetValue(marker) as System.Collections.IEnumerable;
                if (list == null)
                {
                    continue;
                }
                sb.Append(fieldName).Append(":");
                foreach (object item in list)
                {
                    var def = AccessTools.Field(item.GetType(), "thingDef")?.GetValue(item) as ThingDef;
                    object count = AccessTools.Field(item.GetType(), "count")?.GetValue(item);
                    sb.Append(def != null ? def.defName : "null").Append("x").Append(count).Append(" ");
                }
                sb.Append("| ");
            }
            return sb.ToString();
        }

        private static void EnsureTypeLoaded()
        {
            if (markerType != null)
            {
                return;
            }
            markerType = AccessTools.TypeByName(MarkerTypeName);
            if (markerType != null)
            {
                fiConverted = AccessTools.Field(markerType, "converted");
            }
        }

        // ---------- Harmony 补丁 ----------

        /// <summary>给 SMQ 重写后的 GetGizmos 追加空城计按钮（SMQ 不调用基类，基类补丁不会执行）。</summary>
        private static void GetGizmosPostfix(ref IEnumerable<Gizmo> __result, MonumentMarker __instance)
        {
            IEnumerable<Gizmo> extra = KongChengJiGizmos.ForMarker(__instance);
            __result = __result == null ? extra : __result.Concat(extra);
        }

        /// <summary>诊断日志：SMQ 每次按草图重算材料需求时，记录草图实体数、结果与最终需求。</summary>
        private static void TryConvertPostfix(MonumentMarker __instance, bool __result)
        {
            try
            {
                KongChengJiLog.Log("SMQ TryConvert: marker=" + __instance.thingIDNumber
                    + " sketchEntities=" + (__instance.sketch != null ? __instance.sketch.Entities.Count : -1)
                    + " result=" + __result
                    + " reqs=[" + DescribeRequirements(__instance) + "]");
            }
            catch (Exception ex)
            {
                Log.WarningOnce("[KongChengJi] TryConvert log failed: " + ex, 910023);
            }
        }
    }
}
