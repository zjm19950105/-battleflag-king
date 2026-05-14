using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Data;
using BattleKing.Events;
using BattleKing.Pipeline;
using BattleKing.Skills;
using BattleKing.Utils;

namespace BattleKing.Core
{
    public class BattleEngine
    {
        private BattleContext _ctx;
        private StrategyEvaluator _strategyEvaluator;
        private DamageCalculator _damageCalculator;
        private SkillEffectExecutor _skillEffectExecutor;
        private EventBus _eventBus = new EventBus();

        public Action<string> OnLog { get; set; }
        public Action<BattleLogEntry> OnBattleLogEntry { get; set; }
        public List<BattleLogEntry> BattleLogEntries { get; } = new List<BattleLogEntry>();
        public EventBus EventBus => _eventBus;

        // Module 2: Pending action queue
        private Queue<PendingAction> _pendingActions = new Queue<PendingAction>();
        public void EnqueueAction(PendingAction action) => _pendingActions.Enqueue(action);

        // Per-action stepping
        private Queue<BattleUnit> _turnQueue = new Queue<BattleUnit>();
        private int _actionsThisTurn = 0;
        private bool _battleEnded = false;
        private bool _battleEndPhaseStarted = false;
        private BattleResult _finalResult;

        public BattleEngine(BattleContext ctx)
        {
            _ctx = ctx;
            _strategyEvaluator = new StrategyEvaluator(ctx);
            _damageCalculator = new DamageCalculator();
            _skillEffectExecutor = new SkillEffectExecutor(EnqueueAction);
        }

        private void Log(string message)
        {
            if (OnLog != null)
                OnLog(message);
            else
                System.Console.WriteLine(message);
        }

        private void EmitBattleLogEntry(BattleLogEntry entry)
        {
            BattleLogEntries.Add(entry);
            OnBattleLogEntry?.Invoke(entry);
        }

        public BattleResult StartBattle()
        {
            InitBattle();
            while (true)
            {
                var r = StepBattle();
                if (r != BattleStepResult.Continue)
                    return ToBattleResult(r);
            }
        }

        /// <summary>Initialize battle — call once before stepping</summary>
        public void InitBattle()
        {
            _battleEnded = false;
            _battleEndPhaseStarted = false;
            _turnQueue.Clear();
            _actionsThisTurn = 0;
            _ctx.OutgoingActionAugments.Clear();
            Log("=== 战斗开始 ===");
            PrintTeamStatus();
            _eventBus.Publish(new BattleStartEvent { Context = _ctx });
            ProcessPendingActions();
        }

        /// <summary>Execute ONE turn. Returns Continue if battle still ongoing.</summary>
        public BattleStepResult StepBattle()
        {
            if (_battleEnded)
                return ToBattleStepResult(_finalResult);

            while (true)
            {
                var result = StepOneAction();
                if (result == SingleActionResult.ActionDone)
                    continue;
                if (result == SingleActionResult.TurnDone)
                    return BattleStepResult.Continue;
                return ToBattleStepResult(ToBattleResult(result));
            }
        }

        /// <summary>Execute ONE action (one unit's turn). Returns ActionDone/TurnDone/Win/Lose/Draw.</summary>
        public SingleActionResult StepOneAction()
        {
            if (_battleEnded)
                return ToSingleActionResult(_finalResult);

            // If queue is empty, start a new turn
            if (_turnQueue.Count == 0)
            {
                var turnStartResult = BeginNextTurn();
                if (turnStartResult.HasValue)
                    return EndBattleAction(turnStartResult.Value);
            }

            // Pop next alive unit
            var unit = DequeueNextActor();
            if (unit == null || !unit.IsAlive) { _turnQueue.Clear(); return StepOneAction(); }

            // Pre-check
            var preCheck = CheckBattleEnd();
            if (preCheck.HasValue)
                return EndBattleAction(preCheck.Value);

            _actionsThisTurn++;
            ExecuteUnitTurn(unit);
            ProcessPendingActions();
            CompleteAction(unit);
            Log("");

            // Post-check
            var postCheck = CheckBattleEnd();
            if (postCheck.HasValue)
                return EndBattleAction(postCheck.Value);

            if (_turnQueue.Count == 0)
            {
                var turnEndResult = CompleteTurn();
                if (turnEndResult.HasValue)
                    return EndBattleAction(turnEndResult.Value);
                return SingleActionResult.TurnDone;
            }

            return SingleActionResult.ActionDone;
        }

        private BattleResult? BeginNextTurn()
        {
            _ctx.TurnCount++;
            Log($"--- 第 {_ctx.TurnCount} 回合 ---");
            _actionsThisTurn = 0;

            var aliveUnits = _ctx.AllUnits.Where(u => u.IsAlive).ToList();
            if (aliveUnits.Count == 0)
            {
                Log("双方全灭，平局！");
                return BattleResult.Draw;
            }

            var ordered = OrderBySpeedWithRandomTies(aliveUnits);
            foreach (var u in ordered)
                _turnQueue.Enqueue(u);

            return null;
        }

