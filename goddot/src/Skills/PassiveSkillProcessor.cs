using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Events;
using BattleKing.Ai;
using BattleKing.Equipment;
using BattleKing.Pipeline;
using BattleKing.Utils;

namespace BattleKing.Skills
{
    public class PassiveSkillProcessor
    {
        private EventBus _eventBus;
        private GameDataRepository _gameData;
        private Action<string> _log;
        private Action<PendingAction> _enqueueAction;
        private SkillEffectExecutor _skillEffectExecutor = new();

        private BattleContext _ctx;
        // Per-side simultaneous limit: key=true=player, false=enemy
        private Dictionary<bool, HashSet<string>> _battleStartFired = new() { [true] = new(), [false] = new() };
        private Dictionary<bool, HashSet<string>> _afterActionFired = new() { [true] = new(), [false] = new() };

        public PassiveSkillProcessor(EventBus eventBus, GameDataRepository gameData, Action<string> log, Action<PendingAction> enqueueAction = null)
        {
            _eventBus = eventBus;
            _gameData = gameData;
            _log = log;
            _enqueueAction = enqueueAction;
            _skillEffectExecutor = new SkillEffectExecutor(enqueueAction);
        }

        public void SubscribeAll()
        {
            _eventBus.Subscribe<BattleStartEvent>(OnBattleStart);
            _eventBus.Subscribe<BeforeActiveUseEvent>(OnBeforeActiveUse);
            _eventBus.Subscribe<AfterActiveCostEvent>(OnAfterActiveCost);
            _eventBus.Subscribe<BeforeAttackCalculationEvent>(OnBeforeAttackCalculation);
            _eventBus.Subscribe<BeforeHitEvent>(OnBeforeHit);
            _eventBus.Subscribe<AfterHitEvent>(OnAfterHit);
            _eventBus.Subscribe<AfterActiveUseEvent>(OnAfterActiveUse);
            _eventBus.Subscribe<OnKnockdownEvent>(OnKnockdown);
            _eventBus.Subscribe<BattleEndEvent>(OnBattleEnd);
        }

        private void OnBattleStart(BattleStartEvent evt)
        {
            _ctx = evt.Context;
            foreach (var set in _battleStartFired.Values) set.Clear();
            foreach (var set in _afterActionFired.Values) set.Clear();

            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.BattleStart, "战斗开始时",
                limitSimultaneous: true, _battleStartFired);
        }

        private void OnBeforeActiveUse(BeforeActiveUseEvent evt)
        {
            _ctx = evt.Context;
        }

        private void OnAfterActiveCost(AfterActiveCostEvent evt)
        {
            _ctx = evt.Context;
            ProcessForUnit(evt.Caster, PassiveTriggerTiming.SelfOnActiveUse, "主动使用时", limitSimultaneous: false,
                activeSkill: evt.Skill);

            var allies = GetAllies(evt.Caster, evt.Context).Where(u => u != evt.Caster).ToList();
            ProcessTiming(allies, PassiveTriggerTiming.AllyOnActiveUse, "友方主动时",
                limitSimultaneous: false, attacker: evt.Caster, activeSkill: evt.Skill);
        }

        private void OnBeforeAttackCalculation(BeforeAttackCalculationEvent evt)
        {
            _ctx = evt.Context;
            ProcessForUnit(evt.Caster, PassiveTriggerTiming.SelfBeforeAttack, "攻击前", limitSimultaneous: true,
                activeSkill: evt.Skill);

            var allies = GetAllies(evt.Caster, evt.Context).Where(u => u != evt.Caster).ToList();
            ProcessTiming(allies, PassiveTriggerTiming.AllyBeforeAttack, "友方攻击前",
                limitSimultaneous: true, CreateSimultaneousFiredSet(), activeSkill: evt.Skill);
        }

