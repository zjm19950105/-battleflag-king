# 战旗之王 — Codex 项目文档

## 🤖 新 AI 维护者快速上手（CodeX / ChatGPT Pro 交接）

> **项目**: 战旗之王 — 圣兽之王风格的编程自动战斗游戏
> **技术栈**: Godot 4.6 + C# (.NET 8) + NUnit 测试
> **最后更新**: 2026-05-07

### 第一次打开项目必做的事

1. 阅读本文档（AGENTS.md）— 概念框架、机制规则
2. 阅读 `docs/csharp-architecture.md` — 类设计、数据流
3. 运行测试确认环境正常: `cd goddot-test && dotnet test`
4. 在 Godot 中 F5 跑一次验证 UI 正常

### 代码修改后必做

1. **跑测试**: `cd goddot-test && dotnet test`（54个用例，耗时<1秒）
2. **跑 simplify**: 类型 `/simplify` 让 Codex 自动审查代码重复/质量问题
3. **编译确认**: `cd goddot && dotnet build`（0错误0警告）
4. **Godot 验证**: F5 跑场景确认 UI 没崩

### 数值/数据修改规则

- **所有数值在 JSON 里**，不在 C# 代码里
- 改完 JSON 后跑 `dotnet test` 验证测试仍通过
- 新增角色 → 同步更新 characters.json + active/passive_skills.json + equipments.json

### 当前已知问题

- 3个测试用例失败（多段攻击/仅前排过滤/Row同排）— 需要深入引擎调试
- BattleEngine 日志 BattleLogHelper 已接入但 hitChance 显示公式与实际计算不一致
- 被动技能"非毒"/"非buff"等值在 ConditionMeta 中未定义
- 默认策略硬编码在 Main.cs 中，应迁移到 strategy_presets.json

### 关键文件索引

| 类别 | 路径 | 说明 |
|------|------|------|
| 入口 | `goddot/Main.cs` | UI + 游戏循环（~1400行） |
| 战斗引擎 | `goddot/src/core/BattleEngine.cs` | StepOneAction + 伤害流程 |
| 伤害计算 | `goddot/src/Pipeline/DamageCalculator.cs` | 命中/回避/暴击/格挡流水线 |
| 被动处理 | `goddot/src/Skills/PassiveSkillProcessor.cs` | 事件驱动 + 同时发动限制 |
| 条件评估 | `goddot/src/Ai/ConditionEvaluator.cs` | 12类条件(HP/兵种/位置等) |
| 策略UI | `goddot/src/Ai/ConditionMeta.cs` | 条件编辑器级联元数据 |
| 装备系统 | `goddot/src/Equipment/EquipmentSlot.cs` | 5槽装备 + 双持规则 |
| UI辅助 | `goddot/src/ui/BattleStatusHelper.cs` | 战斗面板全属性显示 |
| UI辅助 | `goddot/src/ui/PassiveDetailHelper.cs` | 被动技能详情卡片 |
| UI辅助 | `goddot/src/ui/BattleLogHelper.cs` | 战斗日志多行格式化 |
| 测试 | `goddot-test/TestDataFactory.cs` | 内存创建测试数据 |
| 测试 | `goddot-test/DamageCalculatorTest.cs` | 伤害计算11个用例 |
| 测试 | `goddot-test/ConditionEvaluatorTest.cs` | 条件评估17个用例 |
| 数据 | `goddot/data/characters.json` | 18个角色 |
| 数据 | `goddot/data/active_skills.json` | 55个主动技能 |
| 数据 | `goddot/data/passive_skills.json` | 50个被动技能 |
| 数据 | `goddot/data/equipments.json` | 80件装备 |

## 项目概述
- **名称**：战旗之王（暂定）
- **类型**：编程自动战斗 + 大巴扎爬塔（受圣兽之王启发）
- **核心循环**：战前编程策略 → 自动战斗 → 获取装备/角色 → 下一场战斗
- **技术栈**：Godot 4 + C#
- **目标平台**：PC
- **GitHub 仓库**：`git@github.com:zjm19950105/-battleflag-king.git`

