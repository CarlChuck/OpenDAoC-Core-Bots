namespace DOL.GS
{
    public abstract class BotAI
    {
        protected readonly GameBot _bot;
        protected GameLiving _currentTarget;
        protected DateTime _lastSpellCast = DateTime.MinValue;
        protected DateTime _lastCombatAction = DateTime.MinValue;

        protected const int HEAL_THRESHOLD = 50;
        protected const int CAST_COOLDOWN = 2000;
        protected const int COMBAT_COOLDOWN = 1500;

        public BotAI(GameBot bot) => _bot = bot;

        public virtual void Tick()
        {
            if (_bot.Owner == null || !_bot.IsAlive || !_bot.Owner.IsAlive) return;

            CheckFollowOwner();

            if (_bot.Owner.TargetObject is GameLiving target && target.IsAlive)
            {
                HandleCombat(target);
            }
            else if (_currentTarget != null)
            {
                _currentTarget = null;
                _bot.StopAttack();
            }
        }

        protected virtual void CheckFollowOwner()
        {
            var distance = _bot.GetDistanceTo(_bot.Owner);
            if (distance > BotManager.MAX_FOLLOW_DISTANCE)
            {
                _bot.MoveTo(_bot.Owner.Position); // Teleport if too far
            }
            else if (distance > BotManager.FOLLOW_DISTANCE)
            {
                _bot.Follow(_bot.Owner, BotManager.FOLLOW_DISTANCE);
            }
        }

        protected abstract void HandleCombat(GameLiving target);

        protected bool CanCastSpell() => (DateTime.Now - _lastSpellCast).TotalMilliseconds > CAST_COOLDOWN;
        protected bool CanPerformCombatAction() => (DateTime.Now - _lastCombatAction).TotalMilliseconds > COMBAT_COOLDOWN;

        protected void UpdateLastSpellCast() => _lastSpellCast = DateTime.Now;
        protected void UpdateLastCombatAction() => _lastCombatAction = DateTime.Now;
    }
}