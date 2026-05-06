# 战旗之王 — C# 技术架构文档

> 本文档记录 C# 侧的类设计、接口定义、枚举、方法签名和数据流。
> 与 `CLAUDE.md`（概念框架）互补：CLAUDE 讲"是什么"，本文档讲"怎么写"。

---

## 目录结构

```
src/
  Data/              # JSON 反序列化类（纯数据，无行为）
    Models/
      CharacterData.cs
      ActiveSkillData.cs
      PassiveSkillData.cs
      EquipmentData.cs
      ConditionData.cs
    GameDataRepository.cs
  Core/              # 战斗运行时核心
    BattleUnit.cs
    BattleContext.cs
    BattleEngine.cs
    TurnManager.cs
    SimultaneousActivationLimiter.cs
  Skills/            # 技能定义与执行
    ActiveSkill.cs
    PassiveSkill.cs
    ISkillEffect.cs
    SkillEffectFactory.cs
    Effects/           # 具体效果实现
      DamageEffect.cs
      BuffEffect.cs
      HealEffect.cs
      StatusAilmentEffect.cs
  Ai/                # 策略编程
    Strategy.cs
    StrategyEvaluator.cs
    ConditionEvaluator.cs
    TargetSelector.cs
  Equipment/         # 装备与 Buff
    EquipmentSlot.cs
    EquipmentData.cs
    BuffManager.cs
    TraitApplier.cs
    ITrait.cs
  Pipeline/          # 伤害计算流水线
    DamageCalculator.cs
    DamageCalculation.cs
    DamageResult.cs
  Events/            # 事件总线
    EventBus.cs
    IBattleEvent.cs
    BattleEvents/      # 具体事件定义
      BeforeAttackEvent.cs
      AfterAttackEvent.cs
      OnBattleStartEvent.cs
      OnBattleEndEvent.cs
      OnActionStartEvent.cs
      OnActionEndEvent.cs
  Utils/             # 工具
    JsonLoader.cs
    RandUtil.cs
```

---

## 枚举定义

```csharp
namespace BattleKing.Data
{
    public enum UnitClass
    {
        Infantry,    // 步兵
        Cavalry,     // 骑兵/骑马
        Flying,      // 飞行
        Heavy,       // 重装
        Scout,       // 斥候
        Archer,      // 弓兵
        Mage,        // 术士
        Elf,         // 精灵
        Beastman,    // 兽人
        Winged       // 有翼人
    }

    public enum EquipmentCategory
    {
        Sword,        // 剑（可双持）
        Axe,          // 斧
        Spear,        // 枪（前后列贯通）
        Bow,          // 弓（遠隔）
        Staff,        // 杖（魔法攻击）
        Shield,       // 盾（格挡+25%减轻）
        GreatShield,  // 大盾（格挡+50%减轻）
        Accessory     // 饰品（全职业）
    }

    public enum SkillType
    {
        Physical,   // 物理
        Magical,    // 魔法
        Assist,     // 辅助
        Heal,       // 回复
        Debuff      // 妨害
    }

    public enum AttackType
    {
        Melee,      // 近接物理（只能打前排）
        Ranged,     // 遠隔物理（可打后排）
        Magic       // 魔法攻击（可打后排）
    }

    public enum TargetType
    {
        Self,
        SingleEnemy,
        SingleAlly,
        TwoEnemies,
        ThreeEnemies,
        FrontAndBack,   // 前后列贯通
        Column,         // 一列
        Row,            // 一排
        AllEnemies,
        AllAllies
    }

    public enum ConditionCategory
    {
        Position,          // 队列·状况（前排/后排/前后列/人数）
        UnitClass,         // 兵种
        Hp,                // HP
        ApPp,              // AP·PP
        Status,            // 状态（buff/debuff/异常）
        AttackAttribute,   // 攻击属性
        TeamSize,          // 编成人数
        SelfState,         // 自身状态（第N次行动等）
        SelfHp,            // 自身HP
        SelfApPp,          // 自身AP·PP
        EnemyClassExists,  // 敌兵种存在
        AttributeRank      // 最高/最低属性
    }

    public enum ConditionMode
    {
        Only,       // 仅：条件不满足则跳过
        Priority    // 优先：条件不满足仍发动，按默认目标
    }

    public enum PassiveTriggerTiming
    {
        BattleStart,           // 战斗开始时
        SelfBeforeAttack,      // 自身攻击前（主动）
        AllyBeforeAttack,      // 友方攻击前（主动）
        AllyBeforeHit,         // 友方被攻击前
        SelfBeforeHit,         // 自身被攻击直前
        SelfBeforeMeleeHit,    // 自身被近接攻击直前（招架等）
        SelfBeforePhysicalHit, // 自身被物理攻击直前（格挡/复仇守护等）
        AllyOnAttacked,        // 友方被攻击时（追击斩等）
        SelfOnActiveUse,       // 自身使用主动技能时（蓄力行动/蛮力等）
        AllyOnActiveUse,       // 友方使用主动技能时（主动礼物等）
        AfterAction,           // 行动后（敌我双方）
        BattleEnd,             // 战斗结束时
        OnHit,                 // 命中时
        OnBeingHit,            // 被命中时
        OnKnockdown            // 击倒时
    }

    public enum SimultaneousLimitType
    {
        BattleStart,           // 战斗开始时（敌我各1人）
        AllyBuffBeforeAction,  // 友方强化（同部队1人）
        EnemyDefendBeforeAction,// 防守方防御（对方部队1人）
        AfterActionCorrespond, // 行动后对应（敌我各1人）
        None                   // 无限制
    }

    public enum UnitState
    {
        Normal,     // 正常
        Charging,   // 蓄力中（回避和被动不可用）
        Stunned,    // 气绝（跳过一次行动）
        Frozen,     // 冻结（无法行动，受击解除，回避率=0）
        Darkness,   // 黑暗（下次攻击命中率0）
        BlockSeal,  // 格挡封印
        CritSeal    // 无法暴击
    }

    public enum StatusAilment
    {
        Poison,     // 毒：下次行动损失最大HP30%
        Burn,       // 炎上：固定20HP，每层多结算一次
        Freeze,     // 冻结：无法行动，受击解除，回避率=0
        Darkness,   // 黑暗：下次攻击命中率0
        Stun,       // 气绝：跳过一次行动
        BlockSeal,  // 格挡封印
        CritSeal    // 无法暴击
    }

    public enum BlockType
    {
        None,       // 无格挡
        Small,      // 格挡(小) -25%（无盾或装备Shield时）
        Medium,     // 格挡(中) -50%（装备GreatShield时）
        Large       // 格挡(大) -75%（预留：特定技能/被动可提升至此）
    }
}
```