        private void OnBeforeHit(BeforeHitEvent evt)
        {
            _ctx = evt.Context;
            // Defender self-defense
            ProcessForUnit(evt.Defender, PassiveTriggerTiming.SelfBeforeHit, "被攻击前", limitSimultaneous: false,
                calc: evt.Calc, attacker: evt.Attacker, defender: evt.Defender, sourceKind: evt.SourceKind);
            if (evt.Skill.Data.Type == SkillType.Physical)
                ProcessForUnit(evt.Defender, PassiveTriggerTiming.SelfBeforePhysicalHit, "被物理攻击前", limitSimultaneous: false,
                    calc: evt.Calc, sourceKind: evt.SourceKind);
            if (evt.Skill.Data.AttackType == AttackType.Melee)
                ProcessForUnit(evt.Defender, PassiveTriggerTiming.SelfBeforeMeleeHit, "被近接攻击前", limitSimultaneous: false,
                    calc: evt.Calc, sourceKind: evt.SourceKind);

            // Ally defense/cover
            var allies = GetAllies(evt.Defender, evt.Context).Where(u => u != evt.Defender).ToList();
            ProcessTiming(allies, PassiveTriggerTiming.AllyBeforeHit, "友方被攻击前",
                limitSimultaneous: true, CreateSimultaneousFiredSet(), calc: evt.Calc, attacker: evt.Attacker, defender: evt.Defender,
                sourceKind: evt.SourceKind);
        }

        private void OnAfterHit(AfterHitEvent evt)
        {
            _ctx = evt.Context;
            ProcessForUnit(evt.Defender, PassiveTriggerTiming.OnBeingHit, "被攻击后", limitSimultaneous: false,
                attacker: evt.Attacker, activeSkill: evt.Skill, sourceKind: evt.SourceKind);

            var allies = GetAllies(evt.Defender, evt.Context).Where(u => u != evt.Defender).ToList();
            ProcessTiming(allies, PassiveTriggerTiming.AllyOnAttacked, "友方被攻击后",
                limitSimultaneous: false, attacker: evt.Attacker, defender: evt.Defender,
                activeSkill: evt.Skill, sourceKind: evt.SourceKind);
        }

