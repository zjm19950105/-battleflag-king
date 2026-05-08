# 战旗之王 — C# 技术架构文档

> 本文档记录 C# 侧的类设计、接口定义、枚举、方法签名和数据流。
> 与 `CLAUDE.md`（概念框架）互补：CLAUDE 讲"是什么"，本文档讲"怎么写"。
> **最后更新**: 2026-05-07 — Phase 1.3 完成 + 装备系统 + 策略条件编辑器

---

## 目录结构（实际）

```
src/
  Data/
    Models/
      CharacterData.cs
      ActiveSkillData.cs
      PassiveSkillData.cs
      EquipmentData.cs
      EquipmentCategory.cs
      ConditionCategory.cs
      ConditionData.cs
      ConditionMode.cs
      TraitData.cs
      SkillEffectData.cs
    GameDataRepository.cs
    DayProgression.cs
  Core/
    BattleUnit.cs
    BattleContext.cs
    BattleEngine.cs
    BattleResult.cs
    TemporalState.cs
  Skills/
    ActiveSkill.cs
    PassiveSkill.cs
    ISkillEffect.cs
    SkillEffectFactory.cs
    PassiveSkillProcessor.cs
    PendingAction.cs
    PendingActionType.cs
    PassiveTargetType.cs
  Ai/
    Strategy.cs
    Condition.cs
    StrategyEvaluator.cs
    ConditionEvaluator.cs
    ConditionMeta.cs
    TargetSelector.cs
  Equipment/
    Equipment.cs
    EquipmentSlot.cs
    Buff.cs
    BuffManager.cs
    TraitApplier.cs
    ITrait.cs
  Pipeline/
    DamageCalculator.cs
    DamageCalculation.cs
    DamageResult.cs
  Events/
    EventBus.cs
    IBattleEvent.cs
    BattleEvents.cs
```

---

## 数据文件

| 文件 | 条目数 | 说明 |
|------|--------|------|
| `characters.json` | 18 | 所有角色（含CC数据） |
| `active_skills.json` | 55 | 主动技能 |
| `passive_skills.json` | 50 | 被动技能 |
| `equipments.json` | 80 | 装备（8大类型全覆盖） |
| `enemy_formations.json` | 5 | 敌方阵型模板 |
| `strategy_presets.json` | 3 | 策略预设 |

---

## 枚举定义

```csharp
namespace BattleKing.Data
{
    public enum UnitClass
    {
        Infantry, Cavalry, Flying, Heavy, Scout, Archer, Mage, Elf, Beastman, Winged
    }

    public enum EquipmentCategory
    {
        Sword, Axe, Spear, Bow, Staff, Shield, GreatShield, Accessory
    }

    public enum SkillType { Physical, Magical, Assist, Heal, Debuff }
    public enum AttackType { Melee, Ranged, Magic }

    public enum TargetType
    {
        Self, SingleEnemy, SingleAlly, TwoEnemies, ThreeEnemies,
        FrontAndBack, Column, Row, AllEnemies, AllAllies
    }

    public enum ConditionCategory
    {
        Position, UnitClass, Hp, ApPp, Status, AttackAttribute,
        TeamSize, SelfState, SelfHp, SelfApPp, EnemyClassExists, AttributeRank
    }

    public enum ConditionMode { Only, Priority }
    // Only = 仅（条件不满足则跳过技能）
    // Priority = 优先（条件不满足仍按默认目标发动）

    public enum PassiveTriggerTiming
    {
        BattleStart, SelfBeforeAttack, AllyBeforeAttack, AllyBeforeHit,
        SelfBeforeHit, SelfBeforeMeleeHit, SelfBeforePhysicalHit,
        AllyOnAttacked, SelfOnActiveUse, AllyOnActiveUse,
        AfterAction, BattleEnd, OnHit, OnBeingHit, OnKnockdown
    }

    public enum UnitState
    {
        Normal, Charging, Stunned, Frozen, Darkness, BlockSeal, CritSeal
    }

    public enum StatusAilment
    {
        Poison, Burn, Freeze, Darkness, Stun, BlockSeal, CritSeal
    }

    public enum PendingActionType
    {
        Counter, Pursuit, Preemptive, BattleEndAttack
    }
}
```

---