---

## 数据层（JSON 反序列化）

```csharp
namespace BattleKing.Data
{
    // ==================== CharacterData ====================
    public class CharacterData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<UnitClass> Classes { get; set; } = new();      // 多兵种标签
        public List<EquipmentCategory> EquippableCategories { get; set; } = new();  // 未CC可装备类型
        public List<string> InnateActiveSkillIds { get; set; } = new();   // 基础主动技能
        public List<string> InnatePassiveSkillIds { get; set; } = new();  // 基础被动技能
        public List<string> InnateValorSkillIds { get; set; } = new();
        public Dictionary<string, int> BaseStats { get; set; } = new();  // HP,Str,Def,Mag,MDef,Hit,Eva,Crit,Block,Spd,AP,PP
        public string GrowthType { get; set; }  // HP型/力量型/防御型...
        public List<TraitData> Traits { get; set; } = new();
        public List<string> InitialEquipmentIds { get; set; } = new();    // 未CC初始装备

        // CC (Class Change) 数据
        public string CcClassId { get; set; }
        public string CcName { get; set; }
        public List<EquipmentCategory> CcEquippableCategories { get; set; } = new();  // CC后可装备类型
        public List<string> CcInnateActiveSkillIds { get; set; } = new();   // CC新增主动技能
        public List<string> CcInnatePassiveSkillIds { get; set; } = new();  // CC新增被动技能
        public List<string> CcInitialEquipmentIds { get; set; } = new();    // CC后初始装备（覆盖新增/变化槽位）
    }

    // ==================== ActiveSkillData ====================
    public class ActiveSkillData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int ApCost { get; set; }
        public SkillType Type { get; set; }         // 物/魔/辅助/回复/妨害
        public AttackType AttackType { get; set; }  // 近接/遠隔/魔法
        public int Power { get; set; }              // 技能威力（100=×1.0）
        public int? HitRate { get; set; }           // 100=100%，null=必中
        public TargetType TargetType { get; set; }
        public string EffectDescription { get; set; }
        public List<string> Tags { get; set; } = new();  // 遠隔/同時発動制限/ガード不可...
        public List<SkillEffectData> Effects { get; set; } = new();
        public string LearnCondition { get; set; }   // "领主 Lv1" 或 "毒蛇剑"（显示用）
        public int? UnlockLevel { get; set; }        // 解锁等级（null=无等级限制，如装备赋予技能）
    }

    // ==================== PassiveSkillData ====================
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
        public string LearnCondition { get; set; }   // 显示用
        public int? UnlockLevel { get; set; }        // 解锁等级（null=无等级限制）
    }

    // ==================== EquipmentData ====================
    public class EquipmentData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public EquipmentCategory Category { get; set; }
        public Dictionary<string, int> BaseStats { get; set; } = new();  // phys_atk, magic_atk, phys_def, block_rate...
        public List<string> GrantedActiveSkillIds { get; set; } = new();
        public List<string> GrantedPassiveSkillIds { get; set; } = new();
        public List<UnitClass> UsableByClasses { get; set; } = new();    // 为空表示全职业（饰品）
        public List<string> RestrictedClassIds { get; set; } = new();    // 额外职业限制
        public List<string> SpecialEffects { get; set; } = new();        // 毒无效/炎上无效/黑暗无效...
    }

    // ==================== SkillEffectData ====================
    public class SkillEffectData
    {
        public string EffectType { get; set; }   // "Damage", "Buff", "Heal", "StatusAilment"
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    // ==================== ConditionData ====================
    public class ConditionData
    {
        public ConditionCategory Category { get; set; }
        public string Operator { get; set; }     // equals, less_than, greater_than, contains
        public object Value { get; set; }
    }

    // ==================== TraitData ====================
    public class TraitData
    {
        public string TraitType { get; set; }    // "BowVsFlying", "CavalryVsInfantry", "AddRangedToMelee"...
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    // ==================== GameDataRepository ====================
    public class GameDataRepository
    {
        public Dictionary<string, CharacterData> Characters { get; private set; }
        public Dictionary<string, ActiveSkillData> ActiveSkills { get; private set; }
        public Dictionary<string, PassiveSkillData> PassiveSkills { get; private set; }
        public Dictionary<string, EquipmentData> Equipments { get; private set; }

        public void LoadAll(string dataPath) { /* 一次性加载所有JSON */ }
        public CharacterData GetCharacter(string id) => Characters[id];
        public ActiveSkillData GetActiveSkill(string id) => ActiveSkills[id];
        public PassiveSkillData GetPassiveSkill(string id) => PassiveSkills[id];
        public EquipmentData GetEquipment(string id) => Equipments[id];
    }
}
```

---

## 核心运行时

