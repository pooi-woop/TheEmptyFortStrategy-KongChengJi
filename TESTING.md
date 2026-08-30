# 空城计 (The Empty Fort Strategy) — 测试清单

本文件记录如何测试该 Mod。触发方式与验证步骤均基于 RimWorld 1.6 + Royalty DLC。

---

## 1. 测试前准备

1. **启用 Mod 并重启游戏**，确认无红字、无报错。
2. **研究"空城计"科技**（Neolithic / 默认 200 点，可在 Mod 设置中改为 0~1000）。
   - 未研究则该 Mod 的按钮不出现。
   - 设置"研究点数"为 0 时，该研究默认已完成、按钮直接可用。
   - 可在开发菜单 `General → Finish all research` 一把解锁，最快。
3. **（可选）在设置里把发现概率调高**：选项 → Mod 设置 → 空城计 → 把发现概率拖满，便于快速验证败露逻辑；调到 0% 则永不被发现。

## 2. 如何触发纪念碑任务

纪念碑建造任务由 Royalty 的 Quest 系统随机生成，无法在正常游戏里直接接取。最快捷的方式是**开发者模式手动生成**：

1. 主菜单 → 选项(Options) → 勾选 `Development Mode`。
2. 进入游戏，点击屏幕上沿调试工具栏的 **"Open debug actions menu"**。
3. 顶部搜索框输入 `quest`。
4. 点 **Quests → "Generate Quest…"**。
5. 下拉选择 `QuestScriptDef`：
   - **`BuildMonument_TimeProtect`**：建造 + 保护 N 天（保护期流程，测试本 Mod 必选）。
   - `BuildMonument_Basic`：仅建造，无保护期。

> 提示：
> - 任务根 defName 存放于：`Data/Royalty/Defs/QuestScriptDefs/BuildMonument/Script_BuildMonument_Root_TimeProtect.xml`。
> - 若生成失败提示点数额度不足，可 `General → Finish all research` 并适当提高殖民地财富/进度分数。
> - 王座链版本通过 `Scripts_Decree` 发布，测试直接走 Generate Quest 更快。

## 3. 主流程验证

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 接取纪念碑任务后，选中纪念碑 | 底部出现"空城计"按钮（研究后） |
| 2 | 点击"空城计" | 弹出确认窗，顶部显示**彩色预览图** |
| 3 | 点"开启空城计" | 蓝图只剩**外墙一圈**，内部清空 |
| 4 | 只建造外墙 | 任务判定"完工"，进入保护期 |
| 5 | 设置里把发现概率调到 100% | 建成后**下一天**触发败露 |
| 6 | 触发败露 | 弹出**黑白预览图**窗口，任务失败，委托派系关系下降 |
| 7 | 查看关系 | 下降数值与设置里的"关系减少量"一致（默认 25） |

## 4. 可单独验证的点

- **开关切换**：开启后再次点击"空城计"按钮可关闭，蓝图恢复完整原版。
- **不影响原版**：不开启空城计时，正常建满全图 → 任务正常成功、无惩罚。
- **存档/读档**：开启空城计后存档→读档，状态与原蓝图应保持。

## 4.1 新增设置验证

| 功能 | 操作 | 预期 |
|------|------|------|
| 研究点数 | 设置里改 0 | 研究列表"空城计"直接完成，按钮可用 |
| 研究点数 | 设置里改 200（或其它值） | 研究所需点数随之变化 |
| 被发现概率 0% | 设置里拉到 0%，开空城计并把任务保护期走完 | 永不被发现、任务成功 |
| 提高出现概率 | 勾选"提高纪念碑任务出现概率"并设放大倍数 | Generate Quest 时 BuildMonument 出现频率显著上升；取消勾选恢复正常 |
| 文化仪式刷新（需 Ideology） | 在"编辑文化"里把"空城计-宣传"效果选到某场仪式，概率调高 | 该仪式以"良好及以上"结束后，刷新出新的纪念碑任务 |
| 文化仪式未选效果 | 不给任何仪式选"空城计-宣传"效果 | 仪式结束后不刷新纪念碑任务（该效果为可选项，默认可用但按需选用） |
| 无 Ideology DLC | 仅打包含 Royalty 或基础环境 | 不影响研究/发现/概率加成等其余功能，仪式选项不生效但不报红字 |

## 5. 关键文件参考

- 蓝图按钮 / 外墙提取 / 每日发现判定 / 任务失败：`Source/PooiKongChengJi/Main.cs`（Harmony 补丁 + GameComponent）。
- 研究门槛：`Defs/ResearchDefs.xml`（`KCJ_EmptyCityStrategy`，默认 200 点）。
- 关系惩罚事件：`KCJ_EmptyCityExposed`（`Defs/ResearchDefs.xml`）。
- 研究点数 / 纪念碑权重 / 文化仪式刷新：`KongChengJiHelpers` 与 `RitualOutcomeEffectWorker_KongChengJiMonument`（`Source/PooiKongChengJi/Main.cs`）。
- 文化仪式效果 Def：`Defs/RitualOutcomeEffects.xml`（`KCJ_MonumentRefreshEffect`）。