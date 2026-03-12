using DOL.GS.PropertyCalc;
using System;

namespace DOL.GS.Scripts
{
    /// <summary>
    /// The Focus Level calculator
    ///
    /// BuffBonusCategory1 is used for buffs, uncapped
    /// BuffBonusCategory2 unused
    /// BuffBonusCategory3 unused
    /// BuffBonusCategory4 unused
    /// BuffBonusMultCategory1 unused
    /// </summary>
    [PropertyCalculator(eProperty.Focus_Darkness, eProperty.Focus_Matter)]
    [PropertyCalculator(eProperty.Focus_Mind, eProperty.Focus_Arboreal)]
    [PropertyCalculator(eProperty.Focus_EtherealShriek, eProperty.Focus_Witchcraft)]
    public class FocusLevelCalculator : PropertyCalculator
    {
        public FocusLevelCalculator() { }

        public override int CalcValue(GameLiving living, eProperty property)
        {
            if (living is GamePlayer or IGamePlayer)
            {
                int itemBonus = living.ItemBonus[(int)property];
                int focusLevel = living.BaseBuffBonusCategory[(int)property];

                var characterClass = (living as GamePlayer)?.CharacterClass ?? (living as IGamePlayer)?.CharacterClass;
                if (SkillBase.CheckPropertyType(property, ePropertyType.Focus) && characterClass != null && characterClass.FocusCaster)
                {
                    focusLevel += living.BaseBuffBonusCategory[(int)eProperty.AllFocusLevels];
                    itemBonus = Math.Max(itemBonus, living.ItemBonus[(int)eProperty.AllFocusLevels]);
                }

                return focusLevel + Math.Min(50, itemBonus);
            }

            return 0;
        }
    }
}
