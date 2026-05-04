using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Events;

namespace BattleKing.Skills
{
    public class PassiveSkillProcessor
    {
        private EventBus _eventBus;
        private GameDataRepository _gameData;
        private Action<string> _log;

        // Track simultaneous-limited skills that have fired this battle
        private HashSet<string> _battleStartFired = new();
        private HashSet<string> _allyBuffFired = new();
        private HashSet<string> _defenseFired = new();
        private HashSet<string> _afterActionFired = new();

        public PassiveSkillProcessor(EventBus eventBus, GameDataRepository gameData, Action<string> log)
        {
            _eventBus = eventBus;
            _gameData = gameData;
            _log = log;
        }

        public void SubscribeAll()
        {
            _eventBus.Subscribe<BattleStartEvent>(OnBattleStart);
            _eventBus.Subscribe<BeforeActiveUseEvent>(OnBeforeActiveUse);
            _eventBus.Subscribe<BeforeHitEvent>(OnBeforeHit);
            _eventBus.Subscribe<AfterHitEvent>(OnAfterHit);
            _eventBus.Subscribe<AfterActiveUseEvent>(OnAfterActiveUse);
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
            // Attacker's self-buffs (SelfOnActiveUse, SelfBeforeAttack)
            ProcessForUnit(evt.Caster, PassiveTriggerTiming.SelfOnActiveUse, "主动使用前", limitSimultaneous: false);
            ProcessForUnit(evt.Caster, PassiveTriggerTiming.SelfBeforeAttack, "攻击前", limitSimultaneous: false);

            // Ally buffs (AllyBeforeAttack, AllyOnActiveUse)
            var allies = GetAllies(evt.Caster, evt.Context);
            ProcessTiming(allies, PassiveTriggerTiming.AllyBeforeAttack, "友方攻击前",
                limitSimultaneous: true, _allyBuffFired);
            ProcessTiming(allies, PassiveTriggerTiming.AllyOnActiveUse, "友方主动时",
                limitSimultaneous: false, _allyBuffFired);
        }

        private void OnBeforeHit(BeforeHitEvent evt)
        {
            // Defender's self-defense (SelfBeforeHit, SelfBeforePhysicalHit, SelfBeforeMeleeHit)
            ProcessForUnit(evt.Defender, PassiveTriggerTiming.SelfBeforeHit, "被攻击前", limitSimultaneous: false);
            if (evt.Skill.Data.Type == SkillType.Physical)
                ProcessForUnit(evt.Defender, PassiveTriggerTiming.SelfBeforePhysicalHit, "被物理攻击前", limitSimultaneous: false);
            if (evt.Skill.Data.AttackType == AttackType.Melee)
                ProcessForUnit(evt.Defender, PassiveTriggerTiming.SelfBeforeMeleeHit, "被近接攻击前", limitSimultaneous: false);

            // Ally defense/cover (AllyBeforeHit)
            var allies = GetAllies(evt.Defender, evt.Context).Where(u => u != evt.Defender).ToList();
            ProcessTiming(allies, PassiveTriggerTiming.AllyBeforeHit, "友方被攻击前",
                limitSimultaneous: true, _defenseFired);
        }

        private void OnAfterHit(AfterHitEvent evt)
        {
            // Defender on being hit (OnBeingHit)
            ProcessForUnit(evt.Defender, PassiveTriggerTiming.OnBeingHit, "被攻击后", limitSimultaneous: false);

            // Ally on attacked (AllyOnAttacked)
            var allies = GetAllies(evt.Defender, evt.Context).Where(u => u != evt.Defender).ToList();
            ProcessTiming(allies, PassiveTriggerTiming.AllyOnAttacked, "友方被攻击后",
                limitSimultaneous: false, null);
        }

        private void OnAfterActiveUse(AfterActiveUseEvent evt)
        {
            // Post-action passives for all alive units (both sides)
            _afterActionFired.Clear();
            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.AfterAction, "行动后",
                limitSimultaneous: true, _afterActionFired);
        }