## 数据层（JSON 反序列化）

```csharp
namespace BattleKing.Data
{
    public class CharacterData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<UnitClass> Classes { get; set; } = new();
        public List<EquipmentCategory> EquippableCategories { get; set; } = new();
        public List<string> InnateActiveSkillIds { get; set; } = new();
        public List<string> InnatePassiveSkillIds { get; set; } = new();
        public Dictionary<string, int> BaseStats { get; set; } = new();
        public string GrowthType { get; set; }
        public List<TraitData> Traits { get; set; } = new();
        public List<string> InitialEquipmentIds { get; set; } = new();

        // CC 数据
        public string CcClassId { get; set; }
        public string CcName { get; set; }
        public List<UnitClass> CcClasses { get; set; } = new();       // CC后兵种变化(领主→骑兵)
        public List<TraitData> CcTraits { get; set; } = new();         // CC后特性(物理对步兵2倍)
        public List<EquipmentCategory> CcEquippableCategories { get; set; } = new();
        public List<string> CcInnateActiveSkillIds { get; set; } = new();
        public List<string> CcInnatePassiveSkillIds { get; set; } = new();
        public List<string> CcInitialEquipmentIds { get; set; } = new();
    }

    public class ActiveSkillData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int ApCost { get; set; }
        public SkillType Type { get; set; }
        public AttackType AttackType { get; set; }
        public int Power { get; set; }
        public int? HitRate { get; set; }
        public TargetType TargetType { get; set; }
        public string EffectDescription { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<SkillEffectData> Effects { get; set; } = new();
        public string LearnCondition { get; set; }
        public int? UnlockLevel { get; set; }
    }

    public class PassiveSkillData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int PpCost { get; set; }
        public PassiveTriggerTiming TriggerTiming { get; set; }
        public SkillType Type { get; set; }
        public int? Power { get; set; }
        public int? HitRate { get; set; }
        public string EffectDescription { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<SkillEffectData> Effects { get; set; } = new();
        public bool HasSimultaneousLimit { get; set; }
        public string LearnCondition { get; set; }
        public int? UnlockLevel { get; set; }
    }

    public class EquipmentData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public EquipmentCategory Category { get; set; }
        public Dictionary<string, int> BaseStats { get; set; } = new();
        public List<string> GrantedActiveSkillIds { get; set; } = new();
        public List<string> GrantedPassiveSkillIds { get; set; } = new();
        public List<UnitClass> UsableByClasses { get; set; } = new();
        public List<string> RestrictedClassIds { get; set; } = new();
        public List<string> SpecialEffects { get; set; } = new();
    }

    public class SkillEffectData
    {
        public string EffectType { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class TraitData
    {
        public string TraitType { get; set; }
        public string Description { get; set; }
    }

    // ==================== GameDataRepository ====================
    public class GameDataRepository
    {
        public Dictionary<string, CharacterData> Characters { get; private set; }
        public Dictionary<string, ActiveSkillData> ActiveSkills { get; private set; }
        public Dictionary<string, PassiveSkillData> PassiveSkills { get; private set; }
        public Dictionary<string, EquipmentData> Equipments { get; private set; }
        public Dictionary<string, EnemyFormationData> EnemyFormations { get; private set; }
        public Dictionary<string, StrategyPresetData> StrategyPresets { get; private set; }

        public void LoadAll(string dataPath) { /* 一次性加载所有JSON */ }

        // 所有 Get* 方法使用 TryGetValue，缺key返回null（不抛异常）
        public CharacterData GetCharacter(string id) => Characters.TryGetValue(id, out var v) ? v : null;
        public ActiveSkillData GetActiveSkill(string id) => ActiveSkills.TryGetValue(id, out var v) ? v : null;
        public PassiveSkillData GetPassiveSkill(string id) => PassiveSkills.TryGetValue(id, out var v) ? v : null;
        public EquipmentData GetEquipment(string id) => Equipments.TryGetValue(id, out var v) ? v : null;
        public List<EquipmentData> GetAllEquipment() => Equipments.Values.ToList();
    }
}
```

### 职业描述与命名引用约束（2026-05-08）

职业/角色说明文本来自参考资料：

`C:\Users\ASUS\Music\圣兽之王资料整理\有用的资料\unicorn-overlord-class-compendium.md`