```csharp
namespace BattleKing.Core
{
    // ==================== BattleUnit ====================
    public class BattleUnit
    {
        // 只读模板引用
        public CharacterData Data { get; private set; }
        public GameDataRepository GameData { get; private set; }

        // 运行时可变状态
        public bool IsCc { get; private set; }      // CC状态标志
        public int CurrentLevel { get; set; } = 1;  // 天数系统映射的等效等级
        public void SetCcState(bool isCc) { IsCc = isCc; }
        public int CurrentHp { get; set; }
        public int CurrentAp { get; set; }
        public int CurrentPp { get; set; }
        public int MaxAp { get; private set; }      // 基础+装备
        public int MaxPp { get; private set; }      // 基础+装备
        public int Position { get; set; }           // 1-6
        public bool IsFrontRow => Position <= 3;
        public bool IsAlive => CurrentHp > 0;
        public UnitState State { get; set; } = UnitState.Normal;
        public int ActionCount { get; set; } = 0;   // 第N次行动计数
        public int ConsecutiveWaitCount { get; set; } = 0;

        // 运行时集合
        public EquipmentSlot Equipment { get; private set; } = new();
        public List<Buff> Buffs { get; private set; } = new();
        public List<StatusAilment> Ailments { get; private set; } = new();
        public List<Strategy> Strategies { get; set; } = new();  // 8条策略

        public BattleUnit(CharacterData data, GameDataRepository gameData, bool isPlayer, bool isCc = false)
        {
            Data = data;
            GameData = gameData;
            IsPlayer = isPlayer;
            IsCc = isCc;
            CurrentHp = data.BaseStats.GetValueOrDefault("HP", 0);
            CurrentAp = data.BaseStats.GetValueOrDefault("AP", 2);
            CurrentPp = data.BaseStats.GetValueOrDefault("PP", 1);
        }

        // 根据CC状态返回可装备类型
        public List<EquipmentCategory> GetEquippableCategories()
        {
            return IsCc && Data.CcEquippableCategories != null && Data.CcEquippableCategories.Count > 0
                ? Data.CcEquippableCategories
                : Data.EquippableCategories;
        }

        // 动态属性计算（基础+装备+buff）
        public int GetCurrentStat(string statName)
        {
            int baseValue = Data.BaseStats.GetValueOrDefault(statName, 0);
            int equipValue = Equipment.GetTotalStat(statName);
            float buffRatio = Buffs.Where(b => b.TargetStat == statName).Sum(b => b.Ratio);
            return (int)((baseValue + equipValue) * (1 + buffRatio));
        }

        public int GetCurrentAttackPower(SkillType damageType)
        {
            string stat = damageType == SkillType.Magical ? "Mag" : "Str";
            return GetCurrentStat(stat) + Equipment.GetTotalStat("phys_atk"); // 简化版，实际需按damageType区分
        }

        public int GetCurrentDefense(SkillType damageType)
        {
            string stat = damageType == SkillType.Magical ? "MDef" : "Def";
            return GetCurrentStat(stat) + Equipment.GetTotalStat("phys_def");
        }

        public int GetCurrentSpeed() => GetCurrentStat("Spd");
        public int GetCurrentHitRate() => GetCurrentStat("Hit");
        public int GetCurrentEvasion() => GetCurrentStat("Eva");
        public int GetCurrentCritRate() => GetCurrentStat("Crit");
        public int GetCurrentBlockRate() => GetCurrentStat("Block");

        // 可用主动技能池 = 基础 + CC新增（按CurrentLevel过滤） + 装备赋予
        public List<string> GetAvailableActiveSkillIds()
        {
            var ids = new List<string>(Data.InnateActiveSkillIds);
            if (IsCc && Data.CcInnateActiveSkillIds != null)
                ids.AddRange(Data.CcInnateActiveSkillIds);

            ids = ids.Where(id =>
            {
                var skill = GameData.GetActiveSkill(id);
                return skill?.UnlockLevel == null || skill.UnlockLevel <= CurrentLevel;
            }).ToList();

            ids.AddRange(Equipment.GetGrantedActiveSkillIds());
            return ids.Distinct().ToList();
        }

        // 可用被动技能池 = 基础 + CC新增（按CurrentLevel过滤） + 装备赋予
        public List<string> GetAvailablePassiveSkillIds()
        {
            var ids = new List<string>(Data.InnatePassiveSkillIds);
            if (IsCc && Data.CcInnatePassiveSkillIds != null)
                ids.AddRange(Data.CcInnatePassiveSkillIds);

            ids = ids.Where(id =>
            {
                var skill = GameData.GetPassiveSkill(id);
                return skill?.UnlockLevel == null || skill.UnlockLevel <= CurrentLevel;
            }).ToList();

            ids.AddRange(Equipment.GetGrantedPassiveSkillIds());
            return ids.Distinct().ToList();
        }

        public bool CanUseActiveSkill(ActiveSkill skill) => CurrentAp >= skill.Data.ApCost;
        public bool CanUsePassiveSkill(PassiveSkill skill) => CurrentPp >= skill.Data.PpCost && State != UnitState.Charging;

        public void ConsumeAp(int amount) => CurrentAp = Math.Max(0, CurrentAp - amount);
        public void ConsumePp(int amount) => CurrentPp = Math.Max(0, CurrentPp - amount);
        public void RecoverAp(int amount) => CurrentAp = Math.Min(MaxAp, CurrentAp + amount);
        public void RecoverPp(int amount) => CurrentPp = Math.Min(MaxPp, CurrentPp + amount);

        public void TakeDamage(int damage)
        {
            CurrentHp = Math.Max(0, CurrentHp - damage);
            if (CurrentHp == 0) { /* 触发击倒事件 */ }
        }
    }

    // ==================== BattleContext ====================
    public class BattleContext
    {
        public List<BattleUnit> PlayerUnits { get; set; } = new();   // 6人
        public List<BattleUnit> EnemyUnits { get; set; } = new();    // 6人
        public int TurnCount { get; set; } = 0;
        public bool IsDaytime { get; set; } = true;  // 昼夜系统（影响部分条件）

        public List<BattleUnit> AllUnits => PlayerUnits.Concat(EnemyUnits).ToList();
        public List<BattleUnit> GetAliveUnits(bool isPlayer) => (isPlayer ? PlayerUnits : EnemyUnits).Where(u => u.IsAlive).ToList();
        public BattleUnit GetUnitAtPosition(bool isPlayer, int position) => (isPlayer ? PlayerUnits : EnemyUnits).FirstOrDefault(u => u.Position == position);

        // 用于条件判定的全场扫描
        public bool HasEnemyClass(UnitClass unitClass) => EnemyUnits.Any(u => u.Data.Classes.Contains(unitClass));
        public int GetAliveCount(bool isPlayer) => GetAliveUnits(isPlayer).Count;
    }

    // ==================== BattleEngine ====================
    public class BattleEngine
    {
        private BattleContext _ctx;
        private EventBus _eventBus;
        private SimultaneousActivationLimiter _limiter;
        private DamageCalculator _damageCalculator;
        private StrategyEvaluator _strategyEvaluator;

        public BattleEngine(BattleContext ctx, EventBus eventBus)
        {
            _ctx = ctx;
            _eventBus = eventBus;
            _limiter = new SimultaneousActivationLimiter();
            _damageCalculator = new DamageCalculator();
            _strategyEvaluator = new StrategyEvaluator(ctx);
        }

        public BattleResult RunBattle()
        {
            // 阶段1：战斗开始被动
            Phase_BattleStart();

            // 阶段2：行动循环
            while (!IsBattleOver())
            {
                Phase_ActionLoop();
                _ctx.TurnCount++;
            }

            // 阶段3：战斗结束
            Phase_BattleEnd();

            return JudgeWinner();
        }

        private void Phase_BattleStart()
        {
            var allUnits = _ctx.AllUnits.Where(u => u.IsAlive).OrderByDescending(u => u.GetCurrentSpeed());
            foreach (var unit in allUnits)
            {
                var passives = GetTriggerablePassives(unit, PassiveTriggerTiming.BattleStart);
                foreach (var passive in passives)
                {
                    if (!_limiter.CanTrigger(unit, SimultaneousLimitType.BattleStart, unit.IsPlayer))
                        break;
                    ExecutePassive(unit, passive, null);
                }
            }
        }

        private void Phase_ActionLoop()
        {
            var actionOrder = _ctx.AllUnits
                .Where(u => u.IsAlive && u.State != UnitState.Stunned && u.State != UnitState.Frozen)
                .OrderByDescending(u => u.GetCurrentSpeed())
                .ThenBy(u => u.Position)
                .ToList();

            foreach (var unit in actionOrder)
            {
                if (!unit.IsAlive) continue;

                // 1. 尝试发动主动技能
                var (skill, targets) = _strategyEvaluator.Evaluate(unit);
                if (skill != null && targets != null && targets.Count > 0)
                {
                    ExecuteActiveSkill(unit, skill, targets);
                }
                else
                {
                    unit.ConsecutiveWaitCount++;
                }
            }
        }

        private void ExecuteActiveSkill(BattleUnit attacker, ActiveSkill skill, List<BattleUnit> targets)
        {
            attacker.ActionCount++;
            attacker.ConsecutiveWaitCount = 0;

            // 攻击方被动（自身强化 + 友方最快1人）
            TriggerAttackerPassives(attacker, skill);

            // 防守方被动（对方最快1人防御 + 格挡/回避）
            foreach (var defender in targets)
                TriggerDefenderPassives(attacker, defender, skill);

            // 执行技能
            var results = new List<DamageResult>();
            foreach (var target in targets)
            {
                var result = _damageCalculator.Calculate(attacker, target, skill, _ctx);
                results.Add(result);
                target.TakeDamage(result.TotalDamage);
            }

            // 行动后被动
            TriggerAfterActionPassives(attacker, targets, skill, results);

            _eventBus.Publish(new OnActionEndEvent(attacker, skill, targets, results));
        }

        private bool IsBattleOver()
        {
            bool allPlayerDead = _ctx.PlayerUnits.All(u => !u.IsAlive);
            bool allEnemyDead = _ctx.EnemyUnits.All(u => !u.IsAlive);
            bool allPlayerApDepleted = _ctx.PlayerUnits.All(u => u.CurrentAp <= 0);
            bool allEnemyApDepleted = _ctx.EnemyUnits.All(u => u.CurrentAp <= 0);

            // 如果一方全死，但另一方还有恢复技能...需要一轮确认
            return (allPlayerDead && allEnemyDead) || (allPlayerApDepleted && allEnemyApDepleted);
        }

        private BattleResult JudgeWinner()
        {
            float playerHpRatio = _ctx.PlayerUnits.Sum(u => (float)u.CurrentHp) / _ctx.PlayerUnits.Sum(u => u.Data.BaseStats.GetValueOrDefault("HP", 1));
            float enemyHpRatio = _ctx.EnemyUnits.Sum(u => (float)u.CurrentHp) / _ctx.EnemyUnits.Sum(u => u.Data.BaseStats.GetValueOrDefault("HP", 1));
            return playerHpRatio > enemyHpRatio ? BattleResult.PlayerWin : BattleResult.EnemyWin;
        }

        // 被动触发辅助方法（省略具体实现）
        private List<PassiveSkill> GetTriggerablePassives(BattleUnit unit, PassiveTriggerTiming timing) { throw new NotImplementedException(); }
        private void ExecutePassive(BattleUnit caster, PassiveSkill passive, BattleUnit target) { }
        private void TriggerAttackerPassives(BattleUnit attacker, ActiveSkill skill) { }
        private void TriggerDefenderPassives(BattleUnit attacker, BattleUnit defender, ActiveSkill skill) { }
        private void TriggerAfterActionPassives(BattleUnit attacker, List<BattleUnit> targets, ActiveSkill skill, List<DamageResult> results) { }
    }

    public enum BattleResult { PlayerWin, EnemyWin, Draw }
}
```

