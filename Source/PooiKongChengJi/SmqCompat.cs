using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    ///      SMQ 随后选料时计算出的材料需求就只包含外墙部分，内部结构一律不计；
    ///      关闭时还原完整草图（仅限尚未选料施工时，已选料则锁定切换防止白嫖）。
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

        /// <summary>SMQ 纪念碑是否已完成"选料转换"（材料需求已按当前草图锁定）。</summary>
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
                }

                Log.Message("[KongChengJi] Simple Monument Quest compatibility patched: gizmos + outer-wall sketch swap.");
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[KongChengJi] failed to patch SMQ compatibility: " + ex, 910020);
            }
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
    }
}