提取规则：

1. 每个职业以标题为主键来源，例如 `【射手 / 盾射手 (Shooter / Shield Shooter)】`。
2. 兵种来自该职业表格中的 `| 兵种 | XXX |`。
3. 角色定位来自标题后的 `#### 角色定位` 段落，优先提取 `主要角色` 列表。

展示格式示例：

```text
【射手 / 盾射手 (Shooter / Shield Shooter)】角色定位：
步兵系：
- 高单体火力弩兵（弓兵系中最高物攻）
- 飞行系特攻（2倍伤害）
- CC后前列坦克（大盾装备+掩护技）
- 战后回复辅助
```

硬性约束：

- 描述数据中不得写死其他职业、兵种、角色、技能、装备的最终显示名。
- 所有可被改名/本地化的名称必须通过稳定 ID 引用，再由显示层从同一个数据源解析当前名称。
- 例如描述中提到“飞龙、羽剑士”等目标时，存储层应记录 `ReferencedClassIds` / `ReferencedCharacterIds` / `ReferencedUnitClassIds`，渲染时再解析为当前显示名。
- 后续如果“飞龙”改名，射手描述里的相关名称必须自动同步，不允许靠全文搜索替换。
- 原始参考文本只可作为导入来源或备注，不作为最终 UI 文案的唯一数据形态。

建议后续新增数据模型：

```csharp
public class CharacterRoleDescriptionData
{
    public string CharacterId { get; set; }
    public string BaseTitleSource { get; set; }      // 原参考标题，仅用于追溯
    public List<UnitClass> UnitClasses { get; set; }
    public List<string> RoleBullets { get; set; }    // 可带 {class:xxx}/{character:xxx} 引用 token
    public List<string> ReferencedCharacterIds { get; set; }
    public List<UnitClass> ReferencedUnitClassIds { get; set; }
    public List<string> ReferencedSkillIds { get; set; }
    public List<string> ReferencedEquipmentIds { get; set; }
}
```

渲染规则：

`CharacterRoleDescriptionData` -> `GameDataRepository` 名称解析 -> UI 文案。

不要在 `Main.cs` 或 UI helper 中拼硬编码职业名。职业显示名、CC显示名、技能名、装备名和描述中的引用名都必须从数据层统一解析。

---

## 核心运行时