---

## 技能系统

```csharp
namespace BattleKing.Skills
{
    // ==================== ActiveSkill（运行时包装）====================
    public class ActiveSkill
    {
        public ActiveSkillData Data { get; private set; }
        public List<ISkillEffect> Effects { get; private set; } = new();

        public int ApCost => Data.ApCost;
        public SkillType Type => Data.Type;
        public AttackType AttackType => Data.AttackType;
        public int Power => Data.Power;
        public TargetType TargetType => Data.TargetType;
        public bool HasPhysicalComponent => Data.Type == SkillType.Physical;
        public bool HasMixedDamage => /* 根据Data判断 */ false;

        public ActiveSkill(ActiveSkillData data, GameDataRepository gameData)
        {
            Data = data;
            Effects = SkillEffectFactory.CreateEffects(data.Effects);
        }
    }

    // ==================== PassiveSkill（运行时包装）====================
    public class PassiveSkill
    {
        public PassiveSkillData Data { get; private set; }
        public List<ISkillEffect> Effects { get; private set; } = new();

        public int PpCost => Data.PpCost;
        public PassiveTriggerTiming TriggerTiming => Data.TriggerTiming;
        public bool HasSimultaneousLimit => Data.HasSimultaneousLimit;

        public PassiveSkill(PassiveSkillData data, GameDataRepository gameData)
        {
            Data = data;
            Effects = SkillEffectFactory.CreateEffects(data.Effects);
        }
    }

    // ==================== ISkillEffect ====================
    public interface ISkillEffect
    {
        void Apply(BattleContext ctx, BattleUnit caster, List<BattleUnit> targets, DamageCalculation calc = null);
    }

    // ==================== SkillEffectFactory ====================
    //
    // TWO-PATH ARCHITECTURE for skill effects:
    //
    // Path A — ISkillEffect (generic, context-free):
    //   DamageEffect / BuffEffect / HealEffect / StatusAilmentEffect
    //   Executed via ISkillEffect.Apply(ctx, caster, targets, calc)
    //   Used by: active skill execution in BattleEngine
    //
    // Path B — PassiveSkillProcessor.ExecuteStructuredEffect() (phase-aware):
    //   ModifyDamageCalc / CounterAttack / TemporalMark / CoverAlly /
    //   ModifyCounter / ConsumeCounter / PreemptiveAttack / PursuitAttack /
    //   RecoverAp / RecoverPp / RecoverHp / AddBuff / AddDebuff / etc.
    //   Executed directly by PassiveSkillProcessor with full battle-phase context
    //   (DamageCalculation before a hit, PendingActionQueue, TemporalState, CustomCounters)
    //   These create PassiveOnlyEffect in the factory — Apply() is a no-op.
    //
    // WHY TWO PATHS: Passive skill effects are not fire-and-forget. They need to
    // intercept the damage pipeline at specific timings, queue extra actions, manage
    // temporary state, and read/write custom counters. ISkillEffect.Apply() cannot
    // provide this context. Rather than bloating the interface, passive effects are
    // dispatched in PassiveSkillProcessor where all context is naturally available.
    //
    public static class SkillEffectFactory
    {
        public static List<ISkillEffect> CreateEffects(List<SkillEffectData> effectDatas)
        {
            var effects = new List<ISkillEffect>();
            foreach (var data in effectDatas)
            {
                effects.Add(data.EffectType switch
                {
                    "Damage" => new DamageEffect(data.Parameters),
                    "Buff" => new BuffEffect(data.Parameters),
                    "Heal" => new HealEffect(data.Parameters),
                    "StatusAilment" => new StatusAilmentEffect(data.Parameters),
                    // All other effect types → PassiveOnlyEffect (handled by PassiveSkillProcessor)
                    _ => new PassiveOnlyEffect(data.EffectType)
                });
            }
            return effects;
        }
    }

    // Sentinel: effects dispatched by PassiveSkillProcessor, not via ISkillEffect
    public class PassiveOnlyEffect : ISkillEffect
    {
        public readonly string EffectType;
        public PassiveOnlyEffect(string et) => EffectType = et;
        public void Apply(BattleContext ctx, BattleUnit caster, List<BattleUnit> targets, DamageCalculation calc = null) { }
    }
}
```

