namespace DOL.GS
{
    /// <summary>
    /// Defines the role/specialization of a bot, determining its AI behavior
    /// </summary>
    public enum BotRole
    {
        /// <summary>
        /// Melee combat focused bot (default)
        /// </summary>
        Melee,
        
        /// <summary>
        /// Tank role - focuses on threat generation and damage mitigation
        /// </summary>
        Tank,
        
        /// <summary>
        /// Healer role - focuses on group healing and support
        /// </summary>
        Healer,
        
        /// <summary>
        /// Caster role - focuses on spell damage and crowd control
        /// </summary>
        Caster,
        
        /// <summary>
        /// Ranged combat specialist
        /// </summary>
        Ranged
    }
}