```csharp
namespace BattleKing.Core
{
    public class BattleUnit
    {
        public CharacterData Data { get; private set; }
        public GameDataRepository GameData { get; private set; }
        public bool IsPlayer { get; set; }

        // 运行时状态
        public bool IsCc { get; private set; }
        public int CurrentLevel { get; set; } = 1;
        public int CurrentHp { get; set; }
        public int CurrentAp { get; set; }
        public int CurrentPp { get; set; }
        public int MaxAp { get; private set; }
        public int MaxPp { get; private set; }
        public int Position { get; set; }  // 1-6
        public bool IsFrontRow => Position <= 3;
        public bool IsAlive => CurrentHp > 0;
        public UnitState State { get; set; } = UnitState.Normal;
        public int ActionCount { get; set; } = 0;

        // 运行时集合
        public EquipmentSlot Equipment { get; private set; } = new();
        public List<Buff> Buffs { get; private set; } = new();
        public List<StatusAilment> Ailments { get; private set; } = new();
        public List<Strategy> Strategies { get; set; } = new();         // 8条策略
        public List<string> EquippedPassiveSkillIds { get; set; } = new();

        // 被动条件: skillId → 发动条件
        public Dictionary<string, Condition> PassiveConditions { get; set; } = new();

        // 临时标记（1次免疫、致死耐等）
        public List<TemporalState> TemporalStates { get; private set; } = new();

        // 自定义计数器（小精灵/怒气/连击等）
        public Dictionary<string, int> CustomCounters { get; private set; } = new();

        // CC感知的兵种标签
        public List<UnitClass> GetEffectiveClasses() =>
            IsCc && Data.CcClasses?.Count > 0 ? Data.CcClasses : Data.Classes;

        // CC感知的特性列表
        public List<TraitData> GetEffectiveTraits() { /* 合并 base + CC traits */ }

        // 动态属性计算
        public int GetCurrentStat(string statName) { /* 基础 + 装备 + buff */ }

        // 可用技能池（含装备赋予 + 等级过滤）
        public List<string> GetAvailableActiveSkillIds() { /* 基础 + CC + 装备赋予，按UnlockLevel过滤 */ }
        public List<string> GetAvailablePassiveSkillIds() { /* 同上 */ }
        public List<PassiveSkillData> GetEquippedPassiveSkills() { /* 从EquippedPassiveSkillIds解析 */ }

        public void TakeDamage(int damage) { CurrentHp = Math.Max(0, CurrentHp - damage); }
        public void ConsumeAp(int amount) => CurrentAp = Math.Max(0, CurrentAp - amount);
        public void ConsumePp(int amount) => CurrentPp = Math.Max(0, CurrentPp - amount);
        public int GetUsedPp() { /* 已装备被动PP总和 */ }
        public bool CanEquipPassive(string skillId) { /* PP上限检查 */ }
    }

    public class BattleContext
    {
        public GameDataRepository GameData { get; private set; }
        public List<BattleUnit> PlayerUnits { get; set; } = new();
        public List<BattleUnit> EnemyUnits { get; set; } = new();
        public int TurnCount { get; set; } = 0;
        public bool IsDaytime { get; set; } = true;

        // 当前正在处理的伤害计算（供ConditionEvaluator读取攻击属性）
        public DamageCalculation CurrentCalc { get; set; }

        public List<BattleUnit> AllUnits => PlayerUnits.Concat(EnemyUnits).Where(u => u != null).ToList();
        public List<BattleUnit> GetAliveUnits(bool isPlayer) => (isPlayer ? PlayerUnits : EnemyUnits).Where(u => u != null && u.IsAlive).ToList();
        public int GetAliveCount(bool isPlayer) => GetAliveUnits(isPlayer).Count;

        public static int GetStatValue(BattleUnit unit, string statName) { /* 从BaseStats读取 */ }
    }

    public class TemporalState
    {
        public string Key { get; set; }           // "MeleeHitNullify", "DeathResist", "SureHitNext"
        public int RemainingCount { get; set; } = 1;  // 剩余次数
        public int RemainingTurns { get; set; } = -1; // -1=战斗结束前有效
        public string SourceSkillId { get; set; }
    }

    // BattleEngine — 使用 StepOneAction 逐步推进战斗
    public class BattleEngine
    {
        private BattleContext _ctx;
        private EventBus _eventBus;
        private DamageCalculator _damageCalculator;
        private List<BattleUnit> _turnQueue;  // 本回合行动队列

        public Action<string> OnLog;

        // 逐步推进：每次执行一个单位的行动
        public SingleActionResult StepOneAction()
        {
            if (_turnQueue.Count == 0) { /* 新回合：填充队列，按速度排序 */ }
            // 取出下一个存活单位
            ExecuteUnitTurn(unit);
            ProcessPendingActions();
            BuffManager.CleanupAfterAction(unit);
            if (_turnQueue.Count == 0) { /* 回合结束：清理buff，检查AP耗尽 */ }
        }

        public void InitBattle() { /* 填充初始队列，发布BattleStartEvent */ }
        public void EnqueueAction(PendingAction action) { /* 入队 */ }
        private void ProcessPendingActions() { /* 执行队列中的反击/追击/先制 */ }
    }

    public enum BattleResult { PlayerWin, EnemyWin, Draw }
    public enum SingleActionResult { ActionDone, TurnDone, PlayerWin, EnemyWin, Draw }
}

// PendingAction — 行动队列
namespace BattleKing.Skills
{
    public class PendingAction
    {
        public PendingActionType Type { get; set; }
        public BattleUnit Actor { get; set; }
        public BattleUnit Target { get; set; }
        public int Power { get; set; }
        public int HitRate { get; set; }
        public List<string> Tags { get; set; } = new();
        public AttackType AttackType { get; set; }
    }
}
```

---

## 技能系统 — 双路径架构