        private BattleUnit DequeueNextActor()
        {
            while (_turnQueue.Count > 0)
            {
                var unit = _turnQueue.Dequeue();
                if (unit.IsAlive)
                    return unit;
            }
            return null;
        }

        private static void CompleteAction(BattleUnit unit)
        {
            unit.ActionCount++;
            BattleKing.Equipment.BuffManager.CleanupAfterAction(unit);
        }

        private BattleResult? CompleteTurn()
        {
            foreach (var u in _ctx.AllUnits.Where(u => u != null && u.IsAlive))
                BattleKing.Equipment.BuffManager.CleanupEndOfTurn(u);

            return CheckApExhaustion();
        }

        private BattleResult EndBattle(BattleResult result)
        {
            if (_battleEnded)
                return _finalResult;

            var finalResult = EnterBattleEndPhase(result);
            return FinalizeBattle(finalResult);
        }

        private BattleResult EnterBattleEndPhase(BattleResult preliminaryResult)
        {
            if (_battleEndPhaseStarted)
                return ResolveFinalBattleResult(preliminaryResult);

            _battleEndPhaseStarted = true;
            _turnQueue.Clear();

            _eventBus.Publish(new BattleEndEvent { Context = _ctx, Result = preliminaryResult });
            ProcessPendingActions();

            return ResolveFinalBattleResult(preliminaryResult);
        }

        private BattleResult FinalizeBattle(BattleResult result)
        {
            if (_battleEnded)
                return _finalResult;

            _battleEnded = true;
            _finalResult = result;
            _turnQueue.Clear();

            EmitBattleLogEntry(new BattleLogEntry
            {
                Turn = _ctx.TurnCount,
                Damage = 0,
                Flags = new List<string> { "BattleEnd", result.ToString() },
                Text = $"BattleEnd:{result}"
            });
            return result;
        }

        private BattleResult ResolveFinalBattleResult(BattleResult fallbackResult)
        {
            bool playerAlive = _ctx.PlayerUnits.Any(u => u != null && u.IsAlive);
            bool enemyAlive = _ctx.EnemyUnits.Any(u => u != null && u.IsAlive);

            if (playerAlive && !enemyAlive)
                return BattleResult.PlayerWin;
            if (!playerAlive && enemyAlive)
                return BattleResult.EnemyWin;
            if (!playerAlive && !enemyAlive)
                return BattleResult.Draw;

            bool playerHasAp = _ctx.PlayerUnits.Any(u => u != null && u.IsAlive && u.CurrentAp > 0);
            bool enemyHasAp = _ctx.EnemyUnits.Any(u => u != null && u.IsAlive && u.CurrentAp > 0);
            if (!playerHasAp && !enemyHasAp)
                return CompareHpRatio();

            return fallbackResult;
        }

        private BattleResult CompareHpRatio()
        {
            double playerHpRatio = GetHpRatio(_ctx.PlayerUnits);
            double enemyHpRatio = GetHpRatio(_ctx.EnemyUnits);

            if (playerHpRatio > enemyHpRatio)
                return BattleResult.PlayerWin;
            if (enemyHpRatio > playerHpRatio)
                return BattleResult.EnemyWin;
            return BattleResult.Draw;
        }

        private BattleStepResult EndBattleStep(BattleResult result)
        {
            return ToBattleStepResult(EndBattle(result));
        }

        private SingleActionResult EndBattleAction(BattleResult result)
        {
            return ToSingleActionResult(EndBattle(result));
        }

        private static BattleResult ToBattleResult(BattleStepResult result)
        {
            return result == BattleStepResult.PlayerWin ? BattleResult.PlayerWin
                : result == BattleStepResult.EnemyWin ? BattleResult.EnemyWin
                : BattleResult.Draw;
        }

        private static BattleResult ToBattleResult(SingleActionResult result)
        {
            return result == SingleActionResult.PlayerWin ? BattleResult.PlayerWin
                : result == SingleActionResult.EnemyWin ? BattleResult.EnemyWin
                : BattleResult.Draw;
        }

        private static BattleStepResult ToBattleStepResult(BattleResult result)
        {
            return result == BattleResult.PlayerWin ? BattleStepResult.PlayerWin
                : result == BattleResult.EnemyWin ? BattleStepResult.EnemyWin
                : BattleStepResult.Draw;
        }

        private static SingleActionResult ToSingleActionResult(BattleResult result)
        {
            return result == BattleResult.PlayerWin ? SingleActionResult.PlayerWin
                : result == BattleResult.EnemyWin ? SingleActionResult.EnemyWin
                : SingleActionResult.Draw;
        }

