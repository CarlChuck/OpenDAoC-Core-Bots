using DOL.AI.Brain;
using DOL.Database;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Scripts
{
    public class AssassinBrain : MimicBrain
    {
        private List<int> _envenomSpellIDs;

        public AssassinBrain()
        {
            _envenomSpellIDs = new List<int>();
        }

        public override void OnLeaderAggro()
        {
            Body.Stealth(true);
        }

        public override void OnRefreshSpecDependantSkills()
        {
            SortEnvenomSpells();
        }

        public override bool CheckSpells(eCheckSpellType type)
        {
            if (type == eCheckSpellType.Defensive)
            {
                if (Body.Group == null || Body.Group.MimicGroup.CampPoint != null && !MimicBody.MimicBrain.IsMainPuller)
                    Body.Stealth(true);
                else
                    Body.Stealth(false);

                PoisonWeapons();

                return false;
            }

            return base.CheckSpells(type);
        }

        protected override bool CheckInstantOffensiveSpells(Spell spell)
        {
            if (Body.IsStealthed)
                return false;

            return base.CheckInstantOffensiveSpells(spell);
        }

        private void PoisonWeapons()
        {
            if (_envenomSpellIDs.Count > 0)
            {
                int spellID = 0;

                for (int i = Slot.RIGHTHAND; i <= Slot.LEFTHAND; i++)
                {
                    DbInventoryItem item = Body.Inventory.GetItem((eInventorySlot)i);

                    if (item != null)
                    {
                        if (item.PoisonCharges > 0)
                        {
                            spellID = item.PoisonSpellID;
                            continue;
                        }

                        item.PoisonCharges = 1;
                        item.PoisonMaxCharges = 1;

                        List<int> duplicateList = _envenomSpellIDs.Where(id => id != spellID).ToList();

                        if (duplicateList.Count > 0)
                        {
                            item.PoisonSpellID = duplicateList[Util.Random(duplicateList.Count - 1)];
                            spellID = item.PoisonSpellID;
                        }
                        else
                            item.PoisonSpellID = _envenomSpellIDs[Util.Random(_envenomSpellIDs.Count - 1)];
                    }
                }
            }
        }

        private void SortEnvenomSpells()
        {
            int envenomSpecLevel = Body.GetModifiedSpecLevel(Specs.Envenom);

            if (envenomSpecLevel > 0)
            {
                SpellLine poisonLine = SkillBase.GetSpellLine(GlobalSpellsLines.Mundane_Poisons);

                if (poisonLine != null)
                {
                    _envenomSpellIDs.Clear();

                    List<Spell> highestPoisons = new List<Spell>();
                    List<Spell> poisons = SkillBase.GetSpellList(poisonLine.KeyName);

                    poisons = poisons.OrderByDescending(spell => spell.Level).ToList();

                    foreach (Spell poison in poisons)
                    {
                        if (poison.ID < 30000 || poison.ID > 30049 || poison.Level > envenomSpecLevel)
                            continue;

                        if (!highestPoisons.Select(poison => poison.SpellType).Contains(poison.SpellType))
                            highestPoisons.Add(poison);
                    }

                    if (highestPoisons.Count != 0)
                    {
                        foreach (Spell poison in highestPoisons)
                            _envenomSpellIDs.Add(poison.ID);
                    }
                }
            }
        }
    }
}