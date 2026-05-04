using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Ai;
using BattleKing.Data;
using BattleKing.Pipeline;
using BattleKing.Skills;

namespace BattleKing.Core
{
    public class BattleEngine
    {
        private BattleContext _ctx;
        private StrategyEvaluator _strategyEvaluator;
        private DamageCalculator _damageCalculator;

        public Action<string> OnLog { get; set; }

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

            while (true)
            {
                _ctx.TurnCount++;
                Log($"--- 第 {_ctx.TurnCount} 回合 ---");

                var aliveUnits = _ctx.AllUnits.Where(u => u.IsAlive).ToList();
                if (aliveUnits.Count == 0)
                {
                    Log("双方全灭，平局！");
                    return BattleResult.Draw;
                }

                // Sort by speed descending
                var turnOrder = aliveUnits.OrderByDescending(u => u.GetCurrentSpeed()).ToList();

                foreach (var unit in turnOrder)
                {
                    if (!unit.IsAlive)
                        continue;

                    // Check win condition before each action
                    var preCheck = CheckBattleEnd();
                    if (preCheck.HasValue)
                        return preCheck.Value;

                    ExecuteUnitTurn(unit);
                }

                // Check win condition after the round
                var postCheck = CheckBattleEnd();
                if (postCheck.HasValue)
                    return postCheck.Value;

                // Check AP exhaustion
                var apCheck = CheckApExhaustion();
                if (apCheck.HasValue)
                    return apCheck.Value;
            }
        }

        private void ExecuteUnitTurn(BattleUnit unit)
        {
            var (skill, targets) = _strategyEvaluator.Evaluate(unit);

            if (skill == null || targets == null || targets.Count == 0)
            {
                Log($"{unit.Data.Name} 无法行动，跳过");
                return;
            }

            if (!unit.CanUseActiveSkill(skill))
            {
                Log($"{unit.Data.Name} AP不足，无法行动，跳过");
                return;
            }

            unit.ConsumeAp(skill.ApCost);

            foreach (var target in targets)
            {
                if (!target.IsAlive)
                    continue;

                var result = _damageCalculator.Calculate(unit, target, skill, _ctx);

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
                }

                Log($"{unit.Data.Name} 对 {target.Data.Name} 使用 {skill.Data.Name}，造成 {damage} 伤害{statusText}");
            }
        }

        private BattleResult? CheckBattleEnd()
        {
            bool playerAlive = _ctx.PlayerUnits.Any(u => u.IsAlive);
            bool enemyAlive = _ctx.EnemyUnits.Any(u => u.IsAlive);

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
            bool playerHasAp = _ctx.PlayerUnits.Any(u => u.IsAlive && u.CurrentAp > 0);
            bool enemyHasAp = _ctx.EnemyUnits.Any(u => u.IsAlive && u.CurrentAp > 0);

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
                totalMaxHp += u.Data.BaseStats.GetValueOrDefault("HP", 1);
                totalCurrentHp += u.CurrentHp;
            }
            if (totalMaxHp == 0) return 0;
            return (double)totalCurrentHp / totalMaxHp;
        }

        private void PrintTeamStatus()
        {
            Log("玩家队伍:");
            foreach (var u in _ctx.PlayerUnits)
                Log($"  {u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp}");
            Log("敌方队伍:");
            foreach (var u in _ctx.EnemyUnits)
                Log($"  {u.Data.Name} HP:{u.CurrentHp} AP:{u.CurrentAp}");
        }
    }
}