        private void ExecuteUnitTurn(BattleUnit unit)
        {
            if (unit.State == UnitState.Stunned)
            {
                Log($"{BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 气绝中，跳过本次行动");
                unit.State = UnitState.Normal;
                unit.ConsecutiveWaitCount = 0;
                return;
            }

            // Module 6: Charge state machine
            // If charging, execute the charged skill this turn
            if (unit.State == UnitState.Charging)
            {
                if (unit.ConsecutiveWaitCount >= 3)
                {
                    Log($"{BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 蓄力超时（连续3回待机），蓄力解除");
                    unit.State = UnitState.Normal;
                    unit.ChargedSkillId = null;
                    unit.ConsecutiveWaitCount = 0;
                    return;
                }

                // Execute the charged skill
                var chargeSkill = unit.GameData.GetActiveSkill(unit.ChargedSkillId);
                if (chargeSkill == null)
                {
                    unit.State = UnitState.Normal;
                    unit.ChargedSkillId = null;
                    return;
                }

                Log($"{BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 蓄力发动！→ {chargeSkill.Name}");
                unit.State = UnitState.Normal;
                unit.ChargedSkillId = null;

                var chargeTargets = _strategyEvaluator.SelectTargetsForSkill(unit, chargeSkill);
                ExecuteSkillAgainstTargets(unit, new BattleKing.Skills.ActiveSkill(chargeSkill, unit.GameData), chargeTargets);
                return;
            }

            // Normal flow: evaluate strategy
            var (skill, targets) = _strategyEvaluator.Evaluate(unit);

            if (skill == null || targets == null || targets.Count == 0)
            {
                unit.ConsecutiveWaitCount++;
                // If charging state somehow without skill, count as wait
                if (unit.State == UnitState.Charging)
                    Log($"{BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 蓄力中，无法行动（第{unit.ConsecutiveWaitCount}次待机）");
                else
                    Log($"{BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 无法行动，跳过");
                return;
            }

            if (!unit.CanUseActiveSkill(skill))
            {
                Log($"{BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} AP不足，无法行动，跳过");
                return;
            }

            // Check if skill has Charge tag → enter charging state, skip this turn
            if (skill.Data.Tags != null && skill.Data.Tags.Contains("Charge"))
            {
                Log($"{BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 开始蓄力：{skill.Data.Name}（下次行动时发动）");
                unit.State = UnitState.Charging;
                unit.ChargedSkillId = skill.Data.Id;
                unit.ConsecutiveWaitCount = 0;
                unit.ConsumeAp(skill.ApCost);  // AP consumed on charge start
                return;
            }

            unit.ConsecutiveWaitCount = 0;
            ExecuteSkillAgainstTargets(unit, skill, targets);
        }

        private void ExecuteSkillAgainstTargets(BattleUnit unit, BattleKing.Skills.ActiveSkill skill, List<BattleUnit> targets)
        {
            _ctx.CurrentActionAugments.Clear();
            _ctx.CurrentActionSkill = skill;
            AddOutgoingActionAugments(unit, skill);
            _eventBus.Publish(new BeforeActiveUseEvent { Caster = unit, Skill = skill, Context = _ctx });

            if (unit.State != UnitState.Charging)
            {
                int apBefore = unit.CurrentAp;
                unit.ConsumeAp(skill.ApCost);
                Log($"  AP消耗: {BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} {apBefore}->{unit.CurrentAp}");
            }

            _eventBus.Publish(new AfterActiveCostEvent { Caster = unit, Skill = skill, Context = _ctx });
            _eventBus.Publish(new BeforeAttackCalculationEvent { Caster = unit, Skill = skill, Context = _ctx });

            Log($"--- {BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 发动 {skill.Data.Name} (AP{skill.ApCost} 威力{skill.Power}) [{DumpUnitBrief(unit)}] ---");

            var effectState = new SkillEffectExecutionState();
            var augmentEffectState = new SkillEffectExecutionState();
            var actionEffectLogs = _skillEffectExecutor.ExecuteActionEffects(_ctx, unit, targets, skill.Data.Effects, skill.Data.Id);
            if (actionEffectLogs.Count > 0)
                Log($"  effects: {string.Join(", ", actionEffectLogs)}");

            if (skill.Type == SkillType.Heal)
            {
                _ctx.CurrentActionAugments.Clear();
                _ctx.CurrentActionSkill = null;
                _eventBus.Publish(new AfterActiveUseEvent { Caster = unit, Skill = skill, Context = _ctx });
                return;
            }

            foreach (var target in targets)
            {
                if (!target.IsAlive)
                    continue;

                // Create DamageCalculation BEFORE publishing BeforeHitEvent — passive skills can modify it
                var calc = new DamageCalculation
                {
                    Attacker = unit,
                    Defender = target,
                    Skill = skill,
                    HitCount = 1  // Default; multi-hit skills override this via effects
                };

                var calculationEffectLogs = _skillEffectExecutor.ExecuteCalculationEffects(
                    _ctx, unit, new List<BattleUnit> { target }, skill.Data.Effects, skill.Data.Id, calc, effectState);
                if (calculationEffectLogs.Count > 0)
                    Log($"  calc effects: {string.Join(", ", calculationEffectLogs)}");

                var augmentCalcLogs = ExecuteCurrentActionAugmentCalculationEffects(unit, target, calc, augmentEffectState);
                if (augmentCalcLogs.Count > 0)
                    Log($"  augment calc effects: {string.Join(", ", augmentCalcLogs)}");

                _ctx.CurrentCalc = calc;  // For AttackAttribute conditions
                _eventBus.Publish(new BeforeHitEvent
                {
                    Attacker = unit,
                    Defender = target,
                    Skill = skill,
                    Context = _ctx,
                    Calc = calc,
                    SourceKind = BattleActionSourceKind.ActiveAttack
                });

                var result = _damageCalculator.Calculate(calc);
                var damageReceiver = result.ResolvedDefender ?? calc.ResolvedDefender ?? target;
                calc.ResolvedDefender = damageReceiver;

				bool killed = ApplyDamageAndRecordHp(result, damageReceiver);

				var logLines = BattleKing.Ui.BattleLogHelper.FormatAttack(unit, target, skill, calc, result, killed, result.AppliedAilments.ToList());
				foreach (var l in logLines) Log(l);
                EmitBattleLogEntry(CreateAttackLogEntry(
                    unit,
                    skill.Data.Id,
                    damageReceiver,
                    result,
                    killed,
                    "ActiveAttack",
                    AppendCurrentActionAugmentLog(logLines.LastOrDefault() ?? ""),
                    extraFlags: BuildCurrentActionAugmentFlags()));

				if (killed)
				{
					_eventBus.Publish(new OnKnockdownEvent
					{
						Victim = damageReceiver,
						Killer = unit,
						Context = _ctx
					});
				}

                var postEffectLogs = _skillEffectExecutor.ExecutePostDamageEffects(
                    _ctx, unit, damageReceiver, skill.Data.Effects, skill.Data.Id, calc, result, killed);
                if (postEffectLogs.Count > 0)
                    Log($"  post effects: {string.Join(", ", postEffectLogs)}");

                var augmentPostEffectLogs = ExecuteCurrentActionAugmentPostDamageEffects(unit, damageReceiver, calc, result, killed);
                if (augmentPostEffectLogs.Count > 0)
                    Log($"  augment post effects: {string.Join(", ", augmentPostEffectLogs)}");

				_eventBus.Publish(new AfterHitEvent
				{
					Attacker = unit,
					Defender = damageReceiver,
					Skill = skill,
					DamageDealt = result.TotalDamage,
					IsHit = result.IsHit,
					Context = _ctx,
					SourceKind = BattleActionSourceKind.ActiveAttack
				});
                // State already shown in multi-line log above
            }

            var augmentQueuedActionLogs = ExecuteCurrentActionAugmentQueuedActions(targets);
            if (augmentQueuedActionLogs.Count > 0)
                Log($"  augment queued actions: {string.Join(", ", augmentQueuedActionLogs)}");

            _ctx.CurrentActionAugments.Clear();
            _ctx.CurrentActionSkill = null;
            _eventBus.Publish(new AfterActiveUseEvent { Caster = unit, Skill = skill, Context = _ctx });
            ProcessPendingActions();

            Log($"  {BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 行动结束: {DumpUnitBrief(unit)}");
        }

