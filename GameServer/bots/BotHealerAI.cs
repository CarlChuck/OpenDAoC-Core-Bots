using System.Linq;

namespace DOL.GS
{
    public class BotHealerAI : BotAI
    {
        public BotHealerAI(GameBot bot) : base(bot) { }

        protected override void HandleCombat(GameLiving target)
        {
            if (TryHealGroup()) return;
            if (CanPerformCombatAction())
            {
                if (!_bot.IsAttacking) _bot.StartAttack(target);
                UpdateLastCombatAction();
            }
        }

        private bool TryHealGroup()
        {
            if (!CanCastSpell()) return false;

            var group = _bot.Owner.Group;
            var candidates = group?.GetMembersInTheGroup() ?? new[] { _bot.Owner };
            var mostInjured = candidates
                .Where(m => m.IsAlive && m.HealthPercent < HEAL_THRESHOLD)
                .OrderBy(m => m.HealthPercent)
                .FirstOrDefault();

            if (mostInjured != null)
            {
                CastHeal(mostInjured);
                return true;
            }
            return false;
        }

        private void CastHeal(GameLiving target)
        {
            var (spell, spellLine) = FindBestHealSpell();
            if (spell != null && spellLine != null)
            {
                _bot.CastSpell(spell, spellLine);
                UpdateLastSpellCast();
            }
        }

        private (Spell spell, SpellLine spellLine) FindBestHealSpell()
        {
            // TODO: Properly integrate with bot's spell system once GameBot spell lines are implemented
            // For now, return null to avoid compilation errors
            return (null, null);
        }
    }
}