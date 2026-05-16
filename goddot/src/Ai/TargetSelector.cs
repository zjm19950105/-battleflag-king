using System;
using System.Collections.Generic;
using System.Linq;
using BattleKing.Core;
using BattleKing.Data;

namespace BattleKing.Ai
{
	public class TargetSelector
	{
		private BattleContext _ctx;
		private ConditionEvaluator _conditionEvaluator;
		private Random _random;

		public TargetSelector(BattleContext ctx, Random random = null)
		{
			_ctx = ctx;
			_conditionEvaluator = new ConditionEvaluator(ctx);
			_random = random ?? Random.Shared;
		}

		public List<BattleUnit> SelectTargets(BattleUnit caster, Strategy strategy, ActiveSkillData skill)
		{
			var defaultTargets = skill.TargetType == TargetType.Self
				? new List<BattleUnit> { caster }
				: GetDefaultTargetList(caster, skill);

			bool hasStrategyConditions = HasStrategyConditions(strategy);
			var candidates = ApplyStrategyConditions(defaultTargets, strategy, caster, skill);
			var forcedTargets = GetForcedTargetCandidates(caster, skill, defaultTargets);
			bool hasForcedTargets = forcedTargets.Count > 0;
			if (hasForcedTargets)
				candidates = OrderByGroups(defaultTargets, forcedTargets);

			if (candidates == null || candidates.Count == 0)
				return null;

			bool useRandomTargeting = !hasStrategyConditions && !hasForcedTargets;

			return skill.TargetType switch
			{
				TargetType.Self => new List<BattleUnit> { candidates.First() },
				TargetType.SingleEnemy => new List<BattleUnit> { PickSingleEnemyTarget(candidates, useRandomTargeting) },
				TargetType.SingleAlly => new List<BattleUnit> { candidates.First() },
				TargetType.TwoEnemies => candidates.Take(2).ToList(),
				TargetType.ThreeEnemies => candidates.Take(3).ToList(),
				TargetType.FrontAndBack => GetPiercingTargets(candidates.First()),
				TargetType.Column => GetColumnTargets(candidates.First()),
				TargetType.Row => GetRowTargets(candidates.First(), defaultTargets),
				TargetType.AllEnemies => candidates.ToList(),
				TargetType.AllAllies => candidates.ToList(),
				_ => new List<BattleUnit> { candidates.First() }
			};
		}

		private List<BattleUnit> GetDefaultTargetList(BattleUnit caster, ActiveSkillData skill)
		{
			bool targetIsEnemy = IsEnemyTargetingSkill(skill);

			var pool = targetIsEnemy
				? (caster.IsPlayer ? _ctx.EnemyUnits : _ctx.PlayerUnits)
				: (caster.IsPlayer ? _ctx.PlayerUnits : _ctx.EnemyUnits);

			var aliveUnits = pool.Where(u => u != null && u.IsAlive).ToList();

			// Ranged, Magic, Column, and Flying-unit attacks hit any row
			// (Original game: flying unit attacks are treated as ranged)
			if (skill.AttackType == AttackType.Ranged || skill.AttackType == AttackType.Magic
				|| caster.GetEffectiveClasses()?.Contains(UnitClass.Flying) == true
				|| skill.TargetType == TargetType.Column)
			{
				return aliveUnits.OrderBy(u => u.Position).ToList();
			}

			// Ground melee: front row blocks back row
			if (skill.AttackType == AttackType.Melee && targetIsEnemy)
			{
				var frontRow = aliveUnits.Where(u => u.IsFrontRow).ToList();
				if (frontRow.Count > 0)
					aliveUnits = frontRow;
			}

			return aliveUnits.OrderBy(u => u.Position).ToList();
		}

		private List<BattleUnit> GetForcedTargetCandidates(
			BattleUnit caster,
			ActiveSkillData skill,
			List<BattleUnit> defaultCandidates)
		{
			if (!IsEnemyTargetingSkill(skill) || skill.TargetType == TargetType.AllEnemies)
				return new List<BattleUnit>();

			return defaultCandidates
				.Where(u => u.TemporalStates.Any(s =>
					s.RemainingCount != 0 && s.Key == "ForcedTarget" && ForcedTargetAppliesToCaster(s, caster)))
				.ToList();
		}

		private static bool ForcedTargetAppliesToCaster(TemporalState state, BattleUnit caster)
		{
			if (state?.AffectedUnitIds == null || state.AffectedUnitIds.Count == 0)
				return true;

			var casterId = caster?.Data?.Id;
			return !string.IsNullOrWhiteSpace(casterId) && state.AffectedUnitIds.Contains(casterId);
		}

		private static bool IsEnemyTargetingSkill(ActiveSkillData skill)
		{
			return skill.TargetType switch
			{
				TargetType.Self => false,
				TargetType.SingleAlly => false,
				TargetType.AllAllies => false,
				_ => skill.Type != SkillType.Heal
			};
		}

