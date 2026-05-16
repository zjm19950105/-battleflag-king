# 角色与技能录入规范

最后更新：2026-05-15

用途：给接手的新 AI 录入角色、主动技能、被动技能时使用。先读本文件，再改 `characters.json`、`active_skills.json`、`passive_skills.json`。本规范解决的核心问题是：不要凭印象补技能，不要漏威力/命中倍率/攻击次数，不要把百分比和固定值写反。

## 一手资料优先级

录入前必须逐项对照资料，不允许只按中文短描述猜：

- 当前唯一可信技能/职业资料：`C:\Users\ASUS\Music\圣兽之王资料整理\有用的资料\unicorn-overlord-class-compendium.md`
- 过时且禁止作为技能依据：`unicorn-overlord-active-skills.md`、`unicorn-overlord-passive-skills.md`、`unicorn-overlord-active-skills-by-type.md`、`unicorn-overlord-passive-skills-by-timing.md`

资料冲突时，不要自行发挥。以 `unicorn-overlord-class-compendium.md` 为准；如果它仍与游戏实测或截图冲突，把冲突写进审计记录或 TODO，并补一个失败测试/待确认测试保护当前假设。

## 角色描述录入清单

角色描述同样只从 `unicorn-overlord-class-compendium.md` 抽取，不从旧主动/被动技能文档补：

- 角色标题来自职业段标题，例如 `射手 / 盾射手 (Shooter / Shield Shooter)`。
- 兵种来自该段 `| 兵种 | ... |`。
- `mainRoles` 只取 `#### 角色定位` 下的 `主要角色` bullet。
- `characteristics` 取 `特点` bullet；如果 bullet 里提到其他角色或兵种，必须写成 `{char:ID}` / `{class:ID}` token，不要写死中文名。
- `referencedCharacterIds` / `referencedClassIds` 要与 token 同步，用来做改名影响检查。
- 改名只改 `goddot/data/class_display_names.json` 或角色显示名映射，不全文搜索替换描述正文。

## 角色录入清单

`goddot/data/characters.json` 里的角色不是只填属性和名字，必须同时确认：

- 基础职业、转职职业、兵种标签和 CC 后兵种标签。
- 基础属性、CC 后属性、AP/PP 入场值和装备后的资源同步。
- `initialEquipmentIds` 和 `ccInitialEquipmentIds`。武器槽、盾槽不能空；转职新增的剑、盾、杖等槽必须给最低级默认装备，只有饰品槽可以为空。
- `equippableCategories` 和 `ccEquippableCategories`。转职后新增武器类型要和装备槽、UI 可选项一致。
- `activeSkillIds`、`passiveSkillIds`、技能习得等级。不要只把技能写进 JSON，却忘了角色技能池。
- 进入转职阶段后，全局角色应按转职态处理；如果代码临时仍靠 `isCc` 参数切换，新增角色也必须保证 CC 数据完整，避免后续全局切换时空槽或缺技能。

## 主动技能字段清单

每个主动技能都要从资料逐项搬运：

- `apCost`：资料 AP，不要沿用同职业旧技能。
- `type` / `attackType`：物理、魔法、辅助、妨害；近接、远隔、魔法。不要把“前后贯通”误写成远隔，远隔必须来自技能标签或资料。
- `targetType`：单体、一列、一排、前后贯通、全体等。前后贯通只描述命中锚点前后两个格，不等于无视近战前排阻挡。
- `power`、`physicalPower`、`magicalPower`：普通单段用 `power`；混合伤害必须分别写物理/魔法威力，物理段走 `Str/Def`，魔法段走 `Mag/MDef`。攻击前追加魔法伤害不要写成魔攻 buff，用 `AugmentCurrentAction` + `ModifyDamageCalc.AdditionalMagicalPower`。
- `hitRate`：技能命中倍率。资料写 90% 就是 `hitRate: 90`，不是命中 buff，也不是 0.9。
- `hitCount` / `HitCount`：多段攻击必须显式写，不能靠技能名或 legacy tag 推断。
- `tags`：只写资料明确存在的标签，如 SureHit、Charge、Limited、Ranged 等。不要用 tag 代替已支持的结构化 effect。
- `effects`：只表达资料写明的附加效果。例：Mega Slash 资料只有威力 150，就不能附加骑兵特攻、封印或偷 AP。

远隔战斗语义由 `attackType: "Ranged"` 驱动，不由普通 `Ranged` tag 驱动。当前目标选择、远程掩护、飞行命中惩罚和攻击属性条件都读取 `attackType`；`Ranged` tag 只作为资料标签/日志标记保留，不能替代 `attackType`，也不需要为每个远隔主动技额外补齐。飞行特攻不由远隔 tag 触发，而是由角色 trait（例如 `BowVsFlying`）和 `attackType: "Ranged"` 共同决定；没有该 trait 的远隔技能不会自动对飞行双倍伤害。不要录入没有执行语义或资料来源的补丁 tag，例如 `FlyingNoHitPenalty`。

## 被动技能字段清单

