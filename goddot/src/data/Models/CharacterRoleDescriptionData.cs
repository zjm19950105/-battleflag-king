using System.Collections.Generic;

namespace BattleKing.Data
{
    /// <summary>
    /// A character's role description extracted from the class compendium.
    /// Text may contain {char:ID} and {class:ID} tokens for stable ID referencing —
    /// the display layer resolves these to current display names so that renaming
    /// a class (e.g. "飞龙" → new name) auto-updates all descriptions that mention it.
    /// Keep this data sourced from unicorn-overlord-class-compendium.md only; older
    /// active/passive skill documents are stale and must not drive role text.
    /// </summary>
    public class CharacterRoleDescriptionData
    {
        /// <summary>Stable character ID matching characters.json (e.g. "shooter").</summary>
        public string CharacterId { get; set; }

        /// <summary>Class compendium header, e.g. "射手 / 盾射手 (Shooter / Shield Shooter)".</summary>
        public string DisplayName { get; set; }

        /// <summary>Stable unit class IDs (e.g. "infantry", "flying").</summary>
        public List<string> UnitClasses { get; set; } = new();

        /// <summary>"主要角色" bullet points. May contain {char:ID} and {class:ID} tokens.</summary>
        public List<string> MainRoles { get; set; } = new();

        /// <summary>"特点" bullet points. May contain {char:ID} and {class:ID} tokens.</summary>
        public List<string> Characteristics { get; set; } = new();

        /// <summary>
        /// All character IDs referenced via {char:ID} tokens in MainRoles and Characteristics.
        /// Pre-computed for dependency tracking so renaming a character flags affected descriptions.
        /// </summary>
        public List<string> ReferencedCharacterIds { get; set; } = new();

        /// <summary>
        /// All unit class IDs referenced via {class:ID} tokens.
        /// </summary>
        public List<string> ReferencedClassIds { get; set; } = new();
    }
}
