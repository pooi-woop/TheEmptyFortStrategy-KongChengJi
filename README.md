# 空城计 (The Empty Fort Strategy)

> "虚则实之，实则虚之。" —— 一座空城，足以退敌。

## 简介

灵感来自互联网热梗「空城计」。本 Mod 为 RimWorld 的**纪念碑任务**提供了一种取巧的完成方式——研究「空城计」科技后，选中任意纪念碑即可一键切换：纪念碑蓝图自动精简为**只保留外墙（外围一圈）**，内部清空，只需砌好外墙即视为「完工」。

但空城一计瞒不过天：完工后的保护期内，每天都有一定概率被看穿（默认 1%，可调 0%\~100%）。一旦露馅，任务判定失败、与委托派系关系恶化，4 小时后还会触发委托派系的惩罚。

不过，你仍有机会挽回局面——通过**顶罪仪式**放逐一名人员平息怒火。

此外，你可以用**宣传仪式**扩大影响、招来新的纪念碑委托。

## 核心机制

### 空城计模式

1. 研究「空城计」科技（默认 200 研究点数，可在设置中改为 0\~1000；设为 0 表示默认拥有、无需研究）。
2. 研究完成时，会自动下发一个**双倍点数**的「建造并保护」纪念碑任务。
3. 选中任意纪念碑，点击「空城计」切换按钮，蓝图精简为只保留外墙，内部蓝图与框架被清除。
4. 只需砌好外墙即视为**完工**，进入保护期。
5. 可随时再次点击开关，恢复完整蓝图。

### 败露与惩罚

- 保护期内**每天**有概率被看穿（默认 1%，可调 0%\~100%，0% 为永不被看穿）。

- 被看穿后任务立即判定失败，与委托派系**关系降低**（默认 25 点，可调 1\~100）。

- **4 小时（游戏内）后**触发委托派系惩罚（若原任务包含袭击惩罚，将发动袭击）。

- 败露期间**每小时**发送一次倒计时提醒。

- 若保护期内纪念碑被拆除/撤销，同样判定失败。

### 顶罪仪式

败露后，有两种方式举行顶罪仪式——放逐一名人员来承担败露的责任（殖民者视为「正式员工」，奴隶视为「实习生」，被放逐者将永久离开殖民地）：

| 方式             | 触发条件                             | 可选人员                  |
| -------------- | -------------------------------- | --------------------- |
| 纪念碑 Gizmo      | 选中被看穿的纪念碑，点击「顶罪仪式」按钮（无需文化配置）     | 地图上所有殖民者/奴隶（玩家手动选择）   |
| 文化仪式效果「空城计-顶罪」 | 在「编辑文化 → 仪式效果」中把该效果选到某场仪式上，举行该仪式 | 仪式参与者中的殖民者/奴隶（玩家手动选择） |

- **4 小时内**举行：阻止委托派系的惩罚袭击。

- **24 小时内**举行：撤销部分关系惩罚（默认 70%，可调 0%\~100%）。

### 宣传仪式

研究完成后，纪念碑 Gizmo 出现「宣传仪式」按钮，可手动刷新新的「建造并保护」纪念碑任务：

- 选择参与者，实时预览预期质量；仪式期间参与者聚集在纪念碑周围围观。

- 仪式持续时长默认 **2 游戏小时**（可调 0.5\~12），Gizmo 上有进度条，屏幕顶部有全息进度 HUD。

- 仪式结束后按质量结算：质量越高，刷新出的新纪念碑任务点数越高（0.5x\~1.0x 点数）。

- 冷却时间默认 **5 天**（可调 1\~30 天）。

### 文化仪式效果「空城计-宣传」

在「编辑文化 → 仪式效果」中把「空城计-宣传」选到某场仪式上后，当该仪式以**良好及以上**质量结束时，有概率（默认 100%，可调 0%\~100%）刷新一座新的纪念碑（建造并保护）任务。该效果需要 Ideology DLC。

## 设置选项

| 设置项         | 范围           | 默认值     | 说明                       |
| ----------- | ------------ | ------- | ------------------------ |
| 研究点数        | 0\~1000      | 200     | 0 表示默认拥有，无需研究            |
| 每日发现概率      | 0%\~100%     | 1%      | 保护期内每天被看穿的概率；0% = 永不被看穿  |
| 被发现时关系惩罚    | 1\~100       | 25      | 被看穿后减少的好感度               |
| 顶罪撤销比例      | 0%\~100%     | 70%     | 顶罪仪式撤销关系惩罚的百分比           |
| 纪念碑任务出现概率加成 | 开关 + 1x\~50x | 关 / 10x | 提高平时纪念碑任务的出现概率           |
| 宣传仪式刷新概率    | 0%\~100%     | 100%    | 文化仪式「空城计-宣传」效果触发时刷新任务的概率 |
| 宣传仪式持续时长    | 0.5\~12 小时   | 2 小时    | 手动宣传仪式的持续时间              |
| 宣传仪式冷却天数    | 1\~30 天      | 5 天     | 手动宣传仪式的冷却时间              |