每个被动技能要先确定触发事件，再写效果：

- `ppCost`、`triggerTiming`、`type`、`hasSimultaneousLimit` 必须来自资料。
- “友方主动技能时”用 `AllyOnActiveUse` / 当前主动行动增强；“友方被攻击时”才是受击/命中相关事件。追击类不能因为友方被动 pending 攻击而触发，必要时用 `requiresSourceKind: "ActiveAttack"`。
- 先制、追击、反击这类被动攻击必须在 pending action 参数里写齐 `power`、`hitRate`、`HitCount`、`damageType`、`attackType`、`targetType`、`tags`。
- BattleStart/先制/战斗结束这类没有指定具体敌人的单体 pending attack，写 `target: "AllEnemies"`、`targetType: "SingleEnemy"`、`maxTargets: 1`；引擎会按主动技能同样的合法候选、近战前排阻挡和随机单体语义选目标。反击、追击这类有锚点的技能用 `target: "Attacker"` 或当前主动目标入队，不要写成 `AllEnemies`。
- 掩护类 `CoverAlly` 只保护本次攻击没有被显式点名的友方；如果掩护者也在同一批目标里，不能替另一个同批目标挡刀，避免多目标攻击下互相掩护。
- 攻击前给当前主动追加必中、会心、追击、魔法附加等效果时，优先使用 `AugmentCurrentAction`，不要在还没有 `DamageCalculation` 的阶段直接写 `ModifyDamageCalc`。例如魔法剑刃写 `requiresCurrentSkillType: "Physical"`，并在 `calculationEffects` 里放 `AdditionalMagicalPower: 50`。
- 被主动攻击前/命中前类反击必须检查 `BattleActionSourceKind`。只有资料写“主动技能”时，pending action 不应递归触发。

## 数值语义

这是最容易写错的部分：

- `ratio` 表示百分比。资料写 `+50%` 就写 `0.5`，资料写 `-15%` 就写 `-0.15`。例如会心率 +50% 是把 40 变成 60，不是 90。
- `amount` 表示固定点数或资源个数。资料写 AP+1、PP+1、速度+20、命中+20 这类没有百分号的值，用 `amount`。
- `SkillPowerBonus` 表示威力固定加算。资料写“威力 +50”时，用 `SkillPowerBonus: 50`，不要写成 `ratio`。
- `SkillPowerMultiplier` 只在资料明确是倍率时使用。
- `FixedDamageFromCasterHpRatio` 表示按施法者当前 HP 比例计算的固定物理基础伤害；资料写“造成自身 HP 50% 伤害”时用它，并按资料补 `CannotCrit` / `CannotBeBlocked`。
- `hitRate` 是技能自身命中倍率，不是 `Hit` 属性 buff。
- 率类属性不要凭经验统一按 flat 或 ratio。看资料原文：有 `%` 用 `ratio`，没有 `%` 用 `amount`；资料翻译不确定时查原文或补待确认记录。
- “解除异常”使用 `CleanseAilment`；未指定 `ailment` / `ailments` 时表示解除目标全部异常，不要用 `CleanseDebuff` 代替。

## 持续时间

普通 stat buff/debuff 默认整场战斗持续，用 `turns: -1`。但资料写“一次攻击”“下一次攻击”“一度だけ”“本次攻击”时，必须写当前行动一次性：

```json
{
  "effectType": "AddBuff",
  "parameters": {
    "target": "Self",
    "stat": "CritDmg",
    "ratio": 0.5,
    "turns": 1,
    "oneTime": true
  }
}
```

`pas_charge_action`（蓄力行动）属于这一类：自身使用主动技能时 AP+1，并且会心伤害 +50% 只作用于本次主动攻击；不能写成整场战斗持续。

## 命中、必中、闪避、招架

- `SureHit` / `ForceHit` 的攻击不应触发闪避类被动；否则会出现动画先闪避、结果仍扣血的错误。
- 闪避、招架、格挡、掩护必须在正确事件阶段判断，并且在确定可以生效后才消耗 PP、给 AP/PP 回收或播放日志。
- `ForceEvasion`、`ForceBlock` 不是“展示效果”，它们会改变本次 `DamageCalculation`。如果本次攻击已经必中或不可格挡，应在 PP 消耗前阻止对应被动触发。

## 测试要求

新增或修正技能时至少补一类测试：

- JSON 形状测试：字段是否和资料一致，如 AP、威力、命中倍率、hitCount、ratio/amount、oneTime。
- 行为回归测试：真实战斗中是否触发、是否扣 PP/AP、是否只影响当前行动、是否不被 pending action 误触发。
- 日志/动画语义测试：必中不触发闪避，掩护不覆盖不合法目标，Row Barrier 同一攻击只扣一次 PP。

修改后运行：

```powershell
dotnet test goddot-test\goddot-test.csproj --no-restore
dotnet build goddot\goddot.csproj --no-restore
```

如果只改 JSON，也要跑相关真实 JSON 回归测试；不要认为数据改动不需要测试。