```
Path A — ISkillEffect (通用):
  DamageEffect / BuffEffect / HealEffect / StatusAilmentEffect
  用于主动技能执行: ISkillEffect.Apply(ctx, caster, targets, calc)

Path B — PassiveSkillProcessor.ExecuteStructuredEffect() (阶段感知):
  ModifyDamageCalc / CounterAttack / TemporalMark / CoverAlly /
  ModifyCounter / ConsumeCounter / PreemptiveAttack / PursuitAttack /
  RecoverAp / RecoverPp / RecoverHp / AddBuff / AddDebuff
  有完整战斗阶段上下文(DamageCalculation/PendingActionQueue/TemporalState)
  这些类型在SkillEffectFactory中创建PassiveOnlyEffect — Apply()是空操作
```

```csharp
namespace BattleKing.Skills
{
    public class PassiveSkillProcessor
    {
        private EventBus _eventBus;
        private GameDataRepository _gameData;
        private BattleContext _ctx;  // 从事件中更新
        private Action<string> _log;
        private Action<PendingAction> _enqueueAction;

        // 同时发动限制跟踪
        private HashSet<string> _battleStartFired, _allyBuffFired, _defenseFired, _afterActionFired;

        public void SubscribeAll() { /* 订阅所有战斗事件 */ }

        // 7个事件处理器：
        // OnBattleStart → 战斗开始时被动
        // OnBeforeActiveUse → 攻击方被动（自身+友方）
        // OnBeforeHit → 防守方被动（防御/格挡/回避）+ BeforeHit介入DamageCalculation
        // OnAfterHit → 被命中后被动
        // OnAfterActiveUse → 行动后被动
        // OnKnockdown → 击倒事件
        // OnBattleEnd → 战斗结束时被动

        // 条件检查：触发前检查 BattleUnit.PassiveConditions[skillId]
        private bool CheckPassiveCondition(BattleUnit unit, string skillId) { /* 用ConditionEvaluator评估 */ }

        // 结构化效果分发（15+ EffectType）
        private void ExecuteStructuredEffect(BattleUnit unit, PassiveSkillData skill, ...) { }
    }
}
```

---

## AI / 策略系统

```csharp
namespace BattleKing.Ai
{
    public class Strategy
    {
        public string SkillId { get; set; }
        public Condition Condition1 { get; set; }
        public Condition Condition2 { get; set; }
        public ConditionMode Mode1 { get; set; }  // Only=仅 / Priority=优先
        public ConditionMode Mode2 { get; set; }
    }

    public class Condition
    {
        public ConditionCategory Category { get; set; }
        public string Operator { get; set; }    // equals/less_than/greater_than/lowest/highest
        public object Value { get; set; }
    }

    // ========== ConditionMeta — 条件编辑器元数据 ==========
    public static class ConditionMeta
    {
        // 10个可用条件类别及其中文标签
        public static readonly List<ConditionCategory> AllCategories;

        // 级联: 类别 → 操作符列表
        public static List<string> GetOperators(ConditionCategory cat);

        // 级联: 类别+操作符 → 值列表
        public static List<string> GetValues(ConditionCategory cat, string op);

        // UI选择 → Condition对象
        public static Condition BuildCondition(ConditionCategory cat, string op, string val, bool isOnly);

        // CategoryLabel — 中文显示名
        // 操作符映射: 低于→less_than, 高于→greater_than, 等于→equals
        //             最低→lowest(Only=过滤), 最高→highest(Only=过滤)
        //             以上→greater_than, 以下→less_than, 有→equals
        // 值映射: 25%→0.25f, 50%→0.5f, 75%→0.75f, 100%→1.0f
        //        属性排名→"Str"/"Def"/"Spd"等内部名
    }

    // ========== ConditionEvaluator — 12类条件评估 ==========
    public class ConditionEvaluator
    {
        private BattleContext _ctx;

        public bool Evaluate(Condition condition, BattleUnit subject, BattleUnit target = null)
        {
            return condition.Category switch
            {
                Position => EvaluatePosition,       // 前排/后排
                UnitClass => EvaluateUnitClass,     // 步兵/骑兵/飞行...
                Hp => EvaluateHp,                   // HP比例 低于/高于/等于
                ApPp => EvaluateApPp,               // AP/PP比例
                Status => EvaluateStatus,           // buff/debuff/异常
                AttackAttribute => EvaluateAttackAttribute,  // 物理/魔法/列/全体
                TeamSize => EvaluateTeamSize,       // 敌/友N体以上/以下
                SelfState => EvaluateSelfState,     // 蓄力/气绝/冻结/黑暗
                SelfHp => EvaluateSelfHp,           // 自身HP比例
                SelfApPp => EvaluateSelfApPp,       // 自身AP值
                EnemyClassExists => EvaluateEnemyClassExists,  // 敌有/无某兵种
                AttributeRank => EvaluateAttributeRank,  // 最高/最低属性
                _ => true
            };
        }

        // TeamSize: Value = "enemy:N" 或 "ally:N"，Operator = greater_than/less_than
        // AttackAttribute: 读取 _ctx.CurrentCalc 判断攻击类型
        // AttributeRank: 扫描全场比较指定属性值
    }

    // ========== TargetSelector — 目标选择 ==========
    public class TargetSelector
    {
        // 4步流程:
        // Step 1: GetDefaultTargetList(caster, skill) — 按攻击类型和位置排序
        // Step 2: ApplyCondition(list, condition1, mode1) — 仅=过滤 / 优先=排序
        // Step 3: ApplyCondition(list, condition2, mode2) — 双优先时条件2优先
        // Step 4: 根据TargetType截取目标数量或按阵型形状扩展

        // lowest/highest 操作符:
        //   Category=Hp → OrderBy(u.CurrentHp) / OrderByDescending
        //   Category=ApPp → OrderBy(u.CurrentAp) / OrderByDescending
        //   Category=AttributeRank → SortByAttributeRank(list, condition, ascending)

        // 默认目标规则:
        //   近战 → 前排优先，前排全灭才能打后排
        //   远程/魔法/飞行/贯通 → 全体按位置排序
        //
        // 2026-05-08 修正:
        //   Self 目标不依赖敌方候选池，直接返回 caster。
        //   Row / Column / FrontAndBack 先选中 anchor，再只在 anchor 所属阵营扩展形状，
        //   禁止从 BattleContext.AllUnits 二次捞目标导致误伤友方。
    }
}
```