---

## AI / 策略系统

```csharp
namespace BattleKing.Ai
{
    // ==================== Strategy ====================
    public class Strategy
    {
        public string SkillId { get; set; }
        public Condition Condition1 { get; set; }
        public Condition Condition2 { get; set; }
        public ConditionMode Mode1 { get; set; }
        public ConditionMode Mode2 { get; set; }
    }

    // ==================== Condition ====================
    public class Condition
    {
        public ConditionCategory Category { get; set; }
        public string Operator { get; set; }
        public object Value { get; set; }
    }

    // ==================== StrategyEvaluator ====================
    public class StrategyEvaluator
    {
        private BattleContext _ctx;
        private ConditionEvaluator _conditionEvaluator;
        private TargetSelector _targetSelector;

        public StrategyEvaluator(BattleContext ctx)
        {
            _ctx = ctx;
            _conditionEvaluator = new ConditionEvaluator(ctx);
            _targetSelector = new TargetSelector(ctx);
        }

        // 从上到下评估8条策略，返回第一个满足条件的（技能，目标列表）
        public (ActiveSkill, List<BattleUnit>) Evaluate(BattleUnit unit)
        {
            var availableSkillIds = unit.GetAvailableActiveSkillIds();

            foreach (var strategy in unit.Strategies)
            {
                if (!availableSkillIds.Contains(strategy.SkillId)) continue;

                var skillData = _ctx.GameData.GetActiveSkill(strategy.SkillId);
                if (!unit.CanUseActiveSkill(new ActiveSkill(skillData, _ctx.GameData))) continue;

                var targets = _targetSelector.SelectTargets(unit, strategy, skillData);
                if (targets != null && targets.Count > 0)
                    return (new ActiveSkill(skillData, _ctx.GameData), targets);
            }

            return (null, null); // 全部不满足 → 待机
        }
    }

    // ==================== ConditionEvaluator ====================
    public class ConditionEvaluator
    {
        private BattleContext _ctx;

        public ConditionEvaluator(BattleContext ctx) => _ctx = ctx;

        public bool Evaluate(Condition condition, BattleUnit subject, BattleUnit target = null)
        {
            return condition.Category switch
            {
                ConditionCategory.Position => EvaluatePosition(condition, subject, target),
                ConditionCategory.UnitClass => EvaluateUnitClass(condition, subject, target),
                ConditionCategory.Hp => EvaluateHp(condition, subject, target),
                ConditionCategory.ApPp => EvaluateApPp(condition, subject, target),
                ConditionCategory.Status => EvaluateStatus(condition, subject, target),
                ConditionCategory.SelfState => EvaluateSelfState(condition, subject),
                ConditionCategory.EnemyClassExists => EvaluateEnemyClassExists(condition),
                ConditionCategory.AttributeRank => EvaluateAttributeRank(condition, subject, target),
                _ => true
            };
        }

        private bool EvaluatePosition(Condition c, BattleUnit subject, BattleUnit target) { throw new NotImplementedException(); }
        private bool EvaluateUnitClass(Condition c, BattleUnit subject, BattleUnit target) { throw new NotImplementedException(); }
        private bool EvaluateHp(Condition c, BattleUnit subject, BattleUnit target) { throw new NotImplementedException(); }
        private bool EvaluateApPp(Condition c, BattleUnit subject, BattleUnit target) { throw new NotImplementedException(); }
        private bool EvaluateStatus(Condition c, BattleUnit subject, BattleUnit target) { throw new NotImplementedException(); }
        private bool EvaluateSelfState(Condition c, BattleUnit subject) { throw new NotImplementedException(); }
        private bool EvaluateEnemyClassExists(Condition c) { throw new NotImplementedException(); }
        private bool EvaluateAttributeRank(Condition c, BattleUnit subject, BattleUnit target) { throw new NotImplementedException(); }
    }

    // ==================== TargetSelector ====================
    public class TargetSelector
    {
        private BattleContext _ctx;

        public TargetSelector(BattleContext ctx) => _ctx = ctx;

        // 目标选择流程：
        // Step 1: 生成默认目标列表（前排优先，距离近优先）
        // Step 2: 应用条件1（仅=过滤，优先=重新排序）
        // Step 3: 应用条件2（同上，双优先时条件2优先）
        // Step 4: 选择列表第一个；列表为空则策略不发动
        public List<BattleUnit> SelectTargets(BattleUnit caster, Strategy strategy, ActiveSkillData skill)
        {
            var candidates = GetDefaultTargetList(caster, skill);

            candidates = ApplyCondition(candidates, strategy.Condition1, strategy.Mode1, caster);
            candidates = ApplyCondition(candidates, strategy.Condition2, strategy.Mode2, caster);

            // 优先前排/优先后排：无论放在哪里都最优先执行
            candidates = ApplyPositionPriority(candidates, strategy);

            if (candidates.Count == 0) return null;

            // 根据TargetType确定最终目标数量
            return skill.TargetType switch
            {
                TargetType.SingleEnemy => new List<BattleUnit> { candidates.First() },
                TargetType.SingleAlly => new List<BattleUnit> { candidates.First() },
                TargetType.Column => GetColumnTargets(candidates.First()),
                TargetType.AllEnemies => candidates.ToList(),
                _ => new List<BattleUnit> { candidates.First() }
            };
        }

        private List<BattleUnit> GetDefaultTargetList(BattleUnit caster, ActiveSkillData skill)
        {
            var enemies = caster.IsPlayer ? _ctx.EnemyUnits : _ctx.PlayerUnits;
            var aliveEnemies = enemies.Where(u => u.IsAlive).ToList();

            // 近战物理：前排有敌人时只以前排为目标
            if (skill.AttackType == AttackType.Melee)
            {
                var frontRow = aliveEnemies.Where(u => u.IsFrontRow).ToList();
                if (frontRow.Count > 0) aliveEnemies = frontRow;
            }

            // 默认排序：前排中央 → 前排两侧 → 后排正面 → 后排两侧
            return aliveEnemies.OrderBy(u => u.Position).ToList();
        }

        private List<BattleUnit> ApplyCondition(List<BattleUnit> list, Condition condition, ConditionMode mode, BattleUnit caster)
        {
            if (condition == null) return list;
            // 仅=过滤，优先=重新排序
            // 具体实现省略...
            return list;
        }

        private List<BattleUnit> ApplyPositionPriority(List<BattleUnit> list, Strategy strategy) { return list; }
        private List<BattleUnit> GetColumnTargets(BattleUnit first) { return _ctx.AllUnits.Where(u => u.Position == first.Position).ToList(); }
    }
}
```

