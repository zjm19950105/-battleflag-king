using System.Collections.Generic;

namespace BattleKing.Core
{
    /// <summary>
    /// 天数进度配置。战斗核心只认 CurrentLevel/IsCc，天数系统负责把"第几天"翻译成这两个值。
    /// </summary>
    public class DayProgressionConfig
    {
        public int Day { get; set; }
        public int MaxSkillLevel { get; set; }   // 解锁技能等级上限
        public bool UnlockCc { get; set; }       // 当天是否解锁CC状态
    }

    public static class DayProgression
    {
        private static readonly Dictionary<int, DayProgressionConfig> Configs = new()
        {
            { 1, new DayProgressionConfig { Day = 1, MaxSkillLevel = 1,  UnlockCc = false } },
            { 2, new DayProgressionConfig { Day = 2, MaxSkillLevel = 10, UnlockCc = false } },
            { 3, new DayProgressionConfig { Day = 3, MaxSkillLevel = 20, UnlockCc = false } },
            { 4, new DayProgressionConfig { Day = 4, MaxSkillLevel = 30, UnlockCc = false } },
            { 5, new DayProgressionConfig { Day = 5, MaxSkillLevel = 40, UnlockCc = true } },
            { 6, new DayProgressionConfig { Day = 6, MaxSkillLevel = 50, UnlockCc = true } }
        };

        public static DayProgressionConfig GetConfig(int day)
        {
            return Configs.TryGetValue(day, out var config)
                ? config
                : new DayProgressionConfig { Day = day, MaxSkillLevel = 50, UnlockCc = true };
        }

        /// <summary>
        /// Apply day config: set CurrentLevel (controls skill unlocks). CC state is set separately by user toggle.
        /// </summary>
        public static void Apply(BattleUnit unit, int day)
        {
            var cfg = GetConfig(day);
            unit.CurrentLevel = cfg.MaxSkillLevel;
        }
    }
}
