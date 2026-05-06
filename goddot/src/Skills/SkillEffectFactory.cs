using System;
using System.Collections.Generic;
using BattleKing.Core;
using BattleKing.Data;
using BattleKing.Pipeline;

namespace BattleKing.Skills
{
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
                    // Structured types handled by PassiveSkillProcessor — no-op wrapper
                    _ => new NoOpEffect()
                });
            }
            return effects;
        }
    }

    /// <summary>Placeholder for structured effect types handled by PassiveSkillProcessor directly</summary>
    public class NoOpEffect : ISkillEffect
    {
        public void Apply(BattleContext ctx, BattleUnit caster, List<BattleUnit> targets, DamageCalculation calc = null) { }
    }
}
