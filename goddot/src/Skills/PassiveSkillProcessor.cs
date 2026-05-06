using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Events;
using BattleKing.Equipment;
using BattleKing.Pipeline;

namespace BattleKing.Skills
{
    public class PassiveSkillProcessor
    {
        private EventBus _eventBus;
        private GameDataRepository _gameData;
        private Action<string> _log;
        private Action<PendingAction> _enqueueAction;

        private HashSet<string> _battleStartFired = new();
        private HashSet<string> _allyBuffFired = new();
        private HashSet<string> _defenseFired = new();
        private HashSet<string> _afterActionFired = new();

        public PassiveSkillProcessor(EventBus eventBus, GameDataRepository gameData, Action<string> log, Action<PendingAction> enqueueAction = null)
        {
            _eventBus = eventBus;
            _gameData = gameData;
            _log = log;
            _enqueueAction = enqueueAction;
        }

        public void SubscribeAll()
        {
            _eventBus.Subscribe<BattleStartEvent>(OnBattleStart);
            _eventBus.Subscribe<BeforeActiveUseEvent>(OnBeforeActiveUse);
            _eventBus.Subscribe<BeforeHitEvent>(OnBeforeHit);
            _eventBus.Subscribe<AfterHitEvent>(OnAfterHit);
            _eventBus.Subscribe<AfterActiveUseEvent>(OnAfterActiveUse);
            _eventBus.Subscribe<OnKnockdownEvent>(OnKnockdown);
            _eventBus.Subscribe<BattleEndEvent>(OnBattleEnd);
        }

        private void OnBattleStart(BattleStartEvent evt)
        {
            _battleStartFired.Clear();
            _allyBuffFired.Clear();
            _defenseFired.Clear();
            _afterActionFired.Clear();

            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.BattleStart, "战斗开始时",
                limitSimultaneous: true, _battleStartFired);
        }

        private void OnBeforeActiveUse(BeforeActiveUseEvent evt)
        {
            ProcessForUnit(evt.Caster, PassiveTriggerTiming.SelfOnActiveUse, "主动使用前", limitSimultaneous: false);
            ProcessForUnit(evt.Caster, PassiveTriggerTiming.SelfBeforeAttack, "攻击前", limitSimultaneous: false);

            var allies = GetAllies(evt.Caster, evt.Context);
            ProcessTiming(allies, PassiveTriggerTiming.AllyBeforeAttack, "友方攻击前",
                limitSimultaneous: true, _allyBuffFired);
            ProcessTiming(allies, PassiveTriggerTiming.AllyOnActiveUse, "友方主动时",
                limitSimultaneous: false);
        }

        private void OnBeforeHit(BeforeHitEvent evt)
        {
            // Defender self-defense
            ProcessForUnit(evt.Defender, PassiveTriggerTiming.SelfBeforeHit, "被攻击前", limitSimultaneous: false,
                calc: evt.Calc, attacker: evt.Attacker, defender: evt.Defender);
            if (evt.Skill.Data.Type == SkillType.Physical)
                ProcessForUnit(evt.Defender, PassiveTriggerTiming.SelfBeforePhysicalHit, "被物理攻击前", limitSimultaneous: false,
                    calc: evt.Calc);
            if (evt.Skill.Data.AttackType == AttackType.Melee)
                ProcessForUnit(evt.Defender, PassiveTriggerTiming.SelfBeforeMeleeHit, "被近接攻击前", limitSimultaneous: false,
                    calc: evt.Calc);

            // Ally defense/cover
            var allies = GetAllies(evt.Defender, evt.Context).Where(u => u != evt.Defender).ToList();
            ProcessTiming(allies, PassiveTriggerTiming.AllyBeforeHit, "友方被攻击前",
                limitSimultaneous: true, _defenseFired, calc: evt.Calc, attacker: evt.Attacker, defender: evt.Defender);
        }

        private void OnAfterHit(AfterHitEvent evt)
        {
            ProcessForUnit(evt.Defender, PassiveTriggerTiming.OnBeingHit, "被攻击后", limitSimultaneous: false,
                attacker: evt.Attacker);

            var allies = GetAllies(evt.Defender, evt.Context).Where(u => u != evt.Defender).ToList();
            ProcessTiming(allies, PassiveTriggerTiming.AllyOnAttacked, "友方被攻击后",
                limitSimultaneous: false, attacker: evt.Attacker, defender: evt.Defender);
        }

