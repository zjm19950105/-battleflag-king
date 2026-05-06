# 开发进度日志

## 当前阶段
**Phase 1.2 — 战前配置与 3v3 自动战斗（已完成）**

目标：玩家在 Godot 控制台中完成队伍组建、阵型布置、被动技能配置、策略编程，然后启动 3v3 自动对战。

---

## 已完成

### 基础设施
- [x] 确定技术栈：Godot 4 + C#
- [x] 确定核心设计方向：大巴扎节点 + 圣兽之王自动战斗
- [x] 确定架构原则：数据驱动、正交系统、文档外置
- [x] 建立文档体系（CLAUDE.md + progress.md + roadmap.md + docs/csharp-architecture.md + docs/dev-mistakes.md）
- [x] 完整框架设计（基于圣兽之王资料分析）：14项核心机制框架
- [x] Memory系统记录项目上下文
- [x] GitHub 仓库初始化并记录地址（`git@github.com:zjm19950105/-battleflag-king.git`）

### Phase 1.0 — 环境就绪
- [x] Godot 4 C# 项目创建并配置完毕（`战旗之王/goddot/`）
- [x] 全部 .cs 文件生成 .uid，Godot 编辑器识别正常
- [x] 能 F5 跑起场景
- [x] 能正常 commit 到 git

### Phase 1.1 — 1v1 普攻对战
- [x] Data层：角色/主动技能/被动技能/装备 JSON + GameDataRepository
- [x] Pipeline层：DamageCalculator（命中/回避/暴击/格挡/兵种克制）
- [x] AI层：StrategyEvaluator + ConditionEvaluator + TargetSelector
- [x] Core层：BattleUnit + BattleContext + BattleEngine
- [x] Main.cs 入口脚本（加载数据 → 创建单位 → 启动战斗）
- [x] 1v1 普攻代码闭环完成（逻辑层）
- [x] **CC框架接入**：CharacterData/BattleUnit/Main.cs 支持CC状态切换，默认未CC
- [x] **12/12角色数据已填充**（Subagent批量填充+修正），JSON格式验证通过
- [x] `CcInitialEquipmentIds` 配置：CC后新增装备槽强制带装备
- [x] `EquipmentSlot` 武器槽校验：MainHand永不为空
- [x] 装备槽统一规则（3基础 → 4 CC）：所有12角色基础3槽、CC后4槽已配置
- [x] 格挡规则修正：`GetBlockReduction()` 无盾格挡减25%，大盾减50%
- [x] Godot 运行验证通过（未CC状态）：1v1 战斗循环正常，AP消耗、HP变化、胜负判定正确

### Phase 1.2 — 战前配置与 3v3 自动战斗
- [x] **控制台输入改造**：`Console.ReadLine` → Godot `LineEdit` UI（`async/await` + `ToSignal`）
  - 解决 Windows GUI 程序无法接收 stdin 的问题
  - 新增 `SetupUi()` 创建 CanvasLayer + Panel + Label + LineEdit
  - 新增 `ReadInputAsync(string prompt, string defaultValue)` 方法
  - 新增 `RunGameLoopWithErrorHandlingAsync()` 捕获异步异常
- [x] **队伍组建控制台（TeamBuilder）**
  - 打印12角色摘要表（编号/名称/兵种/HP/Str/Def/Spd）
  - 玩家输入3个编号，去重+越界校验
  - 敌方从预设阵型或随机配置中选择
  - 创建6个 BattleUnit，应用 `DayProgression.Apply(unit, 1)` 设置 Lv1 未CC
- [x] **阵型布置系统**
  - 6位置网格（前排1-3，后排4-6）
  - 逐个角色选择位置，已占用位置不可重复选择
  - `PrintFormationGrid` 可视化当前阵型
- [x] **敌人配置框架**
  - 新增 `data/enemy_formations.json`（敌人阵型模板）
  - 新增 `data/strategy_presets.json`（敌人策略预设）
  - 新增 `EnemyFormationData.cs`、`StrategyPresetData.cs`
  - 支持预设阵型选择和随机敌人生成
- [x] **被动技能选择系统**
  - `BattleUnit.EquippedPassiveSkillIds` + `GetUsedPp()` + `CanEquipPassive()`
  - 玩家为每个己方角色选择被动技能，受 PP 上限约束
  - 敌方被动技能由系统随机分配（在 PP 上限内随机选择）