## 依赖

- **Harmony**（必需）

- **RimWorld - Royalty**（必需，纪念碑任务属于皇家库）

- **RimWorld - Ideology**（可选，文化仪式效果需要）

## 兼容性

- 无 Ideology DLC 时，「空城计-宣传」「空城计-顶罪」两个文化仪式效果不可选，其余功能（含 Gizmo 顶罪/宣传）正常。

- 不影响原版纪念碑任务的其他完成方式。

- 与其它涉及纪念碑的 Mod 兼容。

- 支持本地化：简体中文、English、日本語。

## 版本历史

| 版本    | 说明                                  |
| ----- | ----------------------------------- |
| 1.0   | 核心功能：空城计模式、败露判定、关系惩罚、延迟袭击           |
| 1.1.0 | 新增顶罪仪式、宣传仪式及相关设置；多语言本地化；修复外墙提取不全等问题 |

## 作者与许可

作者：**PooiWoop**

本 Mod 为开源项目，代码基于 MIT 许可证发布。

***

***

# Empty Fort Strategy (空城计)

> "Appearance of emptiness, substance in concealment." — An empty fort is enough to repel an army.

## Introduction

Inspired by the internet meme "Empty Fort Strategy" (空城计). This mod offers a shortcut for completing RimWorld's **monument quests**: after researching the "Empty Fort Strategy" technology, select any monument and toggle it into Empty Fort mode — the blueprint is automatically reduced to **only the outer wall ring**, the interior is cleared, and simply building the outer walls counts as "complete."

But you can't fool the heavens forever: during the protection period after completion, there is a daily chance (default 1%, adjustable 0%\~100%) of being discovered. If exposed, the quest fails, relations with the commissioning faction sour, and the faction's penalty is triggered 4 hours later.

You still have ways to turn things around — hold a **Blame Shift ritual** to banish a pawn and calm the faction's anger, or hold a **Propaganda ceremony** to attract new monument commissions.

## Core Mechanics

### Empty Fort Mode

1. Research the "Empty Fort Strategy" technology (default 200 research points; adjustable 0\~1000 in settings; 0 = unlocked by default).
2. When the research finishes, a monument (build & protect) quest with **double points** is dispatched automatically.
3. Select any monument and click the "Empty Fort Strategy" toggle — the blueprint is reduced to the outer walls only, and interior blueprints/frames are cleared.
4. Building only the outer walls counts as **complete**, entering the protection period.
5. Click the toggle again at any time to restore the full blueprint.

### Exposure & Penalty

- During the protection period, there is a **daily** chance of being discovered (default 1%, adjustable 0%\~100%; 0% = never discovered).

- Once exposed, the quest is judged failed immediately, and your **relations with the commissioning faction drop** (default 25 points, adjustable 1\~100).

- **4 in-game hours later**, the faction's penalty triggers (if the original quest included a raid penalty, a raid occurs).

- An **hourly** countdown warning is sent while exposed.

- If the monument is deconstructed/cancelled during the protection period, the quest also fails.

### Blame Shift Ritual

After exposure, there are two ways to hold a blame shift ritual — banish a pawn to take responsibility (colonists are "Full Employees", slaves are "Interns"; the banished pawn leaves your colony forever):

| Method                                            | Trigger                                                                                  | Candidates                                                       |
| ------------------------------------------------- | ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| Monument Gizmo                                    | Select the exposed monument and click the "Blame Shift" button (no culture setup needed) | All colonists/slaves on the map (manually chosen)                |
| Cultural ritual effect "Empty Fort — Blame Shift" | Assign the effect to a ritual in Edit Ideogram → ritual effects, then hold that ritual   | Colonists/slaves among the ritual participants (manually chosen) |

- Within **4 hours**: prevents the faction's punitive raid.

- Within **24 hours**: restores part of the relation penalty (default 70%, adjustable 0%\~100%).

### Propaganda Ceremony

Once the research is done, the "Propaganda" button appears on the monument Gizmo, letting you manually refresh a new monument (build & protect) quest:

- Pick participants and preview the expected quality; during the ceremony, participants gather around the monument to watch.

- Duration defaults to **2 in-game hours** (adjustable 0.5\~12), with a progress bar on the Gizmo and a holographic progress HUD at the top of the screen.