---

## 装备系统

```csharp
namespace BattleKing.Equipment
{
    public class EquipmentSlot
    {
        public Equipment MainHand { get; set; }      // 永不为空
        public Equipment OffHand { get; set; }
        public Equipment Accessory1, Accessory2, Accessory3 { get; set; }

        public IEnumerable<Equipment> AllEquipped { get; }

        // 双持规则：剑士/剑圣双持剑时，副手攻击力一半加算
        public int GetTotalStat(string statName) { }
        // 2026-05-08: 兼容装备数据别名:
        //   GetTotalStat("Hit")   同时读取 "Hit" + "hit"
        //   GetTotalStat("Block") 同时读取 "Block" + "block_rate"
        //   双持魔攻键统一为 "mag_atk"，不是旧的 "magic_atk"

        // 装备赋予的技能
        public List<string> GetGrantedActiveSkillIds() { }
        public List<string> GetGrantedPassiveSkillIds() { }

        // 新方法 (2026-05-07)
        public Equipment GetBySlot(string slotName) { }       // 按名称读槽位
        public void Unequip(string slotName) { }              // 清空指定槽位
        public void EquipToSlot(string slotName, EquipmentData data) { }  // 指定槽位装备

        // 静态方法
        public static bool CanEquipCategory(EquipmentCategory cat, CharacterData cd, bool isCc) { }
        public static List<string> GetSlotNames(CharacterData cd, bool isCc) { }
        // 槽位顺序 = equippableCategories顺序: 第一个武器→MainHand, 第二个→OffHand,
        //   Shield/GreatShield→OffHand, Accessory→Accessory1/2/3

        public bool ValidateWeaponEquipped() => MainHand != null;
    }

    public class Buff
    {
        public string SkillId { get; set; }
        public string TargetStat { get; set; }
        public float Ratio { get; set; }         // +0.2 = +20%
        public bool IsOneTime { get; set; }
        public bool IsPureBuffOrDebuff { get; set; }
        public int RemainingTurns { get; set; }  // -1 = 战斗结束前有效
    }

    public static class BuffManager
    {
        // 同名技能纯buff/debuff → 不重复施加
        // 同名效果不同技能 → 可重叠
        public static void ApplyBuff(BattleUnit target, Buff newBuff) { }
        public static void CleanupAfterAction(BattleUnit unit) { }  // 清除一次性buff
        public static void CleanupEndOfTurn(BattleUnit unit) { }    // 减回合数，清除过期
        public static float GetTotalBuffRatio(BattleUnit unit, string statName) { }
    }

    public static class TraitApplier
    {
        public static float ApplyTraitsToDamage(DamageCalculation calc) { }
        public static AttackType ModifyAttackType(AttackType baseType, BattleUnit attacker) { }
    }
}
```