        private void AddOutgoingActionAugments(BattleUnit unit, ActiveSkill skill)
        {
            foreach (var augment in _ctx.OutgoingActionAugments
                .Where(augment => augment.IsPlayerSide == unit.IsPlayer
                    && SkillEffectExecutor.CurrentActionRequirementMatches(augment.RequirementParameters, skill)))
            {
                _ctx.CurrentActionAugments.Add(new CurrentActionAugment
                {
                    Actor = unit,
                    SourcePassiveId = augment.SourcePassiveId,
                    CalculationEffects = augment.CalculationEffects,
                    OnHitEffects = augment.OnHitEffects,
                    Tags = augment.Tags
                });
            }
        }

        private List<string> ExecuteCurrentActionAugmentCalculationEffects(
            BattleUnit unit,
            BattleUnit target,
            DamageCalculation calc,
            SkillEffectExecutionState state)
        {
            var logs = new List<string>();
            foreach (var augment in _ctx.CurrentActionAugments)
            {
                var tagged = ApplyCurrentActionAugmentTags(calc, augment.Tags);
                if (tagged.Count > 0)
                    logs.Add($"{augment.SourcePassiveId}: tags={FormatTags(tagged)}");

                var effectLogs = _skillEffectExecutor.ExecuteCalculationEffects(
                    _ctx,
                    unit,
                    new List<BattleUnit> { target },
                    augment.CalculationEffects,
                    augment.SourcePassiveId,
                    calc,
                    state);
                logs.AddRange(effectLogs.Select(log => $"{augment.SourcePassiveId}: {log}"));
            }

            return logs;
        }

        private List<string> ExecuteCurrentActionAugmentPostDamageEffects(
            BattleUnit unit,
            BattleUnit damageReceiver,
            DamageCalculation calc,
            DamageResult result,
            bool killed)
        {
            var logs = new List<string>();
            foreach (var augment in _ctx.CurrentActionAugments.Where(augment => augment.OnHitEffects.Count > 0))
            {
                var wrapped = new List<SkillEffectData>
                {
                    new SkillEffectData
                    {
                        EffectType = "OnHitEffect",
                        Parameters = new Dictionary<string, object>
                        {
                            { "effects", augment.OnHitEffects }
                        }
                    }
                };

                var effectLogs = _skillEffectExecutor.ExecutePostDamageEffects(
                    _ctx,
                    unit,
                    damageReceiver,
                    wrapped,
                    augment.SourcePassiveId,
                    calc,
                    result,
                    killed);
                logs.AddRange(effectLogs.Select(log => $"{augment.SourcePassiveId}: {log}"));
            }

            return logs;
        }

