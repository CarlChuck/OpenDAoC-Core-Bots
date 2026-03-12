/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;

namespace DOL.GS.PropertyCalc
{
	/// <summary>
	/// The Skill Level calculator
	/// 
	/// BuffBonusCategory1 is used for buffs, uncapped
	/// BuffBonusCategory2 unused
	/// BuffBonusCategory3 unused
	/// BuffBonusCategory4 unused
	/// BuffBonusMultCategory1 unused
	/// </summary>
	[PropertyCalculator(eProperty.Skill_First, eProperty.Skill_Last)]
	public class SkillLevelCalculator : PropertyCalculator
	{
		public SkillLevelCalculator() {}

		public override int CalcValue(GameLiving living, eProperty property)
		{
			if (living is IGamePlayer igp)
			{
				int itemCap = igp.Level / 5 + 1;

				int itemBonus = living.ItemBonus[property];

				if (SkillBase.CheckPropertyType(property, ePropertyType.SkillMeleeWeapon))
					itemBonus += living.ItemBonus[eProperty.AllMeleeWeaponSkills];
				if (SkillBase.CheckPropertyType(property, ePropertyType.SkillMagical))
					itemBonus += living.ItemBonus[eProperty.AllMagicSkills];
				if (SkillBase.CheckPropertyType(property, ePropertyType.SkillDualWield))
					itemBonus += living.ItemBonus[eProperty.AllDualWieldingSkills];
				if (SkillBase.CheckPropertyType(property, ePropertyType.SkillArchery))
					itemBonus += living.ItemBonus[eProperty.AllArcherySkills];

				itemBonus += living.ItemBonus[eProperty.AllSkills];

				if (itemBonus > itemCap)
					itemBonus = itemCap;
				int buffs = living.BaseBuffBonusCategory[property];
				return itemBonus + buffs + igp.RealmLevel / 10;
			}

			return 0;
		}
	}
}