- [x] **策略编程控制台（8条策略栏位）**
  - 显示当前8条策略摘要（技能名 | 条件1 | 条件2）
  - 支持按编号编辑（1-8）、0确认、d恢复默认
  - 条件1：无条件/自身HP>50%/自身AP>=2/优先HP最低/目标前排/目标步兵
  - 条件2：优先HP最低/优先前排
  - 支持跳过剩余栏位（直接回车结束）
- [x] **条件系统 MVP**
  - `ConditionEvaluator` 新增 `SelfHp`（HP比例判定）和 `SelfApPp`（AP阈值判定）分支
  - `TargetSelector.ApplyCondition` 支持 `lowest`/`highest` 排序操作符
  - `TargetSelector.GetDefaultTargetList` 修复 NullReferenceException（`u != null &&` 过滤）
- [x] **EventBus 接入 BattleEngine**
  - 新增 `src/Events/BattleEvents.cs`：6个战斗事件（`BattleStartEvent`, `BeforeActiveUseEvent`, `BeforeHitEvent`, `AfterHitEvent`, `AfterActiveUseEvent`, `BattleEndEvent`）
  - `BattleEngine.StartBattle()` 发布 `BattleStartEvent`
  - `ExecuteUnitTurn()` 发布 `BeforeActiveUseEvent` → `BeforeHitEvent` → `AfterHitEvent` → `AfterActiveUseEvent`
  - `EndBattle()` 发布 `BattleEndEvent`
- [x] **被动技能处理器（PassiveSkillProcessor）**
  - 订阅全部6个战斗事件，按 `TriggerTiming` 匹配被动技能
  - 实现12个触发时机：`BattleStart`, `SelfOnActiveUse`, `SelfBeforeAttack`, `AllyBeforeAttack`, `AllyOnActiveUse`, `SelfBeforeHit`, `SelfBeforePhysicalHit`, `SelfBeforeMeleeHit`, `AllyBeforeHit`, `OnBeingHit`, `AllyOnAttacked`, `AfterAction`, `BattleEnd`
  - 同時発動制限：`HashSet<string>` 跟踪各时机的已触发状态
  - 按速度降序排列候选者
- [x] **被动技能效果执行（ExecuteSimpleEffect）**
  - `ApPlus1`：自身 AP+1
  - `DefUp20`/`AtkUp20`/`SpdUp20`/`SpdUp30`/`EvaUp30`/`CritDamageUp50`：自身 BUFF（无限回合）
  - `AtkUp20Stackable`：可叠加物攻 BUFF
  - `Heal25`：回复 25% 最大 HP
  - 复杂效果标记为"待实现"并打印日志（Counter, CoverAlly, SureHit, EvasionSkill 等）
- [x] **战斗日志改进**
  - 每次攻击显示被攻击者剩余 HP
  - 显示附加异常状态（`AppliedAilments`）
  - 被动技能触发时打印实际效果（如 `→ 效果: AP+1, 物攻+20%`）
- [x] **完整角色属性展示**
  - `PrintUnitDetail` 显示：HP/AP/PP、Str/Def/Spd/Mag/MDef/Hit/Eva/Crit/Block、已装备被动
- [x] **3v3 完整对战验证**
  - Godot 中 F5 运行，完整流程：角色选择 → 阵型布置 → 敌人选择 → 被动配置 → 策略配置 → 自动战斗
  - 战斗按速度顺序执行，AP 耗尽后按 HP 比例判定胜负
  - 多轮对战支持（"是否再来一局? y/n"）
- [x] **关键 Bug 修复**
  - C# string interpolation 单引号导致 CS1012 → 改用 `string.Format`
  - TargetSelector NRE（PlayerUnits/EnemyUnits 含 null 占位）→ 添加 `u != null &&` 过滤
  - Godot scene `unique_id` 导致加载错误 → 移除该属性
  - UI anchor/size 冲突 → 改用 Offset 替代 Size/Position
  - `BattleContext.AllUnits` / `GetAliveUnits` / `GetUnitAtPosition` 添加 null 过滤
- [x] **被动技能架构文档**
  - 创建 `被动技能实现融入现有框架.md`
  - 分析5大架构改造模块：DamageCalculation可变上下文、PendingActionQueue、TemporalState、友方目标选择、效果结构化
  - 制定5阶段实施路线图