---

## 装备系统

```csharp
namespace BattleKing.Equipment
{
    // ==================== EquipmentSlot ====================
    public class EquipmentSlot
    {
        public Equipment MainHand { get; set; }      // 武器槽：永远不能为空
        public Equipment OffHand { get; set; }       // 副手：盾/副手武器/饰品
        public Equipment Accessory1 { get; set; }
        public Equipment Accessory2 { get; set; }
        public Equipment Accessory3 { get; set; }

        public IEnumerable<Equipment> AllEquipped => new[] { MainHand, OffHand, Accessory1, Accessory2, Accessory3 }.Where(e => e != null);

        public int GetTotalStat(string statName)
        {
            int total = AllEquipped.Sum(e => e.Data.BaseStats.GetValueOrDefault(statName, 0));

            // 双持规则：剑士/剑圣双持剑时，副手攻击的一半加算
            if (CanDualWield() && MainHand?.Data.Category == EquipmentCategory.Sword && OffHand?.Data.Category == EquipmentCategory.Sword)
            {
                int mainAtk = MainHand.Data.BaseStats.GetValueOrDefault(statName, 0);
                int offAtk = OffHand.Data.BaseStats.GetValueOrDefault(statName, 0);
                if (statName == "phys_atk" || statName == "magic_atk")
                {
                    total = mainAtk + offAtk / 2;
                }
            }

            return total;
        }

        public List<string> GetGrantedActiveSkillIds() => AllEquipped.SelectMany(e => e.Data.GrantedActiveSkillIds).ToList();
        public List<string> GetGrantedPassiveSkillIds() => AllEquipped.SelectMany(e => e.Data.GrantedPassiveSkillIds).ToList();

        // 装备放入逻辑：武器优先MainHand，已有则放OffHand（支持双持）；盾放OffHand；饰品依次填充
        public void Equip(EquipmentData data)
        {
            var equipment = new Equipment(data);
            switch (data.Category)
            {
                case EquipmentCategory.Sword:
                case EquipmentCategory.Axe:
                case EquipmentCategory.Spear:
                case EquipmentCategory.Bow:
                case EquipmentCategory.Staff:
                    if (MainHand == null)
                        MainHand = equipment;
                    else
                        OffHand = equipment;
                    break;
                case EquipmentCategory.Shield:
                case EquipmentCategory.GreatShield:
                    OffHand = equipment;
                    break;
                case EquipmentCategory.Accessory:
                    if (Accessory1 == null) Accessory1 = equipment;
                    else if (Accessory2 == null) Accessory2 = equipment;
                    else if (Accessory3 == null) Accessory3 = equipment;
                    break;
            }
        }

        public bool ValidateWeaponEquipped() => MainHand != null;

        private bool CanDualWield() { /* 检查职业是否为剑士/剑圣 */ return false; }
    }

    // ==================== 装备槽与格挡规则（2026-05-02更新）====================
    //
    // 装备槽统一规则：
    // - 所有角色基础状态固定3个槽，CC后固定4个槽
    // - 分配逻辑：武器（含盾/大盾）占多少槽，剩余全是饰品槽
    //   * 单武器（无副手）：基础=1武器+2饰品，CC后=1武器+3饰品
    //   * 剑+盾 / 双持：基础=2武器+1饰品，CC后=2武器+2饰品
    // - EquipmentSlot.Equip 已支持双持：第二把武器自动放入 OffHand
    //
    // 格挡规则：
    // - 所有角色都有基础格挡率（Block属性），无盾也能格挡
    // - 无盾格挡：减免25%
    // - 装备盾（Shield）：减免25%（盾提升格挡率 block_rate）
    // - 装备大盾（GreatShield）：减免50%
    // - Block属性是格挡概率，不是格挡前提条件
    //
    // ==================== Equipment（运行时包装）====================
    public class Equipment
    {
        public EquipmentData Data { get; private set; }
        public Equipment(EquipmentData data) => Data = data;
    }

    // ==================== Buff ====================
    public class Buff
    {
        public string SkillId { get; set; }       // 来源技能ID（用于同技能去重）
        public string TargetStat { get; set; }     // "Str", "Def", "Spd"...
        public float Ratio { get; set; }           // +0.3 = +30%
        public bool IsOneTime { get; set; }        // 一次性buff
        public bool IsPureBuffOrDebuff { get; set; } // 纯buff/debuff（用于去重判断）
        public int RemainingTurns { get; set; }    // -1 = 永久
    }

    // ==================== BuffManager ====================
    public class BuffManager
    {
        public void ApplyBuff(BattleUnit target, Buff newBuff)
        {
            var existing = target.Buffs.FirstOrDefault(b => b.SkillId == newBuff.SkillId);

            // 规则：同技能纯buff/debuff → 不重复施加
            if (existing != null && newBuff.IsPureBuffOrDebuff)
                return;

            // 规则：同名效果不同技能 → 可以重叠
            target.Buffs.Add(newBuff);
        }

        public void RemoveOneTimeBuffsAfterAction(BattleUnit unit)
        {
            unit.Buffs.RemoveAll(b => b.IsOneTime);
        }

        public float GetTotalBuffRatio(BattleUnit unit, string statName)
        {
            return unit.Buffs.Where(b => b.TargetStat == statName).Sum(b => b.Ratio);
        }
    }

    // ==================== ITrait ====================
    public interface ITrait
    {
        void OnCalculateDamage(DamageCalculation calc, BattleUnit owner);
        AttackType ModifyAttackType(AttackType baseType, BattleUnit owner);
    }

    // ==================== TraitApplier ====================
    public class TraitApplier
    {
        public void ApplyTraitsToDamage(DamageCalculation calc, BattleUnit attacker)
        {
            foreach (var trait in attacker.Data.Traits)
            {
                // 根据 trait.TraitType 创建对应的 ITrait 实例并应用
            }
        }

        public AttackType ModifyAttackType(AttackType baseType, BattleUnit attacker)
        {
            var result = baseType;
            foreach (var trait in attacker.Data.Traits)
            {
                // 如雪游侠"无遠隔技能附加遠隔"
            }
            return result;
        }
    }
}
```

