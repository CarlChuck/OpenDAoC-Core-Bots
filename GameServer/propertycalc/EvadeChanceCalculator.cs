namespace DOL.GS.PropertyCalc
{
    /// <summary>
    /// The evade chance calculator. Returns 0 .. 1000 chance.
    /// 
    /// BuffBonusCategory1 unused
    /// BuffBonusCategory2 unused
    /// BuffBonusCategory3 unused
    /// BuffBonusCategory4 unused
    /// BuffBonusMultCategory1 unused
    /// </summary>
    [PropertyCalculator(eProperty.EvadeChance)]
    public class EvadeChanceCalculator : PropertyCalculator
    {
        public override int CalcValue(GameLiving living, eProperty property)
        {
            int chance = 0;

            if (living is IGamePlayer igp)
            {
                if (igp.HasAbility(Abilities.Evade))
                    chance += (900 + igp.Quickness + igp.Dexterity) * living.GetAbilityLevel(Abilities.Evade) * 5 / 100;

                chance += living.BaseBuffBonusCategory[property] * 10;
                chance += living.SpecBuffBonusCategory[property] * 10;
                chance -= living.DebuffCategory[property] * 10;
                chance += living.OtherBonus[property] * 10;
                chance += living.AbilityBonus[property] * 10;
            }
            else if (living is GameNPC npc)
                chance += npc.AbilityBonus[property] * 10 + npc.EvadeChance * 10;

            return chance;
        }
    }
}
