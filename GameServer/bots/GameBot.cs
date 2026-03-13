using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Logging;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;
using DOL.GS.PropertyCalc;
using DOL.GS.Realm;
using DOL.GS.Styles;
using static DOL.GS.GamePlayer;

namespace DOL.GS
{
    public class GameBot : GameNPC, IGamePlayer
    {
        private static new readonly Logger log = LoggerManager.Create(typeof(GameBot));

        #region Core Properties

        public GamePlayer Owner { get; private set; }
        public string ClassName { get; private set; }
        public new string InternalID { get; set; }
        public byte ClassId { get; set; }
        public byte RaceId { get; set; }
        public byte GenderId { get; set; }
        public long DatabaseID { get; set; }
        public BotSpec BotSpec { get; private set; }
        private int m_leftOverSpecPoints;

        #endregion

        #region IGamePlayer — DummyClient / DummyPacketLib

        private readonly BotDummyPacketLib _dummyLib;
        private readonly BotDummyClient _dummyClient;

        public IPacketLib Out => _dummyLib;
        public GameClient Client => _dummyClient;

        #endregion

        #region IGamePlayer — CharacterClass

        protected ICharacterClass m_characterClass;

        public virtual ICharacterClass CharacterClass => m_characterClass;

        public virtual bool SetCharacterClass(int id)
        {
            ICharacterClass cl = ScriptMgr.FindCharacterClass(id);
            if (cl == null)
            {
                if (log.IsErrorEnabled)
                    log.ErrorFormat("No CharacterClass with ID {0} found", id);
                return false;
            }

            m_characterClass = cl;
            // Note: ICharacterClass.Init() expects GamePlayer. We skip it for GameBot
            // since GameBot is not a GamePlayer. The class identity (ID, stats, etc.)
            // is still usable without Init().

            if (Group != null)
                Group.UpdateMember(this, false, true);

            return true;
        }

        #endregion

        #region IGamePlayer — ECS Components (Explicit Interface)

        // The ECS components are public fields on GameLiving (lowercase).
        // IGamePlayer expects PascalCase properties.
        AttackComponent IGamePlayer.AttackComponent => attackComponent;
        RangeAttackComponent IGamePlayer.RangeAttackComponent => rangeAttackComponent;
        StyleComponent IGamePlayer.StyleComponent => styleComponent;
        EffectListComponent IGamePlayer.EffectListComponent => effectListComponent;

        #endregion

        #region IGamePlayer — Property Indexers (Explicit)

        // GameLiving returns PropertyIndexer (concrete) but IGamePlayer expects IPropertyIndexer.
        IPropertyIndexer IGamePlayer.AbilityBonus => AbilityBonus;
        IPropertyIndexer IGamePlayer.ItemBonus => ItemBonus;
        IPropertyIndexer IGamePlayer.BaseBuffBonusCategory => BaseBuffBonusCategory;
        IPropertyIndexer IGamePlayer.SpecBuffBonusCategory => SpecBuffBonusCategory;
        IPropertyIndexer IGamePlayer.DebuffCategory => DebuffCategory;

        public PropertyIndexer BuffBonusCategory4 { get; } = new();

        IPropertyIndexer IGamePlayer.BuffBonusCategory4 => BuffBonusCategory4;
        IPropertyIndexer IGamePlayer.OtherBonus => OtherBonus;
        IPropertyIndexer IGamePlayer.SpecDebuffCategory => SpecDebuffCategory;

        #endregion

        #region IGamePlayer — Stats (Explicit Interface — short vs int mismatch)

        int IGamePlayer.Strength => Strength;
        int IGamePlayer.Dexterity => Dexterity;
        int IGamePlayer.Quickness => Quickness;
        int IGamePlayer.Intelligence => Intelligence;

        #endregion

        #region IGamePlayer — Health/Mana/Endurance Overrides

        public override int Health
        {
            get => base.Health;
            set
            {
                byte oldPercent = HealthPercent;
                base.Health = value;

                if (oldPercent != HealthPercent)
                    Group?.UpdateMember(this, false, false);
            }
        }

        public override int Mana
        {
            get => m_mana;
            set
            {
                byte oldPercent = ManaPercent;
                int maxMana = MaxMana;
                m_mana = Math.Clamp(value, 0, maxMana);

                if (m_mana < maxMana)
                    StartPowerRegeneration();

                if (oldPercent != ManaPercent)
                    Group?.UpdateMember(this, false, false);
            }
        }

        public override int MaxMana
        {
            get
            {
                if (CharacterClass?.ManaStat is eStat manaStat && manaStat != eStat.UNDEFINED)
                {
                    int calculated = CalculateMaxMana(Level, GetBaseStat(manaStat));
                    if (calculated > 0)
                        return calculated;
                }

                return GetModified(eProperty.MaxMana);
            }
        }

        public override int Endurance
        {
            get => m_endurance;
            set
            {
                byte oldPercent = EndurancePercent;
                int maxEndurance = MaxEndurance;
                m_endurance = Math.Clamp(value, 0, maxEndurance);

                if (m_endurance < maxEndurance)
                    StartEnduranceRegeneration();

                if (oldPercent != EndurancePercent)
                    Group?.UpdateMember(this, false, false);
            }
        }

        #endregion

        #region IGamePlayer — Health/Mana Calculations

        public virtual int CalculateMaxHealth(int level, int constitution)
        {
            // Simplified player-like health calculation
            int hp = CharacterClass != null ? CharacterClass.BaseHP : 500;
            hp += hp * (level - 1) * 5 / 100;
            hp += constitution * 2;
            return Math.Max(1, hp);
        }

        public virtual int CalculateMaxMana(int level, int manaStat)
        {
            if (CharacterClass == null || CharacterClass.ManaStat == eStat.UNDEFINED)
                return 0;

            int mana = level * 5 + manaStat;
            return Math.Max(0, mana);
        }

        #endregion

        #region IGamePlayer — PlayerDeck / Misc

        private PlayerDeck _randomNumberDeck;

        public PlayerDeck RandomNumberDeck
        {
            get
            {
                if (_randomNumberDeck == null)
                    _randomNumberDeck = new PlayerDeck();
                return _randomNumberDeck;
            }
            set { _randomNumberDeck = value; }
        }

        public List<int> SelfBuffChargeIDs { get; } = new List<int>();
        public int TotalConstitutionLostAtDeath { get; set; }
        public double SpecLock { get; set; }

        #endregion

        #region IGamePlayer — Level / Class Properties

        public byte MaxLevel => 50;
        public int RealmLevel { get; set; }
        public int MLLevel { get; set; }
        public bool Champion { get; set; }
        public int ChampionLevel { get; set; }

        #endregion

        #region IGamePlayer — Guild

        private Guild _guild;

        public Guild Guild
        {
            get => _guild;
            set => _guild = value;
        }

        #endregion

        #region IGamePlayer — Duel System

        private GameDuel Duel { get; set; }

        public GameLiving DuelPartner => Duel?.GetPartnerOf(this);

        public void OnDuelStart(GameDuel duel)
        {
            Duel = duel;
        }