        private static bool ApplyDamageAndRecordHp(DamageResult result, BattleUnit damageReceiver)
        {
            int hpBefore = damageReceiver.CurrentHp;
            if (result.IsHit && result.TotalDamage > 0)
                damageReceiver.TakeDamage(result.TotalDamage);

            result.RecordHpChange(hpBefore, damageReceiver.CurrentHp);
            return result.IsHit && result.TotalDamage > 0 && !damageReceiver.IsAlive;
        }

        private List<string> ExecuteCurrentActionAugmentQueuedActions(IReadOnlyList<BattleUnit> targets)
        {
            var logs = new List<string>();
            var actionTargets = targets?.Where(target => target != null).ToList() ?? new List<BattleUnit>();
            if (actionTargets.Count == 0)
                return logs;

            foreach (var augment in _ctx.CurrentActionAugments.Where(augment => augment.QueuedActionEffects.Count > 0))
            {
                if (augment.Actor?.IsAlive != true)
                    continue;

                var effectLogs = _skillEffectExecutor.ExecuteActionEffects(
                    _ctx,
                    augment.Actor,
                    actionTargets,
                    augment.QueuedActionEffects,
                    augment.SourcePassiveId);
                foreach (var action in _pendingActions.Where(action =>
                    string.Equals(action.SourcePassiveId, augment.SourcePassiveId, StringComparison.Ordinal)
                    && action.Actor == augment.Actor
                    && action.SourcePpCost == 0))
                {
                    action.SourcePpCost = augment.SourcePpCost;
                    action.SourceTimingLabel = augment.SourceTimingLabel;
                }
                logs.AddRange(effectLogs.Select(log => $"{augment.SourcePassiveId}: {log}"));
            }

            return logs;
        }

        private static List<string> ApplyCurrentActionAugmentTags(DamageCalculation calc, IEnumerable<string> tags)
        {
            var applied = tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct().ToList()
                ?? new List<string>();
            if (calc == null)
                return applied;

            if (applied.Contains("SureHit"))
                calc.ForceHit = true;
            if (applied.Contains("CannotBeBlocked"))
                calc.CannotBeBlocked = true;
            if (applied.Contains("CannotBeCovered"))
                calc.CannotBeCovered = true;

            return applied;
        }

