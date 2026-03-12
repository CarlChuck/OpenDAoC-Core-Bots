using System.Collections.Generic;

namespace DOL.GS
{
    public class BotSpec
    {
        public static string SpecName;
        public eObjectType WeaponOneType;
        public eObjectType WeaponTwoType;
        public eWeaponDamageType DamageType = 0;
        public eSpecType SpecType;
        public bool Is2H;
        public List<BotSpecLine> SpecLines = new List<BotSpecLine>();

        public BotSpec()
        { }

        protected void Add(string spec, uint cap, float ratio)
        {
            SpecLines.Add(new BotSpecLine(spec, cap, ratio));
        }

        protected string ObjToSpec(eObjectType obj)
        {
            return SkillBase.ObjectTypeToSpec(obj);
        }

        public static BotSpec GetSpec(eCharacterClass charClass, eSpecType spec = eSpecType.None)
        {
            switch (charClass)
            {
                case eCharacterClass.Armsman: return new ArmsmanBotSpec(spec);
                case eCharacterClass.Cabalist: return new CabalistBotSpec(spec);
                case eCharacterClass.Cleric: return new ClericBotSpec(spec);
                case eCharacterClass.Friar: return new FriarBotSpec(spec);
                case eCharacterClass.Infiltrator: return new InfiltratorBotSpec();
                case eCharacterClass.Mercenary: return new MercenaryBotSpec(spec);
                case eCharacterClass.Minstrel: return new MinstrelBotSpec();
                case eCharacterClass.Paladin: return new PaladinBotSpec(spec);
                case eCharacterClass.Reaver: return new ReaverBotSpec();
                case eCharacterClass.Scout: return new ScoutBotSpec();
                case eCharacterClass.Sorcerer: return new SorcererBotSpec(spec);
                case eCharacterClass.Theurgist: return new TheurgistBotSpec(spec);
                case eCharacterClass.Wizard: return new WizardBotSpec(spec);

                case eCharacterClass.Bard: return new BardBotSpec();
                case eCharacterClass.Blademaster: return new BlademasterBotSpec(spec);
                case eCharacterClass.Champion: return new ChampionBotSpec(spec);
                case eCharacterClass.Druid: return new DruidBotSpec(spec);
                case eCharacterClass.Eldritch: return new EldritchBotSpec(spec);
                case eCharacterClass.Enchanter: return new EnchanterBotSpec(spec);
                case eCharacterClass.Hero: return new HeroBotSpec(spec);
                case eCharacterClass.Mentalist: return new MentalistBotSpec(spec);
                case eCharacterClass.Nightshade: return new NightshadeBotSpec();
                case eCharacterClass.Ranger: return new RangerBotSpec();
                case eCharacterClass.Valewalker: return new ValewalkerBotSpec();
                case eCharacterClass.Warden: return new WardenBotSpec(spec);

                case eCharacterClass.Berserker: return new BerserkerBotSpec();
                case eCharacterClass.Bonedancer: return new BonedancerBotSpec(spec);
                case eCharacterClass.Healer: return new HealerBotSpec(spec);
                case eCharacterClass.Hunter: return new HunterBotSpec();
                case eCharacterClass.Runemaster: return new RunemasterBotSpec(spec);
                case eCharacterClass.Savage: return new SavageBotSpec(spec);
                case eCharacterClass.Shadowblade: return new ShadowbladeBotSpec(spec);
                case eCharacterClass.Shaman: return new ShamanBotSpec(spec);
                case eCharacterClass.Skald: return new SkaldBotSpec();
                case eCharacterClass.Spiritmaster: return new SpiritmasterBotSpec(spec);
                case eCharacterClass.Thane: return new ThaneBotSpec(spec);
                case eCharacterClass.Warrior: return new WarriorBotSpec();
            }

            return null;
        }
    }
}