        private void OnAfterActiveUse(AfterActiveUseEvent evt)
        {
            _ctx = evt.Context;
            _afterActionFired.Clear();
            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.AfterAction, "行动后",
                limitSimultaneous: true, _afterActionFired);
        }

        private void OnKnockdown(OnKnockdownEvent evt)
        {
            _ctx = evt.Context;
            // Process OnKnockdown timing passives for all units
            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.OnKnockdown, "击倒时",
                limitSimultaneous: false, knockoutVictim: evt.Victim, knockoutKiller: evt.Killer);
        }

        private void OnBattleEnd(BattleEndEvent evt)
        {
            _ctx = evt.Context;
            ProcessTiming(evt.Context.AllUnits, PassiveTriggerTiming.BattleEnd, "战斗结束时",
                limitSimultaneous: false);
        }

        private void ProcessForUnit(BattleUnit unit, PassiveTriggerTiming timing, string timingLabel,
            bool limitSimultaneous, DamageCalculation calc = null, BattleUnit attacker = null,
            BattleUnit defender = null, BattleUnit knockoutVictim = null, BattleUnit knockoutKiller = null,
            ActiveSkill activeSkill = null, BattleActionSourceKind sourceKind = BattleActionSourceKind.ActiveAttack)
        {
            if (unit == null || !unit.IsAlive) return;
            if (unit.Ailments.Contains(StatusAilment.PassiveSeal)) return;

            bool simultaneousFired = false;
            foreach (var row in unit.GetPassiveStrategiesInOrder())
            {
                var skillId = row.SkillId;
                if (!_gameData.PassiveSkills.TryGetValue(skillId, out var skillData))
                    continue;
                if (skillData.TriggerTiming != timing)
                    continue;
                if (!PassiveMatchesBattleContext(skillData, calc, activeSkill, sourceKind))
                    continue;
                if (!unit.CanUsePassiveSkill(new PassiveSkill(skillData, _gameData)))
                    continue;

                // Check player-set passive condition
                if (!CheckPassiveConditions(unit, row)) continue;

                if (limitSimultaneous && skillData.HasSimultaneousLimit)
                {
                    if (simultaneousFired)
                        continue;
                    simultaneousFired = true;
                }

                bool deferPp = ShouldDeferPpUntilPendingAction(skillData);
                if (!deferPp)
                {
                    unit.ConsumePp(skillData.PpCost);
                    _log?.Invoke($"  [被动] {BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 触发 {skillData.Name} ({timingLabel})");
                }

                ExecuteEffect(unit, skillData, calc, attacker, defender, knockoutVictim, knockoutKiller, deferPp, timingLabel);
            }
        }

        private bool CheckPassiveConditions(BattleUnit unit, PassiveStrategy row)
        {
            var conditions = new List<Condition>();
            if (row.Condition1 != null)
                conditions.Add(row.Condition1);
            if (row.Condition2 != null)
                conditions.Add(row.Condition2);
            if (conditions.Count == 0
                && unit.PassiveConditions.TryGetValue(row.SkillId, out var legacyCondition)
                && legacyCondition != null)
                conditions.Add(legacyCondition);

            if (conditions.Count == 0)
                return true;

            var evaluator = new ConditionEvaluator(_ctx);
            return conditions.All(condition => evaluator.Evaluate(condition, unit, null));
        }

        private void ProcessTiming(List<BattleUnit> candidates, PassiveTriggerTiming timing, string timingLabel,
            bool limitSimultaneous, Dictionary<bool, HashSet<string>> firedSet = null, DamageCalculation calc = null,
            BattleUnit attacker = null, BattleUnit defender = null, BattleUnit knockoutVictim = null,
            BattleUnit knockoutKiller = null, ActiveSkill activeSkill = null,
            BattleActionSourceKind sourceKind = BattleActionSourceKind.ActiveAttack)
        {
            if (candidates == null || candidates.Count == 0) return;

            var ordered = candidates
                .GroupBy(u => u.GetCurrentSpeed())
                .OrderByDescending(g => g.Key)
                .SelectMany(g => g.OrderBy(_ => RandUtil.Roll(int.MaxValue)))
                .ToList();

            foreach (var unit in ordered)
            {
                if (!unit.IsAlive) continue;
                if (unit.Ailments.Contains(StatusAilment.PassiveSeal)) continue;

                foreach (var row in unit.GetPassiveStrategiesInOrder())
                {
                    var skillId = row.SkillId;
                    if (!_gameData.PassiveSkills.TryGetValue(skillId, out var skillData))
                        continue;
                    if (skillData.TriggerTiming != timing)
                        continue;
                    if (!PassiveMatchesBattleContext(skillData, calc, activeSkill, sourceKind))
                        continue;
                    if (!unit.CanUsePassiveSkill(new PassiveSkill(skillData, _gameData)))
                        continue;

                    // Check player-set passive condition before claiming a simultaneous slot.
                    if (!CheckPassiveConditions(unit, row)) continue;

                    if (limitSimultaneous && skillData.HasSimultaneousLimit && firedSet != null)
                    {
                        var sideSet = firedSet[unit.IsPlayer];
                        if (sideSet.Contains(timingLabel))
                            continue;
                        sideSet.Add(timingLabel);
                    }

                    bool deferPp = ShouldDeferPpUntilPendingAction(skillData);
                    if (!deferPp)
                    {
                        unit.ConsumePp(skillData.PpCost);
                        _log?.Invoke($"  [被动] {BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 触发 {skillData.Name} ({timingLabel})");
                    }

                    ExecuteEffect(unit, skillData, calc, attacker, defender, knockoutVictim, knockoutKiller, deferPp, timingLabel);
                    break;
                }
            }
        }

        private static Dictionary<bool, HashSet<string>> CreateSimultaneousFiredSet()
        {
            return new Dictionary<bool, HashSet<string>> { [true] = new(), [false] = new() };
        }

        private static bool PassiveMatchesBattleContext(
            PassiveSkillData skillData,
            DamageCalculation calc,
            ActiveSkill activeSkill = null,
            BattleActionSourceKind sourceKind = BattleActionSourceKind.ActiveAttack)
        {
            var tags = skillData.Tags ?? new List<string>();
            if (tags.Contains("RangedCover"))
            {
                return calc?.Skill?.Data?.Type == SkillType.Physical
                    && calc.Skill.Data.AttackType == AttackType.Ranged;
            }

            if (!IncomingSkillRequirementsMatch(skillData, calc))
                return false;

            if (!CurrentActionRequirementsMatch(skillData, activeSkill))
                return false;

            if (!SourceKindRequirementsMatch(skillData, sourceKind))
                return false;

            return true;
        }

        private static bool SourceKindRequirementsMatch(PassiveSkillData skillData, BattleActionSourceKind sourceKind)
        {
            var effectsWithRequirements = skillData.Effects?
                .Where(effect => effect?.Parameters != null
                    && !string.IsNullOrWhiteSpace(SkillEffectExecutor.GetString(effect.Parameters, "requiresSourceKind", "")))
                .ToList() ?? new List<SkillEffectData>();

            return effectsWithRequirements.Count == 0
                || effectsWithRequirements.Any(effect => SourceKindMatches(effect.Parameters, sourceKind));
        }

        private static bool SourceKindMatches(Dictionary<string, object> parameters, BattleActionSourceKind sourceKind)
        {
            string required = SkillEffectExecutor.GetString(parameters, "requiresSourceKind", "");
            if (string.IsNullOrWhiteSpace(required))
                return true;

            return required
                .Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(value => string.Equals(value.Trim(), sourceKind.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        private static bool IncomingSkillRequirementsMatch(PassiveSkillData skillData, DamageCalculation calc)
        {
            var effectsWithRequirements = skillData.Effects?
                .Where(effect => effect != null
                    && SkillEffectExecutor.HasIncomingSkillRequirement(effect.Parameters))
                .ToList() ?? new List<SkillEffectData>();

            return effectsWithRequirements.Count == 0
                || effectsWithRequirements.Any(effect =>
                    SkillEffectExecutor.IncomingSkillRequirementMatches(effect.Parameters, calc?.Skill));
        }

        private static bool CurrentActionRequirementsMatch(PassiveSkillData skillData, ActiveSkill activeSkill)
        {
            var augmentsWithRequirements = skillData.Effects?
                .Where(effect => effect?.EffectType == "AugmentCurrentAction"
                    && HasCurrentActionRequirement(effect.Parameters))
                .ToList() ?? new List<SkillEffectData>();

            return augmentsWithRequirements.Count == 0
                || augmentsWithRequirements.Any(effect =>
                    SkillEffectExecutor.CurrentActionRequirementMatches(effect.Parameters, activeSkill));
        }

        private static bool HasCurrentActionRequirement(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return false;

            return !string.IsNullOrWhiteSpace(SkillEffectExecutor.GetString(parameters, "requiresCurrentSkillType", ""))
                || !string.IsNullOrWhiteSpace(SkillEffectExecutor.GetString(parameters, "currentSkillType", ""))
                || !string.IsNullOrWhiteSpace(SkillEffectExecutor.GetString(parameters, "requiresCurrentAttackType", ""))
                || !string.IsNullOrWhiteSpace(SkillEffectExecutor.GetString(parameters, "currentAttackType", ""));
        }

        private void ExecuteEffect(BattleUnit unit, PassiveSkillData skillData,
            DamageCalculation calc, BattleUnit attacker, BattleUnit defender,
            BattleUnit knockoutVictim, BattleUnit knockoutKiller,
            bool deferPpUntilPendingAction = false, string timingLabel = null)
        {
            var effects = new List<string>();

            // === Module 4: Structured Effects (preferred) ===
            if (skillData.Effects != null && skillData.Effects.Count > 0)
            {
                foreach (var effect in skillData.Effects)
                {
                    ExecuteStructuredEffect(unit, skillData, effect, calc, attacker, defender, effects,
                        deferPpUntilPendingAction, timingLabel);
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
                _log?.Invoke($"    → {string.Join(", ", effects)} | {BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)}: {summary}");
            }
        }

        private void ExecuteStructuredEffect(BattleUnit unit, PassiveSkillData skillData, SkillEffectData effect,
            DamageCalculation calc, BattleUnit attacker, BattleUnit defender, List<string> effects,
            bool deferPpUntilPendingAction = false, string timingLabel = null)
        {
            if (TryExecuteThroughSharedExecutor(unit, skillData, effect, calc, attacker, defender, effects,
                deferPpUntilPendingAction, timingLabel))
                return;

            effects.Add($"{effect.EffectType}: unsupported");
        }

        private bool TryExecuteThroughSharedExecutor(BattleUnit unit, PassiveSkillData skillData, SkillEffectData effect,
            DamageCalculation calc, BattleUnit attacker, BattleUnit defender, List<string> effects,
            bool deferPpUntilPendingAction = false, string timingLabel = null)
        {
            if (!CanExecuteThroughSharedExecutor(effect.EffectType))
                return false;

            var targets = SelectExecutorFallbackTargets(unit, effect, attacker, defender);
            int augmentCountBefore = _ctx?.CurrentActionAugments.Count ?? 0;
            var executor = deferPpUntilPendingAction
                ? new SkillEffectExecutor(action =>
                {
                    action.SourcePpCost = skillData.PpCost;
                    action.SourceTimingLabel = timingLabel;
                    _enqueueAction?.Invoke(action);
                })
                : _skillEffectExecutor;
            var state = new SkillEffectExecutionState();
            var logs = IsCalculationEffect(effect.EffectType)
                ? executor.ExecuteCalculationEffects(_ctx, unit, targets, new List<SkillEffectData> { effect }, skillData.Id, calc, state)
                : executor.ExecuteActionEffects(_ctx, unit, targets, new List<SkillEffectData> { effect }, skillData.Id, calc);
            if (deferPpUntilPendingAction && effect.EffectType == "AugmentCurrentAction" && _ctx != null)
            {
                foreach (var augment in _ctx.CurrentActionAugments.Skip(augmentCountBefore)
                    .Where(augment => string.Equals(augment.SourcePassiveId, skillData.Id, StringComparison.Ordinal)))
                {
                    augment.SourcePpCost = skillData.PpCost;
                    augment.SourceTimingLabel = timingLabel;
                }
            }
            effects.AddRange(logs);
            return true;
        }

        private static bool ShouldDeferPpUntilPendingAction(PassiveSkillData skillData)
        {
            if (skillData?.Effects == null || skillData.Effects.Count == 0 || skillData.PpCost <= 0)
                return false;

            return skillData.Effects.Any(effect => IsDirectPendingActionEffect(effect?.EffectType)
                || IsQueuedOnlyCurrentActionAugment(effect));
        }

        private static bool IsDirectPendingActionEffect(string effectType)
        {
            return effectType == "CounterAttack"
                || effectType == "PursuitAttack"
                || effectType == "PreemptiveAttack"
                || effectType == "BattleEndAttack"
                || effectType == "PendingAttack";
        }

        private static bool IsQueuedOnlyCurrentActionAugment(SkillEffectData effect)
        {
            if (effect?.EffectType != "AugmentCurrentAction")
                return false;

            var parameters = effect.Parameters;
            if (parameters == null)
                return false;

            return HasNestedEffects(parameters, "queuedActions")
                && !HasNestedEffects(parameters, "calculationEffects")
                && !HasNestedEffects(parameters, "onHitEffects")
                && !HasStringList(parameters, "tags");
        }

        private static bool HasNestedEffects(Dictionary<string, object> parameters, string key)
        {
            return parameters.TryGetValue(key, out var raw) && raw != null;
        }

        private static bool HasStringList(Dictionary<string, object> parameters, string key)
        {
            return parameters.TryGetValue(key, out var raw) && raw != null;
        }

        private void ExecuteLegacyTags(BattleUnit unit, PassiveSkillData skillData,
            DamageCalculation calc, BattleUnit attacker, BattleUnit defender,
            BattleUnit knockoutVictim, BattleUnit knockoutKiller, List<string> effects)
        {
            var tags = skillData.Tags ?? new List<string>();

            if (tags.Contains("ApPlus1"))
            {
                int apBefore = unit.CurrentAp;
                unit.RecoverAp(1);
                effects.Add($"[AP] AP+1 ({apBefore}→{unit.CurrentAp})");
            }
            if (tags.Contains("ApPlus1Ally"))
            {
                effects.Add("[AP] 友方AP+1(待结构化)");
            }
            if (tags.Contains("HitPpPlus1"))
            {
                effects.Add("[PP] 命中时PP+1(待结构化)");
            }
            if (tags.Contains("DefUp20"))
            {
                int before = unit.GetCurrentStat("Def");
                unit.Buffs.Add(new Buff { TargetStat = "Def", Ratio = 0.20f, RemainingTurns = 1 });
                effects.Add($"[+] Def+20% ({before}→{unit.GetCurrentStat("Def")})");
            }
            if (tags.Contains("AtkUp20"))
            {
                int before = unit.GetCurrentStat("Str");
                unit.Buffs.Add(new Buff { TargetStat = "Str", Ratio = 0.20f, RemainingTurns = 1 });
                effects.Add($"[+] Str+20% ({before}→{unit.GetCurrentStat("Str")})");
            }
            if (tags.Contains("AtkUp20Stackable"))
            {
                int before = unit.GetCurrentStat("Str");
                unit.Buffs.Add(new Buff { TargetStat = "Str", Ratio = 0.20f, RemainingTurns = 1 });
                effects.Add($"[+] Str+20%可叠加 ({before}→{unit.GetCurrentStat("Str")})");
            }
            if (tags.Contains("SpdUp20"))
            {
                int before = unit.GetCurrentStat("Spd");
                unit.Buffs.Add(new Buff { TargetStat = "Spd", Ratio = 0.20f, RemainingTurns = 1 });
                effects.Add($"[+] Spd+20% ({before}→{unit.GetCurrentStat("Spd")})");
            }
            if (tags.Contains("SpdUp30"))
            {
                int before = unit.GetCurrentStat("Spd");
                unit.Buffs.Add(new Buff { TargetStat = "Spd", Ratio = 0.30f, RemainingTurns = 1 });
                effects.Add($"[+] Spd+30% ({before}→{unit.GetCurrentStat("Spd")})");
            }
            if (tags.Contains("EvaUp30"))
            {
                int before = unit.GetCurrentStat("Eva");
                unit.Buffs.Add(new Buff { TargetStat = "Eva", Ratio = 0.30f, RemainingTurns = 1 });
                effects.Add($"[+] Eva+30% ({before}→{unit.GetCurrentStat("Eva")})");
            }
            if (tags.Contains("CritDamageUp50"))
            {
                unit.Buffs.Add(new Buff { TargetStat = "CritDmg", Ratio = 0.50f, RemainingTurns = 1 });
                effects.Add("[+] 暴击伤害+50%");
            }
            if (tags.Contains("MediumGuard") && calc != null)
            {
                calc.ForceBlock = true;
                calc.ForcedBlockReduction = 0.50f;
                effects.Add("中格挡防御");
            }
            if (tags.Contains("LargeGuardAlly") && calc != null)
            {
                calc.ForceBlock = true;
                calc.ForcedBlockReduction = 0.75f;
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
                int maxHp = Math.Max(1, unit.GetCurrentStat("HP"));
                int heal = (int)(maxHp * 0.25f);
                int healed = Math.Min(maxHp, unit.CurrentHp + Math.Max(1, heal));
                unit.CurrentHp = Math.Max(unit.CurrentHp, healed);
                effects.Add($"HP回复{Math.Max(1, heal)}");
            }
            if (tags.Contains("HealAlly"))
            {
                effects.Add("回复友方HP(待结构化)");
            }
        }

        private static bool CanExecuteThroughSharedExecutor(string effectType)
        {
            return effectType == "RecoverAp"
                || effectType == "ApDamage"
                || effectType == "RecoverPp"
                || effectType == "PpDamage"
                || effectType == "TransferResource"
                || effectType == "RecoverHp"
                || effectType == "Heal"
                || effectType == "HealRatio"
                || effectType == "AddBuff"
                || effectType == "AddDebuff"
                || effectType == "RemoveBuff"
                || effectType == "RemoveDebuff"
                || effectType == "CleanseDebuff"
                || effectType == "ModifyCounter"
                || effectType == "ConsumeCounter"
                || effectType == "ModifyDamageCalc"
                || effectType == "CoverAlly"
                || effectType == "StatusAilment"
                || effectType == "TemporalMark"
                || effectType == "ForcedTarget"
                || effectType == "ActionOrderPriority"
                || effectType == "CounterAttack"
                || effectType == "PursuitAttack"
                || effectType == "PreemptiveAttack"
                || effectType == "BattleEndAttack"
                || effectType == "PendingAttack"
                || effectType == "AugmentCurrentAction"
                || effectType == "AugmentOutgoingActions"
                || effectType == "OnHitEffect"
                || effectType == "OnKillEffect";
        }

        private static bool IsCalculationEffect(string effectType)
        {
            return effectType == "ModifyDamageCalc"
                || effectType == "ConsumeCounter"
                || effectType == "CoverAlly";
        }

        private List<BattleUnit> SelectExecutorFallbackTargets(
            BattleUnit owner,
            SkillEffectData effect,
            BattleUnit attacker,
            BattleUnit defender)
        {
            var defaultTarget = effect.EffectType switch
            {
                "CounterAttack" => "Attacker",
                "PursuitAttack" => "Attacker",
                "PreemptiveAttack" => "AllEnemies",
                "BattleEndAttack" => "AllEnemies",
                "PendingAttack" => "AllEnemies",
                _ => "Self"
            };
            var targetType = GetTargetType(effect.Parameters ?? new Dictionary<string, object>(), "target", defaultTarget);
            var selected = SelectPassiveTargets(owner, targetType, attacker, defender);
            if (effect.Parameters != null && TryGetIntParam(effect.Parameters, "maxTargets", out int maxTargets))
                selected = selected.Take(Math.Max(0, maxTargets)).ToList();
            return selected;
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
                    var allies = GetAllies(owner, _ctx);
                    return allies.Any() ? new List<BattleUnit> { allies.OrderBy(u => u.CurrentHp).First() } : new List<BattleUnit>();
                case PassiveTargetType.HighestHpAlly:
                    var alliesH = GetAllies(owner, _ctx);
                    return alliesH.Any() ? new List<BattleUnit> { alliesH.OrderByDescending(u => u.CurrentHp).First() } : new List<BattleUnit>();
                case PassiveTargetType.AllAllies:
                    return GetAllies(owner, _ctx);
                case PassiveTargetType.AllEnemies:
                    return _ctx?.GetAliveUnits(!owner.IsPlayer) ?? new List<BattleUnit>();
                case PassiveTargetType.RowAllies:
                    return GetAllies(owner, _ctx).Where(u => u.IsFrontRow == owner.IsFrontRow).ToList();
                case PassiveTargetType.FrontRowAlly:
                    return GetAllies(owner, _ctx).Where(u => u.IsFrontRow).ToList();
                case PassiveTargetType.BackRowAlly:
                    return GetAllies(owner, _ctx).Where(u => !u.IsFrontRow).ToList();
                case PassiveTargetType.ColumnAllies:
                    return GetAllies(owner, _ctx).Where(u => IsSameColumn(u.Position, owner.Position)).ToList();
                case PassiveTargetType.RandomAlly:
                    var alliesR = GetAllies(owner, _ctx);
                    return alliesR.Any() ? new List<BattleUnit> { alliesR[new Random().Next(alliesR.Count)] } : new List<BattleUnit>();
                default:
                    return new List<BattleUnit> { owner };
            }
        }

        private static string DumpUnitBrief(BattleUnit u)
        {
            if (u == null || !u.IsAlive) return "[阵亡]";
            string s = $"HP:{u.CurrentHp}/{Math.Max(1, u.GetCurrentStat("HP"))} AP:{u.CurrentAp}/{u.MaxAp} PP:{u.CurrentPp}/{u.MaxPp}";
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

        private static bool IsSameColumn(int a, int b)
        {
            if (a <= 0 || b <= 0)
                return false;
            return (a - 1) % 3 == (b - 1) % 3;
        }

        // === Parameter helpers ===

        private int GetIntParam(Dictionary<string, object> p, string key, int defaultVal)
        {
            return SkillEffectExecutor.GetInt(p, key, defaultVal);
        }

        private bool TryGetIntParam(Dictionary<string, object> p, string key, out int value)
        {
            if (p != null && p.ContainsKey(key))
            {
                value = SkillEffectExecutor.GetInt(p, key, 0);
                return true;
            }

            value = 0;
            return false;
        }

        private float GetFloatParam(Dictionary<string, object> p, string key, float defaultVal)
        {
            return SkillEffectExecutor.GetFloat(p, key, defaultVal);
        }

        private string GetStringParam(Dictionary<string, object> p, string key, string defaultVal)
        {
            return SkillEffectExecutor.GetString(p, key, defaultVal);
        }

        private List<string> GetStringListParam(Dictionary<string, object> p, string key)
        {
            return SkillEffectExecutor.GetStringList(p, key);
        }

        private TEnum GetEnumParam<TEnum>(Dictionary<string, object> p, string key, TEnum defaultVal) where TEnum : struct
        {
            return SkillEffectExecutor.ParseEnum(GetStringParam(p, key, defaultVal.ToString()), defaultVal);
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
                "Allies" => PassiveTargetType.AllAllies,
                "AllEnemies" => PassiveTargetType.AllEnemies,
                "Enemies" => PassiveTargetType.AllEnemies,
                "ColumnAllies" => PassiveTargetType.ColumnAllies,
                "RowAllies" => PassiveTargetType.RowAllies,
                "FrontRowAllies" => PassiveTargetType.FrontRowAlly,
                "FrontRowAlly" => PassiveTargetType.FrontRowAlly,
                "BackRowAllies" => PassiveTargetType.BackRowAlly,
                "BackRowAlly" => PassiveTargetType.BackRowAlly,
                _ => PassiveTargetType.Self
            };
        }
    }
}
