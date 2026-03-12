using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DOL.GS.Scripts
{
    public enum eHand
    {
        None = -1,

        oneHand,
        twoHand,
        leftHand,
    }

    public enum eMimicClass
    {
        None = -1,

        Armsman = 2,
        Cabalist = 13,
        Cleric = 6,
        Friar = 10,
        Infiltrator = 9,
        Mercenary = 11,
        Minstrel = 4,
        Paladin = 1,
        Reaver = 19,
        Scout = 3,
        Sorcerer = 8,
        Theurgist = 5,
        Wizard = 7,

        Bard = 48,
        Blademaster = 43,
        Champion = 45,
        Druid = 47,
        Eldritch = 40,
        Enchanter = 41,
        Hero = 44,
        Mentalist = 42,
        Nightshade = 49,
        Ranger = 50,
        Valewalker = 56,
        Warden = 46,

        Berserker = 31,
        Bonedancer = 30,
        Healer = 26,
        Hunter = 25,
        Runemaster = 29,
        Savage = 32,
        Shadowblade = 23,
        Shaman = 28,
        Skald = 24,
        Spiritmaster = 27,
        Thane = 21,
        Warrior = 22
    }

    public enum eMimicClassesHib
    {
        None = -1,

        Bard,
        Blademaster,
        Champion,
        Druid,
        Eldritch,
        Enchanter,
        Hero,
        Mentalist,
        Nightshade,
        Ranger,
        Valewalker,
        Warden,
    }

    public enum eMimicClassesMid
    {
        None = -1,

        Berserker,
        Bonedancer,
        Healer,
        Hunter,
        Runemaster,
        Savage,
        Shadowblade,
        Shaman,
        Skald,
        Spiritmaster,
        Thane,
        Warrior
    }

    public enum eMimicGroupRole
    {
        None = -1,

        Leader,
        MainAssist,
        MainTank,
        MainCC
    }

    public enum eQueueMessage
    {
        None = -1,

        WaitForBuffs,

        LowPower,
        OutOfPower,

        LowEndurance,
        OutOfEndurance
    }

    public enum eQueueMessageResult
    {
        None = -1,

        Accept,
        Deny,
        OnHold
    }

    public enum eSpecType
    {
        None = -1,

        MatterCab,
        BodyCab,
        SpiritCab,

        RejuvCleric,
        EnhanceCleric,
        SmiteCleric,

        RejuvFriar,
        EnhanceFriar,
        StaffFriar,

        MatterSorc,
        BodySorc,
        MindSorc,

        EarthTheur,
        IceTheur,
        AirTheur,

        EarthWiz,
        IceWiz,
        FireWiz,

        RegrowthBard,
        NurtureBard,
        MusicBard,
        BattleBard,

        RegrowthDruid,
        NurtureDruid,
        NatureDruid,

        LightEld,
        ManaEld,
        VoidEld,

        LightEnchanter,
        ManaEnchanter,
        EnchantmentEnchanter,

        LightMenta,
        ManaMenta,
        MentaMenta,

        RegrowthWarden,
        NurtureWarden,
        BattleWarden,

        DarkSpirit,
        SuppSpirit,
        SummSpirit,

        MendShaman,
        AugShaman,
        SubtShaman,

        DarkRune,
        SuppRune,
        RuneRune,

        MendHealer,
        AugHealer,
        PacHealer,

        DarkBone,
        SuppBone,
        ArmyBone,

        OneHanded,
        TwoHanded,
        DualWield,
        LeftAxe,
        Ranged,
        Mid,
        Instrument,
        OneHandAndShield,
        TwoHandAndShield,
        DualWieldAndShield,

        OneHandHybrid,
        TwoHandHybrid,
    }
}