		/// <summary>
		/// Piercing attack: hits front row target + the unit directly behind them (same column).
		/// Positions 1-3 = front, 4-6 = back. Column = (pos-1) % 3.
		/// </summary>
		private List<BattleUnit> GetPiercingTargets(BattleUnit first)
		{
			if (first == null) return new List<BattleUnit>();
			var targets = new List<BattleUnit> { first };
			var sidePool = _ctx.GetAliveUnits(first.IsPlayer);

			int col = (first.Position - 1) % 3;
			bool firstIsFront = first.IsFrontRow;
			int oppositePos = firstIsFront ? (col + 1 + 3) : (col + 1);

			var opposite = sidePool.FirstOrDefault(u => u.Position == oppositePos && u != first);
			if (opposite != null)
				targets.Add(opposite);

			return targets;
		}

		/// <summary>
		/// Column attack: hits all units in the same column (front + back, same (pos-1)%3).
		/// </summary>
		private List<BattleUnit> GetColumnTargets(BattleUnit first)
		{
			if (first == null) return new List<BattleUnit>();
			int col = (first.Position - 1) % 3;
			return _ctx.GetAliveUnits(first.IsPlayer)
				.Where(u => (u.Position - 1) % 3 == col)
				.OrderBy(u => u.Position)
				.ToList();
		}

		private List<BattleUnit> GetRowTargets(BattleUnit first, List<BattleUnit> legalTargets)
		{
			if (first == null) return new List<BattleUnit>();
			bool isFront = first.IsFrontRow;
			return legalTargets
				.Where(u => u.IsFrontRow == isFront)
				.OrderBy(u => u.Position)
				.ToList();
		}

		private List<BattleUnit> ApplyStrategyConditions(
			List<BattleUnit> defaultCandidates,
			Strategy strategy,
			BattleUnit caster,
			ActiveSkillData skill)
		{
			if (defaultCandidates.Count == 0)
				return defaultCandidates;

			var slots = GetConditionSlots(strategy)
				.Where(slot => slot.Condition != null)
				.ToList();
			if (slots.Count == 0)
				return defaultCandidates;

			var requiredCandidates = defaultCandidates;
			foreach (var slot in slots.Where(slot => slot.Mode == ConditionMode.Only))
			{
				requiredCandidates = GetMatchingTargets(requiredCandidates, slot.Condition, caster, skill);
				if (requiredCandidates.Count == 0)
					return null;
			}

			var prioritySlots = slots
				.Where(slot => slot.Mode == ConditionMode.Priority)
				.ToList();
			if (prioritySlots.Count == 0)
				return requiredCandidates;

			return ApplyPriorityConditions(requiredCandidates, prioritySlots, caster, skill);
		}

		private List<BattleUnit> ApplyPriorityConditions(
			List<BattleUnit> candidates,
			List<ConditionSlot> prioritySlots,
			BattleUnit caster,
			ActiveSkillData skill)
		{
			if (prioritySlots.Count == 1)
			{
				var preferred = GetMatchingTargets(candidates, prioritySlots[0].Condition, caster, skill);
				return preferred.Count == 0
					? candidates
					: OrderByGroups(candidates, preferred);
			}

			var first = prioritySlots.FirstOrDefault(slot => slot.SlotIndex == 1) ?? prioritySlots[0];
			var second = prioritySlots.FirstOrDefault(slot => slot.SlotIndex == 2) ?? prioritySlots[^1];
			var firstMatches = GetMatchingTargets(candidates, first.Condition, caster, skill);
			var secondMatches = GetMatchingTargets(candidates, second.Condition, caster, skill);
			var intersection = firstMatches.Where(secondMatches.Contains).ToList();
			if (intersection.Count > 0)
				return OrderByGroups(candidates, intersection, secondMatches, firstMatches);

			var positionPriority = prioritySlots.FirstOrDefault(slot => IsFrontOrBackPositionPriority(slot.Condition));
			if (positionPriority != null)
			{
				var positionMatches = GetMatchingTargets(candidates, positionPriority.Condition, caster, skill);
				if (positionMatches.Count > 0)
				{
					var otherMatches = positionPriority.SlotIndex == first.SlotIndex ? secondMatches : firstMatches;
					return OrderByGroups(candidates, positionMatches, otherMatches);
				}
			}

			if (secondMatches.Count > 0)
				return OrderByGroups(candidates, secondMatches, firstMatches);
			if (firstMatches.Count > 0)
				return OrderByGroups(candidates, firstMatches);
			return candidates;
		}

