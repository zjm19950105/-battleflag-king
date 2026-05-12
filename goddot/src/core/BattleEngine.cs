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
            _skillEffectExecutor = new SkillEffectExecutor();
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
            _eventBus.Publish(new BeforeActiveUseEvent { Caster = unit, Skill = skill, Context = _ctx });

            if (unit.State != UnitState.Charging)
                unit.ConsumeAp(skill.ApCost);

            Log($"--- {BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 发动 {skill.Data.Name} (AP{skill.ApCost} 威力{skill.Power}) [{DumpUnitBrief(unit)}] ---");

            var effectState = new SkillEffectExecutionState();
            var actionEffectLogs = _skillEffectExecutor.ExecuteActionEffects(_ctx, unit, targets, skill.Data.Effects, skill.Data.Id);
            if (actionEffectLogs.Count > 0)
                Log($"  effects: {string.Join(", ", actionEffectLogs)}");

            if (skill.Type == SkillType.Heal)
            {
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

                _ctx.CurrentCalc = calc;  // For AttackAttribute conditions
                _eventBus.Publish(new BeforeHitEvent
                {
                    Attacker = unit,
                    Defender = target,
                    Skill = skill,
                    Context = _ctx,
                    Calc = calc
                });

                var result = _damageCalculator.Calculate(calc);
                var damageReceiver = result.ResolvedDefender ?? calc.ResolvedDefender ?? target;

				bool killed = false;
				if (result.IsHit && result.TotalDamage > 0)
				{
					int hpBefore = damageReceiver.CurrentHp;
					damageReceiver.TakeDamage(result.TotalDamage);
					killed = !damageReceiver.IsAlive;
				}

				var logLines = BattleKing.Ui.BattleLogHelper.FormatAttack(unit, target, skill, calc, result, killed, result.AppliedAilments.ToList());
				foreach (var l in logLines) Log(l);
                EmitBattleLogEntry(CreateAttackLogEntry(unit, skill.Data.Id, damageReceiver, result, killed, "ActiveAttack", logLines.LastOrDefault() ?? ""));

				if (killed)
				{
					_eventBus.Publish(new OnKnockdownEvent
					{
						Victim = damageReceiver,
						Killer = unit,
						Context = _ctx
					});
				}

				_eventBus.Publish(new AfterHitEvent
				{
					Attacker = unit,
					Defender = damageReceiver,
					Skill = skill,
					DamageDealt = result.TotalDamage,
					IsHit = result.IsHit,
					Context = _ctx
				});
                // State already shown in multi-line log above
            }

            _eventBus.Publish(new AfterActiveUseEvent { Caster = unit, Skill = skill, Context = _ctx });

            Log($"  {BattleKing.Ui.BattleLogHelper.FormatUnitName(unit)} 行动结束: {DumpUnitBrief(unit)}");
        }

        private void ProcessPendingActions()
        {
            while (_pendingActions.Count > 0)
            {
                var action = _pendingActions.Dequeue();
                if (!action.Actor.IsAlive)
                    continue;

                foreach (var target in ExpandPendingActionTargets(action))
                {
                    if (!target.IsAlive)
                        continue;

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

                    _ctx.CurrentCalc = calc;  // For AttackAttribute conditions
                    _eventBus.Publish(new BeforeHitEvent
                    {
                        Attacker = action.Actor,
                        Defender = target,
                        Skill = tempSkill,
                        Context = _ctx,
                        Calc = calc
                    });

                    var result = _damageCalculator.Calculate(calc);
                    var damageReceiver = result.ResolvedDefender ?? calc.ResolvedDefender ?? target;
                    bool killed = false;

                    if (result.IsHit && result.TotalDamage > 0)
                    {
                        damageReceiver.TakeDamage(result.TotalDamage);
                        killed = !damageReceiver.IsAlive;

                        if (killed)
                        {
                            _eventBus.Publish(new OnKnockdownEvent
                            {
                                Victim = damageReceiver,
                                Killer = action.Actor,
                                Context = _ctx
                            });
                        }
                    }

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

                    _eventBus.Publish(new AfterHitEvent
                    {
                        Attacker = action.Actor,
                        Defender = damageReceiver,
                        Skill = tempSkill,
                        DamageDealt = result.TotalDamage,
                        IsHit = result.IsHit,
                        Context = _ctx
                    });

                    Log(pendingLogText);
                }
            }
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
                TargetType = action.TargetType
            };
            return new ActiveSkill(data, _ctx.PlayerUnits.FirstOrDefault()?.GameData);
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
                totalMaxHp += u.Data.BaseStats.GetValueOrDefault("HP", 1);
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
                .GroupBy(u => u.GetCurrentSpeed())
                .OrderByDescending(g => g.Key)
                .SelectMany(g => g.OrderBy(_ => RandUtil.Roll(int.MaxValue)))
                .ToList();
        }

        /// <summary>One-line unit summary: HP, AP, active buffs</summary>
        private static string DumpUnitBrief(BattleUnit u)
        {
            if (u == null || !u.IsAlive) return "[阵亡]";
            string s = $"HP:{u.CurrentHp}/{u.Data.BaseStats.GetValueOrDefault("HP",0)} AP:{u.CurrentAp} PP:{u.CurrentPp}/{u.MaxPp}";
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
            return $"[{action.Type}] actor={BattleKing.Ui.BattleLogHelper.FormatUnitName(action.Actor)}"
                + $" passive={GetPassiveDisplayName(action.SourcePassiveId)}"
                + $" target={BattleKing.Ui.BattleLogHelper.FormatUnitName(declaredTarget)}"
                + $" receiver={BattleKing.Ui.BattleLogHelper.FormatUnitName(damageReceiver)}"
                + $" damage={result.TotalDamage}"
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
