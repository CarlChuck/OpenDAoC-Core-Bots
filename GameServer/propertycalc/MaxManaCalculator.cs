using System;

namespace DOL.GS.PropertyCalc
{
    /// <summary>
    /// The Power Pool calculator
    /// 
    /// BuffBonusCategory1 unused
    /// BuffBonusCategory2 unused
    /// BuffBonusCategory3 unused
    /// BuffBonusCategory4 unused
    /// BuffBonusMultCategory1 unused
    /// </summary>
    [PropertyCalculator(eProperty.MaxMana)]
    public class MaxManaCalculator : PropertyCalculator
    {
        public MaxManaCalculator() {}

        public override int CalcValue(GameLiving living, eProperty property)
        {
            if (living is not IGamePlayer igp)
                return 0;

            eStat manaStat;

            if (igp.CharacterClass.ManaStat is not eStat.UNDEFINED)
                manaStat = igp.CharacterClass.ManaStat;
            else
            {
                if ((eCharacterClass) igp.CharacterClass.ID is eCharacterClass.Vampiir)
                    manaStat = eStat.STR;
                else if (igp.Champion && igp.ChampionLevel > 0)
                    return igp.CalculateMaxMana(igp.Level, 0);
                else
                    return 0;
            }

            int flatItemBonusCap = igp.Level / 2 + 1;
            int poolItemBonusCap = igp.Level / 2 + Math.Min(living.ItemBonus[eProperty.PowerPoolCapBonus], igp.Level);

            int manaBase = igp.CalculateMaxMana(igp.Level, living.GetModified((eProperty) manaStat));
            int flatItemBonus = Math.Min(flatItemBonusCap, living.ItemBonus[property]);
            int poolItemBonus = Math.Min(poolItemBonusCap, living.ItemBonus[eProperty.PowerPool]);
            int flatAbilityBonus = living.AbilityBonus[property];
            int poolAbilityBonus = living.AbilityBonus[eProperty.PowerPool];

            double result = manaBase;
            result *= 1 + poolAbilityBonus * 0.01;
            result += flatItemBonus + flatAbilityBonus;
            result *= 1 + poolItemBonus * 0.01;
            return (int) result;
        }
    }
}