        private void OnAfterActiveUse(AfterActiveUseEvent evt)
        {
            _afterActionFired.Clear();
            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.AfterAction, "行动后",
                limitSimultaneous: true, _afterActionFired);
        }

        private void OnKnockdown(OnKnockdownEvent evt)
        {
            // Process OnKnockdown timing passives for all units
            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.OnKnockdown, "击倒时",
                limitSimultaneous: false, knockoutVictim: evt.Victim, knockoutKiller: evt.Killer);
        }

        private void OnBattleEnd(BattleEndEvent evt)
        {
            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.BattleEnd, "战斗结束时",
                limitSimultaneous: false);
        }

        private void ProcessForUnit(BattleUnit unit, PassiveTriggerTiming timing, string timingLabel,
            bool limitSimultaneous, DamageCalculation calc = null, BattleUnit attacker = null,
            BattleUnit defender = null, BattleUnit knockoutVictim = null, BattleUnit knockoutKiller = null)
        {
            if (unit == null || !unit.IsAlive) return;

            foreach (var skillId in unit.EquippedPassiveSkillIds)
            {
                if (!_gameData.PassiveSkills.TryGetValue(skillId, out var skillData))
                    continue;
                if (skillData.TriggerTiming != timing)
                    continue;
                if (!unit.CanUsePassiveSkill(new PassiveSkill(skillData, _gameData)))
                    continue;

                unit.ConsumePp(skillData.PpCost);

                _log?.Invoke($"  [被动] {unit.Data.Name} 触发 {skillData.Name} ({timingLabel})");
                ExecuteEffect(unit, skillData, calc, attacker, defender, knockoutVictim, knockoutKiller);
            }
        }

        private void ProcessTiming(List<BattleUnit> candidates, PassiveTriggerTiming timing, string timingLabel,
            bool limitSimultaneous, HashSet<string> firedSet = null, DamageCalculation calc = null,
            BattleUnit attacker = null, BattleUnit defender = null, BattleUnit knockoutVictim = null,
            BattleUnit knockoutKiller = null)
        {
            if (candidates == null || candidates.Count == 0) return;

            var ordered = candidates.OrderByDescending(u => u.GetCurrentSpeed()).ToList();

            foreach (var unit in ordered)
            {
                if (!unit.IsAlive) continue;

                foreach (var skillId in unit.EquippedPassiveSkillIds)
                {
                    if (!_gameData.PassiveSkills.TryGetValue(skillId, out var skillData))
                        continue;
                    if (skillData.TriggerTiming != timing)
                        continue;
                    if (!unit.CanUsePassiveSkill(new PassiveSkill(skillData, _gameData)))
                        continue;

                    if (limitSimultaneous && skillData.HasSimultaneousLimit && firedSet != null)
                    {
                        if (firedSet.Contains(timingLabel))
                            continue;
                        firedSet.Add(timingLabel);
                    }

                    unit.ConsumePp(skillData.PpCost);

                    _log?.Invoke($"  [被动] {unit.Data.Name} 触发 {skillData.Name} ({timingLabel})");
                    ExecuteEffect(unit, skillData, calc, attacker, defender, knockoutVictim, knockoutKiller);
                    break;
                }
            }
        }

        private void ExecuteEffect(BattleUnit unit, PassiveSkillData skillData,
            DamageCalculation calc, BattleUnit attacker, BattleUnit defender,
            BattleUnit knockoutVictim, BattleUnit knockoutKiller)
        {
            var effects = new List<string>();

            // === Module 4: Structured Effects (preferred) ===
            if (skillData.Effects != null && skillData.Effects.Count > 0)
            {
                foreach (var effect in skillData.Effects)
                {
                    ExecuteStructuredEffect(unit, skillData, effect, calc, attacker, defender, effects);
                }
            }
            else
            {
                // Fallback to legacy tag-based execution
                ExecuteLegacyTags(unit, skillData, calc, attacker, defender, knockoutVictim, knockoutKiller, effects);
            }

            if (effects.Count > 0)
            {
                string summary = DumpUnitBrief(unit);
                _log?.Invoke($"    → {string.Join(", ", effects)} | {unit.Data.Name}: {summary}");
            }
        }

        private void ExecuteStructuredEffect(BattleUnit unit, PassiveSkillData skillData, SkillEffectData effect,
            DamageCalculation calc, BattleUnit attacker, BattleUnit defender, List<string> effects)
        {
            var p = effect.Parameters ?? new Dictionary<string, object>();

            switch (effect.EffectType)
            {
                case "RecoverAp":
                    int apAmt = GetIntParam(p, "amount", 1);
                    // Check target: Self or Ally
                    var apTarget = GetTargetType(p, "target", "Self");
                    var apUnits = SelectPassiveTargets(unit, apTarget, attacker, defender);
                    foreach (var t in apUnits)
                    {
                        t.RecoverAp(apAmt);
                        effects.Add($"{t.Data.Name} AP+{apAmt}");
                    }
                    break;

                case "RecoverPp":
                    int ppAmt = GetIntParam(p, "amount", 1);
                    var ppTarget = GetTargetType(p, "target", "Self");
                    var ppUnits = SelectPassiveTargets(unit, ppTarget, attacker, defender);
                    foreach (var t in ppUnits)
                    {
                        t.RecoverPp(ppAmt);
                        effects.Add($"{t.Data.Name} PP+{ppAmt}");
                    }
                    break;

                case "RecoverHp":
                    int healPct = GetIntParam(p, "amount", 25);
                    var healTarget = GetTargetType(p, "target", "Self");
                    var healUnits = SelectPassiveTargets(unit, healTarget, attacker, defender);
                    foreach (var t in healUnits)
                    {
                        int heal = (int)(t.Data.BaseStats.GetValueOrDefault("HP", 0) * healPct / 100f);
                        t.CurrentHp = Math.Min(t.Data.BaseStats.GetValueOrDefault("HP", 0), t.CurrentHp + Math.Max(1, heal));
                        effects.Add($"{t.Data.Name} HP回复{healPct}%");
                    }
                    break;

                case "AddBuff":
                    var buffStat = GetStringParam(p, "stat", "Str");
                    float buffRatio = GetFloatParam(p, "ratio", 0.2f);
                    int buffTurns = GetIntParam(p, "turns", 1);  // default 1 turn; use -1 for battle-long
                    var buffTarget = GetTargetType(p, "target", "Self");
                    var buffUnits = SelectPassiveTargets(unit, buffTarget, attacker, defender);
                    foreach (var t in buffUnits)
                    {
                        t.Buffs.Add(new Buff { TargetStat = buffStat, Ratio = buffRatio, RemainingTurns = buffTurns });
                        effects.Add($"{t.Data.Name} {buffStat}+{(int)(buffRatio * 100)}%");
                    }
                    break;

                case "ModifyCounter":
                    string counterKey = GetStringParam(p, "counter", "sprite");
                    int counterDelta = GetIntParam(p, "amount", 1);
                    unit.ModifyCounter(counterKey, counterDelta);
                    effects.Add($"{counterKey}+{counterDelta} (当前: {unit.GetCounter(counterKey)})");
                    break;

                case "ConsumeCounter":
                    string consumeKey = GetStringParam(p, "counter", "sprite");
                    int powerPer = GetIntParam(p, "powerPerCounter", 30);
                    int consumed = unit.ConsumeCounter(consumeKey);
                    if (calc != null)
                    {
                        calc.CounterPowerBonus += consumed * powerPer;
                    }
                    effects.Add($"消耗全部{consumeKey}({consumed}个), 威力+{consumed * powerPer}");
                    break;

                case "ModifyDamageCalc":
                    if (calc != null)
                    {
                        if (p.ContainsKey("ForceHit") && (bool)p["ForceHit"])
                        { calc.ForceHit = true; effects.Add("必中"); }
                        if (p.ContainsKey("ForceEvasion") && (bool)p["ForceEvasion"])
                        { calc.ForceEvasion = true; effects.Add("强制回避"); }
                        if (p.ContainsKey("CannotBeBlocked") && (bool)p["CannotBeBlocked"])
                        { calc.CannotBeBlocked = true; effects.Add("格挡不可"); }
                        if (p.ContainsKey("IgnoreDefenseRatio"))
                        { calc.IgnoreDefenseRatio = (float)(double)p["IgnoreDefenseRatio"]; effects.Add($"无视{(int)(calc.IgnoreDefenseRatio * 100)}%防御"); }
                        if (p.ContainsKey("SkillPowerMultiplier"))
                        { calc.SkillPowerMultiplier *= (float)(double)p["SkillPowerMultiplier"]; effects.Add($"威力×{calc.SkillPowerMultiplier}"); }
                    }
                    break;

                case "CoverAlly":
                    if (calc != null && !calc.CannotBeCovered)
                    {
                        calc.CoverTarget = unit;
                        effects.Add($"{unit.Data.Name} 掩护目标");
                    }
                    break;

                case "TemporalMark":
                    string markKey = GetStringParam(p, "key", "OneTimeImmunity");
                    int markCount = GetIntParam(p, "count", 1);
                    var markTarget = GetTargetType(p, "target", "Self");
                    var markUnits = SelectPassiveTargets(unit, markTarget, attacker, defender);
                    foreach (var t in markUnits)
                    {
                        t.AddTemporal(markKey, markCount, -1, skillData.Id);
                        effects.Add($"{t.Data.Name} 获得{markKey}({markCount}次)");
                    }
                    break;

                case "CounterAttack":
                    if (attacker != null && _enqueueAction != null)
                    {
                        int cntPower = GetIntParam(p, "power", 75);
                        int? cntHit = p.ContainsKey("hitRate") ? GetIntParam(p, "hitRate", 100) : null;
                        _enqueueAction(new PendingAction
                        {
                            Type = PendingActionType.Counter,
                            Actor = unit,
                            Targets = new List<BattleUnit> { attacker },
                            Power = cntPower,
                            HitRate = cntHit,
                            DamageType = SkillType.Physical,
                            AttackType = AttackType.Melee,
                            SourcePassiveId = skillData.Id,
                            Tags = GetStringListParam(p, "tags")
                        });
                        effects.Add($"反击(威力{cntPower})");
                    }
                    break;

                case "PursuitAttack":
                    if (attacker != null && _enqueueAction != null)
                    {
                        int purPower = GetIntParam(p, "power", 75);
                        int? purHit = p.ContainsKey("hitRate") ? GetIntParam(p, "hitRate", 100) : null;
                        _enqueueAction(new PendingAction
                        {
                            Type = PendingActionType.Pursuit,
                            Actor = unit,
                            Targets = new List<BattleUnit> { attacker },
                            Power = purPower,
                            HitRate = purHit,
                            DamageType = SkillType.Physical,
                            AttackType = AttackType.Melee,
                            SourcePassiveId = skillData.Id,
                            Tags = GetStringListParam(p, "tags")
                        });
                        effects.Add($"追击(威力{purPower})");
                    }
                    break;

                case "PreemptiveAttack":
                    if (_enqueueAction != null)
                    {
                        int prePower = GetIntParam(p, "power", 100);
                        int? preHit = p.ContainsKey("hitRate") ? GetIntParam(p, "hitRate", 100) : null;
                        var preTarget = GetTargetType(p, "target", "Attacker");
                        var preUnits = SelectPassiveTargets(unit, preTarget, attacker, defender);
                        _enqueueAction(new PendingAction
                        {
                            Type = PendingActionType.Preemptive,
                            Actor = unit,
                            Targets = preUnits,
                            Power = prePower,
                            HitRate = preHit,
                            DamageType = SkillType.Physical,
                            AttackType = AttackType.Melee,
                            SourcePassiveId = skillData.Id,
                            Tags = GetStringListParam(p, "tags")
                        });
                        effects.Add($"先制攻击(威力{prePower})");
                    }
                    break;

                default:
                    effects.Add($"{effect.EffectType}(待实现)");
                    break;
            }
        }

        private void ExecuteLegacyTags(BattleUnit unit, PassiveSkillData skillData,
            DamageCalculation calc, BattleUnit attacker, BattleUnit defender,
            BattleUnit knockoutVictim, BattleUnit knockoutKiller, List<string> effects)
        {
            var tags = skillData.Tags ?? new List<string>();

            if (tags.Contains("ApPlus1"))
            {
                unit.RecoverAp(1);
                effects.Add("AP+1");
            }
            if (tags.Contains("ApPlus1Ally"))
            {
                effects.Add("友方AP+1(待结构化)");
            }
            if (tags.Contains("HitPpPlus1"))
            {
                effects.Add("命中时PP+1(待结构化)");
            }
            if (tags.Contains("DefUp20"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Def", Ratio = 0.20f, RemainingTurns = 1 });
                effects.Add("防御+20%");
            }
            if (tags.Contains("AtkUp20"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Str", Ratio = 0.20f, RemainingTurns = 1 });
                effects.Add("物攻+20%");
            }
            if (tags.Contains("AtkUp20Stackable"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Str", Ratio = 0.20f, RemainingTurns = 1 });
                effects.Add("物攻+20%(可叠加)");
            }
            if (tags.Contains("SpdUp20"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Spd", Ratio = 0.20f, RemainingTurns = 1 });
                effects.Add("速度+20");
            }
            if (tags.Contains("SpdUp30"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Spd", Ratio = 0.30f, RemainingTurns = 1 });
                effects.Add("速度+30");
            }
            if (tags.Contains("EvaUp30"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Eva", Ratio = 0.30f, RemainingTurns = 1 });
                effects.Add("回避+30");
            }
            if (tags.Contains("CritDamageUp50"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "CritDmg", Ratio = 0.50f, RemainingTurns = 1 });
                effects.Add("暴击伤害+50%");
            }
            if (tags.Contains("MediumGuard") && calc != null)
            {
                calc.ForceBlock = true;
                effects.Add("中格挡防御");
            }
            if (tags.Contains("LargeGuardAlly") && calc != null)
            {
                calc.ForceBlock = true;
                effects.Add("大格挡防御(友方)");
            }
            if (tags.Contains("CoverAlly") && calc != null && !calc.CannotBeCovered)
            {
                calc.CoverTarget = unit;
                effects.Add("掩护友方");
            }
            if (tags.Contains("Counter") && _enqueueAction != null && attacker != null)
            {
                _enqueueAction(new PendingAction
                {
                    Type = PendingActionType.Counter,
                    Actor = unit,
                    Targets = new List<BattleUnit> { attacker },
                    Power = 75,
                    HitRate = 90,
                    DamageType = SkillType.Physical,
                    AttackType = AttackType.Melee,
                    SourcePassiveId = skillData.Id
                });
                effects.Add("反击(威力75)");
            }
            if (tags.Contains("NullifyFirstMeleeHit"))
            {
                unit.AddTemporal("MeleeHitNullify", 1);
                effects.Add("免疫下一次近战伤害");
            }
            if (tags.Contains("BlockSealEnemy"))
            {
                effects.Add("攻击附带格挡封印(待结构化)");
            }
            if (tags.Contains("SureHit") && calc != null)
            {
                calc.ForceHit = true;
                effects.Add("必中");
            }
            if (tags.Contains("EvasionSkill") && calc != null)
            {
                calc.ForceEvasion = true;
                effects.Add("回避攻击");
            }
            if (tags.Contains("Heal25"))
            {
                int heal = (int)(unit.Data.BaseStats.GetValueOrDefault("HP", 0) * 0.25f);
                unit.CurrentHp = Math.Min(unit.Data.BaseStats.GetValueOrDefault("HP", 0), unit.CurrentHp + Math.Max(1, heal));
                effects.Add($"HP回复{Math.Max(1, heal)}");
            }
            if (tags.Contains("HealAlly"))
            {
                effects.Add("回复友方HP(待结构化)");
            }
        }

        // === Module 4: Target selection helpers ===

        private List<BattleUnit> SelectPassiveTargets(BattleUnit owner, PassiveTargetType targetType,
            BattleUnit attacker, BattleUnit defender)
        {
            switch (targetType)
            {
                case PassiveTargetType.Self:
                    return new List<BattleUnit> { owner };
                case PassiveTargetType.Attacker:
                    return attacker != null ? new List<BattleUnit> { attacker } : new List<BattleUnit>();
                case PassiveTargetType.Defender:
                    return defender != null ? new List<BattleUnit> { defender } : new List<BattleUnit>();
                case PassiveTargetType.LowestHpAlly:
                    var allies = GetAllies(owner, null);
                    return allies.Any() ? new List<BattleUnit> { allies.OrderBy(u => u.CurrentHp).First() } : new List<BattleUnit>();
                case PassiveTargetType.HighestHpAlly:
                    var alliesH = GetAllies(owner, null);
                    return alliesH.Any() ? new List<BattleUnit> { alliesH.OrderByDescending(u => u.CurrentHp).First() } : new List<BattleUnit>();
                case PassiveTargetType.AllAllies:
                    return GetAllies(owner, null);
                case PassiveTargetType.RandomAlly:
                    var alliesR = GetAllies(owner, null);
                    return alliesR.Any() ? new List<BattleUnit> { alliesR[new Random().Next(alliesR.Count)] } : new List<BattleUnit>();
                default:
                    return new List<BattleUnit> { owner };
            }
        }

        private static string DumpUnitBrief(BattleUnit u)
        {
            if (u == null || !u.IsAlive) return "[阵亡]";
            string s = $"HP:{u.CurrentHp}/{u.Data.BaseStats.GetValueOrDefault("HP",0)} AP:{u.CurrentAp}";
            var buffs = u.Buffs.Where(b => b.Ratio != 0).Select(b => {
                string sign = b.Ratio > 0 ? "+" : "";
                return $"{b.TargetStat}{sign}{(int)(b.Ratio*100)}%";
            }).ToList();
            if (buffs.Count > 0) s += " Buffs:[" + string.Join(", ", buffs) + "]";
            return s;
        }

        private List<BattleUnit> GetAllies(BattleUnit unit, BattleContext ctx)
        {
            if (ctx != null)
            {
                return unit.IsPlayer
                    ? ctx.PlayerUnits.Where(u => u != null && u.IsAlive).ToList()
                    : ctx.EnemyUnits.Where(u => u != null && u.IsAlive).ToList();
            }
            return new List<BattleUnit> { unit };
        }

        // === Parameter helpers ===

        private int GetIntParam(Dictionary<string, object> p, string key, int defaultVal)
        {
            if (p.TryGetValue(key, out var val))
            {
                if (val is int i) return i;
                if (val is long l) return (int)l;
                if (val is double d) return (int)d;
            }
            return defaultVal;
        }

        private float GetFloatParam(Dictionary<string, object> p, string key, float defaultVal)
        {
            if (p.TryGetValue(key, out var val))
            {
                if (val is float f) return f;
                if (val is double d) return (float)d;
            }
            return defaultVal;
        }

        private string GetStringParam(Dictionary<string, object> p, string key, string defaultVal)
        {
            if (p.TryGetValue(key, out var val) && val is string s)
                return s;
            return defaultVal;
        }

        private List<string> GetStringListParam(Dictionary<string, object> p, string key)
        {
            if (p.TryGetValue(key, out var val) && val is List<string> list)
                return list;
            return new List<string>();
        }

        private PassiveTargetType GetTargetType(Dictionary<string, object> p, string key, string defaultVal)
        {
            string val = GetStringParam(p, key, defaultVal);
            return val switch
            {
                "Self" => PassiveTargetType.Self,
                "Attacker" => PassiveTargetType.Attacker,
                "Defender" => PassiveTargetType.Defender,
                "RandomAlly" => PassiveTargetType.RandomAlly,
                "LowestHpAlly" => PassiveTargetType.LowestHpAlly,
                "HighestHpAlly" => PassiveTargetType.HighestHpAlly,
                "AllAllies" => PassiveTargetType.AllAllies,
                "ColumnAllies" => PassiveTargetType.ColumnAllies,
                _ => PassiveTargetType.Self
            };
        }
    }
}