- On completion, quality is calculated: higher quality refreshes a new monument quest with higher points (0.5x\~1.0x points).

- Cooldown defaults to **5 days** (adjustable 1\~30).

### Cultural Ritual Effect "Empty Fort — Propaganda"

After assigning "Empty Fort — Propaganda" to a ritual in Edit Ideogram → ritual effects, when that ritual ends with **Good or above** quality, there is a chance (default 100%, adjustable 0%\~100%) to refresh a new monument (build & protect) quest. This effect requires the Ideology DLC.

## Settings

| Setting                       | Range            | Default   | Description                                                                         |
| ----------------------------- | ---------------- | --------- | ----------------------------------------------------------------------------------- |
| Research points               | 0\~1000          | 200       | 0 = unlocked by default                                                             |
| Daily discovery chance        | 0%\~100%         | 1%        | Daily chance of exposure during protection; 0% = never exposed                      |
| Relation penalty on discovery | 1\~100           | 25        | Goodwill lost when exposed                                                          |
| Blame shift restore ratio     | 0%\~100%         | 70%       | Percentage of relation penalty restored by the blame shift ritual                   |
| Monument quest spawn bonus    | toggle + 1x\~50x | off / 10x | Increase the normal monument quest appearance rate                                  |
| Propaganda refresh chance     | 0%\~100%         | 100%      | Chance to refresh a quest when the "Empty Fort — Propaganda" ritual effect triggers |
| Propaganda duration           | 0.5\~12 h        | 2 h       | Duration of the manual propaganda ceremony                                          |
| Propaganda cooldown           | 1\~30 days       | 5 days    | Cooldown of the manual propaganda ceremony                                          |

## Dependencies

- **Harmony** (required)

- **RimWorld - Royalty** (required, monument quests are from Royalty)

- **RimWorld - Ideology** (optional, needed for cultural ritual effects)

## Compatibility

- Without the Ideology DLC, the "Empty Fort — Propaganda" and "Empty Fort — Blame Shift" cultural ritual effects are unavailable; everything else (including Gizmo blame shift / propaganda) works normally.

- Does not affect other ways of completing vanilla monument quests.

- Compatible with other mods that touch monuments.

- Localization: Simplified Chinese, English, Japanese.

## Version History

| Version | Notes                                                                                                                             |
| ------- | --------------------------------------------------------------------------------------------------------------------------------- |
| 1.0     | Core features: Empty Fort mode, exposure check, relation penalty, delayed raid                                                    |
| 1.1.0   | Added Blame Shift ritual, Propaganda ceremony and related settings; multilingual localization; fixed outer-wall extraction issues |

## Author & License

Author: **PooiWoop**

Open source project; the code is released under the MIT License.

***

***

# 空城計 (The Empty Fort Strategy)

> 「虚は実に、実は虚に。」—— 空っぽの城だけでも、十分に敵を退けられる。

## 紹介

インターネットのネタ「空城計」から着想を得た Mod です。RimWorld の**記念碑クエスト**をズルく達成する方法を追加します。「空城計」テクノロジーを研究すると、記念碑を選択してワンタップで切替えが可能になり、設計図が**外壁（外周）のみ**に自動簡略化され、内部は空になります。外壁を建てるだけで「完成」とみなされます。

しかし、空城の計は長くは通用しません。完成後の保護期間中、毎日一定の確率（デフォルト 1%、0%〜100% で調整可）で「見破られ」ます。露見すればクエストは失敗し、依頼派閥との関係が悪化、4時間後に依頼派閥のペナルティが発動します。

それでも挽回のチャンスはあります——**責任転嫁儀式**で人員を追放して怒りを静めるか、**宣伝儀式**で注目を集めて新たな記念碑の依頼を呼び込みましょう。

## 核心メカニズム

### 空城計モード

1. 「空城計」テクノロジーを研究します（デフォルト 200 研究ポイント、設定で 0〜1000 に変更可。0 はデフォルト所有）。
2. 研究完了時に、**2倍ポイント**の「建設＋保護」記念碑クエストが自動的に届きます。
3. 記念碑を選択し、「空城計」トグルをクリックすると、設計図は外壁のみになり、内部の設計図とフレームは除去されます。
4. 外壁を建てるだけで**完成**とみなされ、保護期間に入ります。
5. もう一度クリックすれば、いつでも完全な設計図に戻せます。

### 露見とペナルティ

- 保護期間中は**毎日**、見破られる確率があります（デフォルト 1%、0%〜100% で調整可。0% なら永久に見破られません）。

- 露見すると即座にクエスト失敗となり、依頼派閥との**関係が低下**します（デフォルト 25、1〜100 で調整可）。