> **文档体系**：
> - `AGENTS.md`（本文档）：概念框架、机制规则、开发状态
> - `docs/csharp-architecture.md`：**C# 技术架构** — 类设计、接口定义、方法签名、数据流、枚举
> - `docs/dev-mistakes.md`：开发错误记录 — Subagent 规范、机制规则、常犯错误
> - `docs/被动技能实现融入现有框架.md`：被动技能完整架构改造方案 — 5 大模块、文件修改清单、实施路线图

## 核心设计原则
1. **数据驱动**：角色、技能、装备、条件全部走 JSON，运行时为只读
2. **事件驱动**：被动技能通过 `EventBus` 订阅战斗事件，避免直接引用
3. **框架优先**：先搭骨架（一个职业+一个技能），跑通 1v1 普攻闭环，再批量填数据

## 架构概览

```
src/
  data/        # JSON 配置（角色、技能、装备、条件）
  core/        # 战斗核心（BattleEngine, BattleUnit, DamageCalculator）
  skills/      # 技能系统（ActiveSkill, PassiveSkill, SkillEffect, StrategyEvaluator）
  ai/          # 策略编程（Strategy, Condition, TargetSelector）
  equipment/   # 装备系统（Equipment, EquipmentSlot, BuffManager）
  pipeline/    # 伤害判定流水线（DamageCalculation, DamagePipeline）
  events/      # 事件总线（EventBus, 战斗事件定义）
  utils/       # 通用工具（JsonLoader, RandUtil）
```

### 模块职责

| 模块 | 职责 | 关键类 |
|------|------|--------|
| `data` | JSON 加载为只读字典 | `JsonLoader`, `GameDataRepository` |
| `core` | 战斗流程 + 角色实体 | `BattleEngine`, `BattleUnit`, `TurnManager` |
| `skills` | 技能定义 + 效果执行 | `ActiveSkill`, `PassiveSkill`, `ISkillEffect` |
| `ai` | 策略评估 + 条件判定 + 目标选择 | `StrategyEvaluator`, `ConditionEvaluator`, `TargetSelector` |
| `equipment` | 装备属性 + Buff/Debuff | `EquipmentSlot`, `BuffManager`, `TraitApplier` |
| `pipeline` | 伤害计算流水线 | `DamageCalculator`, `DamageCalculation`, `DamageResult` |
| `events` | 发布-订阅事件系统 | `EventBus`, `BattleEvent` |

## 接口契约

- **模块间通信**：优先通过 `EventBus`（发布-订阅）
- **数据流向**：`Strategy → AI → Core → Pipeline → Events → UI`
- **配置加载**：`data/` 下的 JSON 在启动时一次性加载为只读对象
- **Core 是唯一直接修改游戏状态的地方**

## 核心机制框架

### 1. 人物-技能-装备连接

```
角色的可用技能池 = 职业自带技能 + 当前装备赋予的技能
```

- **职业自带技能**：每个职业有固定的主动/被动/勇气技能列表，其他职业无法使用
- **装备赋予技能**：角色只能使用**自己能装备的装备类型**所赋予的技能
  - 例：雪游侠只能装备弓和饰品 → 只能用弓/饰品赋予的技能
- **技能ID系统**：所有技能（无论职业习得还是装备赋予）进入同一个技能池，通过唯一ID引用，解决翻译/命名差异

### 2. 多兵种标签系统

一个职业可以有多个兵种标签：
- 雪游侠：弓兵/步兵
- 战士：重装/步兵
- 盗贼：步兵/斥候

用途：
- 策略条件判定（"步兵系的敌人"匹配所有带步兵标签的）
- 装备限制（某些装备要求特定兵种）
- 克制计算（基于标签）

### 3. 兵种克制链

| 攻击方 | 被特攻方 | 效果 |
|--------|----------|------|
| 骑兵（骑马） | 步兵系 | 物理攻击威力 **2倍** |
| 飞行系 | 骑乘系 | 物理攻击威力 **2倍** |
| 弓兵 | 飞行系 | 弓攻击威力 **2倍** |
| 飞行系防御特性 | 地上近接攻击 | 命中率 **半减** |

### 4. 职业特性（Trait）

