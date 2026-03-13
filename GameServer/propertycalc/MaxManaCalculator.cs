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
            if (living is not GamePlayer player)
            {
                if (living is IGamePlayer igp && igp.CharacterClass?.ManaStat is not eStat.UNDEFINED)
                {
                    eStat botManaStat = igp.CharacterClass.ManaStat;
                    return igp.CalculateMaxMana(igp.Level, living.GetModified((eProperty) botManaStat));
                }

                return 0;
            }

            eStat manaStat;

            if (player.CharacterClass.ManaStat is not eStat.UNDEFINED)
                manaStat = player.CharacterClass.ManaStat;
            else
            {
                // Special handling for Vampiirs:
                /* There is no stat that affects the Vampiir's power pool or the damage done by its power based spells.
                 * The Vampiir is not a focus based class like, say, an Enchanter.
                 * The Vampiir is a lot more cut and dried than the typical casting class.
                 * EDIT, 12/13/04 - I was told today that this answer is not entirely accurate.
                 * While there is no stat that affects the damage dealt (in the way that intelligence or piety affects how much damage a more traditional caster can do),
                 * the Vampiir's power pool capacity is intended to be increased as the Vampiir's strength increases.
                 *
                 * This means that strength ONLY affects a Vampiir's mana pool
                 */
                if ((eCharacterClass) player.CharacterClass.ID is eCharacterClass.Vampiir)
                    manaStat = eStat.STR;
                else if (player.Champion && player.ChampionLevel > 0)
                    return player.CalculateMaxMana(player.Level, 0);
                else
                    return 0;
            }

            int flatItemBonusCap = player.Level / 2 + 1;
            int poolItemBonusCap = player.Level / 2 + Math.Min(player.ItemBonus[eProperty.PowerPoolCapBonus], player.Level);

            int manaBase = player.CalculateMaxMana(player.Level, player.GetModified((eProperty) manaStat));
            int flatItemBonus = Math.Min(flatItemBonusCap, player.ItemBonus[property]); // Pre-ToA flat bonus.
            int poolItemBonus = Math.Min(poolItemBonusCap, player.ItemBonus[eProperty.PowerPool]); // ToA bonus.
            int flatAbilityBonus = player.AbilityBonus[property]; // New Ethereal Bond.
            int poolAbilityBonus = player.AbilityBonus[eProperty.PowerPool]; // Old Ethereal Bond.

            double result = manaBase;
            result *= 1 + poolAbilityBonus * 0.01;
            result += flatItemBonus + flatAbilityBonus;
            result *= 1 + poolItemBonus * 0.01;
            return (int) result;
        }
    }
}