        private void OnBattleEnd(BattleEndEvent evt)
        {
            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.BattleEnd, "战斗结束时",
                limitSimultaneous: false, null);
        }

        private void ProcessForUnit(BattleUnit unit, PassiveTriggerTiming timing, string timingLabel, bool limitSimultaneous)
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

                _log?.Invoke($"  [被动] {unit.Data.Name} 触发 {skillData.Name} ({timingLabel})");
                ExecuteSimpleEffect(unit, skillData);
            }
        }

        private void ProcessTiming(List<BattleUnit> candidates, PassiveTriggerTiming timing, string timingLabel,
            bool limitSimultaneous, HashSet<string> firedSet)
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

                    _log?.Invoke($"  [被动] {unit.Data.Name} 触发 {skillData.Name} ({timingLabel})");
                    ExecuteSimpleEffect(unit, skillData);
                    break;
                }
            }
        }

        private void ExecuteSimpleEffect(BattleUnit unit, PassiveSkillData skillData)
        {
            var tags = skillData.Tags ?? new List<string>();
            var effects = new List<string>();

            if (tags.Contains("ApPlus1"))
            {
                unit.RecoverAp(1);
                effects.Add("AP+1");
            }
            if (tags.Contains("ApPlus1Ally"))
            {
                // Ally-targeted AP buff requires context; log for now
                effects.Add("友方AP+1(待实现)");
            }
            if (tags.Contains("HitPpPlus1"))
            {
                // PP+1 on hit requires AfterHit context; handled separately
                effects.Add("命中时PP+1(待实现)");
            }
            if (tags.Contains("DefUp20"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Def", Ratio = 0.20f, RemainingTurns = -1 });
                effects.Add("防御+20%");
            }
            if (tags.Contains("AtkUp20"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Str", Ratio = 0.20f, RemainingTurns = -1 });
                effects.Add("物攻+20%");
            }
            if (tags.Contains("AtkUp20Stackable"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Str", Ratio = 0.20f, RemainingTurns = -1 });
                effects.Add("物攻+20%(可叠加)");
            }
            if (tags.Contains("SpdUp20"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Spd", Ratio = 0.20f, RemainingTurns = -1 });
                effects.Add("速度+20");
            }
            if (tags.Contains("SpdUp30"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Spd", Ratio = 0.30f, RemainingTurns = -1 });
                effects.Add("速度+30");
            }
            if (tags.Contains("EvaUp30"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "Eva", Ratio = 0.30f, RemainingTurns = -1 });
                effects.Add("回避+30");
            }
            if (tags.Contains("CritDamageUp50"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "CritDmg", Ratio = 0.50f, RemainingTurns = -1 });
                effects.Add("暴击伤害+50%");
            }
            if (tags.Contains("MediumGuard"))
            {
                effects.Add("中格挡防御(格挡率提升，伤害减免)");
            }
            if (tags.Contains("LargeGuardAlly"))
            {
                effects.Add("大格挡防御(友方)");
            }
            if (tags.Contains("CoverAlly"))
            {
                effects.Add("掩护友方");
            }
            if (tags.Contains("Counter"))
            {
                effects.Add("反击(待实现额外攻击)");
            }
            if (tags.Contains("NullifyFirstMeleeHit"))
            {
                effects.Add("免疫下一次近战伤害(待实现)");
            }
            if (tags.Contains("BlockSealEnemy"))
            {
                effects.Add("攻击附带格挡封印(待实现)");
            }
            if (tags.Contains("SureHit"))
            {
                effects.Add("必中(待实现)");
            }
            if (tags.Contains("EvasionSkill"))
            {
                effects.Add("回避攻击(待实现)");
            }
            if (tags.Contains("Heal25"))
            {
                int heal = (int)(unit.Data.BaseStats.GetValueOrDefault("HP", 0) * 0.25f);
                unit.CurrentHp = Math.Min(unit.Data.BaseStats.GetValueOrDefault("HP", 0), unit.CurrentHp + Math.Max(1, heal));
                effects.Add($"HP回复{Math.Max(1, heal)}");
            }
            if (tags.Contains("HealAlly"))
            {
                effects.Add("回复友方HP(待实现)");
            }

            if (effects.Count > 0)
                _log?.Invoke($"    → 效果: {string.Join(", ", effects)}");
        }

        private List<BattleUnit> GetAllies(BattleUnit unit, BattleContext ctx)
        {
            return unit.IsPlayer
                ? ctx.PlayerUnits.Where(u => u != null && u.IsAlive).ToList()
                : ctx.EnemyUnits.Where(u => u != null && u.IsAlive).ToList();
        }
    }
}
