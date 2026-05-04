# 开发进度日志

## 当前阶段
Phase 1.1 — 1v1 普攻对战（12/12角色数据已填充，CC框架已调整，等待未CC状态Godot验证）

## 已完成
- [x] 确定技术栈：Godot 4 + C#
- [x] 确定核心设计方向：大巴扎节点 + 圣兽之王自动战斗
- [x] 确定架构原则：数据驱动、正交系统、文档外置
- [x] 建立文档体系（CLAUDE.md + progress.md + docs/csharp-architecture.md）
- [x] 完整框架设计（基于圣兽之王资料分析）
  - [x] 14项核心机制框架
  - [x] C#技术架构文档（类设计、接口、枚举、数据流）
  - [x] 伤害计算Pipeline设计
  - [x] 条件系统12大类设计
- [x] Memory系统记录项目上下文
- [x] Subagent并行编码（第一批+第二批全部完成）
  - [x] Data层：18个枚举 + 8个数据类 + GameDataRepository
  - [x] JSON数据文件：characters.json、active_skills.json、passive_skills.json、equipments.json
  - [x] 事件系统：EventBus + 6个战斗事件
  - [x] 接口定义：ISkillEffect、ITrait、SkillEffectFactory
  - [x] 工具类：RandUtil、JsonLoader
  - [x] Pipeline层：DamageCalculator（命中/回避/暴击/格挡全阶段）
  - [x] Equipment层：Buff、BuffManager、EquipmentSlot、TraitApplier
  - [x] AI层：Strategy、Condition、StrategyEvaluator、TargetSelector、ConditionEvaluator
  - [x] Core层：BattleUnit（完整公共接口）、BattleContext、ActiveSkill、PassiveSkill
- [x] Godot 4 项目骨架搭建
  - [x] 项目创建在 `战旗之王/goddot/`
  - [x] Main.cs / main.tscn / project.godot 配置正确
  - [x] src/ 和 data/ 已复制到项目内
  - [x] 全部 .cs 文件生成 .uid，Godot 编辑器识别正常
- [x] Core层最终代码
  - [x] BattleEngine（回合循环、速度排序、胜负判定、AP耗尽兜底）
  - [x] Main.cs 入口脚本（加载数据→创建单位→启动战斗→打印结果）
  - [x] DamageCalculator 补全命中判定（RollHit）和回避判定（RollEvasion）
- [x] **CC（Class Change）框架接入**
  - [x] CharacterData 增加 CcClassId、CcName、CcEquippableCategories、CcInnateActiveSkillIds、CcInnatePassiveSkillIds
  - [x] BattleUnit 根据 isCc 自动切换装备槽和技能池
  - [x] Main.cs 默认创建 CC 后角色（剑士→剑圣、佣兵→兰茨克内希特）
- [x] **角色数据填充（12/12基础职业）**
  - [x] 剑士/剑圣、佣兵/兰茨克内希特：完整数据验证通过
  - [x] Subagent 批量填充：领主、战士、士兵、家臣、重装步兵、角斗士、勇士、猎人、射手、盗贼
  - [x] Subagent 修正：技能名称/描述/威力/命中率、被动技能触发时机、角色装备配置
  - [x] 斗士武器误改修正（Sword→Axe，已恢复）
  - [x] 删除旧占位技能（skill_basic_attack、skill_block）
  - [x] 补充 PassiveTriggerTiming 枚举值（SelfBeforeMeleeHit、SelfBeforePhysicalHit、AllyOnAttacked、SelfOnActiveUse）
- [x] **CC框架调整**
  - [x] `CharacterData` 新增 `CcInitialEquipmentIds`：CC后新增/变化装备槽必须带装备
  - [x] `Main.cs` 默认 `isCc: false`（角色出场未CC）
  - [x] `EquipmentSlot.Equip` 支持副手武器（双持）
  - [x] `EquipmentSlot.ValidateWeaponEquipped()`：武器槽永不为空
  - [x] `CreateUnit` 根据CC状态加载不同装备列表
- [x] **装备槽统一规则（3基础 → 4 CC）**
  - [x] 所有12角色基础3槽、CC后4槽已配置
  - [x] 单武器角色（弓/枪/斧单手）：基础2饰品，CC后3饰品
  - [x] 双持/剑盾角色：基础1饰品，CC后2饰品
- [x] **格挡规则修正**
  - [x] `GetBlockReduction()`：无盾格挡减25%，大盾减50%
- [x] **文档体系**
  - [x] 创建 `docs/dev-mistakes.md`：记录 Subagent 调用规范、机制规则
  - [x] 同步 `docs/csharp-architecture.md`：CharacterData/BattleUnit/EquipmentSlot/PassiveTriggerTiming 与代码一致
  - [x] 更新 `CLAUDE.md`：装备槽规则、格挡规则、文档索引