---

## 伤害计算 Pipeline

```csharp
namespace BattleKing.Pipeline
{
    public class DamageCalculation  // 可变中间状态 — 被动技能可在BeforeHitEvent中修改
    {
        public BattleUnit Attacker, Defender;
        public ActiveSkill Skill;

        // 可变字段（被动技能介入点）
        public bool ForceHit { get; set; } = false;
        public bool ForceEvasion { get; set; } = false;
        public bool? ForceBlock { get; set; } = null;
        public float SkillPowerMultiplier { get; set; } = 1.0f;
        public float DamageMultiplier { get; set; } = 1.0f;
        public float IgnoreDefenseRatio { get; set; } = 0f;
        public bool NullifyPhysicalDamage { get; set; } = false;
        public bool NullifyMagicalDamage { get; set; } = false;
        public BattleUnit CoverTarget { get; set; } = null;
        public bool CannotBeCovered { get; set; } = false;
        public bool CannotBeBlocked { get; set; } = false;
        public int HitCount { get; set; } = 1;  // 多段攻击
        public float CounterPowerBonus { get; set; } = 0f;  // 计数器加成

        // 结果字段
        public int PhysicalDamage, MagicalDamage;
        public int TotalDamage => PhysicalDamage + MagicalDamage;
    }

    public class DamageCalculator
    {
        // 主签名 — 接收完整 DamageCalculation
        public DamageResult Calculate(DamageCalculation calc)
        {
            // 攻防读取:
            //   物理: Str + phys_atk vs Def + phys_def
            //   魔法: Mag + mag_atk  vs MDef + mag_def
            // 多段攻击循环: for hit in 0..HitCount
            //   每hit独立判定: 命中→回避→格挡→暴击
            //   累积 float totalPhysical, totalMagical
            //   最后 Math.Round 一次
        }

        // 兼容重载
        public DamageResult Calculate(BattleUnit a, BattleUnit d, ActiveSkill s, BattleContext c)
            => Calculate(new DamageCalculation { Attacker = a, Defender = d, Skill = s, Context = c });
    }

    public class DamageResult
    {
        public int PhysicalDamage, MagicalDamage, TotalDamage;
        public bool IsHit, IsCritical, IsBlocked, IsEvaded;
        public List<StatusAilment> AppliedAilments;
    }
}
```

---

## 事件系统

```csharp
namespace BattleKing.Events
{
    public interface IBattleEvent { }

    public class BattleStartEvent : IBattleEvent    { public BattleContext Context; }
    public class BeforeActiveUseEvent : IBattleEvent { public BattleUnit Unit; public ActiveSkill Skill; public BattleContext Context; }
    public class BeforeHitEvent : IBattleEvent
    {
        public BattleUnit Attacker, Defender;
        public ActiveSkill Skill;
        public BattleContext Context;
        public DamageCalculation Calc;  // 可变引用 — 被动技能可修改
    }
    public class AfterHitEvent : IBattleEvent       { public BattleUnit Attacker, Defender; public DamageResult Result; public BattleContext Context; }
    public class AfterActiveUseEvent : IBattleEvent  { public BattleUnit Unit; public ActiveSkill Skill; public BattleContext Context; }
    public class OnKnockdownEvent : IBattleEvent     { public BattleUnit Victim, Killer; public BattleContext Context; }
    public class BattleEndEvent : IBattleEvent       { public BattleContext Context; }

    public class EventBus
    {
        public void Subscribe<T>(Action<T> handler) where T : IBattleEvent { }
        public void Publish<T>(T evt) where T : IBattleEvent { }
    }
}
```

---

## Main.cs UI 架构

