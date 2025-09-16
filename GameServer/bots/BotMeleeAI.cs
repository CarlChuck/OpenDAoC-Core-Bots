namespace DOL.GS
{
    /// <summary>
    /// Basic melee AI implementation for bots
    /// </summary>
    public class BotMeleeAI : BotAI
    {
        public BotMeleeAI(GameBot bot) : base(bot) { }

        protected override void HandleCombat(GameLiving target)
        {
            if (CanPerformCombatAction())
            {
                if (!_bot.IsAttacking) 
                {
                    _bot.StartAttack(target);
                }
                UpdateLastCombatAction();
            }
        }
    }
}