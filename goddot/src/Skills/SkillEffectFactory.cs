using System;
using System.Collections.Generic;
using BattleKing.Data;

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
                    _ => throw new NotSupportedException($"Unknown effect type: {data.EffectType}")
                });
            }
            return effects;
        }
    }
}