```
GamePhase 枚举: ModeSelect → PlayerFormation → EnemyChoice →
    EnemyDragFormation → EquipmentSetup → PassiveSetup → StrategySetup → Battle → Result

UI 布局 (SetupUi):
  CanvasLayer
    └ VBoxContainer (root)
      ├ Label _statusLabel          (黄色状态栏)
      ├ HBoxContainer _formationArea
      │   ├ VBoxContainer _leftPanel   (左侧: 角色池/装备槽/策略编辑)
      │   ├ VSeparator
      │   └ VBoxContainer _rightPanel  (右侧: 阵型网格/装备详情/策略说明)
      ├ HBoxContainer _buttonBar    (底部按钮: 确认/下一步/上一步)
      └ TextEdit _logLabel          (日志: 战斗中移到右面板)

导航:
  Stack<GamePhase> _phaseHistory    (上一步栈)
  Go(GamePhase p) → 入栈当前阶段, 执行阶段方法, 自动 AddBackBtn()
  GoBack() → 出栈并跳回

关键方法:
  StarStr(current, max) → "★★★☆☆"   (AP/PP 星星显示)
  BuildDragUI(teamLabel, slots, onConfirm)  (拖拽布阵, 含 ScrollContainer)
  BuildConditionRow(parent, unit, slot, isCond1)  (条件编辑器 — 类别/操作符/值级联)

阶段流程:
  Phase_ModeSelect → 1v1/3v3 + Day + CC
  Phase_PlayerFormation → 拖拽布阵
  Phase_EnemyChoice → 预设/自定义敌方
  Phase_EquipmentSetup → 装备配置（每角色独立，槽位下拉+属性详情）
  Phase_PassiveSetup → 被动选择（每个被动可设发动条件）
  Phase_StrategySetup → 策略编程（我方→敌方，8条×条件1+条件2+仅/优先）
  Phase_Battle → 逐步战斗（StepOneAction + 实时状态刷新）
  Phase_Result → 再来一局/结束
```

---

## 数据流

```
启动:
  GameDataRepository.LoadAll("res://data/")
    → 加载6个JSON文件 → Dictionary<string, T>

战斗创建:
  Main.CreateAllUnits()
    → CharacterData → new BattleUnit()
    → DayProgression.Apply(unit, day) → 设置 CurrentLevel
    → unit.SetCcState(isCc) → 切换装备槽+兵种+技能池
    → InitialEquipmentIds → Equipment.Equip()
    → 自动装备 Level≤1 的被动

策略编程:
  ShowStrategy()
    → BuildConditionRow() × 2 per slot
    → Category → cascades to Operator → cascades to Value → ☐仅
    → ConditionMeta.BuildCondition() → Condition对象
    → 存入 Strategy.Condition1/Condition2

战斗执行:
  BattleEngine.StepOneAction()
    → BeforeHitEvent { Calc = mutable DamageCalculation }
    → PassiveSkillProcessor 修改 Calc (ForceHit/ForceBlock/NullifyDamage...)
    → DamageCalculator.Calculate(calc)  — 多段攻击每hit独立判定
    → 应用伤害 + buff/debuff/异常
    → AfterActiveUseEvent → 行动后被动
    → ProcessPendingActions() → 反击/追击/先制
```

---

## 关键设计决策

1. **TryGetValue 而非直接索引**: 所有 GameDataRepository.Get*() 方法返回 null 而不抛异常
2. **Condition.Value 是 object**: System.Text.Json 反序列化数字为 JsonElement，需要 ToFloat()/ToInt() 辅助
3. **ConditionMode 默认 Priority**: Strategy 的条件默认 Mode=Priority（不满足仍发动），用户勾选"仅"后变为 Only
4. **Buff.RemainingTurns = -1 = 永久**: 战斗结束前有效的buff用-1表示
5. **BuffManager 是静态类**: 无状态，直接操作 BattleUnit.Buffs 列表
6. **被动条件存储在 BattleUnit.PassiveConditions**: Dictionary<skillId, Condition>，由 PassiveSkillProcessor 触发前检查
7. **装备槽顺序 = equippableCategories 顺序**: GetSlotNames 按顺序解析出 MainHand/OffHand/Accessory1-3
8. **AP/PP 显示为星星**: StarStr(n, max) → ★★☆☆☆（红色AP，蓝色PP）
