using DOL.AI.Brain;

namespace DOL.GS.Scripts
{
    public class ArcherBrain : MimicBrain
    {
        public ArcherBrain()
        { }

        public override void OnLeaderAggro()
        {
            Body.Stealth(true);
        }

        public override bool CheckSpells(eCheckSpellType type)
        {
            if (type == eCheckSpellType.Defensive)
            {
                if (base.CheckSpells(type))
                    return true;

                if (Body.Group == null || Body.Group.MimicGroup.CampPoint != null && !MimicBody.MimicBrain.IsMainPuller)
                    Body.Stealth(true);
                else
                    Body.Stealth(false);

                if (Body.ControlledBrain != null && PvPMode)
                    MimicBody.CommandNpcRelease();

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
    }
}