- **ゲーム内4時間後**に依頼派閥のペナルティが発動します（元のクエストに襲撃ペナルティが含まれる場合は襲撃が発生）。

- 露見中は**毎時**カウントダウン警告が届きます。

- 保護期間中に記念碑が取り壊し/撤回された場合も失敗となります。

### 責任転嫁儀式

露見後、2つの方法で責任転嫁儀式を行えます——人員を追放して敗露の責任を負わせます（入植者は「正社員」、奴隷は「インターン」と呼ばれ、追放された人員は永久にコロニーを去ります）：

| 方法                 | トリガー                                | 候補者                 |
| ------------------ | ----------------------------------- | ------------------- |
| 記念碑 Gizmo          | 見破られた記念碑を選択し「責任転嫁」ボタンをクリック（文化設定不要）  | マップ上の全入植者/奴隷（手動選択）  |
| 文化儀式効果「空城計 — 責任転嫁」 | 「イデオロギー編集 → 儀式効果」で効果を儀式に設定し、その儀式を開催 | 儀式参加者中の入植者/奴隷（手動選択） |

- **4時間以内**に開催：依頼派閥のペナルティ襲撃を阻止。

- **24時間以内**に開催：関係ペナルティの一部を回復（デフォルト 70%、0%〜100% で調整可）。

### 宣伝儀式

研究完了後、記念碑 Gizmo に「宣伝儀式」ボタンが現れ、新しい「建設＋保護」記念碑クエストを手動で更新できます：

- 参加者を選択して予想品質をリアルタイム確認。儀式中、参加者は記念碑の周りに集まって見守ります。

- 儀式時間はデフォルト **ゲーム内2時間**（0.5〜12 で調整可）。Gizmo に進行バー、画面最上部にホログラムの進行 HUD が表示されます。

- 終了時に品質が計算され、品質が高いほどポイントの高い新クエストが届きます（0.5x〜1.0x ポイント）。

- クールダウンはデフォルト **5日**（1〜30 で調整可）。

### 文化儀式効果「空城計 — 宣伝」

「イデオロギー編集 → 儀式効果」で「空城計 — 宣伝」を儀式に設定すると、その儀式が**良好以上**の質で終了した際、確率（デフォルト 100%、0%〜100% で調整可）で新しい記念碑（建設＋保護）クエストが生成されます。この効果には Ideology DLC が必要です。

## 設定

| 設定項目           | 範囲            | デフォルト    | 説明                           |
| -------------- | ------------- | -------- | ---------------------------- |
| 研究ポイント         | 0〜1000        | 200      | 0 はデフォルト所有・研究不要              |
| 1日あたりの発見確率     | 0%〜100%       | 1%       | 保護期間中の露見確率。0% = 永久に露見しない     |
| 露見時の関係ペナルティ    | 1〜100         | 25       | 露見時に減少する友好度                  |
| 責任転嫁の回復割合      | 0%〜100%       | 70%      | 責任転嫁儀式で回復する関係ペナルティの割合        |
| 記念碑クエスト出現率ブースト | スイッチ + 1x〜50x | オフ / 10x | 通常時の記念碑クエスト出現率を上昇            |
| 宣伝儀式の更新確率      | 0%〜100%       | 100%     | 文化儀式効果「空城計 — 宣伝」発動時のクエスト更新確率 |
| 宣伝儀式の持続時間      | 0.5〜12時間      | 2時間      | 手動宣伝儀式の持続時間                  |
| 宣伝儀式のクールダウン    | 1〜30日         | 5日       | 手動宣伝儀式のクールダウン                |

## 依存関係

- **Harmony**（必須）

- **RimWorld - Royalty**（必須、記念碑クエストは Royalty 由来）

- **RimWorld - Ideology**（任意、文化儀式効果に必要）

## 互換性

- Ideology DLC がない場合、「空城計 — 宣伝」「空城計 — 責任転嫁」の文化儀式効果は選択できませんが、それ以外（Gizmo の責任転嫁/宣伝を含む）は正常に動作します。

- 原版の記念碑クエストの他の達成方法には影響しません。

- 記念碑に関わる他の Mod とも互換です。

- 対応言語：簡体字中国語、English、日本語。

## バージョン履歴

| バージョン | 内容                                   |
| ----- | ------------------------------------ |
| 1.0   | 核心機能：空城計モード、露見判定、関係ペナルティ、遅延襲撃        |
| 1.1.0 | 責任転嫁儀式、宣伝儀式と関連設定を追加。多言語対応。外壁抽出の問題を修正 |

## 作者とライセンス

作者：**PooiWoop**

オープンソースプロジェクトです。コードは MIT ライセンスで公開されています。