---

## 伤害计算 Pipeline

```csharp
namespace BattleKing.Pipeline
{
    // ==================== DamageCalculation（中间状态对象）====================
    public class DamageCalculation
    {
        public BattleUnit Attacker { get; set; }
        public BattleUnit Defender { get; set; }
        public ActiveSkill Skill { get; set; }

        // 阶段1-2：攻击力/防御力
        public int FinalAttackPower { get; set; }
        public int FinalDefense { get; set; }

        // 阶段3：基础差值
        public int BaseDifference => Math.Max(1, FinalAttackPower - FinalDefense);

        // 阶段4-6：乘区
        public float SkillPowerRatio { get; set; } = 1.0f;
        public float ClassTraitMultiplier { get; set; } = 1.0f;      // 兵种克制
        public float CharacterTraitMultiplier { get; set; } = 1.0f;   // 职业Trait

        // 阶段7-8：判定
        public bool IsHit { get; set; } = true;
        public bool IsCritical { get; set; } = false;
        public bool IsBlocked { get; set; } = false;
        public bool IsEvaded { get; set; } = false;
        public float CritMultiplier { get; set; } = 1.5f;
        public float BlockReduction { get; set; } = 0f;  // 0.25/0.50/0.75

        // 混合伤害
        public int PhysicalDamage { get; set; }
        public int MagicalDamage { get; set; }
        public int TotalDamage => PhysicalDamage + MagicalDamage;

        // 状态异常
        public List<StatusAilment> AppliedAilments { get; set; } = new();
    }

    // ==================== DamageResult ====================
    public class DamageResult
    {
        public int PhysicalDamage { get; set; }
        public int MagicalDamage { get; set; }
        public int TotalDamage => PhysicalDamage + MagicalDamage;
        public bool IsHit { get; set; }
        public bool IsCritical { get; set; }
        public bool IsBlocked { get; set; }
        public bool IsEvaded { get; set; }
        public List<StatusAilment> AppliedAilments { get; set; } = new();
    }

    // ==================== DamageCalculator ====================
    public class DamageCalculator
    {
        public DamageResult Calculate(BattleUnit attacker, BattleUnit defender, ActiveSkill skill, BattleContext ctx)
        {
            var calc = new DamageCalculation
            {
                Attacker = attacker,
                Defender = defender,
                Skill = skill
            };

            // 阶段1：计算攻击力
            calc.FinalAttackPower = attacker.GetCurrentAttackPower(skill.Type);

            // 阶段2：计算防御力
            calc.FinalDefense = defender.GetCurrentDefense(skill.Type);

            // 阶段3-6：基础伤害 × 技能威力 × 兵种补正 × Trait补正
            float baseDmg = calc.BaseDifference * (skill.Power / 100f);
            baseDmg *= GetClassTraitMultiplier(attacker, defender);
            baseDmg *= GetCharacterTraitMultiplier(attacker, defender, skill);

            // 分离物理/魔法部分
            if (skill.HasMixedDamage)
            {
                calc.PhysicalDamage = (int)(baseDmg * 0.7f); // 示例比例
                calc.MagicalDamage = (int)(baseDmg * 0.3f);
            }
            else
            {
                calc.PhysicalDamage = (int)baseDmg;
            }

            // 阶段7：暴击判定
            if (RollCrit(attacker, defender, skill))
            {
                calc.IsCritical = true;
                calc.CritMultiplier = Math.Min(3.0f, 1.5f + attacker.Buffs.Where(b => b.TargetStat == "CritDmg").Sum(b => b.Ratio));
                calc.PhysicalDamage = (int)(calc.PhysicalDamage * calc.CritMultiplier);
                calc.MagicalDamage = (int)(calc.MagicalDamage * calc.CritMultiplier);
            }

            // 阶段8：格挡判定（只对物理部分）
            if (skill.HasPhysicalComponent && RollBlock(defender, skill))
            {
                calc.IsBlocked = true;
                calc.BlockReduction = defender.GetBlockReduction();
                calc.PhysicalDamage = (int)(calc.PhysicalDamage * (1 - calc.BlockReduction));
            }

            // 四舍五入
            calc.PhysicalDamage = (int)Math.Round(calc.PhysicalDamage);
            calc.MagicalDamage = (int)Math.Round(calc.MagicalDamage);

            return new DamageResult
            {
                PhysicalDamage = calc.PhysicalDamage,
                MagicalDamage = calc.MagicalDamage,
                IsHit = calc.IsHit,
                IsCritical = calc.IsCritical,
                IsBlocked = calc.IsBlocked,
                IsEvaded = calc.IsEvaded,
                AppliedAilments = calc.AppliedAilments
            };
        }

        private float GetClassTraitMultiplier(BattleUnit attacker, BattleUnit defender)
        {
            // 骑兵→步兵=2.0，飞行→骑乘=2.0，弓→飞行=2.0
            if (attacker.Data.Classes.Contains(UnitClass.Cavalry) && defender.Data.Classes.Contains(UnitClass.Infantry))
                return 2.0f;
            if (attacker.Data.Classes.Contains(UnitClass.Flying) && defender.Data.Classes.Contains(UnitClass.Cavalry))
                return 2.0f;
            if (attacker.Data.Classes.Contains(UnitClass.Archer) && defender.Data.Classes.Contains(UnitClass.Flying))
                return 2.0f;
            return 1.0f;
        }

        private float GetCharacterTraitMultiplier(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            // 职业Trait介入（如白骑士"物理攻击对步兵2倍"）
            // 通过 TraitApplier 计算
            return 1.0f;
        }

        private bool RollCrit(BattleUnit attacker, BattleUnit defender, ActiveSkill skill)
        {
            if (defender.Ailments.Contains(StatusAilment.CritSeal)) return false;
            int critRate = attacker.GetCurrentCritRate();
            return RandUtil.Roll100() < critRate;
        }

        private bool RollBlock(BattleUnit defender, ActiveSkill skill)
        {
            if (defender.Ailments.Contains(StatusAilment.BlockSeal)) return false;
            if (!skill.HasPhysicalComponent) return false;
            int blockRate = defender.GetCurrentBlockRate();
            return RandUtil.Roll100() < blockRate;
        }
    }
}
```