- [ ] **Godot 运行验证（未CC状态）**
  - [ ] 等待玛奇玛重新 Build + F5 验证未CC角色数据加载

## 进行中
- [ ] Godot 运行验证（未CC状态）：等待玛奇玛 Build + F5

## 待办（按优先级排序）
### 🔴 当前聚焦
1. **Godot 运行验证（未CC状态）**
   - 等待玛奇玛 Build + F5 验证未CC角色数据加载
   - 验证武器槽是否有装备、饰品槽是否为空
   - 验证战斗循环正常（AP消耗、HP变化、胜负判定）

### 🟡 验证完成后
2. **运行 1v1 战斗验证（不同职业对战）**
   - 测试多种职业组合（高攻vs高防、高速vs低速等）
   - 验证伤害计算是否符合预期
   - 验证 CC 后装备槽变化是否正确生效

3. **技能系统解耦设计**
   - 确定技能持有 vs 技能解锁的架构
   - 初始解锁数量配置化（当前：2主动+1被动）
   - 后续奖励/装备解锁更多技能的机制

### 🟢 后续扩展
3. **给 BattleEngine 接入 EventBus**
   - 战斗开始时触发 OnBattleStartEvent
   - 攻击前触发 BeforeAttackEvent
   - 攻击后触发 AfterAttackEvent
   - 让被动技能真正生效
4. **2D 视觉表现（占位即可）**
   - 场景里放两个 Sprite 代表敌我角色
   - 血条（ProgressBar 或自定义）
   - 战斗日志显示在屏幕上（RichTextLabel）
5. **站位/射程系统**
   - 前排 3 位置 + 后排 3 位置
   - 近战只能打前排，远程可以打后排
   - 前排全灭后才能打后排
6. **策略编程 UI**
   - 给角色设置 8 条策略
   - 条件选择器（HP 比例、兵种、距离等）
   - 目标选择器
7. **城镇购买系统框架**
   - 商店数据结构
   - 装备购买逻辑
   - 经济系统（金币）

## 已知问题/待优化
- **Lv50 数据导致 1v1 不平衡**：当前所有角色使用 Lv50 数值，高攻职业（佣兵物攻79）对低防职业（剑士物防15）可造成100+伤害，几乎秒杀。这是参考文档的真实设计，非bug。如需平衡测试，需调整为 Lv1 等效值（但参考文档未提供 Lv1 数据）。
- **PassiveSkill 未接入**：BattleEngine 最小版没有触发任何被动技能（EventBus 未接入战斗循环）。快速打击、招架、追击斩等被动当前不生效。
- **回避判定未完成**：`RollEvasion` 只是框架，没有实际判定逻辑（因为没有回避系技能数据）。
- **DamageCalculator 黑暗判定 MVP 简化**：黑暗状态直接 miss，没有做冻结/气绝/被掩护/蓄力中的例外。
- **核心装备不可卸下机制**：当前 EquipmentSlot 没有校验，角色可以裸装。需在后续版本中加"武器/防具槽位不可空"的限制。

## 最近会话记录
- 2026/05/02：MCP连接解决（HTTP transport + 8000端口）。用户确认 Godot 编辑器、项目、插件均已就绪。
- 2026/05/02：修复 BattleEngine 日志输出（Console.WriteLine → GD.Print 回调）。验证 1v1 战斗循环正常（AP消耗、HP变化、胜负判定）。
- 2026/05/02：用户指出框架问题——角色应有初始装备、无"普攻"概念、每次行动必须消耗AP使用技能、数值以参考文档为准。
- 2026/05/02：找到参考文档（`Music/圣兽之王资料整理/`）。确认剑士无盾（双剑职业）、佣兵CC前无盾。
- 2026/05/02：用户要求 CC 无缝接入现有框架。修改 CharacterData、BattleUnit、Main.cs 支持 CC。
- 2026/05/02：Subagent 填充剑士+佣兵数据。修复 PassiveTriggerTiming 枚举缺失值。Godot 运行验证通过。
- 2026/05/02：用户要求角色默认未CC、武器槽永不为空、CC后新增槽位必须有装备。创建 `dev-mistakes.md`，修改 `CharacterData`/`EquipmentSlot`/`Main.cs`，新增 `CcInitialEquipmentIds`。
- 2026/05/02：Subagent 批量填充+修正剩余10角色数据。斗士武器被误改为Sword，已手动修正回Axe。
- 2026/05/02：同步 `csharp-architecture.md` 到当前代码状态。更新 `progress.md` 和 `roadmap.md`。
- 2026/05/02：用户提出技能系统解耦设计——全持有+部分解锁 vs 渐进获得，待决策。
- 2026/05/02：用户确定装备槽统一规则（基础3槽→CC后4槽）和无盾格挡减25%。已修改所有12角色JSON、BattleUnit.GetBlockReduction、同步4份文档。