        public void OnDuelStop()
        {
            Duel = null;
        }

        public bool IsDuelPartner(GameLiving living)
        {
            return DuelPartner == living;
        }

        public bool IsDuelReady { get; set; }

        #endregion

        #region IGamePlayer — Shade System

        protected ShadeECSGameEffect m_ShadeEffect;

        public ShadeECSGameEffect ShadeEffect
        {
            get => m_ShadeEffect;
            set => m_ShadeEffect = value;
        }

        public bool IsShade => m_ShadeEffect != null || Model == ShadeModel;

        public ushort CreationModel => Model;

        public ushort ShadeModel
        {
            get
            {
                if (CharacterClass != null && CharacterClass.ID == (int)eCharacterClass.Necromancer)
                    return 822;
                return Model;
            }
        }

        public virtual void Shade(bool state)
        {
            // ICharacterClass.Shade has signature: bool Shade(bool state, out ECSGameAbilityEffect effect)
            // We call it and discard the output.
            CharacterClass?.Shade(state, out _);
        }

        #endregion

        #region IGamePlayer — Horse System

        public ControlledHorse ActiveHorse => null;

        public virtual bool IsOnHorse
        {
            get => false;
            set { }
        }

        #endregion

        #region IGamePlayer — Sprint

        public bool IsSprinting => effectListComponent.ContainsEffectForEffectType(eEffect.Sprint);

        public virtual bool Sprint(bool state)
        {
            if (state == IsSprinting)
                return state;

            if (state)
            {
                if (Endurance <= 10 || IsStealthed || !IsAlive)
                    return false;

                ECSGameEffectFactory.Create(new ECSGameEffectInitParams(this, 0, 1),
                    static (in ECSGameEffectInitParams i) => new SprintECSGameEffect(i));
                return true;
            }
            else
            {
                ECSGameEffect effect = EffectListService.GetEffectOnTarget(this, eEffect.Sprint);
                effect?.End();
                return false;
            }
        }

        #endregion

        #region IGamePlayer — Status Flags

        public bool CanBreathUnderWater { get; set; }
        public bool IsOverencumbered { get; set; }
        public int Encumberance => 0;
        public int MaxEncumberance => 0;

        #endregion

        #region IGamePlayer — Skill System

        protected readonly Dictionary<string, Specialization> m_specialization = new Dictionary<string, Specialization>();
        protected readonly List<SpellLine> m_spellLines = new List<SpellLine>();