独立于技能的常驻效果，在特定时机介入：
- 雪游侠："弓攻击对飞行系威力2倍，无遠隔技能附加遠隔"
- 白骑士："物理攻击对步兵系威力2倍"

### 5. 策略编程系统

- 每个角色 **8个策略栏位**
- 每条策略 = 技能 + 条件1 + 条件2 + 模式（仅/优先）
- 条件组合逻辑：
  - 仅 + 仅 = AND
  - 仅 + 优先 = 满足仅后按优先筛选
  - 优先 + 优先 = 条件2优先！顺序: 1+2 > 2 > 1
- 目标选择：默认规则（前排优先）→ 应用条件1（过滤/排序）→ 条件2 → 选优先级最高

### 6. 战斗流程

```
战斗开始阶段
  └── 战斗开始时被动（敌我各1人，速度优先，同时发动限制）

行动阶段（循环）
  ├── 主动技能发动（按速度顺序，每人1回）
  │   └── 按策略优先级从上到下尝试（AP足够+条件满足）
  ├── 攻击方被动（主动发动前）
  │   ├── 自身强化被动（发动者自己）
  │   └── 友方强化被动（同部队最快1人，同时发动限制）
  ├── 防守方被动（主动发动前）
  │   ├── 防御/减益被动（对方部队最快1人，同时发动限制）
  │   └── 格挡/回避被动（所有满足条件者）
  ├── 主动技能实际执行（伤害计算、buff、状态异常）
  └── 行动后被动（敌我双方按速度，每人1回）

战斗结束阶段
  ├── 战斗结束时被动（无同时发动限制）
  └── 胜负判定（HP比例高者胜）
```

### 7. AP/PP 系统

| 资源 | 用途 | 战斗结束条件 |
|------|------|--------------|
| **AP** | 主动技能消耗 | 双方AP全部耗尽 → 战斗结束 |
| **PP** | 被动技能消耗 | - |

- **初始值**：所有角色初始 `AP = 2`，`PP = 1`（写在 `characters.json` 的 `baseStats` 中）
- 装备可以增加AP/PP上限（绯石吊坠AP+1，苍石吊坠PP+2）
- 技能效果可回复AP/PP（击倒时AP+1、命中时PP+1）

### 8. Buff/Debuff 系统（精确规则）

| 规则 | 实现 |
|------|------|
| 同名效果不同技能 | **可重叠**（粉碎-20% + 防御诅咒-50% = -70%） |
| 同技能重复施加 | **不发动**（纯buff/debuff技能） |
| 攻击兼buff/debuff | buff只适用一次，之后只攻击 |
| 一次性buff | 主动行动后消失，不计入"已有该buff"判定 |
| buff/debuff赋予判定 | 某人行动结束直后 |

### 9. 蓄力（Charge）状态机

- 第1行动蓄力（回避和被动不可用）
- 第2行动发动
- 连续3回待机后不能发动

### 10. 伤害判定流水线

```
黑暗判定 → 命中判定 → 格挡/回避判定 → 暴击判定 → 实际伤害
```

各阶段规则：
- **黑暗**：命中率必定为0；冻结/气绝/被掩护/蓄力中例外
- **必中**：无视命中/回避，但无法无视回避系技能
- **格挡**：只减轻**物理伤害**，魔法威力部分不减
- **无物理威力**：格挡不可，格挡系被动不发动
- **回避攻击**：无视黑暗和命中，必定发动

### 11. 伤害计算公式

```
攻击力 = (基础面板攻击力 + 武器威力) × (1 + 加攻buff倍率总和)
防御力 = (基础面板防御力 + 装备防御力) × (1 + 加防buff倍率总和)

总伤害 = [攻击力 - 防御力] × (技能威力/100) × 兵种特性补正 × 职业Trait补正
         ↓
       暴击：× (1.5 + 暴击增伤buff) 【上限3.0】
         ↓
       格挡：× (1 - 格挡减伤率) 【无盾25% / 大盾50%】
         ↓
       四舍五入

保底伤害：当攻击力 < 防御力时，保底为 1
```

混合攻击（物理+魔法）分两部分计算，格挡只减物理部分。

### 12. 装备槽规则（统一简化）