---

## 进行中

### Phase 1.3 — 被动技能完整实现与架构改造
- [ ] **模块1：DamageCalculation 可变上下文**（最高优先级）
  - `BeforeHitEvent` 增加 `DamageCalculation Calc` 属性
  - `DamageCalculation` 扩展：ForceHit/ForceEvasion/ForceBlock/SkillPowerMultiplier/DamageMultiplier/IgnoreDefenseRatio/NullifyDamage/CoverTarget
  - `DamageCalculator.Calculate` 签名改为接收 `DamageCalculation`
  - `BattleEngine` 在 BeforeHit 前创建 Calc 并传给事件
- [ ] **模块2：PendingActionQueue 行动队列**
  - 新增 `PendingAction` / `PendingActionType`
  - `BattleEngine` 核心循环支持队列（反击/追击/先制攻击/战斗结束时攻击）
  - 被动处理器在 `AfterHit` / `BattleStart` / `BattleEnd` 中入队额外攻击
- [ ] **模块3：TemporalState 临时标记系统**
  - `BattleUnit` 增加 `TemporalStates` 列表
  - 管理"1次魔法无效"、"1hit变0"、"致死耐"、"必中下一次"等标记
- [ ] **模块4：友方目标选择 + 效果结构化**
  - `PassiveSkillProcessor` 支持选择友方目标（randomAlly/lowestHpAlly/allAllies/columnAllies）
  - 逐步把 JSON Tags 迁移到结构化的 `Effects` 字段
- [ ] **模块5：逐个职业实现完整被动技能效果**
  - 按 wiki 文档填充 `passive_skills.json` 的 `Effects` 数组
  - 验证：格挡/回避/必中/反击/追击/掩护/先制攻击

---

## 待办（按优先级排序）

### 🔴 当前聚焦（Phase 1.3）
1. **DamageCalculation 可变上下文改造**
   - 这是所有伤害干预类被动（格挡/回避/必中/免疫/掩护）的基础
   - 预计影响：BeforeHitEvent.cs, DamageCalculation.cs, DamageCalculator.cs, BattleEngine.cs

2. **TemporalState 系统**
   - 实现"1次免疫"类被动（招架、魔法屏障、纯净领域）
   - 在 BeforeHit 中检查并消耗标记

3. **PendingActionQueue**
   - 实现反击/追击/先制攻击（追击斩、广域反击、快速打击、终结打击）
   - 改造 BattleEngine 核心循环

### 🟡 近期（Phase 1.4）
4. **条件系统完整实现**
   - 当前只实现了 SelfHp / SelfApPp / lowest/highest 排序
   - 需要：Position（前排/后排）、UnitClass（兵种判定）、Status（异常状态）、EnemyClassExists（敌兵种存在）
5. **策略默认配置**
   - 用户将提供默认策略文档
   - 为每个职业的8条策略栏设置合理的默认策略
6. **玩家策略持久化**
   - 记住玩家的策略选择（JSON save/load）
7. **6v6 完整竞技场**
   - 从3v3扩展到6v6
   - 完整的 if/条件/执行/优先级 编程系统验证

### 🟢 后续扩展（Phase 2+）
8. **2D 视觉表现**
   - 占位 Sprite、血条、战斗动画
   - 战斗日志 RichTextLabel 显示
9. **站位/射程系统深化**
   - 近战只能打前排的完整逻辑（当前 TargetSelector 已有前排优先）
   - 前排全灭后才能打后排
10. **大巴扎循环（Phase 2）**
    - 节点地图、抽牌机制、天数系统、经济系统
11. **局外成长（Phase 2.3）**
    - 解锁新角色/技能、难度选择
12. **本地化与合规（Phase 3）**
    - 角色/技能名称原创化、资产清理

---

