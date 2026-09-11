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
    ///   2. SMQ 纪念碑是 1x1 单体建筑，没有"内部"和"外墙"之分，
    ///      空城计模式对它不再替换外墙草图，而是对 SMQ 依据原任务草图算出的材料用量打折；
    ///   3. SMQ 的蓝图、框架（搬运/消耗）和竣工结算（血量/美观/冥想加成）全部实时读取
    ///      MonumentMarker_Simple.MaterialCosts() 的返回值，
    ///      所以后缀补丁该方法即可实现"动态打折"：开关立即生效，无需改动任何存档数据，
    ///      偷工减料也会如实反映在纪念碑的血量与美观度上。
    ///
    /// 为避免 SMQ 未安装时类型加载失败，这里不引用 SMQ 程序集，全部通过反射访问；
    /// 仅当 ModLister 检测到 SMQ 已启用时才会应用补丁。
    /// </summary>
    public static class SmqCompat
    {
        private const string SmqPackageId = "ZuoYao.SimpleMonumentQuest";
        private const string MarkerTypeName = "ZuoYao.SimpleMonumentQuest.MonumentMarker_Simple";

        private static bool? active;
        private static bool patched;

        private static Type markerType;
        private static FieldInfo fiConverted;

        /// <summary>SMQ 是否已启用（结果缓存）。</summary>
        public static bool Active
        {
            get
            {
                if (active == null)
                {
                    active = ModLister.GetActiveModWithIdentifier(SmqPackageId) != null;
                }
                return active.Value;
            }
        }

        /// <summary>空城计模式下的材料减免百分比（0~90，50 = 打五折）。</summary>
        public static float DiscountPercent => Mathf.Clamp(KongChengJiMod.settings?.smqDiscountPercent ?? 50f, 0f, 90f);

        /// <summary>材料用量折扣系数（DiscountPercent=50 时为 0.5）。</summary>
        public static float DiscountFactor => 1f - DiscountPercent / 100f;

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

        /// <summary>SMQ 纪念碑是否已完成"选料转换"（requirements 已生成）。</summary>
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

        /// <summary>在空城计静态启动时调用：若 SMQ 已启用，则给 SMQ 的方法打补丁。</summary>
        public static void ApplyPatches(Harmony harmony)
        {
            if (!Active || patched || harmony == null)
            {
                return;
            }
            patched = true;
            try
            {
                EnsureTypeLoaded();
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

                var materialCosts = AccessTools.Method(markerType, "MaterialCosts");
                if (materialCosts != null)
                {
                    harmony.Patch(materialCosts,
                        postfix: new HarmonyMethod(typeof(SmqCompat), nameof(MaterialCostsPostfix)));
                }

                Log.Message("[KongChengJi] Simple Monument Quest compatibility patched: gizmos + material discount (" + DiscountPercent.ToString("0.#") + "% off).");
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

        /// <summary>
        /// 空城计核心折扣：该纪念碑处于空城计模式时，把 SMQ 计算出的材料用量按设置打折。
        /// SMQ 的蓝图/框架/竣工结算全部实时调用本方法，因此折扣立即生效；
        /// 关闭空城计后下一次调用即恢复原价。
        /// </summary>
        private static void MaterialCostsPostfix(List<ThingDefCountClass> __result, MonumentMarker __instance)
        {
            if (__result == null || __result.Count == 0)
            {
                return;
            }
            var comp = Current.Game?.GetComponent<GameComponent_KongChengJi>();
            if (comp == null || !comp.IsKongChengJiNoCreate(__instance))
            {
                return;
            }
            float factor = DiscountFactor;
            if (factor >= 0.999f)
            {
                return;
            }
            for (int i = 0; i < __result.Count; i++)
            {
                var cost = __result[i];
                if (cost == null || cost.thingDef == null)
                {
                    continue;
                }
                // 与 SMQ 自身取整风格一致：至少保留 1 个
                cost.count = Mathf.Max(1, Mathf.RoundToInt(cost.count * factor));
            }
        }
    }
}