| 阶段 | 总槽数 | 单武器（无副手） | 剑+盾 / 双持 |
|------|--------|------------------|--------------|
| 基础（未CC） | 3 | 1武器 + 2饰品 | 2武器 + 1饰品 |
| CC后 | 4 | 1武器 + 3饰品 | 2武器 + 2饰品 |

- **武器槽永不为空**：角色创建时必须装备武器。
- **CC必须提供 tangible benefit**：所有角色CC后都比基础多1个饰品槽。
- 双持第二把武器放入 OffHand，副手攻击威力减半加算。

### 13. 格挡规则

- 所有角色都有基础格挡率（`Block` 属性），**无盾也能格挡**。
- 盾牌的作用是**提升格挡率**（`block_rate`），而非改变减伤率。
- 减伤率：
  - 无盾格挡：**-25%**
  - 装备盾（Shield）：**-25%**（盾提升格挡概率）
  - 装备大盾（GreatShield）：**-50%**

### 12. 条件系统（12大类）

1. 队列·状况（前排/后排/前后列/人数）
2. 兵种（步兵/骑兵/飞行/重装/斥候/弓兵/术士/精灵/兽人/有翼人）
3. HP（最高/最低/比例/25%/50%/75%）
4. AP·PP（最高/最低/阈值）
5. 状态（buff/debuff/异常/毒/炎上/冻结/气绝/黑暗）
6. 攻击属性（被物理/魔法/列/全体攻击时）
7. 编成人数（敌/友数量）
8. 自身状态（自身/自身以外/第N次行动）
9. 自身HP
10. 自身AP·PP
11. 敌兵种存在（敌有/无特定兵种）
12. 最高/最低属性（最大HP/物攻/魔攻/速度等）

`ConditionEvaluator` 需要 `BattleContext` 作为输入（某些条件需要扫描全场）。

### 13. 同时发动限制

按事件类型分层：
- 战斗开始时被动（敌我各1人）
- 友方强化被动（同部队1人）
- 防守方防御被动（对方部队1人）
- 行动后对应被动（敌我各1人）
- 战斗结束时（无限制）

### 14. 隐藏机制

- 增益技能的目标如果都已有该增益，自动跳过（不浪费AP）
- "自身HP100%时"：HP100%以上也适用
- 会心率溢出：内部保持原值，不做上限截断
- 守护命令"友方"包含"自己和以外的所有友方"

## 数据 Schema 约定

### JSON 文件列表

| 文件 | 内容 |
|------|------|
| `characters.json` | 职业模板：基础属性（含AP/PP初始值）、成长类型、兵种标签、可装备类型、自带技能ID列表 |
| `active_skills.json` | 主动技能：AP消耗、类型、威力、命中、范围、效果、特性标签 |
| `passive_skills.json` | 被动技能：PP消耗、触发时机、效果、特性标签、同时发动限制标记 |
| `equipments.json` | 装备：80件（8大类型全覆盖）、属性字典、赋予技能ID、可装备职业、特殊效果 |
| `conditions.json` | 条件定义：12大类条件的枚举和参数结构 |

### 文档体系索引

| 文档 | 位置 | 作用 |
|------|------|------|
| `AGENTS.md` | 项目根目录 | 概念框架、机制规则、开发状态（本文档） |
| `docs/csharp-architecture.md` | `docs/` | C#技术架构：类设计、接口、枚举、数据流 |
| `docs/dev-mistakes.md` | `docs/` | 开发错误记录：Subagent规范、机制规则、常犯错误 |
| `docs/被动技能实现融入现有框架.md` | `docs/` | 被动技能架构改造方案：5大模块、文件清单、路线图 |
| `progress.md` | 项目根目录 | 开发进度日志：已完成/进行中/待办 |
| `roadmap.md` | 项目根目录 | 开发路线图：阶段划分、完结标准、想法池 |

### 运行时数据流

```
JSON (只读模板)
  ↓ 加载
BattleUnit (运行时实例)
  ├── CurrentHp, CurrentAp, CurrentPp (可变)
  ├── Buffs, Ailments (运行时动态)
  ├── Equipment (当前装备)
  └── Strategies (玩家配置的8条策略)
```

## 开发状态

