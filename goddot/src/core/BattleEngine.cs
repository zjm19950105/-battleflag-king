using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Data;
using BattleKing.Events;
using BattleKing.Pipeline;
using BattleKing.Skills;

namespace BattleKing.Core
{
    public class BattleEngine
    {
        private BattleContext _ctx;
        private StrategyEvaluator _strategyEvaluator;
        private DamageCalculator _damageCalculator;
        private EventBus _eventBus = new EventBus();

        public Action<string> OnLog { get; set; }
        public EventBus EventBus => _eventBus;

        // Module 2: Pending action queue (counter-attacks, pursuit, preemptive)
        private Queue<PendingAction> _pendingActions = new Queue<PendingAction>();

        public void EnqueueAction(PendingAction action) => _pendingActions.Enqueue(action);

        public BattleEngine(BattleContext ctx)
        {
            _ctx = ctx;
            _strategyEvaluator = new StrategyEvaluator(ctx);
            _damageCalculator = new DamageCalculator();
        }

        private void Log(string message)
        {
            if (OnLog != null)
                OnLog(message);
            else
                System.Console.WriteLine(message);
        }

        public BattleResult StartBattle()
        {
            Log("=== 战斗开始 ===");
            PrintTeamStatus();

            _eventBus.Publish(new BattleStartEvent { Context = _ctx });

            // Process preemptive actions queued during BattleStart (e.g. Quick Strike)
            ProcessPendingActions();

            while (true)
            {
                _ctx.TurnCount++;
                Log($"--- 第 {_ctx.TurnCount} 回合 ---");

                var aliveUnits = _ctx.AllUnits.Where(u => u.IsAlive).ToList();
                if (aliveUnits.Count == 0)
                {
                    Log("双方全灭，平局！");
                    return EndBattle(BattleResult.Draw);
                }

                var turnOrder = aliveUnits.OrderByDescending(u => u.GetCurrentSpeed()).ToList();

                foreach (var unit in turnOrder)
                {
                    if (!unit.IsAlive)
                        continue;

                    var preCheck = CheckBattleEnd();
                    if (preCheck.HasValue)
                        return EndBattle(preCheck.Value);

                    ExecuteUnitTurn(unit);

                    // Process any pending counter/pursuit actions from this turn
                    ProcessPendingActions();
                }

                var postCheck = CheckBattleEnd();
                if (postCheck.HasValue)
                    return EndBattle(postCheck.Value);

                var apCheck = CheckApExhaustion();
                if (apCheck.HasValue)
                    return EndBattle(apCheck.Value);
            }
        }

        private BattleResult EndBattle(BattleResult result)
        {
            // Process BattleEnd pending actions (e.g. Finishing Strike) BEFORE judging winner
            ProcessPendingActions();

            _eventBus.Publish(new BattleEndEvent { Context = _ctx, Result = result });
            return result;
        }

