using System;
using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;

namespace BattleKing.Skills
{
    public static class SkillEffectFactory
    {
        /// <summary>
        /// Create ISkillEffect instances from JSON effect data.
        /// NOTE: This factory only handles effects that can be executed generically
        /// via ISkillEffect.Apply(ctx, caster, targets, calc).
        ///
        /// Effects that require battle-phase-specific context (DamageCalculation before a hit,
        /// PendingActionQueue, TemporalState, CustomCounters) are handled directly by
        /// PassiveSkillProcessor.ExecuteStructuredEffect() — they go through PassiveOnlyEffect
        /// here and are NOT dispatched via ISkillEffect.
        ///
        /// See also: docs/csharp-architecture.md §SkillEffectFactory
        /// </summary>
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
                    // All structured effect types below are dispatched by PassiveSkillProcessor,
                    // which has access to the battle-phase context (Calc, queue, temporals, counters).
                    // They MUST NOT be dispatched via ISkillEffect.Apply().
                    _ => new PassiveOnlyEffect(data.EffectType)
                });
            }
            return effects;
        }
    }

    /// <summary>
    /// Sentinel for effect types that are handled directly by PassiveSkillProcessor
    /// (e.g. ModifyDamageCalc, CounterAttack, TemporalMark, ModifyCounter, etc.)
    ///
    /// These effects require battle-phase-specific context that ISkillEffect.Apply()
    /// cannot provide — they need the DamageCalculation before a hit, access to the
    /// PendingActionQueue, or the ability to read/write TemporalState / CustomCounters
    /// at specific battle timings.
    ///
    /// DO NOT call Apply() on this — it is intentionally a no-op. The real execution
    /// happens in PassiveSkillProcessor.ExecuteStructuredEffect().
    /// </summary>
    public class PassiveOnlyEffect : ISkillEffect
    {
        public readonly string EffectType;
        public PassiveOnlyEffect(string effectType) => EffectType = effectType;
        public void Apply(BattleContext ctx, BattleUnit caster, List<BattleUnit> targets, DamageCalculation calc = null) { }
    }
}