### 已完成
- [x] 项目目录结构初始化
- [x] Git 仓库初始化并记录地址（`git@github.com:zjm19950105/-battleflag-king.git`）
- [x] 开发规范文档（AGENTS.md + progress.md + roadmap.md + docs/csharp-architecture.md + docs/dev-mistakes.md）
- [x] 技术选型确定（Godot 4 + C#）
- [x] 核心设计方向确定（大巴扎节点 + 圣兽之王自动战斗）
- [x] 完整框架设计（基于圣兽之王资料分析）：14项核心机制框架
- [x] Memory系统记录项目上下文
- [x] Godot 4 C# 项目创建并配置完毕
- [x] 全部 .cs 文件生成 .uid，Godot 编辑器识别正常
- [x] 能 F5 跑起场景
- [x] 能正常 commit 到 git
- [x] Data层：角色/主动技能/被动技能/装备 JSON + GameDataRepository
- [x] Pipeline层：DamageCalculator（命中/回避/暴击/格挡/兵种克制）
- [x] AI层：StrategyEvaluator + ConditionEvaluator + TargetSelector
- [x] Core层：BattleUnit + BattleContext + BattleEngine
- [x] Main.cs 入口脚本（加载数据 → 创建单位 → 启动战斗）
- [x] 1v1 普攻代码闭环完成（逻辑层）
- [x] CC框架接入：CharacterData/BattleUnit/Main.cs 支持CC状态切换，默认未CC
- [x] 12/12角色数据已填充（Subagent批量填充+修正），JSON格式验证通过
- [x] `CcInitialEquipmentIds` 配置：CC后新增装备槽强制带装备
- [x] `EquipmentSlot` 武器槽校验：MainHand永不为空
- [x] 装备槽统一规则（3基础 → 4 CC）：所有12角色基础3槽、CC后4槽已配置
- [x] 格挡规则修正：`GetBlockReduction()` 无盾格挡减25%，大盾减50%
- [x] Godot 运行验证通过（未CC状态）：1v1 战斗循环正常，AP消耗、HP变化、胜负判定正确
- [x] **Phase 1.2 — 战前配置与 3v3 自动战斗**：
  - [x] 控制台输入改为 Godot LineEdit UI（async/await，解决 Windows GUI stdin 问题）
  - [x] 队伍组建：12角色选3人
  - [x] 阵型布置：6位置网格（前排1-3，后排4-6）
  - [x] 敌人配置框架：enemy_formations.json + strategy_presets.json
  - [x] 被动技能选择系统（PP上限控制）
  - [x] 策略编程控制台（8条策略栏位，支持编辑/确认/默认）
  - [x] 条件系统MVP（SelfHp、SelfApPp、lowest/highest排序）
  - [x] EventBus接入BattleEngine（6个战斗事件）
  - [x] PassiveSkillProcessor：12个触发时机 + 同時発動制限
  - [x] 3v3完整对战验证通过（Godot中可正常运行）
  - [x] 攻击日志改进（显示剩余HP和debuff）
  - [x] 被动技能效果打印（ExecuteSimpleEffect）
  - [x] 多轮对战支持（"是否再来一局"）
  - [x] 被动技能架构文档：`docs/被动技能实现融入现有框架.md`

- [x] **Phase 1.3 — 被动技能完整实现与架构改造（含主动技能联动）**：
  - [x] DamageCalculation 13可变字段 + 多段攻击每hit独立判定
  - [x] PendingActionQueue 行动队列（反击/追击/先制）
  - [x] TemporalState 临时标记系统（1次免疫/致死耐）
  - [x] 自定义计数器 Dictionary<string,int> + 10+ EffectType
  - [x] Charge 蓄力状态机 + OnKnockdownEvent
  - [x] CcClasses 框架（CC后兵种变化）
  - [x] CC Trait 系统（领主物攻对步兵2倍等）
  - [x] Buff 生命周期框架（默认1回合持续，回合末清理）
  - [x] 12角色数据全面审计修正
  - [x] 单行动步进（StepOneAction）
  - [x] 拖拽布阵 UI + 1v1/3v3 模式选择
  - [x] 战前条件面板（Day+CC独立控制）
  - [x] 策略选择 OptionButton 下拉框
  - [x] 战斗双栏面板 + 被动效果日志增强
  - [x] 编译通过，0错误0警告