---

## 事件系统

```csharp
namespace BattleKing.Events
{
    // ==================== EventBus ====================
    public class EventBus
    {
        private Dictionary<Type, List<Delegate>> _handlers = new();

        public void Subscribe<T>(Action<T> handler) where T : IBattleEvent
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type)) _handlers[type] = new List<Delegate>();
            _handlers[type].Add(handler);
        }

        public void Publish<T>(T evt) where T : IBattleEvent
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type)) return;
            foreach (var handler in _handlers[type].Cast<Action<T>>())
                handler(evt);
        }
    }

    // ==================== IBattleEvent ====================
    public interface IBattleEvent { }

    // ==================== 具体事件定义 ====================
    public class OnBattleStartEvent : IBattleEvent
    {
        public BattleContext Context { get; set; }
    }

    public class BeforeAttackEvent : IBattleEvent
    {
        public BattleUnit Attacker { get; set; }
        public BattleUnit Defender { get; set; }
        public ActiveSkill Skill { get; set; }
        public DamageCalculation Calculation { get; set; }
    }

    public class AfterAttackEvent : IBattleEvent
    {
        public BattleUnit Attacker { get; set; }
        public BattleUnit Defender { get; set; }
        public ActiveSkill Skill { get; set; }
        public DamageResult Result { get; set; }
    }

    public class OnActionStartEvent : IBattleEvent
    {
        public BattleUnit Unit { get; set; }
    }

    public class OnActionEndEvent : IBattleEvent
    {
        public BattleUnit Unit { get; set; }
        public ActiveSkill Skill { get; set; }
        public List<BattleUnit> Targets { get; set; }
        public List<DamageResult> Results { get; set; }
    }

    public class OnBattleEndEvent : IBattleEvent
    {
        public BattleContext Context { get; set; }
        public BattleResult Result { get; set; }
    }
}
```

---

## 同时发动限制器

```csharp
namespace BattleKing.Core
{
    public class SimultaneousActivationLimiter
    {
        // 按事件类型和阵营追踪已触发的人
        private Dictionary<string, HashSet<int>> _triggered = new();

        public bool CanTrigger(BattleUnit unit, SimultaneousLimitType limitType, bool isPlayerTeam)
        {
            if (limitType == SimultaneousLimitType.None) return true;

            string key = $"{limitType}_{(isPlayerTeam ? "player" : "enemy")}";
            if (!_triggered.ContainsKey(key)) _triggered[key] = new HashSet<int>();

            // 该事件类型该阵营已有更快的人触发过了
            if (_triggered[key].Count > 0) return false;

            _triggered[key].Add(unit.Position);
            return true;
        }

        public void ResetForNewRound()
        {
            _triggered.Clear();
        }
    }
}
```

---

## 数据流总结

```
启动时：
  GameDataRepository.LoadAll("res://data/")
    → 加载 characters.json, active_skills.json, passive_skills.json, equipments.json
    → 存入 Dictionary<string, T>（只读）

战斗开始时：
  BattleEngine.RunBattle()
    → Phase_BattleStart()：触发战斗开始时被动（同时发动限制）
    → Phase_ActionLoop()：
      1. 按速度排序生成行动序列
      2. 对每个角色：
         StrategyEvaluator.Evaluate(unit)
           → 从上到下检查8条策略
           → ConditionEvaluator 判断条件
           → TargetSelector 选择目标
         → 触发攻击方被动（自身强化 + 友方最快1人）
         → 触发防守方被动（对方最快1人 + 格挡/回避）
         → DamageCalculator.Calculate()（Pipeline 10阶段）
         → 应用伤害和状态异常
         → 触发行动后被动
    → Phase_BattleEnd()：触发战斗结束时被动（无限制）
    → JudgeWinner()：比较HP比例

伤害计算时：
  DamageCalculator
    → 获取攻击力（面板 + 装备 + buff）
    → 获取防御力（面板 + 装备 + buff）
    → 基础差值（保底1）
    → × 技能威力 × 兵种补正 × Trait补正
    → 暴击判定（上限3倍）
    → 格挡判定（只减物理部分）
    → 四舍五入
    → 返回 DamageResult
```

---

## 需要外部实现的接口（Godot 侧）

```csharp
// 由 Godot 的 Node/Scene 实现，Core 只调接口
public interface IBattleScene
{
    void PlayAttackAnimation(BattleUnit attacker, BattleUnit defender, ActiveSkill skill);
    void PlayDamageNumber(BattleUnit target, int damage, bool isCrit, bool isBlocked);
    void PlayBuffEffect(BattleUnit target, string buffName);
    void PlayStatusAilmentEffect(BattleUnit target, StatusAilment ailment);
    void PlayBattleStartEffect();
    void PlayBattleEndEffect(BattleResult result);
}
```