## 已知问题/待优化
- **大量被动技能仍为"待实现"**：当前只有简单 BUFF/AP/HP 效果已实现。反击/追击/先制攻击/掩护/必中/回避/格挡封印/伤害免疫等复杂效果需要架构改造后才能实现。
- **DamageCalculator 黑暗判定 MVP 简化**：黑暗状态直接 miss，没有做冻结/气绝/被掩护/蓄力中的例外。
- **双持装备逻辑**：`EquipmentSlot.Equip` 当前只支持单武器，CC后剑圣双持第二把剑会覆盖主手。需后续完善 Equip 方法支持双持。
- **条件系统不完整**：只实现了 SelfHp / SelfApPp / lowest / highest，Position / UnitClass / Status / EnemyClassExists 等条件返回 true（不阻塞MVP）。
- **被动技能 Tags 与 Effects 脱节**：`passive_skills.json` 中 `Effects` 数组为空，大量 Tags 是占位符（如 `Crit100`、`DefDown15Random`），无法直接映射到执行逻辑。
- **敌方 AI 策略较简单**：当前敌方使用预设策略模板（preset_aggressive），策略条件固定为"无条件"。
- **未做蓄力（Charge）状态机**：蓄力行动、连续待机限制等机制未实现。

---

## 最近会话记录
- **2026/05/02**：MCP连接解决。用户确认 Godot 编辑器就绪。修复 BattleEngine 日志输出。CC框架接入。12角色数据填充完成。装备槽统一规则（3→4）和无盾格挡减25%。
- **2026/05/03**：启动 Phase 1.2 实施。计划8步：队伍组建、阵型布置、敌人配置、被动技能选择、策略编程、条件系统、被动接入战斗、3v3验证。
- **2026/05/03**：实现角色选择控制台 + 阵型布置。遇到 Windows GUI stdin 问题，改用 Godot LineEdit UI。实现 async/await 输入流程。
- **2026/05/03**：实现敌人配置框架（enemy_formations.json + strategy_presets.json）。敌方随机分配3人并赋予预设策略。
- **2026/05/03**：实现被动技能选择系统（PP上限控制）。实现策略编程控制台（8条策略栏位，支持编辑/确认/默认）。
- **2026/05/03**：修复 TargetSelector NullReferenceException。修复 C# string interpolation 单引号 CS1012。
- **2026/05/04**：实现 EventBus 接入 BattleEngine（6个战斗事件）。创建 PassiveSkillProcessor.cs，实现12个触发时机和同時発動制限。
- **2026/05/04**：实现被动技能效果打印（ExecuteSimpleEffect）。战斗日志显示剩余HP和debuff。完整角色属性展示。
- **2026/05/04**：3v3完整对战验证通过。修复 Godot scene unique_id 错误。修复 UI anchor/size 冲突。
- **2026/05/05**：用户要求分析被动技能对框架的影响。对照 wiki 完整被动技能资料，发现需要5大架构改造：DamageCalculation可变上下文、PendingActionQueue、TemporalState、友方目标选择、效果结构化。
- **2026/05/05**：创建 `被动技能实现融入现有框架.md` 架构文档，详细记录改造方案、文件修改清单、5阶段实施路线图、关键设计决策。
- **2026/05/05**：用户不小心还原 progress.md / roadmap.md / CLAUDE.md，从 git 历史恢复并更新到最新状态。准备上传代码到 GitHub。
  - **2026/05/06 夜间（自主执行）**：Phase 1.3 全部6模块实施完成：
    - 模块1：DamageCalculation 扩展13个可变字段+多段攻击；DamageCalculator 签名改为 Calculate(DamageCalculation)
    - 模块2：PendingAction/PendingActionType 行动队列，BattleEngine ProcessPendingActions
    - 模块3：TemporalState 临时标记系统（BattleUnit.TemporalStates）
    - 模块4：自定义计数器+友方目标选择+10+种新 EffectType
    - 模块5：Python 脚本批量 Tags→Effects 迁移（17被动+5主动已填充）
    - 模块6：Charge 蓄力状态机+OnKnockdownEvent+动态威力
    - 编译通过，0错误0警告
- **2026/05/06**：回顾项目状态。对照4份被动技能文档和2份主动技能文档，确认 class-compendium.md 为最全面的数据源（40+职业含成长数据）。发现以下框架影响：
  - **多段攻击**：约15+个主动技能有2~9hit，需每hit独立判定命中/格挡/暴击 → 模块1范围扩大
  - **小精灵系统**：精灵女先知专属自定义计数器（积累/消耗循环），AP/PP/HP之外的第四种资源 → 想法池记录，后续独立追加
  - **资源偷取（AP/PP转移）**：暂时锁定，待奖励系统确定后再设计
  - **勇气技能**：全部排除，不纳入本项目
  - **击倒事件/蓄力/动态威力**：新增模块6
  - roadmap.md 已更新，Phase 1.3 从5模块扩展为6模块