        private string AppendCurrentActionAugmentLog(string text)
        {
            var augments = _ctx.CurrentActionAugments;
            if (augments.Count == 0)
                return text;

            var sources = augments
                .Select(augment => augment.SourcePassiveId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            var tags = augments
                .SelectMany(augment => augment.Tags ?? new List<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct()
                .ToList();
            return text
                + $" augments={FormatTags(sources)}"
                + $" augmentTags={FormatTags(tags)}";
        }

        private IEnumerable<string> BuildCurrentActionAugmentFlags()
        {
            var flags = new List<string>();
            foreach (var augment in _ctx.CurrentActionAugments)
            {
                if (!string.IsNullOrWhiteSpace(augment.SourcePassiveId))
                    flags.Add($"Augment:{augment.SourcePassiveId}");
                flags.AddRange((augment.Tags ?? new List<string>()).Where(tag => !string.IsNullOrWhiteSpace(tag)));
            }

            return flags.Distinct();
        }

        private void ProcessPendingActions()
        {
            while (_pendingActions.Count > 0)
            {
                var action = _pendingActions.Dequeue();
                if (!action.Actor.IsAlive)
                    continue;

                var pendingTargets = ExpandPendingActionTargets(action)
                    .Where(target => target != null && target.IsAlive)
                    .ToList();
                if (pendingTargets.Count == 0)
                    continue;
                if (!TryActivatePendingAction(action))
                    continue;

                foreach (var target in pendingTargets)
                {
                    // Build a temporary ActiveSkill for the pending attack
                    var tempSkill = BuildTempSkill(action);

                    var calc = new DamageCalculation
                    {
                        Attacker = action.Actor,
                        Defender = target,
                        Skill = tempSkill,
                        HitCount = 1
                    };

                    // Apply tags from the pending action
                    if (action.Tags.Contains("SureHit"))
                        calc.ForceHit = true;
                    if (action.Tags.Contains("CannotBeBlocked"))
                        calc.CannotBeBlocked = true;
                    if (action.Tags.Contains("CannotBeCovered"))
                        calc.CannotBeCovered = true;
                    if (ShouldApplyPendingIgnoreDefense(action, target))
                        calc.IgnoreDefenseRatio = Math.Clamp(action.IgnoreDefenseRatio, 0f, 1f);

                    var pendingCalculationEffectLogs = _skillEffectExecutor.ExecuteCalculationEffects(
                        _ctx,
                        action.Actor,
                        new List<BattleUnit> { target },
                        tempSkill.Data.Effects,
                        action.SourcePassiveId,
                        calc,
                        new SkillEffectExecutionState());
                    if (pendingCalculationEffectLogs.Count > 0)
                        Log($"  pending calc effects: {string.Join(", ", pendingCalculationEffectLogs)}");

                    _ctx.CurrentCalc = calc;  // For AttackAttribute conditions
                    _eventBus.Publish(new BeforeHitEvent
                    {
                        Attacker = action.Actor,
                        Defender = target,
                        Skill = tempSkill,
                        Context = _ctx,
                        Calc = calc,
                        SourceKind = BattleActionSourceKind.PendingAction
                    });

                    var result = _damageCalculator.Calculate(calc);
                    var damageReceiver = result.ResolvedDefender ?? calc.ResolvedDefender ?? target;
                    calc.ResolvedDefender = damageReceiver;
                    bool killed = ApplyDamageAndRecordHp(result, damageReceiver);
                    if (killed)
                    {
                        _eventBus.Publish(new OnKnockdownEvent
                        {
                            Victim = damageReceiver,
                            Killer = action.Actor,
                            Context = _ctx
                        });
                    }

                    var postEffectLogs = _skillEffectExecutor.ExecutePostDamageEffects(
                        _ctx, action.Actor, damageReceiver, tempSkill.Data.Effects, action.SourcePassiveId, calc, result, killed);
                    if (postEffectLogs.Count > 0)
                        Log($"  post effects: {string.Join(", ", postEffectLogs)}");

                    var pendingLogText = BuildPendingActionLogText(action, target, damageReceiver, calc, result, killed);
                    EmitBattleLogEntry(CreateAttackLogEntry(
                        action.Actor,
                        action.SourcePassiveId,
                        damageReceiver,
                        result,
                        killed,
                        "PassiveTrigger",
                        pendingLogText,
                        action.Type.ToString(),
                        BuildPendingActionFlags(action, calc)));

                    Log(pendingLogText);

                    _eventBus.Publish(new AfterHitEvent
                    {
                        Attacker = action.Actor,
                        Defender = damageReceiver,
                        Skill = tempSkill,
                        DamageDealt = result.TotalDamage,
                        IsHit = result.IsHit,
                        Context = _ctx,
                        SourceKind = BattleActionSourceKind.PendingAction
                    });
                }
            }
        }

        private bool TryActivatePendingAction(PendingAction action)
        {
            if (action.SourcePpCost <= 0)
                return true;

            if (action.Actor.CurrentPp < action.SourcePpCost)
                return false;

            action.Actor.ConsumePp(action.SourcePpCost);
            Log($"  [被动] {BattleKing.Ui.BattleLogHelper.FormatUnitName(action.Actor)} 触发 {GetPassiveName(action.SourcePassiveId)} ({action.SourceTimingLabel ?? action.Type.ToString()})");
            return true;
        }

        private ActiveSkill BuildTempSkill(PendingAction action)
        {
            var data = new ActiveSkillData
            {
                Id = $"temp_{action.SourcePassiveId}",
                Name = GetPassiveDisplayName(action.SourcePassiveId),
                ApCost = 0,
                Type = action.DamageType,
                AttackType = action.AttackType,
                Power = action.Power,
                HitRate = action.HitRate,
                TargetType = action.TargetType,
                Effects = GetPendingActionEffects(action.SourcePassiveId)
            };
            return new ActiveSkill(data, _ctx.PlayerUnits.FirstOrDefault()?.GameData);
        }

        private List<SkillEffectData> GetPendingActionEffects(string sourceSkillId)
        {
            if (string.IsNullOrWhiteSpace(sourceSkillId) || _ctx?.GameData == null)
                return new List<SkillEffectData>();

            if (_ctx.GameData.PassiveSkills.TryGetValue(sourceSkillId, out var passive))
                return passive.Effects ?? new List<SkillEffectData>();

            if (_ctx.GameData.ActiveSkills.TryGetValue(sourceSkillId, out var active))
                return active.Effects ?? new List<SkillEffectData>();

            return new List<SkillEffectData>();
        }

        private List<BattleUnit> ExpandPendingActionTargets(PendingAction action)
        {
            var anchors = action.Targets?
                .Where(t => t != null && t.IsAlive)
                .ToList() ?? new List<BattleUnit>();

            if (anchors.Count == 0)
                return anchors;

            return action.TargetType switch
            {
                TargetType.Row => anchors
                    .SelectMany(anchor => _ctx.GetAliveUnits(anchor.IsPlayer)
                        .Where(unit => unit.IsFrontRow == anchor.IsFrontRow))
                    .Distinct()
                    .ToList(),
                TargetType.Column => anchors
                    .SelectMany(anchor => _ctx.GetAliveUnits(anchor.IsPlayer)
                        .Where(unit => IsSameColumn(unit.Position, anchor.Position)))
                    .Distinct()
                    .ToList(),
                _ => anchors
            };
        }

        private static bool ShouldApplyPendingIgnoreDefense(PendingAction action, BattleUnit target)
        {
            if (action.IgnoreDefenseRatio <= 0f || target == null)
                return false;

            return !action.IgnoreDefenseTargetClass.HasValue
                || target.GetEffectiveClasses().Contains(action.IgnoreDefenseTargetClass.Value);
        }

        private static bool IsSameColumn(int a, int b)
        {
            if (a <= 0 || b <= 0)
                return false;

            return (a - 1) % 3 == (b - 1) % 3;
        }

        private BattleResult? CheckBattleEnd()
        {
            bool playerAlive = _ctx.PlayerUnits.Any(u => u != null && u.IsAlive);
            bool enemyAlive = _ctx.EnemyUnits.Any(u => u != null && u.IsAlive);

            if (playerAlive && !enemyAlive)
            {
                Log("\n=== 玩家胜利 ===");
                return BattleResult.PlayerWin;
            }
            if (!playerAlive && enemyAlive)
            {
                Log("\n=== 敌方胜利 ===");
                return BattleResult.EnemyWin;
            }
            if (!playerAlive && !enemyAlive)
            {
                Log("\n=== 双方全灭，平局 ===");
                return BattleResult.Draw;
            }
            return null;
        }

        private BattleResult? CheckApExhaustion()
        {
            bool playerHasAp = _ctx.PlayerUnits.Any(u => u != null && u.IsAlive && u.CurrentAp > 0);
            bool enemyHasAp = _ctx.EnemyUnits.Any(u => u != null && u.IsAlive && u.CurrentAp > 0);

            if (!playerHasAp && !enemyHasAp)
            {
                Log("\n=== 双方AP耗尽 ===");
                double playerHpRatio = GetHpRatio(_ctx.PlayerUnits);
                double enemyHpRatio = GetHpRatio(_ctx.EnemyUnits);

                Log($"玩家HP比例: {playerHpRatio:F2}, 敌方HP比例: {enemyHpRatio:F2}");

                if (playerHpRatio > enemyHpRatio)
                {
                    Log("=== 玩家胜利（HP优势）===");
                    return BattleResult.PlayerWin;
                }
                if (enemyHpRatio > playerHpRatio)
                {
                    Log("=== 敌方胜利（HP优势）===");
                    return BattleResult.EnemyWin;
                }
                Log("=== 平局 ===");
                return BattleResult.Draw;
            }
            return null;
        }

        private double GetHpRatio(List<BattleUnit> units)
        {
            int totalMaxHp = 0;
            int totalCurrentHp = 0;
            foreach (var u in units)
            {
                if (u == null) continue;
                totalMaxHp += Math.Max(1, u.GetCurrentStat("HP"));
                totalCurrentHp += u.CurrentHp;
            }
            if (totalMaxHp == 0) return 0;
            return (double)totalCurrentHp / totalMaxHp;
        }

        private void PrintTeamStatus()
        {
            Log("玩家队伍:");
            foreach (var u in _ctx.PlayerUnits.Where(u => u != null))
                Log($"  {BattleKing.Ui.BattleLogHelper.FormatUnitName(u)} HP:{u.CurrentHp} AP:{u.CurrentAp}");
            Log("敌方队伍:");
            foreach (var u in _ctx.EnemyUnits.Where(u => u != null))
                Log($"  {BattleKing.Ui.BattleLogHelper.FormatUnitName(u)} HP:{u.CurrentHp} AP:{u.CurrentAp}");
        }

        private static List<BattleUnit> OrderBySpeedWithRandomTies(IEnumerable<BattleUnit> units)
        {
            return units
                .GroupBy(u => new { u.ActionOrderPriority, Speed = u.GetCurrentSpeed() })
                .OrderByDescending(g => g.Key.ActionOrderPriority)
                .ThenByDescending(g => g.Key.Speed)
                .SelectMany(g => g.OrderBy(_ => RandUtil.Roll(int.MaxValue)))
                .ToList();
        }

        /// <summary>One-line unit summary: HP, AP, active buffs</summary>
        private static string DumpUnitBrief(BattleUnit u)
        {
            if (u == null || !u.IsAlive) return "[阵亡]";
            string s = $"HP:{u.CurrentHp}/{Math.Max(1, u.GetCurrentStat("HP"))} AP:{u.CurrentAp}/{u.MaxAp} PP:{u.CurrentPp}/{u.MaxPp}";
            var buffs = u.Buffs.Where(b => b.Ratio != 0).Select(b => {
                string sign = b.Ratio > 0 ? "+" : "";
                return $"{b.TargetStat}{sign}{(int)(b.Ratio*100)}%";
            }).ToList();
            if (buffs.Count > 0) s += " [" + string.Join(",", buffs) + "]";
            if (u.State != UnitState.Normal) s += $" [{u.State}]";
            return s;
        }

        private BattleLogEntry CreateAttackLogEntry(
            BattleUnit actor,
            string skillId,
            BattleUnit damageReceiver,
            DamageResult result,
            bool killed,
            string primaryFlag,
            string text,
            string secondaryFlag = null,
            IEnumerable<string> extraFlags = null)
        {
            var flags = new List<string> { primaryFlag };
            if (!string.IsNullOrWhiteSpace(secondaryFlag))
                flags.Add(secondaryFlag);
            if (result.IsHit)
                flags.Add("Hit");
            if (!result.IsHit)
                flags.Add("Miss");
            if (result.IsEvaded)
                flags.Add("Evade");
            if (result.IsCritical)
                flags.Add("Critical");
            if (result.IsBlocked)
                flags.Add("Blocked");
            if (result.LethalDamageResisted)
                flags.Add("DeathResist");
            if (killed)
                flags.Add("Knockdown");
            flags.AddRange(result.AppliedAilments.Select(a => a.ToString()));
            if (extraFlags != null)
                flags.AddRange(extraFlags.Where(f => !string.IsNullOrWhiteSpace(f)));

            return new BattleLogEntry
            {
                Turn = _ctx.TurnCount,
                ActorId = actor?.Data?.Id ?? "",
                SkillId = skillId ?? "",
                TargetIds = damageReceiver == null ? new List<string>() : new List<string> { damageReceiver.Data.Id },
                Damage = result.TotalDamage,
                HpBefore = result.DamageReceiverHpBefore,
                HpAfter = result.DamageReceiverHpAfter,
                HpLost = result.DamageReceiverHpBefore.HasValue && result.DamageReceiverHpAfter.HasValue
                    ? (int?)result.AppliedHpDamage
                    : null,
                Flags = flags.Distinct().ToList(),
                Text = text ?? ""
            };
        }

        private string BuildPendingActionLogText(
            PendingAction action,
            BattleUnit declaredTarget,
            BattleUnit damageReceiver,
            DamageCalculation calc,
            DamageResult result,
            bool killed)
        {
            return $"[{action.Type}] actor={FormatPendingUnit(action.Actor)}"
                + $" passive={GetPassiveDisplayName(action.SourcePassiveId)}"
                + $" target={FormatPendingUnit(declaredTarget)}"
                + $" receiver={FormatPendingUnit(damageReceiver)}"
                + $" damage={result.TotalDamage}"
                + FormatPendingHpChange(result)
                + $" hit={result.IsHit}"
                + $" blocked={result.IsBlocked}"
                + $" critical={result.IsCritical}"
                + $" evaded={result.IsEvaded}"
                + $" killed={killed}"
                + $" sureHit={calc.ForceHit}"
                + $" ignoreDefense={calc.IgnoreDefenseRatio:0.##}"
                + $" ailments={FormatAilments(result.AppliedAilments)}"
                + $" temporary=actor:{FormatTemporalStates(action.Actor)};receiver:{FormatTemporalStates(damageReceiver)}"
                + $" tags={FormatTags(action.Tags)}";
        }

        private static string FormatPendingUnit(BattleUnit unit)
        {
            if (unit == null)
                return "";

            return $"{BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)}(id={unit.Data?.Id ?? ""},pos={unit.Position})";
        }

        private static string FormatPendingHpChange(DamageResult result)
        {
            if (!result.DamageReceiverHpBefore.HasValue || !result.DamageReceiverHpAfter.HasValue)
                return "";

            string text = $" hp={result.DamageReceiverHpBefore.Value}->{result.DamageReceiverHpAfter.Value}(-{result.AppliedHpDamage})";
            if (result.LethalDamageResisted)
                text += " deathResist=true";
            return text;
        }

        private string GetPassiveDisplayName(string passiveId)
        {
            if (!string.IsNullOrWhiteSpace(passiveId)
                && _ctx?.GameData?.PassiveSkills?.TryGetValue(passiveId, out var passive) == true
                && !string.IsNullOrWhiteSpace(passive.Name))
            {
                return $"{passive.Name} ({passiveId})";
            }

            return passiveId ?? "";
        }

        private string GetPassiveName(string passiveId)
        {
            if (!string.IsNullOrWhiteSpace(passiveId)
                && _ctx?.GameData?.PassiveSkills?.TryGetValue(passiveId, out var passive) == true
                && !string.IsNullOrWhiteSpace(passive.Name))
            {
                return passive.Name;
            }

            return passiveId ?? "";
        }

        private static IEnumerable<string> BuildPendingActionFlags(PendingAction action, DamageCalculation calc)
        {
            var flags = new List<string>();
            if (calc.ForceHit)
                flags.Add("ForceHit");
            if (calc.CannotBeBlocked)
                flags.Add("CannotBeBlocked");
            if (calc.CannotBeCovered)
                flags.Add("CannotBeCovered");
            if (calc.IgnoreDefenseRatio > 0f)
                flags.Add($"IgnoreDefense={calc.IgnoreDefenseRatio:0.##}");
            flags.AddRange(action.Tags ?? new List<string>());
            return flags;
        }

        private static string FormatAilments(IEnumerable<StatusAilment> ailments)
        {
            var values = ailments?.Select(a => a.ToString()).Where(a => !string.IsNullOrWhiteSpace(a)).ToList()
                ?? new List<string>();
            return values.Count == 0 ? "None" : string.Join("|", values);
        }

        private static string FormatTemporalStates(BattleUnit unit)
        {
            var states = unit?.TemporalStates?
                .Select(s => $"{s.Key}:{s.RemainingCount}")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList() ?? new List<string>();
            return states.Count == 0 ? "None" : string.Join("|", states);
        }

        private static string FormatTags(IEnumerable<string> tags)
        {
            var values = tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
            return values.Count == 0 ? "None" : string.Join("|", values);
        }
    }
}
