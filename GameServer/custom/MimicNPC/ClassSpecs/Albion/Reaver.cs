using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    //public class MimicReaver : MimicNPC
    //{
    //    public MimicReaver(byte level) : base(new ClassReaver(), level)
    //    { }
    //}

    public class ReaverSpec : MimicSpec
    {
        public ReaverSpec()
        {
            SpecName = "ReaverSpec";

            int randBaseWeap = Util.Random(4);

            switch (randBaseWeap)
            {
                case 0: WeaponOneType = eObjectType.SlashingWeapon; break;
                case 1: WeaponOneType = eObjectType.ThrustWeapon; break;
                case 2: WeaponOneType = eObjectType.CrushingWeapon; break;
                case 3:
                case 4: WeaponOneType = eObjectType.Flexible; break;
            }

            int randVariance = Util.Random(2);

            switch (randVariance)
            {
                case 0:
                case 1:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Soulrending, 41, 1.0f);
                Add(Specs.Shields, 42, 0.5f);
                Add(Specs.Parry, 13, 0.1f);
                break;

                case 2:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Soulrending, 50, 1.0f);
                Add(Specs.Shields, 29, 0.4f);
                Add(Specs.Parry, 16, 0.1f);
                break;
            }
        }
    }
}