        public virtual bool HasSpecialization(string keyName)
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                return m_specialization.ContainsKey(keyName);
            }
        }

        public virtual SpellLine GetSpellLine(string keyname)
        {
            lock (m_spellLines)
            {
                return m_spellLines.FirstOrDefault(sl => sl.KeyName == keyname);
            }
        }

        #endregion

        #region IGamePlayer — Equipment / Armor

        public virtual eArmorSlot CalculateArmorHitLocation(AttackData ad)
        {
            int random = Util.Random(99);
            if (random < 35) return eArmorSlot.TORSO;
            if (random < 55) return eArmorSlot.LEGS;
            if (random < 70) return eArmorSlot.ARMS;
            if (random < 82) return eArmorSlot.HEAD;
            if (random < 91) return eArmorSlot.HAND;
            return eArmorSlot.FEET;
        }

        public double WeaponDamageWithoutQualityAndCondition(DbInventoryItem weapon)
        {
            if (weapon == null)
                return 0;

            double dps = weapon.DPS_AF / 10.0;
            double speed = weapon.SPD_ABS / 10.0;
            return dps * speed;
        }

        #endregion

        #region IGamePlayer — XP / Realm Points

        public void AddXPGainer(GameObject xpGainer, float damageAmount)
        {
        }

        public void GainRealmPoints(long amount, bool modify)
        {
        }

        #endregion

        #region IGamePlayer — Stealth

        public void StartStealthUncoverAction()
        {
        }

        public void StopStealthUncoverAction()
        {
        }

        #endregion

        #region IGamePlayer — Pet Control

        public void CommandNpcRelease()
        {
            if (ControlledBrain != null)
            {
                var brain = ControlledBrain;
                if (brain.Body != null)
                    brain.Body.Delete();
                RemoveControlledBrain(brain);
            }
        }

        #endregion

        #region IGamePlayer — TargetInView

        protected bool m_targetInView = true;

        public override bool TargetInView
        {
            get
            {
                if (TargetObject != null && GetDistanceTo(TargetObject) <= TargetInViewAlwaysTrueMinRange)
                    return true;
                return m_targetInView;
            }
            set => m_targetInView = value;
        }

        public override int TargetInViewAlwaysTrueMinRange =>
            (TargetObject is GamePlayer targetPlayer && targetPlayer.IsMoving) ? 100 : 64;

        protected bool _groundTargetInView = true;

        public override bool GroundTargetInView
        {
            get => _groundTargetInView;
            set => _groundTargetInView = value;
        }

        #endregion

        #region Combat Styles and Spell Lists

        public new List<Style> StylesChain { get; protected set; }
        public new List<Style> StylesDefensive { get; protected set; }
        public new List<Style> StylesBack { get; protected set; }
        public new List<Style> StylesSide { get; protected set; }
        public new List<Style> StylesFront { get; protected set; }
        public new List<Style> StylesAnytime { get; protected set; }
        public List<Style> StylesTaunt { get; protected set; }
        public List<Style> StylesDetaunt { get; protected set; }
        public List<Style> StylesShield { get; protected set; }

        public List<Spell> InstantCrowdControlSpells { get; set; }
        public List<Spell> CrowdControlSpells { get; set; }
        public List<Spell> BoltSpells { get; set; }

        public bool CanUseSideStyles => StylesSide != null && StylesSide.Count > 0;
        public bool CanUseBackStyles => StylesBack != null && StylesBack.Count > 0;
        public bool CanUseFrontStyle => StylesFront != null && StylesFront.Count > 0;
        public bool CanUseAnytimeStyles => StylesAnytime != null && StylesAnytime.Count > 0;
        public bool CanUsePositionalStyles => CanUseSideStyles || CanUseBackStyles;
        public bool CanCastCrowdControlSpells => CrowdControlSpells != null && CrowdControlSpells.Count > 0;
        public bool CanCastBolts => BoltSpells != null && BoltSpells.Count > 0;

        // Heal spell properties
        public Spell HealBig { get; protected set; }
        public Spell HealEfficient { get; protected set; }
        public Spell HealGroup { get; protected set; }
        public Spell HealInstant { get; protected set; }
        public Spell HealInstantGroup { get; protected set; }
        public Spell HealOverTime { get; protected set; }
        public Spell HealOverTimeGroup { get; protected set; }
        public Spell HealOverTimeInstant { get; protected set; }
        public Spell HealOverTimeInstantGroup { get; protected set; }
        public Spell CureMezz { get; protected set; }
        public Spell CureDisease { get; protected set; }
        public Spell CureDiseaseGroup { get; protected set; }
        public Spell CurePoison { get; protected set; }
        public Spell CurePoisonGroup { get; protected set; }

        protected const int HEALTH_REGEN_PERIOD = 6000;

        public static double HealAmount(Spell spell, GameLiving target)
        {
            return spell.Value >= 0
                ? spell.Value
                : target.MaxHealth * spell.Value * -0.01d;
        }

        public int PowerCost(Spell spell)
        {
            int powerCost = spell.Power;

            if (powerCost < 0)
            {
                if (CharacterClass.ManaStat is not eStat.UNDEFINED)
                    powerCost = (int)(CalculateMaxMana(Level, GetBaseStat(CharacterClass.ManaStat)) * powerCost * -0.01);
                else
                    powerCost = (int)(MaxMana * powerCost * -0.01);
            }

            return powerCost;
        }

        public override void SortSpells()
        {
            if (Spells.Count < 1)
                return;

            InstantHarmfulSpells?.Clear();
            HarmfulSpells?.Clear();
            InstantHealSpells?.Clear();
            HealSpells?.Clear();
            InstantCrowdControlSpells?.Clear();
            CrowdControlSpells?.Clear();
            BoltSpells?.Clear();
            InstantMiscSpells?.Clear();
            MiscSpells?.Clear();

            HealBig = null;
            HealEfficient = null;
            HealGroup = null;
            HealInstant = null;
            HealInstantGroup = null;
            HealOverTime = null;
            HealOverTimeGroup = null;
            HealOverTimeInstant = null;
            HealOverTimeInstantGroup = null;
            CureMezz = null;
            CureDisease = null;
            CureDiseaseGroup = null;
            CurePoison = null;
            CurePoisonGroup = null;

            foreach (Spell spell in Spells)
            {
                if (spell == null)
                    continue;

                if (spell.SpellType == eSpellType.Bolt)
                {
                    BoltSpells ??= new List<Spell>(1);
                    BoltSpells.Add(spell);
                }
                else if (spell.SpellType == eSpellType.Mesmerize ||
                        (spell.SpellType == eSpellType.SpeedDecrease && spell.Value >= 99))
                {
                    CrowdControlSpells ??= new List<Spell>(1);
                    CrowdControlSpells.Add(spell);
                }
                else if (spell.IsHarmful)
                {
                    if (spell.IsInstantCast)
                    {
                        InstantHarmfulSpells ??= new List<Spell>(1);
                        InstantHarmfulSpells.Add(spell);
                    }
                    else
                    {
                        HarmfulSpells ??= new List<Spell>(1);
                        HarmfulSpells.Add(spell);
                    }
                }
                else if (spell.IsHealing && !spell.IsPulsing && !spell.IsConcentration)
                {
                    if (spell.Target == eSpellTarget.PET)
                        continue;

                    double valueNew = HealAmount(spell, this);

                    if (spell.SpellType == eSpellType.CureMezz)
                        CureMezz = spell;
                    else if (spell.SpellType == eSpellType.CureDisease)
                    {
                        if ((spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            && (CureDiseaseGroup == null || spell.Duration > CureDiseaseGroup.Duration))
                            CureDiseaseGroup = spell;
                        else if (spell.Target == eSpellTarget.REALM && (CureDisease == null || spell.Duration > CureDisease.Duration))
                            CureDisease = spell;
                    }
                    else if (spell.SpellType == eSpellType.CurePoison)
                    {
                        if ((spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            && (CurePoisonGroup == null || spell.Duration > CurePoisonGroup.Duration))
                            CurePoisonGroup = spell;
                        else if (spell.Target == eSpellTarget.REALM && (CurePoison == null || spell.Duration > CurePoison.Duration))
                            CurePoison = spell;
                    }
                    else if (spell.IsInstantCast)
                    {
                        InstantHealSpells ??= new List<Spell>(1);
                        InstantHealSpells.Add(spell);

                        if (spell.SpellType == eSpellType.HealOverTime || spell.SpellType == eSpellType.HealthRegenBuff)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealOverTimeInstantGroup == null)
                                    HealOverTimeInstantGroup = spell;
                                else
                                {
                                    double perSecondOld = HealOverTimeInstantGroup.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTimeInstantGroup, this) / HealOverTimeInstantGroup.Frequency
                                        : HealAmount(HealOverTimeInstantGroup, this) / HEALTH_REGEN_PERIOD / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HEALTH_REGEN_PERIOD / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTimeInstantGroup = spell;
                                }
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealOverTimeInstant == null)
                                    HealOverTimeInstant = spell;
                                else
                                {
                                    double perSecondOld = HealOverTimeInstant.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTimeInstant, this) / HealOverTimeInstant.Frequency
                                        : HealAmount(HealOverTimeInstant, this) / HEALTH_REGEN_PERIOD / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HEALTH_REGEN_PERIOD / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTimeInstant = spell;
                                }
                            }
                        }
                        else if (spell.SpellType == eSpellType.Heal)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealInstantGroup == null || valueNew > HealAmount(HealInstantGroup, this))
                                    HealInstantGroup = spell;
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealInstant == null || valueNew > HealAmount(HealInstant, this))
                                    HealInstant = spell;
                            }
                        }
                    }
                    else
                    {
                        HealSpells ??= new List<Spell>(1);
                        HealSpells.Add(spell);

                        if (spell.SpellType == eSpellType.HealOverTime || spell.SpellType == eSpellType.HealthRegenBuff)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealOverTimeGroup == null)
                                    HealOverTimeGroup = spell;
                                else
                                {
                                    double perSecondOld = HealOverTimeGroup.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTimeGroup, this) / HealOverTimeGroup.Frequency
                                        : HealAmount(HealOverTimeGroup, this) / HEALTH_REGEN_PERIOD / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HEALTH_REGEN_PERIOD / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTimeGroup = spell;
                                }
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealOverTime == null)
                                    HealOverTime = spell;
                                else
                                {
                                    double perSecondOld = HealOverTime.SpellType == eSpellType.HealOverTime
                                        ? HealAmount(HealOverTime, this) / HealOverTime.Frequency
                                        : HealAmount(HealOverTime, this) / HEALTH_REGEN_PERIOD / 2;
                                    double perSecondNew = spell.SpellType == eSpellType.HealOverTime
                                        ? valueNew / spell.Frequency
                                        : valueNew / HEALTH_REGEN_PERIOD / 2;

                                    if (perSecondNew > perSecondOld)
                                        HealOverTime = spell;
                                }
                            }
                        }
                        else if (spell.SpellType == eSpellType.Heal)
                        {
                            if (spell.Target == eSpellTarget.GROUP || spell.Radius > 0)
                            {
                                if (HealGroup == null || valueNew / spell.CastTime > HealAmount(HealGroup, this) / HealGroup.CastTime)
                                    HealGroup = spell;
                            }
                            else if (spell.Target == eSpellTarget.REALM)
                            {
                                if (HealEfficient == null)
                                    HealEfficient = spell;
                                else
                                {
                                    double perSecondNew = valueNew / spell.CastTime;
                                    double perSecondEff = HealAmount(HealEfficient, this) / HealEfficient.CastTime;
                                    double perSecondBig = HealBig == null ? 0.0 : HealAmount(HealBig, this) / HealBig.CastTime;

                                    double effNew = valueNew / PowerCost(spell) + 0.25;
                                    double effOld = HealAmount(HealEfficient, this) / PowerCost(HealEfficient);

                                    if (effNew > effOld)
                                    {
                                        if (perSecondEff > perSecondNew && perSecondEff > perSecondBig)
                                            HealBig = HealEfficient;

                                        HealEfficient = spell;
                                    }
                                    else if (perSecondNew > perSecondEff && perSecondNew > perSecondBig)
                                        HealBig = spell;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (spell.IsInstantCast && spell.SpellType != eSpellType.SpeedEnhancement)
                    {
                        InstantMiscSpells ??= new List<Spell>(1);
                        InstantMiscSpells.Add(spell);
                    }
                    else
                    {
                        MiscSpells ??= new List<Spell>(1);
                        MiscSpells.Add(spell);
                    }
                }
            }
        }

        public override void SortStyles()
        {
            StylesChain?.Clear();
            StylesDefensive?.Clear();
            StylesBack?.Clear();
            StylesSide?.Clear();
            StylesFront?.Clear();
            StylesAnytime?.Clear();
            StylesTaunt?.Clear();
            StylesDetaunt?.Clear();
            StylesShield?.Clear();

            if (Styles == null)
                return;

            foreach (Style s in Styles)
            {
                if (s == null)
                    continue;

                if (s.WeaponTypeRequirement != (int)eObjectType.Shield ||
                    s.WeaponTypeRequirement == (int)eObjectType.Shield && s.OpeningRequirementType == Style.eOpening.Defensive)
                {
                    switch (s.OpeningRequirementType)
                    {
                        case Style.eOpening.Defensive:
                            StylesDefensive ??= new List<Style>(1);
                            StylesDefensive.Add(s);
                            break;

                        case Style.eOpening.Positional:
                            switch ((Style.eOpeningPosition)s.OpeningRequirementValue)
                            {
                                case Style.eOpeningPosition.Back:
                                    StylesBack ??= new List<Style>(1);
                                    StylesBack.Add(s);
                                    break;

                                case Style.eOpeningPosition.Side:
                                    StylesSide ??= new List<Style>(1);
                                    StylesSide.Add(s);
                                    break;

                                case Style.eOpeningPosition.Front:
                                    StylesFront ??= new List<Style>(1);
                                    StylesFront.Add(s);
                                    break;
                            }
                            break;

                        default:
                            if (s.OpeningRequirementValue > 0)
                            {
                                StylesChain ??= new List<Style>(1);
                                StylesChain.Add(s);
                            }
                            else
                            {
                                bool added = false;

                                if (s.Procs.Count > 0)
                                {
                                    foreach (StyleProcInfo proc in s.Procs)
                                    {
                                        if (proc.Spell.SpellType == eSpellType.StyleTaunt)
                                        {
                                            if (proc.Spell.ID == 20000)
                                            {
                                                StylesTaunt ??= new List<Style>(1);
                                                StylesTaunt.Add(s);
                                                added = true;
                                            }
                                            else if (proc.Spell.ID == 20001)
                                            {
                                                StylesDetaunt ??= new List<Style>(1);
                                                StylesDetaunt.Add(s);
                                                added = true;
                                            }
                                        }
                                    }
                                }

                                if (!added)
                                {
                                    StylesAnytime ??= new List<Style>(1);
                                    StylesAnytime.Add(s);
                                }
                            }
                            break;
                    }
                }
                else
                {
                    StylesShield ??= new List<Style>(1);
                    StylesShield.Add(s);
                }
            }
        }

        #endregion

        #region Constructor

        public GameBot(GamePlayer owner, byte classId, string name = null, byte raceId = 0, byte genderId = 0)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            ClassId = classId;
            RaceId = raceId;
            GenderId = genderId;
            ClassName = BotManager.GetClassNameById(classId);
            InternalID = Guid.NewGuid().ToString();
            _dummyClient = new BotDummyClient();
            _dummyLib = new BotDummyPacketLib();

            Level = owner.Level;

            // Set class identity first — needed for stat init and specs
            if (!SetCharacterClass(ClassId))
                throw new InvalidOperationException($"Failed to set character class for class ID {ClassId}. Ensure the class ID is valid.");
            SetRaceAndRealm(owner);

            Name = string.IsNullOrEmpty(name) ? $"{owner.Name}'s {ClassName} Bot" : name;
            MaxSpeedBase = PLAYER_BASE_SPEED;

            // Stat and spec initialization — order matters
            InitializeBotStats();
            BotSpec = BotSpec.GetSpec((eCharacterClass)ClassId);
            LoadClassSpecializations(false);
            SpendSpecPoints(Level, 0);
            RefreshSpecDependantSkills(false);
            SetBotSpells();
            SortStyles();
            SortSpells();
            EquipBot();

            Health = MaxHealth;
            Endurance = MaxEndurance;
            Mana = MaxMana;

            RespawnInterval = -1;

            // Set up BotBrain — replaces old BotAI system
            var brain = new DOL.AI.Brain.BotBrain();
            brain.IsHealer = IsHealerClass();
            SetOwnBrain(brain);
            
            // Also set as ControlledBrain so combat system works properly
            InitControlledBrainArray(1);
            ControlledBrain = brain;

            GameEventMgr.AddHandler(Owner, GamePlayerEvent.Quit, new DOLEventHandler(OnOwnerQuit));
        }

        private bool IsHealerClass()
        {
            return CharacterClass?.ID is (int)eCharacterClass.Cleric or (int)eCharacterClass.Druid
                or (int)eCharacterClass.Healer or (int)eCharacterClass.Shaman
                or (int)eCharacterClass.Bard or (int)eCharacterClass.Warden;
        }

        #endregion

        #region Bot Management

        private void OnOwnerQuit(DOLEvent e, object sender, EventArgs arguments)
        {
            BotManager.RemoveBot(this);
        }

        public override bool AddToWorld()
        {
            if (!base.AddToWorld())
                return false;

            RandomNumberDeck = new PlayerDeck();
            return true;
        }

        public override void Delete()
        {
            Group?.RemoveMember(this);
            GameEventMgr.RemoveHandler(Owner, GamePlayerEvent.Quit, new DOLEventHandler(OnOwnerQuit));
            base.Delete();
        }

        public override bool RemoveFromWorld()
        {
            if (!base.RemoveFromWorld())
                return false;

            Duel?.Stop();

            if (ControlledBrain != null)
                CommandNpcRelease();

            return true;
        }

        public override void OnAttackedByEnemy(AttackData ad)
        {
            // Notify BotBrain of the attack so it can add aggro and transition to combat state
            if (Brain is BotBrain botBrain)
                botBrain.OnAttackedByEnemy(ad);

            base.OnAttackedByEnemy(ad);
        }

        #endregion

        #region Race and Realm

        private void SetRaceAndRealm(GamePlayer owner)
        {
            if (CharacterClass == null)
                return;

            var eligibleRaces = CharacterClass.EligibleRaces;
            eGender gender = GenderId == 1 ? eGender.Female : eGender.Male;

            if (RaceId != 0 && eligibleRaces != null)
            {
                // Use the user-provided race if it's eligible for this class
                var requestedRace = eligibleRaces.FirstOrDefault(r => (byte)r.ID == RaceId);
                if (requestedRace != null)
                {
                    Race = (short)requestedRace.ID;
                    Model = (ushort)requestedRace.GetModel(gender);
                    Gender = gender;
                }
                else
                {
                    // Provided race not eligible — fall back to random
                    PlayerRace playerRace = eligibleRaces[Util.Random(eligibleRaces.Count - 1)];
                    Race = (short)playerRace.ID;
                    Model = (ushort)playerRace.GetModel(gender);
                    Gender = gender;
                }
            }
            else if (eligibleRaces != null && eligibleRaces.Count > 0)
            {
                // No race specified — pick random eligible race
                PlayerRace playerRace = eligibleRaces[Util.Random(eligibleRaces.Count - 1)];
                Race = (short)playerRace.ID;
                Model = (ushort)playerRace.GetModel(gender);
                Gender = gender;
            }
            else
            {
                Race = owner.Race;
                Model = owner.Model;
                Gender = gender;
            }

            // Determine realm from class
            foreach (var kvp in GlobalConstants.STARTING_CLASSES_DICT)
            {
                if (kvp.Value.Contains((eCharacterClass)CharacterClass.ID))
                {
                    Realm = kvp.Key;
                    break;
                }
            }

            Size = (byte)Util.Random(45, 60);
        }

        #endregion

        #region Stats

        private void InitializeBotStats()
        {
            if (CharacterClass == null)
                return;

            // Apply race base stats
            if (GlobalConstants.STARTING_STATS_DICT.TryGetValue((eRace)Race, out var statDict))
            {
                ChangeBaseStat(eStat.STR, (short)statDict[eStat.STR]);
                ChangeBaseStat(eStat.CON, (short)statDict[eStat.CON]);
                ChangeBaseStat(eStat.DEX, (short)statDict[eStat.DEX]);
                ChangeBaseStat(eStat.QUI, (short)statDict[eStat.QUI]);
                ChangeBaseStat(eStat.INT, (short)statDict[eStat.INT]);
                ChangeBaseStat(eStat.PIE, (short)statDict[eStat.PIE]);
                ChangeBaseStat(eStat.EMP, (short)statDict[eStat.EMP]);
                ChangeBaseStat(eStat.CHR, (short)statDict[eStat.CHR]);
            }

            // Apply class stat bonuses
            ChangeBaseStat(CharacterClass.PrimaryStat, 10);
            ChangeBaseStat(CharacterClass.SecondaryStat, 10);
            ChangeBaseStat(CharacterClass.TertiaryStat, 10);

            // Apply per-level stat growth (for levels 6+)
            for (int i = 6; i <= Level; i++)
            {
                if (CharacterClass.PrimaryStat != eStat.UNDEFINED)
                    ChangeBaseStat(CharacterClass.PrimaryStat, 1);

                if (CharacterClass.SecondaryStat != eStat.UNDEFINED && ((i - 6) % 2 == 0))
                    ChangeBaseStat(CharacterClass.SecondaryStat, 1);

                if (CharacterClass.TertiaryStat != eStat.UNDEFINED && ((i - 6) % 3 == 0))
                    ChangeBaseStat(CharacterClass.TertiaryStat, 1);
            }
        }

        #endregion

        #region Specialization System

        protected virtual void AddSpecialization(Specialization skill, bool notify)
        {
            if (skill == null)
                return;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (m_specialization.TryGetValue(skill.KeyName, out Specialization specialization))
                {
                    specialization.Level = skill.Level;
                    return;
                }

                m_specialization.Add(skill.KeyName, skill);
            }
        }

        public virtual bool RemoveSpecialization(string specKeyName)
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                return m_specialization.Remove(specKeyName);
            }
        }

        public virtual IList<Specialization> GetSpecList()
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                return m_specialization.Select(item => item.Value)
                    .OrderBy(it => it.LevelRequired).ThenBy(it => it.ID).ToList();
            }
        }

        public virtual Specialization GetSpecializationByName(string name)
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                foreach (var entry in m_specialization)
                {
                    if (entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return entry.Value;
                }
            }

            return null;
        }

        public override int GetBaseSpecLevel(string keyName)
        {
            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (m_specialization.TryGetValue(keyName, out Specialization spec))
                    return spec.Level;
            }

            return 0;
        }

        public override int GetModifiedSpecLevel(string keyName)
        {
            if (keyName.StartsWith(GlobalSpellsLines.Champion_Lines_StartWith))
                return 50;

            if (keyName.StartsWith(GlobalSpellsLines.Realm_Spells))
                return Level;

            Specialization spec = null;
            int level = 0;

            lock (((ICollection)m_specialization).SyncRoot)
            {
                if (!m_specialization.TryGetValue(keyName, out spec))
                {
                    if (keyName == GlobalSpellsLines.Combat_Styles_Effect)
                    {
                        if (CharacterClass.ID == (int)eCharacterClass.Reaver || CharacterClass.ID == (int)eCharacterClass.Heretic)
                            level = GetModifiedSpecLevel(Specs.Flexible);
                        if (CharacterClass.ID == (int)eCharacterClass.Valewalker)
                            level = GetModifiedSpecLevel(Specs.Scythe);
                        if (CharacterClass.ID == (int)eCharacterClass.Savage)
                            level = GetModifiedSpecLevel(Specs.Savagery);
                    }

                    return level;
                }
            }

            if (spec != null)
            {
                level = spec.Level;
                eProperty skillProp = SkillBase.SpecToSkill(keyName);
                if (skillProp != eProperty.Undefined)
                    level += GetModified(skillProp);
            }

            return level;
        }

        public virtual void LoadClassSpecializations(bool sendMessage)
        {
            if (CharacterClass == null)
                return;

            IDictionary<Specialization, int> careers = SkillBase.GetSpecializationCareer(CharacterClass.ID);
            var speclist = GetSpecList();
            var careerslist = careers.Keys.Select(k => k.KeyName.ToLower());

            foreach (var spec in speclist.Where(sp => sp.Trainable || !sp.AllowSave))
            {
                if (!careerslist.Contains(spec.KeyName.ToLower()))
                    RemoveSpecialization(spec.KeyName);
            }

            foreach (var constraint in careers)
            {
                if (constraint.Key is IMasterLevelsSpecialization)
                    continue;

                if (Level >= constraint.Value)
                {
                    if (!HasSpecialization(constraint.Key.KeyName))
                        AddSpecialization(constraint.Key, sendMessage);
                }
                else
                {
                    if (HasSpecialization(constraint.Key.KeyName))
                        RemoveSpecialization(constraint.Key.KeyName);
                }
            }
        }

        public void SpendSpecPoints(byte level, byte previousLevel)
        {
            if (BotSpec == null)
                return;

            BotSpec.SpecLines = BotSpec.SpecLines.OrderByDescending(ratio => ratio.levelRatio).ToList();

            int leftOverSpecPoints = m_leftOverSpecPoints;
            bool spentPoints = true;
            bool spendLeftOverPoints = false;
            bool botCreation = level - previousLevel > 1;

            byte index = (byte)(previousLevel + 1);

            if (level == previousLevel)
                index = level;

            for (byte i = index; i <= Level; i++)
            {
                spentPoints = true;
                spendLeftOverPoints = false;

                int totalSpecPointsThisLevel = GetSpecPointsForLevel(i, botCreation) + leftOverSpecPoints;

                while (spentPoints)
                {
                    spentPoints = false;

                    foreach (BotSpecLine specLine in BotSpec.SpecLines)
                    {
                        if (specLine.levelRatio <= 0 && Level < 50)
                            continue;

                        Specialization spec = GetSpecializationByName(specLine.Spec);

                        if (spec != null)
                        {
                            if (spec.Level < specLine.SpecCap && spec.Level < i)
                            {
                                int specRatio = (int)(i * specLine.levelRatio);

                                if (!spendLeftOverPoints && spec.Level >= specRatio)
                                    continue;

                                int totalCost = spec.Level + 1;

                                if (totalSpecPointsThisLevel >= totalCost)
                                {
                                    totalSpecPointsThisLevel -= totalCost;
                                    spec.Level++;
                                    spentPoints = true;
                                }
                            }
                        }
                    }

                    if (!spentPoints && !spendLeftOverPoints)
                    {
                        spendLeftOverPoints = true;
                        spentPoints = true;
                    }
                }

                m_leftOverSpecPoints = leftOverSpecPoints = totalSpecPointsThisLevel;
            }
        }

        private int GetSpecPointsForLevel(int level, bool botCreation)
        {
            int specpoints = 0;

            if (botCreation)
                if (level > 40)
                    specpoints += CharacterClass.SpecPointsMultiplier * (level - 1) / 20;

            if (level > 5)
                specpoints += CharacterClass.SpecPointsMultiplier * level / 10;
            else if (level >= 2)
                specpoints = level;

            return specpoints;
        }

        public virtual void RefreshSpecDependantSkills(bool sendMessages)
        {
            LoadClassSpecializations(sendMessages);

            lock (((ICollection)m_specialization).SyncRoot)
            {
                foreach (Specialization spec in m_specialization.Values)
                {
                    foreach (Ability ab in spec.GetAbilitiesForLiving(this))
                    {
                        if (!HasAbility(ab.KeyName) || GetAbility(ab.KeyName).Level < ab.Level)
                            AddAbility(ab, false);
                    }

                    foreach (Style st in spec.GetStylesForLiving(this))
                    {
                        if (st.SpecLevelRequirement == 2 && Level > 5)
                        {
                            if (Styles.Contains(st))
                                Styles.Remove(st);

                            continue;
                        }

                        AddStyle(st, false);
                    }

                    foreach (SpellLine sl in spec.GetSpellLinesForLiving(this))
                    {
                        AddSpellLine(sl, false);
                    }
                }
            }
        }

        public virtual void AddStyle(Style st, bool notify)
        {
            lock (Styles)
            {
                if (!Styles.Contains(st))
                    Styles.Add(st);
            }

            Styles = Styles;
        }

        public virtual void AddSpellLine(SpellLine line, bool notify)
        {
            if (line == null)
                return;

            SpellLine oldline = GetSpellLine(line.KeyName);
            if (oldline == null)
            {
                lock (m_spellLines)
                {
                    m_spellLines.Add(line);
                }
            }
            else
            {
                oldline.Level = line.Level;
            }
        }

        public virtual List<SpellLine> GetSpellLines()
        {
            lock (m_spellLines)
            {
                return new List<SpellLine>(m_spellLines);
            }
        }

        #endregion

        #region Spell Resolution

        protected ReaderWriterList<Tuple<Skill, Skill>> m_usableSkills = new ReaderWriterList<Tuple<Skill, Skill>>();
        protected ReaderWriterList<Tuple<SpellLine, List<Skill>>> m_usableListSpells = new ReaderWriterList<Tuple<SpellLine, List<Skill>>>();

        public virtual new List<Tuple<SpellLine, List<Skill>>> GetAllUsableListSpells(bool update = false)
        {
            List<Tuple<SpellLine, List<Skill>>> results = new List<Tuple<SpellLine, List<Skill>>>();

            if (!update)
            {
                if (m_usableListSpells.Count > 0)
                    results = new List<Tuple<SpellLine, List<Skill>>>(m_usableListSpells);

                if (results.Count > 0)
                    return results;
            }

            m_usableListSpells.FreezeWhile(innerList =>
            {
                List<Tuple<SpellLine, List<Skill>>> finalbase = new List<Tuple<SpellLine, List<Skill>>>();
                List<Tuple<SpellLine, List<Skill>>> finalspec = new List<Tuple<SpellLine, List<Skill>>>();

                foreach (Specialization spec in GetSpecList().Where(item => !item.HybridSpellList))
                {
                    var spells = spec.GetLinesSpellsForLiving(this);

                    foreach (SpellLine sl in spec.GetSpellLinesForLiving(this))
                    {
                        List<Tuple<SpellLine, List<Skill>>> working;
                        if (sl.IsBaseLine)
                            working = finalbase;
                        else
                            working = finalspec;

                        List<Skill> sps = new List<Skill>();
                        SpellLine key = spells.Keys.FirstOrDefault(el => el.ID == sl.ID);

                        if (key != null && spells.TryGetValue(key, out List<Skill> spellsInLine))
                        {
                            foreach (Skill sp in spellsInLine)
                                sps.Add(sp);
                        }

                        working.Add(new Tuple<SpellLine, List<Skill>>(sl, sps));
                    }
                }

                innerList.Clear();
                foreach (var tp in finalbase)
                {
                    innerList.Add(tp);
                    results.Add(tp);
                }

                foreach (var tp in finalspec)
                {
                    innerList.Add(tp);
                    results.Add(tp);
                }
            });

            return results;
        }

        public virtual List<Tuple<Skill, Skill>> GetAllUsableSkills(bool update = false)
        {
            List<Tuple<Skill, Skill>> results = new List<Tuple<Skill, Skill>>();

            if (!update)
            {
                if (m_usableSkills.Count > 0)
                    results = new List<Tuple<Skill, Skill>>(m_usableSkills);

                if (results.Count > 0)
                    return results;
            }

            m_usableSkills.FreezeWhile(innerList =>
            {
                IList<Specialization> specs = GetSpecList();
                List<Tuple<Skill, Skill>> copylist = new List<Tuple<Skill, Skill>>(innerList);

                foreach (Specialization spec in specs.Where(item => item.Trainable))
                {
                    int index = innerList.FindIndex(e => (e.Item1 is Specialization specialization) && specialization.ID == spec.ID);

                    if (index < 0)
                        innerList.Insert(innerList.Count(e => e.Item1 is Specialization), new Tuple<Skill, Skill>(spec, spec));
                    else
                    {
                        copylist.Remove(innerList[index]);
                        innerList[index] = new Tuple<Skill, Skill>(spec, spec);
                    }
                }

                foreach (Specialization spec in specs)
                {
                    foreach (Ability abv in spec.GetAbilitiesForLiving(this))
                    {
                        Ability ab = GetAbility(abv.KeyName);
                        if (ab == null)
                            ab = abv;

                        int index = innerList.FindIndex(k => (k.Item1 is Ability ability) && ability.ID == ab.ID);

                        if (index < 0)
                            innerList.Add(new Tuple<Skill, Skill>(ab, spec));
                        else
                        {
                            copylist.Remove(innerList[index]);
                            innerList[index] = new Tuple<Skill, Skill>(ab, spec);
                        }
                    }
                }

                foreach (Specialization spec in specs.Where(item => item.HybridSpellList))
                {
                    foreach (var sl in spec.GetLinesSpellsForLiving(this))
                    {
                        int index = -1;

                        foreach (Spell sp in sl.Value.Where(it => (it is Spell) && !((Spell)it).NeedInstrument).Cast<Spell>())
                        {
                            if (index < innerList.Count)
                                index = innerList.FindIndex(index + 1, e => (e.Item2 is SpellLine spellLine) && spellLine.ID == sl.Key.ID && (e.Item1 is Spell spell) && !spell.NeedInstrument);

                            if (index < 0 || index >= innerList.Count)
                            {
                                innerList.Add(new Tuple<Skill, Skill>(sp, sl.Key));
                                index = innerList.Count;
                            }
                            else
                            {
                                copylist.Remove(innerList[index]);
                                innerList[index] = new Tuple<Skill, Skill>(sp, sl.Key);
                            }
                        }
                    }
                }

                int songIndex = -1;
                foreach (Specialization spec in specs.Where(item => item.HybridSpellList))
                {
                    foreach (var sl in spec.GetLinesSpellsForLiving(this))
                    {
                        foreach (Spell sp in sl.Value.Where(it => (it is Spell) && ((Spell)it).NeedInstrument).Cast<Spell>())
                        {
                            if (songIndex < innerList.Count)
                                songIndex = innerList.FindIndex(songIndex + 1, e => (e.Item1 is Spell) && ((Spell)e.Item1).NeedInstrument);

                            if (songIndex < 0 || songIndex >= innerList.Count)
                            {
                                innerList.Add(new Tuple<Skill, Skill>(sp, sl.Key));
                                songIndex = innerList.Count;
                            }
                            else
                            {
                                copylist.Remove(innerList[songIndex]);
                                innerList[songIndex] = new Tuple<Skill, Skill>(sp, sl.Key);
                            }
                        }
                    }
                }

                foreach (Specialization spec in specs)
                {
                    foreach (Style st in spec.GetStylesForLiving(this))
                    {
                        int index = innerList.FindIndex(e => (e.Item1 is Style) && e.Item1.ID == st.ID);
                        if (index < 0)
                            innerList.Add(new Tuple<Skill, Skill>(st, spec));
                        else
                        {
                            copylist.Remove(innerList[index]);
                            innerList[index] = new Tuple<Skill, Skill>(st, spec);
                        }
                    }
                }

                foreach (var item in copylist)
                    innerList.Remove(item);

                foreach (var el in innerList)
                    results.Add(el);
            });

            return results;
        }

        private void SetBotSpells()
        {
            if (CharacterClass == null)
                return;

            // Casters use list spells (highest level per type), hybrids use all usable skills
            if (CharacterClass.ClassType == eClassType.ListCaster)
                SetCasterSpells();
            else
                SetHybridSpells();
        }

        private void SetCasterSpells()
        {
            List<Spell> spells = new List<Spell>();

            var dict = GetAllUsableListSpells();

            if (dict != null && dict.Count > 0)
            {
                foreach (var tuple in dict)
                {
                    if (tuple.Item2.Count > 0)
                    {
                        foreach (Skill skill in tuple.Item2)
                        {
                            if (skill is Spell spell && !spells.Contains(spell))
                                spells.Add(spell);
                        }
                    }
                }
            }

            List<Spell> highestSpellLevels = GetHighestLevelSpells(spells);

            if (highestSpellLevels.Count > 0)
                Spells = highestSpellLevels;
        }

        private void SetHybridSpells()
        {
            List<Spell> spells = new List<Spell>();

            var usableSkills = GetAllUsableSkills();

            for (int i = 0; i < usableSkills.Count; i++)
            {
                Skill skill = usableSkills[i].Item1;

                if (skill is Spell)
                    spells.Add((Spell)skill);
            }

            if (spells.Count > 0)
                Spells = spells;
        }

        public List<Spell> GetHighestLevelSpells(List<Spell> spells)
        {
            spells = spells.OrderByDescending(spell => spell.Level).ToList();

            List<Spell> highestLevelSpells = new List<Spell>();

            foreach (Spell currentSpell in spells)
            {
                Spell existingSpell = highestLevelSpells.FirstOrDefault(x => AreSpellsEqual(x, currentSpell));

                if (existingSpell != null)
                {
                    if (existingSpell.Level < currentSpell.Level)
                        highestLevelSpells.Remove(existingSpell);
                    else
                        continue;
                }

                highestLevelSpells.Add(currentSpell);
            }

            return highestLevelSpells;
        }

        private bool AreSpellsEqual(Spell spellOne, Spell spellTwo)
        {
            return spellOne.DamageType == spellTwo.DamageType &&
                   spellOne.SpellType == spellTwo.SpellType &&
                   spellOne.Frequency == spellTwo.Frequency &&
                   spellOne.CastTime == spellTwo.CastTime &&
                   spellOne.Target == spellTwo.Target &&
                   spellOne.Group == spellTwo.Group &&
                   spellOne.IsPBAoE == spellTwo.IsPBAoE;
        }

        #endregion

        #region Equipment

        public int BestArmorLevel
        {
            get
            {
                int bestLevel = -1;
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("AlbArmor"));
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("HibArmor"));
                bestLevel = Math.Max(bestLevel, GetAbilityLevel("MidArmor"));
                return bestLevel;
            }
        }

        public int BestShieldLevel => GetAbilityLevel("Shield");

        private void EquipBot()
        {
            Inventory = new BotInventory();

            SetWeapons();
            SetShield();
            SetRanged();
            SetArmor();
            SetJewelry();
            RefreshItemBonuses();
        }

        private void SetWeapons()
        {
            if (BotSpec == null)
                return;

            switch (BotSpec.SpecType)
            {
                case eSpecType.DualWield:
                case eSpecType.DualWieldAndShield:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.leftHand);
                    break;

                case eSpecType.LeftAxe:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.twoHand);
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponTwoType, eHand.leftHand);
                    break;

                case eSpecType.OneHandAndShield:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    break;

                case eSpecType.OneHandHybrid:
                case eSpecType.TwoHandHybrid:
                case eSpecType.TwoHanded when CharacterClass.ID != (int)eCharacterClass.Valewalker:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponTwoType, eHand.twoHand, BotSpec.DamageType);
                    break;

                case eSpecType.Mid:
                case eSpecType.PacHealer:
                case eSpecType.AugHealer:
                case eSpecType.MendHealer:
                case eSpecType.MendShaman:
                case eSpecType.AugShaman:
                case eSpecType.SubtShaman:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.twoHand);
                    break;

                case eSpecType.Instrument:
                    BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    BotEquipment.SetInstrumentROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Instrument, eInventorySlot.TwoHandWeapon, eInstrumentType.Flute);
                    BotEquipment.SetInstrumentROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Instrument, eInventorySlot.DistanceWeapon, eInstrumentType.Drum);
                    BotEquipment.SetInstrumentROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Instrument, eInventorySlot.FirstEmptyBackpack, eInstrumentType.Lute);
                    break;

                default:
                    if (CharacterClass.ClassType == eClassType.ListCaster ||
                        CharacterClass.ID == (int)eCharacterClass.Friar ||
                        CharacterClass.ID == (int)eCharacterClass.Valewalker)
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.twoHand);
                    else if (CharacterClass.ID != (int)eCharacterClass.Hunter)
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    else
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.twoHand);

                    if (BotSpec.WeaponOneType == eObjectType.Sword)
                        BotEquipment.SetMeleeWeapon(this, BotSpec.WeaponOneType, eHand.oneHand);
                    break;
            }

            if (BotSpec.Is2H)
                SwitchWeapon(eActiveWeaponSlot.TwoHanded);
            else
                SwitchWeapon(eActiveWeaponSlot.Standard);
        }

        private void SetShield()
        {
            BotEquipment.SetShield(this, BestShieldLevel);
        }

        private void SetRanged()
        {
            foreach (Ability ability in GetAllAbilities())
            {
                switch (ability.KeyName)
                {
                    case "Weaponry: Thrown": BotEquipment.SetRangedWeapon(this, eObjectType.Thrown); break;
                    case "Weaponry: Shortbows": BotEquipment.SetRangedWeapon(this, eObjectType.Fired); break;
                    case "Weaponry: Crossbow": BotEquipment.SetRangedWeapon(this, eObjectType.Crossbow); break;
                    case "Weaponry: Recurved Bows": BotEquipment.SetRangedWeapon(this, eObjectType.RecurvedBow); SwitchWeapon(eActiveWeaponSlot.Distance); break;
                    case "Weaponry: Longbows": BotEquipment.SetRangedWeapon(this, eObjectType.Longbow); SwitchWeapon(eActiveWeaponSlot.Distance); break;
                    case "Weaponry: Composite Bows": BotEquipment.SetRangedWeapon(this, eObjectType.CompositeBow); SwitchWeapon(eActiveWeaponSlot.Distance); break;
                }
            }
        }

        private void SetArmor()
        {
            int armorLevel = BestArmorLevel;
            eObjectType armorType = eObjectType.GenericArmor;

            switch (armorLevel)
            {
                case 1: armorType = eObjectType.Cloth; break;
                case 2: armorType = eObjectType.Leather; break;
                case 3:
                    armorType = (Realm == eRealm.Hibernia) ? eObjectType.Reinforced : eObjectType.Studded;
                    break;
                case 4:
                    armorType = (Realm == eRealm.Hibernia) ? eObjectType.Scale : eObjectType.Chain;
                    break;
                case 5: armorType = eObjectType.Plate; break;
            }

            BotEquipment.SetArmor(this, armorType);
        }

        private void SetJewelry()
        {
            BotEquipment.SetJewelryROG(this, Realm, (eCharacterClass)CharacterClass.ID, Level, eObjectType.Magical);
        }

        public virtual void RefreshItemBonuses()
        {
            ItemBonus.Clear();
            string slotToLoad = string.Empty;

            switch (VisibleActiveWeaponSlots)
            {
                case 16: slotToLoad = "rightandleftHandSlot"; break;
                case 18: slotToLoad = "leftandtwoHandSlot"; break;
                case 31: slotToLoad = "leftHandSlot"; break;
                case 34: slotToLoad = "twoHandSlot"; break;
                case 51: slotToLoad = "distanceSlot"; break;
                case 240: slotToLoad = "righttHandSlot"; break;
                case 242: slotToLoad = "twoHandSlot"; break;
            }

            foreach (DbInventoryItem item in Inventory.EquippedItems)
            {
                if (item == null)
                    continue;

                bool add = true;

                if (slotToLoad != string.Empty)
                {
                    switch (item.SlotPosition)
                    {
                        case Slot.TWOHAND:
                            if (!slotToLoad.Contains("twoHandSlot"))
                                add = false;
                            break;
                        case Slot.RIGHTHAND:
                            if (!slotToLoad.Contains("right"))
                                add = false;
                            break;
                        case Slot.SHIELD:
                        case Slot.LEFTHAND:
                            if (!slotToLoad.Contains("left"))
                                add = false;
                            break;
                        case Slot.RANGED:
                            if (slotToLoad != "distanceSlot")
                                add = false;
                            break;
                    }
                }

                if (!add)
                    continue;

                if (item.IsMagical)
                {
                    if (item.Bonus1 != 0) ItemBonus[(eProperty)item.Bonus1Type] += item.Bonus1;
                    if (item.Bonus2 != 0) ItemBonus[(eProperty)item.Bonus2Type] += item.Bonus2;
                    if (item.Bonus3 != 0) ItemBonus[(eProperty)item.Bonus3Type] += item.Bonus3;
                    if (item.Bonus4 != 0) ItemBonus[(eProperty)item.Bonus4Type] += item.Bonus4;
                    if (item.Bonus5 != 0) ItemBonus[(eProperty)item.Bonus5Type] += item.Bonus5;
                    if (item.Bonus6 != 0) ItemBonus[(eProperty)item.Bonus6Type] += item.Bonus6;
                    if (item.Bonus7 != 0) ItemBonus[(eProperty)item.Bonus7Type] += item.Bonus7;
                    if (item.Bonus8 != 0) ItemBonus[(eProperty)item.Bonus8Type] += item.Bonus8;
                    if (item.Bonus9 != 0) ItemBonus[(eProperty)item.Bonus9Type] += item.Bonus9;
                    if (item.Bonus10 != 0) ItemBonus[(eProperty)item.Bonus10Type] += item.Bonus10;
                    if (item.ExtraBonus != 0) ItemBonus[(eProperty)item.ExtraBonusType] += item.ExtraBonus;
                }
            }
        }

        #endregion

        #region Database

        public void SaveToDatabase()
        {
            BotDatabase.SaveBot(this);
        }

        public void LoadFromDatabase()
        {
            log.Debug($"LoadFromDatabase called for bot {Name}");
        }

        #endregion

        #region Movement

        public new void StopFollowing()
        {
            base.StopFollowing();
        }

        public void MoveTo(Vector3 position)
        {
            WalkTo(position, MaxSpeed);
        }

        public override bool IsMoving => base.IsMoving || IsStrafing;

        #endregion

        #region Effectiveness

        public override double Effectiveness
        {
            get => 1.0;
            set { }
        }

        double IGamePlayer.Effectiveness
        {
            get => Effectiveness;
            set { }
        }

        #endregion

        #region InteractDistance

        public override int InteractDistance => WorldMgr.VISIBILITY_DISTANCE;

        #endregion
    }
}
