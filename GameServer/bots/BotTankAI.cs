namespace DOL.GS
{
    /// <summary>
    /// Tank AI implementation focusing on threat management and protection
    /// </summary>
    public class BotTankAI : BotAI
    {
        public BotTankAI(GameBot bot) : base(bot) { }

        protected override void HandleCombat(GameLiving target)
        {
            // Priority: Taunt if not primary target, then attack
            if (CanPerformCombatAction())
            {
                if (target.TargetObject != _bot && CanCastSpell())
                {
                    // Try to taunt target to focus on this bot
                    TryTaunt(target);
                }
                
                if (!_bot.IsAttacking)
                {
                    _bot.StartAttack(target);
                }
                UpdateLastCombatAction();
            }
        }

        private void TryTaunt(GameLiving target)
        {
            // TODO: Implement taunt spell casting when spell system is integrated
            // For now, just ensure we're attacking to generate threat
            UpdateLastSpellCast();
        }
    }
}