		private List<BattleUnit> GetMatchingTargets(
			List<BattleUnit> candidates,
			Condition condition,
			BattleUnit caster,
			ActiveSkillData skill)
		{
			if (condition == null || candidates.Count == 0)
				return candidates;

			if (condition.Operator == "lowest")
			{
				int min = candidates.Min(u => GetRankValue(u, condition));
				return candidates.Where(u => GetRankValue(u, condition) == min).ToList();
			}

			if (condition.Operator == "highest")
			{
				int max = candidates.Max(u => GetRankValue(u, condition));
				return candidates.Where(u => GetRankValue(u, condition) == max).ToList();
			}

			return candidates.Where(u => _conditionEvaluator.Evaluate(condition, caster, u, skill)).ToList();
		}

		private int GetRankValue(BattleUnit unit, Condition condition)
		{
			return condition.Category switch
			{
				ConditionCategory.Position => GetPositionRankValue(unit, condition),
				ConditionCategory.Hp => GetHpRankValue(unit, condition),
				ConditionCategory.ApPp => GetApPpValue(unit, condition),
				ConditionCategory.SelfApPp => GetApPpValue(unit, condition),
				ConditionCategory.AttributeRank => GetAttributeRankValue(unit, condition.Value?.ToString() ?? ""),
				_ => 0
			};
		}

		private int GetPositionRankValue(BattleUnit unit, Condition condition)
		{
			if (unit == null)
				return 0;

			string value = condition.Value?.ToString() ?? "";
			// Compatibility: "column_unit_count" is a legacy internal value from
			// an early translation pass. The actual original-game condition is
			// row population, meaning front row vs back row.
			if (value == "row_unit_count" || value == "column_unit_count")
			{
				return _ctx.GetAliveUnits(unit.IsPlayer)
					.Count(u => u.IsFrontRow == unit.IsFrontRow);
			}

			return 0;
		}

		private static int GetHpRankValue(BattleUnit unit, Condition condition)
		{
			if (unit == null)
				return 0;

			string value = condition.Value?.ToString() ?? "";
			if (value == "ratio")
			{
				int maxHp = Math.Max(1, unit.GetCurrentStat("HP"));
				return (int)((float)unit.CurrentHp / maxHp * 100000);
			}

			return unit.CurrentHp;
		}

		private static int GetAttributeRankValue(BattleUnit unit, string statName)
		{
			if (unit == null) return 0;

			// HP priority intentionally follows current HP. MaxHp is separate for
			// the original-style "highest/lowest ability" catalog.
			if (string.Equals(statName, "HP", System.StringComparison.OrdinalIgnoreCase))
				return unit.CurrentHp;
			if (string.Equals(statName, "MaxHp", System.StringComparison.OrdinalIgnoreCase))
				return unit.GetCurrentStat("HP");
			if (string.Equals(statName, "MaxAp", System.StringComparison.OrdinalIgnoreCase))
				return unit.InitialAp;
			if (string.Equals(statName, "MaxPp", System.StringComparison.OrdinalIgnoreCase))
				return unit.InitialPp;

			return unit.GetCurrentStat(statName);
		}

		private static int GetApPpValue(BattleUnit unit, Condition condition)
		{
			if (unit == null) return 0;
			string resource = condition.Value?.ToString() ?? "AP";
			if (resource.Contains(":"))
				resource = resource.Split(':')[0];

			return resource.Equals("PP", System.StringComparison.OrdinalIgnoreCase)
				? unit.CurrentPp
				: unit.CurrentAp;
		}

		private static List<BattleUnit> OrderByGroups(List<BattleUnit> candidates, params List<BattleUnit>[] groups)
		{
			var ordered = new List<BattleUnit>();
			var seen = new HashSet<BattleUnit>();
			foreach (var group in groups)
			{
				if (group == null || group.Count == 0)
					continue;

				foreach (var candidate in candidates)
				{
					if (group.Contains(candidate) && seen.Add(candidate))
						ordered.Add(candidate);
				}
			}

			foreach (var candidate in candidates)
			{
				if (seen.Add(candidate))
					ordered.Add(candidate);
			}

			return ordered;
		}

		private BattleUnit PickSingleEnemyTarget(List<BattleUnit> candidates, bool useRandomTargeting)
		{
			if (!useRandomTargeting || candidates.Count == 1)
				return candidates.First();

			return candidates[_random.Next(candidates.Count)];
		}

		private static bool HasStrategyConditions(Strategy strategy)
		{
			return GetConditionSlots(strategy).Any(slot => slot.Condition != null);
		}

		private static bool IsFrontOrBackPositionPriority(Condition condition)
		{
			string value = condition?.Value?.ToString() ?? "";
			return condition?.Category == ConditionCategory.Position
				&& condition.Operator == "equals"
				&& (value == "front" || value == "back");
		}

		private static IEnumerable<ConditionSlot> GetConditionSlots(Strategy strategy)
		{
			if (strategy == null)
				yield break;

			yield return new ConditionSlot(strategy.Condition1, strategy.Mode1, 1);
			yield return new ConditionSlot(strategy.Condition2, strategy.Mode2, 2);
		}

		private sealed record ConditionSlot(Condition Condition, ConditionMode Mode, int SlotIndex);
	}
}