        private void ExecuteUnitTurn(BattleUnit unit)
        {
            // Module 6: Charge state machine
            // If charging, execute the charged skill this turn
            if (unit.State == UnitState.Charging)
            {
                if (unit.ConsecutiveWaitCount >= 3)
                {
                    Log($"{unit.Data.Name} 蓄力超时（连续3回待机），蓄力解除");
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

                Log($"{unit.Data.Name} 蓄力发动！→ {chargeSkill.Name}");
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
                    Log($"{unit.Data.Name} 蓄力中，无法行动（第{unit.ConsecutiveWaitCount}次待机）");
                else
                    Log($"{unit.Data.Name} 无法行动，跳过");
                return;
            }

            if (!unit.CanUseActiveSkill(skill))
            {
                Log($"{unit.Data.Name} AP不足，无法行动，跳过");
                return;
            }

            // Check if skill has Charge tag → enter charging state, skip this turn
            if (skill.Data.Tags != null && skill.Data.Tags.Contains("Charge"))
            {
                Log($"{unit.Data.Name} 开始蓄力：{skill.Data.Name}（下次行动时发动）");
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

            if (unit.State != UnitState.Charging)  // AP already consumed for Charge skills
                unit.ConsumeAp(skill.ApCost);

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

                _eventBus.Publish(new BeforeHitEvent
                {
                    Attacker = unit,
                    Defender = target,
                    Skill = skill,
                    Context = _ctx,
                    Calc = calc
                });

                var result = _damageCalculator.Calculate(calc);

                string statusText = "";
                if (!result.IsHit)
                    statusText = "（未命中）";
                else if (result.IsEvaded)
                    statusText = "（被回避）";
                else
                {
                    if (result.IsCritical)
                        statusText += "（暴击）";
                    if (result.IsBlocked)
                        statusText += "（格挡）";
                }

                int damage = result.TotalDamage;
                if (result.IsHit && !result.IsEvaded)
                {
                    target.TakeDamage(damage);

                    // Publish OnKnockdown if target died
                    if (!target.IsAlive)
                    {
                        _eventBus.Publish(new OnKnockdownEvent
                        {
                            Victim = target,
                            Killer = unit,
                            Context = _ctx
                        });
                    }
                }

                _eventBus.Publish(new AfterHitEvent
                {
                    Attacker = unit,
                    Defender = target,
                    Skill = skill,
                    DamageDealt = damage,
                    IsHit = result.IsHit,
                    Context = _ctx
                });

                string aftermath = $"{target.Data.Name} 剩余 HP:{target.CurrentHp}";
                if (result.AppliedAilments.Count > 0)
                    aftermath += $", 附加状态: {string.Join(",", result.AppliedAilments)}";

                Log($"{unit.Data.Name} → {target.Data.Name} [{skill.Data.Name}] {damage}伤害{statusText} | {aftermath}");
            }

            _eventBus.Publish(new AfterActiveUseEvent { Caster = unit, Skill = skill, Context = _ctx });
        }

        private void ProcessPendingActions()
        {
            while (_pendingActions.Count > 0)
            {
                var action = _pendingActions.Dequeue();
                if (!action.Actor.IsAlive)
                    continue;

                foreach (var target in action.Targets)
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

                    _eventBus.Publish(new BeforeHitEvent
                    {
                        Attacker = action.Actor,
                        Defender = target,
                        Skill = tempSkill,
                        Context = _ctx,
                        Calc = calc
                    });

                    var result = _damageCalculator.Calculate(calc);

                    if (result.IsHit && !result.IsEvaded)
                    {
                        target.TakeDamage(result.TotalDamage);

                        if (!target.IsAlive)
                        {
                            _eventBus.Publish(new OnKnockdownEvent
                            {
                                Victim = target,
                                Killer = action.Actor,
                                Context = _ctx
                            });
                        }
                    }

                    _eventBus.Publish(new AfterHitEvent
                    {
                        Attacker = action.Actor,
                        Defender = target,
                        Skill = tempSkill,
                        DamageDealt = result.TotalDamage,
                        IsHit = result.IsHit,
                        Context = _ctx
                    });

                    Log($"[{action.Type}] {action.Actor.Data.Name} → {target.Data.Name} {result.TotalDamage}伤害 [{action.SourcePassiveId}]");
                }
            }
        }

        private ActiveSkill BuildTempSkill(PendingAction action)
        {
            var data = new ActiveSkillData
            {
                Id = $"temp_{action.SourcePassiveId}",
                Name = action.SourcePassiveId,
                ApCost = 0,
                Type = action.DamageType,
                AttackType = action.AttackType,
                Power = action.Power,
                HitRate = action.HitRate,
                TargetType = action.TargetType
            };
            return new ActiveSkill(data, _ctx.PlayerUnits.FirstOrDefault()?.GameData);
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
                Log($"  {u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp}");
            Log("敌方队伍:");
            foreach (var u in _ctx.EnemyUnits.Where(u => u != null))
                Log($"  {u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp}");
        }
    }
}