- [x] **自动化测试框架**: 54个NUnit测试用例(51通过), 5个测试类
  - DamageCalculator: 基础/暴击/格挡/多段/ForceHit/兵种克制
  - ConditionEvaluator: HP/兵种/位置/状态/编成人数/属性排名(17用例)
  - BuffManager: 叠层/去重/一次性/回合清除
  - TargetSelector: 前排遮挡/远程/仅/优先/贯通/双优先
  - EquipmentSlot: 装备/卸下/双持/槽位/CanEquip
  - 运行: `cd goddot-test && dotnet test`
- [x] **Codex Skills 配置**: review/simplify 技能已可用, 改代码后自动触发

### 当前聚焦
**Phase 1.4 — 6v6 完整竞技场**

Phase 1.3 已完成。下一步：
1. 从3v3扩展到6v6（填满6个位置）
2. 完整的 if/条件/执行/优先级 编程系统验证
3. 更丰富的条件和执行指令
4. 战斗日志/回放，方便调试编程逻辑

### 待办（按优先级）
1. **6v6 完整竞技场**（Phase 1.4）
2. 玩家策略持久化（JSON save/load）
3. 2D 视觉表现（占位 Sprite、血条、战斗日志 UI）
4. 大巴扎循环（Phase 2）
5. 本地化与合规（Phase 3）

## 协作规则
- **每次只修改一个模块**，修改前 Read 相关文件确认上下文
- **复杂功能先 Plan 再执行**，得到确认后写代码
- **每个可运行功能点后立即 commit**，commit 信息遵循 `<类型>: <描述>`
- **数值与规则写进 `data/`，不写死在代码里**
- **阶段管理严格遵循 `roadmap.md`**，新想法写入对应阶段的想法池，不准跨阶段施工
- **AI 可直接更新 `roadmap.md`、`progress.md` 和本文件**，无需逐条确认

## 代码质量标准（强制执行）

**此项目的维护者是没有项目记忆的 AI**。每一行代码必须让一个刚启动的 Codex 会话能在 10 秒内理解。这是生死线。

### 架构优雅
- **单一数据流**：同类效果走同一条路径。如果被迫分两条路径，必须在代码注释和架构文档中解释 WHY
- **不要为未来写代码**：不用的抽象、预留的扩展点、"可能以后会用到"的参数 —— 全部删除
- **接口要窄**：一个接口只做一件事。不要为了"通用"塞 5 个参数进去，其中 4 个永远是 null
- **新模块必须文档化**：每新增一个模块/类/枚举，必须在 `docs/csharp-architecture.md` 同步更新

### 字段一致性
- **同一个概念用同一个名字**：不能 `HitRate` 在这里，`Accuracy` 在那里，`Hit` 在第三个地方
- **JSON 字段名 = C# 属性名**：`"triggerTiming"` ↔ `TriggerTiming`，不允许中间翻译
- **枚举值不许自创**：Subagent 填充 JSON 时必须对照 C# 枚举定义，发现缺枚举值→报错，不准自己造

### 注释（缺一不可）
1. **非显而易见的 WHY**：为什么这么设计，为什么不用那个看起来更简单的方案
2. **类和公共方法的 XML doc**：一句话说明"这个类/方法解决什么问题"
3. **跨模块的调用关系**：如果 A 的效果不在 A 执行而在 B 执行，必须在 A 注释里写清楚"实际执行见 B.xxx()"

### 文档同步
- 修改任何 C# 类字段、枚举值、方法签名 → **同一批次**更新 `docs/csharp-architecture.md`
- 新增/重命名文件 → 更新 AGENTS.md 的目录结构
- 完成一个 Phase → 更新 `progress.md` + `roadmap.md`

### 禁止事项
- 🚫 `// TODO` 注释超过 3 个不清理
- 🚫 字段名缩写（`dmg`、`ctx` 作为公开 API 名禁止；`_ctx` 做私有字段可以）
- 🚫 魔法数字不解释（`0.5f` 旁边必须写 `// 格挡减伤50%`）
- 🚫 复制粘贴同类代码超过 3 处不提取
