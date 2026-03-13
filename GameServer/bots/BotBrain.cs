using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Events;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;
using DOL.GS.RealmAbilities;
using DOL.GS.SkillHandler;
using DOL.GS.Spells;
using DOL.Logging;

namespace DOL.AI.Brain
{
    public class BotBrain : ABrain, IOldAggressiveBrain
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public GameBot BotBody => Body as GameBot;

        #region IOldAggressiveBrain

        public int AggroLevel { get; set; } = 100;
        public int AggroRange { get; set; } = 3600;

        #endregion

        #region Aggro List

        public const int MAX_AGGRO_DISTANCE = 3600;
        public const int MAX_AGGRO_LIST_DISTANCE = 6000;
        private const int EFFECTIVE_AGGRO_AMOUNT_CALCULATION_DISTANCE_THRESHOLD = 500;

        protected ConcurrentDictionary<GameLiving, AggroAmount> AggroList { get; private set; } = new();
        protected List<(GameLiving, long)> OrderedAggroList { get; private set; } = [];
        public GameLiving LastHighestThreatInAttackRange { get; private set; }

        public class AggroAmount(long @base = 0)
        {
            public long Base { get; set; } = @base;
            public long Effective { get; set; }
        }

        public virtual bool HasAggro => !AggroList.IsEmpty;

        public virtual void AddToAggroList(GameLiving living, long aggroAmount)
        {
            if (Body.IsConfused || !Body.IsAlive || living == null)
                return;

            ForceAddToAggroList(living, aggroAmount);
        }

        public void ForceAddToAggroList(GameLiving living, long aggroAmount)
        {
            if (aggroAmount > 0)
            {
                foreach (ProtectECSGameEffect protect in living.effectListComponent.GetAbilityEffects().Where(e => e.EffectType is eEffect.Protect))
                {
                    if (protect.Target != living)
                        continue;

                    GameLiving protectSource = protect.Source;

                    if (protectSource.IsIncapacitated || protectSource.IsSitting)
                        continue;

                    if (!living.IsWithinRadius(protectSource, ProtectAbilityHandler.PROTECT_DISTANCE))
                        continue;

                    int abilityLevel = protectSource.GetAbilityLevel(Abilities.Protect);
                    long protectAmount = (long)(abilityLevel * 0.1 * aggroAmount);

                    if (protectAmount > 0)
                    {
                        aggroAmount -= protectAmount;

                        if (protectSource is GamePlayer playerProtectSource)
                        {
                            playerProtectSource.Out.SendMessage(
                                $"You absorb {protectAmount} aggro from {living.GetName(0, false)} for {Body.GetName(0, false)}!",
                                eChatType.CT_System, eChatLoc.CL_SystemWindow);
                        }

                        AggroList.AddOrUpdate(protectSource, Add, Update, protectAmount);
                    }
                }
            }

            AggroList.AddOrUpdate(living, Add, Update, aggroAmount);

            if (living is IGamePlayer player)
            {
                if (player.Group != null)
                {
                    foreach (GamePlayer playerInGroup in player.Group.GetPlayersInTheGroup())
                    {
                        if (playerInGroup != living)
                            AggroList.TryAdd((GameLiving)playerInGroup, new());
                    }
                }
            }

            if (FSM.GetCurrentState() != FSM.GetState(eFSMStateType.AGGRO) && HasAggro && !IsHealer)
            {
                FSM.SetCurrentState(eFSMStateType.AGGRO);
                NextThinkTick = GameLoop.GameLoopTime;
            }

            static AggroAmount Add(GameLiving key, long arg)
            {
                return new(Math.Max(0, arg));
            }

            static AggroAmount Update(GameLiving key, AggroAmount oldValue, long arg)
            {
                oldValue.Base = Math.Max(0, oldValue.Base + arg);
                return oldValue;
            }
        }

        public virtual void RemoveFromAggroList(GameLiving living)
        {
            AggroList.TryRemove(living, out _);
        }

        public long GetBaseAggroAmount(GameLiving living)
        {
            return AggroList.TryGetValue(living, out AggroAmount aggroAmount) ? aggroAmount.Base : 0;
        }

        public virtual void ClearAggroList()
        {
            AggroList.Clear();

            lock (((ICollection)OrderedAggroList).SyncRoot)
            {
                OrderedAggroList.Clear();
            }

            LastHighestThreatInAttackRange = null;
        }

        protected virtual bool ShouldBeRemovedFromAggroList(GameLiving living)
        {
            return !living.IsAlive ||
                   living.ObjectState != GameObject.eObjectState.Active ||
                   living.CurrentRegion != Body.CurrentRegion ||
                   !Body.IsWithinRadius(living, MAX_AGGRO_LIST_DISTANCE) ||
                   (!GameServer.ServerRules.IsAllowedToAttack(Body, living, true) && !living.effectListComponent.ContainsEffectForEffectType(eEffect.Shade));
        }

        protected virtual bool ShouldBeIgnoredFromAggroList(GameLiving living)
        {
            return living.effectListComponent.ContainsEffectForEffectType(eEffect.Shade);
        }

        protected virtual GameLiving CleanUpAggroListAndGetHighestModifiedThreat()
        {
            OrderedAggroList.Clear();

            int attackRange = Body.attackComponent.AttackRange;
            GameLiving highestThreat = null;
            KeyValuePair<GameLiving, AggroAmount> currentTarget = default;
            long highestEffectiveAggro = -1;
            long highestEffectiveAggroInAttackRange = -1;

            foreach (var pair in AggroList)
            {
                GameLiving living = pair.Key;

                if (Body.TargetObject == living)
                    currentTarget = pair;

                if (ShouldBeRemovedFromAggroList(living))
                {
                    AggroList.TryRemove(living, out _);
                    continue;
                }

                if (ShouldBeIgnoredFromAggroList(living))
                    continue;

                AggroAmount aggroAmount = pair.Value;
                double distance = Body.GetDistanceTo(living);
                aggroAmount.Effective = distance > EFFECTIVE_AGGRO_AMOUNT_CALCULATION_DISTANCE_THRESHOLD ?
                                        (long)Math.Ceiling(aggroAmount.Base * (EFFECTIVE_AGGRO_AMOUNT_CALCULATION_DISTANCE_THRESHOLD / distance)) :
                                        aggroAmount.Base;

                if (aggroAmount.Effective > highestEffectiveAggroInAttackRange)
                {
                    if (distance <= attackRange)
                    {
                        highestEffectiveAggroInAttackRange = aggroAmount.Effective;
                        LastHighestThreatInAttackRange = living;
                    }

                    if (aggroAmount.Effective > highestEffectiveAggro)
                    {
                        highestEffectiveAggro = aggroAmount.Effective;
                        highestThreat = living;
                    }
                }
            }

            if (highestThreat != null)
            {
                if (currentTarget.Key != null && currentTarget.Key != highestThreat && currentTarget.Value.Effective >= highestEffectiveAggro)
                    highestThreat = currentTarget.Key;
            }
            else
            {
                return AggroList.FirstOrDefault().Key?.ControlledBrain?.Body;
            }

            return highestThreat;
        }

        protected virtual GameLiving CalculateNextAttackTarget()
        {
            return CleanUpAggroListAndGetHighestModifiedThreat();
        }

        public virtual bool CanAggroTarget(GameLiving target)
        {
            if (!GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                return false;

            GameLiving realTarget = target;

            if (realTarget is GameNPC npcTarget && npcTarget.Brain is IControlledBrain npcTargetBrain)
                realTarget = npcTargetBrain.GetLivingOwner();

            if (realTarget.IsObjectGreyCon(Body))
                return false;

            if (realTarget is IGamePlayer && realTarget.Realm != Body.Realm)
                return true;

            return AggroLevel > 0;
        }

        public virtual void OnAttackedByEnemy(AttackData ad)
        {
            if (!Body.IsAlive || Body.ObjectState != GameObject.eObjectState.Active || FSM.GetCurrentState() == FSM.GetState(eFSMStateType.PASSIVE))
                return;

            if (ad.GeneratesAggro)
                ConvertDamageToAggroAmount(ad.Attacker, Math.Max(1, ad.Damage + ad.CriticalDamage));
        }

        protected virtual void ConvertDamageToAggroAmount(GameLiving attacker, int damage)
        {
            if (attacker is GameNPC NpcAttacker && NpcAttacker.Brain is ControlledMobBrain controlledBrain)
            {
                damage = controlledBrain.ModifyDamageWithTaunt(damage);

                int aggroForOwner = (int)(damage * 0.15);

                if (aggroForOwner == 0)
                {
                    AddToAggroList(controlledBrain.Owner, 1);
                    AddToAggroList(NpcAttacker, Math.Max(2, damage));
                }
                else
                {
                    AddToAggroList(controlledBrain.Owner, aggroForOwner);
                    AddToAggroList(NpcAttacker, damage - aggroForOwner);
                }
            }
            else
                AddToAggroList(attacker, damage);
        }

        public void OnGroupMemberAttacked(AttackData ad)
        {
            switch (ad.AttackResult)
            {
                case eAttackResult.Blocked:
                case eAttackResult.Evaded:
                case eAttackResult.Fumbled:
                case eAttackResult.HitStyle:
                case eAttackResult.HitUnstyled:
                case eAttackResult.Missed:
                case eAttackResult.Parried:
                    AddToAggroList(ad.Attacker, ad.Attacker.EffectiveLevel + ad.Damage + ad.CriticalDamage);
                    break;
            }

            if (FSM.GetState(eFSMStateType.AGGRO) != FSM.GetCurrentState() && !IsHealer)
                FSM.SetCurrentState(eFSMStateType.AGGRO);
        }

        #endregion

        #region Brain Core

        public bool IsHealer;
        public bool AlreadyCheckedHeals;
        public override int ThinkInterval { get; set; } = 500;

        public BotBrain() : base()
        {
            FSM = new FSM();
            FSM.Add(new BotState_Idle(this));
            FSM.Add(new BotState_Follow(this));
            FSM.Add(new BotState_Aggro(this));
            FSM.Add(new BotState_Passive(this));
            FSM.SetCurrentState(eFSMStateType.IDLE);
        }

        public override bool Stop()
        {
            if (base.Stop())
            {
                ClearAggroList();
                return true;
            }

            return false;
        }

        public override void Think()
        {
            FSM.Think();
        }

        public override void KillFSM()
        {
            FSM.KillFSM();
        }

        public override void Notify(DOLEvent e, object sender, EventArgs args)
        {
            if (e == GameLivingEvent.AttackedByEnemy && args is AttackedByEnemyEventArgs attackArgs)
            {
                OnAttackedByEnemy(attackArgs.AttackData);
            }
        }

        #endregion

        #region FSM States

        private class BotState : FSMState
        {
            protected BotBrain _brain;

            public BotState(BotBrain brain) : base()
            {
                _brain = brain;
            }

            public override void Enter() { }
            public override void Exit() { }
            public override void Think() { }
        }

        private class BotState_Idle : BotState
        {
            public BotState_Idle(BotBrain brain) : base(brain)
            {
                StateType = eFSMStateType.IDLE;
            }

            public override void Enter()
            {
                _brain.Body.StopFollowing();
                _brain.Body.StopAttack();
            }

            public override void Think()
            {
                _brain.AlreadyCheckedHeals = false;

                if (_brain.HasAggro)
                {
                    _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    return;
                }

                if (_brain.BotBody?.Owner != null && _brain.BotBody.Owner.IsAlive)
                {
                    if (!_brain.Body.IsWithinRadius(_brain.BotBody.Owner, BotManager.MAX_FOLLOW_DISTANCE))
                    {
                        _brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);
                        return;
                    }
                }

                _brain.CheckSpells(eCheckSpellType.Defensive);
            }
        }

        private class BotState_Follow : BotState
        {
            public BotState_Follow(BotBrain brain) : base(brain)
            {
                StateType = eFSMStateType.FOLLOW;
            }

            public override void Enter()
            {
                if (_brain.BotBody?.Owner != null)
                    _brain.Body.Follow(_brain.BotBody.Owner, BotManager.FOLLOW_DISTANCE, 5000);
            }

            public override void Think()
            {
                _brain.AlreadyCheckedHeals = false;

                if (_brain.CheckHeals())
                    return;

                if (_brain.HasAggro)
                {
                    _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    return;
                }

                if (_brain.BotBody?.Owner == null || !_brain.BotBody.Owner.IsAlive)
                {
                    _brain.FSM.SetCurrentState(eFSMStateType.IDLE);
                    return;
                }

                // Check if owner is attacking something
                if (_brain.BotBody.Owner.TargetObject is GameLiving ownerTarget
                    && (_brain.BotBody.Owner.IsAttacking || (_brain.BotBody.Owner.IsCasting && _brain.BotBody.Owner.castingComponent?.SpellHandler?.Spell?.IsHarmful == true))
                    && _brain.CanAggroTarget(ownerTarget))
                {
                    _brain.AddToAggroList(ownerTarget, 1);
                    return;
                }

                // Check if any group member is being attacked
                if (_brain.Body.Group != null)
                {
                    foreach (GameLiving member in _brain.Body.Group.GetMembersInTheGroup())
                    {
                        if (member == _brain.Body || !member.IsAlive || !member.InCombat)
                            continue;

                        // If a group member is in combat, check if their target is hostile
                        if (member.TargetObject is GameLiving memberTarget
                            && memberTarget.IsAlive
                            && _brain.CanAggroTarget(memberTarget)
                            && _brain.Body.IsWithinRadius(memberTarget, _brain.AggroRange))
                        {
                            _brain.AddToAggroList(memberTarget, 1);
                        }
                    }

                    if (_brain.HasAggro)
                        return;
                }

                if (_brain.Body.FollowTarget != _brain.BotBody.Owner)
                    _brain.Body.Follow(_brain.BotBody.Owner, BotManager.FOLLOW_DISTANCE, 5000);

                if (!_brain.Body.InCombat)
                    _brain.CheckSpells(eCheckSpellType.Defensive);
            }

            public override void Exit()
            {
                _brain.Body.StopFollowing();
            }
        }

        private class BotState_Aggro : BotState
        {
            private const int LEAVE_WHEN_OUT_OF_COMBAT_FOR = 6000;
            private long _aggroEndTime;

            public BotState_Aggro(BotBrain brain) : base(brain)
            {
                StateType = eFSMStateType.AGGRO;
            }

            public override void Enter()
            {
                _aggroEndTime = GameLoop.GameLoopTime + LEAVE_WHEN_OUT_OF_COMBAT_FOR;
            }

            public override void Exit()
            {
                _brain.Body.StopAttack();
                _brain.Body.TargetObject = null;
                _brain.ClearAggroList();
            }

            public override void Think()
            {
                _brain.AlreadyCheckedHeals = false;

                if (!_brain.HasAggro || (!_brain.Body.InCombatInLast(LEAVE_WHEN_OUT_OF_COMBAT_FOR) && GameServiceUtils.ShouldTick(_aggroEndTime)))
                {
                    if (!_brain.Body.IsMezzed && !_brain.Body.IsStunned)
                    {
                        if (_brain.BotBody?.Owner != null && _brain.BotBody.Owner.IsAlive)
                            _brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);
                        else
                            _brain.FSM.SetCurrentState(eFSMStateType.IDLE);

                        return;
                    }
                }

                if (_brain.IsHealer)
                    _brain.CheckHeals();
                else
                    _brain.AttackMostWanted();
            }
        }

        private class BotState_Passive : BotState
        {
            public BotState_Passive(BotBrain brain) : base(brain)
            {
                StateType = eFSMStateType.PASSIVE;
            }

            public override void Enter()
            {
                _brain.Body.StopAttack();
                _brain.Body.StopFollowing();
                _brain.ClearAggroList();
            }

            public override void Think()
            {
                _brain.AlreadyCheckedHeals = false;

                if (_brain.IsHealer)
                    _brain.CheckHeals();
            }
        }

        #endregion

        #region Combat

        public virtual void AttackMostWanted()
        {
            if (!IsActive)
                return;

            Body.TargetObject = CalculateNextAttackTarget();

            if (Body.TargetObject != null)
            {
                if (CheckSpells(eCheckSpellType.Offensive))
                {
                    Body.StopAttack();
                }
                else
                {
                    CheckOffensiveAbilities();

                    if (Body.ControlledBrain != null)
                        Body.ControlledBrain.Attack(Body.TargetObject);

                    if (BotBody.CharacterClass.ClassType == eClassType.ListCaster && BotBody.CharacterClass.ID != (int)eCharacterClass.Valewalker)
                    {
                        ECSGameAbilityEffect quickCast = EffectListService.GetAbilityEffectOnTarget(Body, eEffect.QuickCast);

                        if (quickCast != null)
                        {
                            CheckSpells(eCheckSpellType.Offensive);
                            return;
                        }

                        return;
                    }

                    Body.StartAttack(Body.TargetObject);
                }
            }
        }

        public void CheckOffensiveAbilities()
        {
            if (Body.Abilities == null || Body.Abilities.Count <= 0)
                return;

            foreach (Ability ab in Body.GetAllAbilities())
            {
                if (Body.GetSkillDisabledDuration(ab) == 0)
                {
                    switch (ab.KeyName)
                    {
                        case Abilities.Berserk:
                        {
                            if (Body.TargetObject is GameLiving target)
                            {
                                if (Body.IsWithinRadius(Body.TargetObject, Body.MeleeAttackRange) &&
                                    GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                                {
                                    new BerserkECSGameEffect(new ECSGameEffectInitParams(Body, 20000, 1));
                                    Body.DisableSkill(ab, 420000);
                                }
                            }
                            break;
                        }

                        case Abilities.Stag:
                        {
                            if (Body.TargetObject is GameLiving target)
                            {
                                if (Body.IsWithinRadius(Body.TargetObject, Body.MeleeAttackRange) &&
                                    GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                                {
                                    new StagECSGameEffect(new ECSGameEffectInitParams(Body, StagAbilityHandler.DURATION * 1000, 1), ab.Level);
                                    Body.DisableSkill(ab, StagAbilityHandler.DURATION * 5000);
                                }
                            }
                            break;
                        }
                    }
                }
            }
        }

        #endregion

        #region Spells

        public enum eCheckSpellType
        {
            Offensive,
            Defensive,
            CrowdControl
        }

        protected static SpellLine m_mobSpellLine = SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells);

        public virtual bool CheckSpells(eCheckSpellType type)
        {
            if (Body == null || Body.Spells == null || Body.Spells.Count <= 0)
                return false;

            bool casted = false;
            List<Spell> spellsToCast = new();

            if (CheckHeals())
                return true;

            if (!casted && type == eCheckSpellType.Defensive)
            {
                if (Body.CanCastMiscSpells)
                    casted = CheckDefensiveSpells(Body.MiscSpells);
            }
            else if (!casted && type == eCheckSpellType.Offensive)
            {
                if (BotBody.CharacterClass.ID == (int)eCharacterClass.Cleric)
                {
                    if (!Util.Chance(Math.Max(5, Body.ManaPercent - 50)))
                        return false;
                }

                // Check instant spells
                if (Body.CanCastInstantHarmfulSpells)
                {
                    foreach (Spell spell in Body.InstantHarmfulSpells)
                    {
                        if (CheckInstantOffensiveSpells(spell))
                            break;
                    }
                }

                if (Body.CanCastInstantMiscSpells)
                {
                    foreach (Spell spell in Body.InstantMiscSpells)
                    {
                        if (CheckInstantDefensiveSpells(spell))
                            break;
                    }
                }

                // Nightshade melee focus
                if (BotBody.CharacterClass.ID == (int)eCharacterClass.Nightshade)
                    return false;

                // Melee hybrids prefer melee when in range
                if ((BotBody.CanUsePositionalStyles || BotBody.CanUseAnytimeStyles) && (Body.IsWithinRadius(Body.TargetObject, 550) || Body.ManaPercent <= 10))
                    return false;

                if (BotBody.CanCastCrowdControlSpells)
                {
                    int ccChance = 50;

                    GameLiving livingTarget = Body.TargetObject as GameLiving;

                    if (livingTarget?.TargetObject == Body && Body.IsWithinRadius(Body.TargetObject, 500))
                        ccChance = 95;

                    if (Util.Chance(ccChance))
                    {
                        foreach (Spell spell in BotBody.CrowdControlSpells)
                        {
                            if (CanCastOffensiveSpell(spell) && !LivingHasEffect((GameLiving)Body.TargetObject, spell))
                                spellsToCast.Add(spell);
                        }
                    }
                }

                if (BotBody.CanCastBolts && spellsToCast.Count < 1)
                {
                    foreach (Spell spell in BotBody.BoltSpells)
                    {
                        if (CanCastOffensiveSpell(spell))
                            spellsToCast.Add(spell);
                    }
                }

                if (spellsToCast.Count < 1)
                {
                    if (Body.CanCastHarmfulSpells)
                    {
                        foreach (Spell spell in Body.HarmfulSpells)
                        {
                            if (spell.SpellType == eSpellType.Charm ||
                                spell.SpellType == eSpellType.Amnesia ||
                                spell.SpellType == eSpellType.Confusion ||
                                spell.SpellType == eSpellType.Taunt)
                                continue;

                            if (CanCastOffensiveSpell(spell))
                                spellsToCast.Add(spell);
                        }
                    }
                }

                if (spellsToCast.Count > 0)
                {
                    Spell spellToCast = spellsToCast[Util.Random(spellsToCast.Count - 1)];

                    if (spellToCast.Uninterruptible || !Body.IsBeingInterrupted)
                        casted = CheckOffensiveSpells(spellToCast);
                    else if (!spellToCast.Uninterruptible && Body.IsBeingInterrupted)
                    {
                        if (BotBody.CharacterClass.ClassType == eClassType.ListCaster)
                        {
                            Ability quickCast = Body.GetAbility(Abilities.Quickcast);

                            if (quickCast != null)
                            {
                                if (Body.GetSkillDisabledDuration(quickCast) <= 0)
                                {
                                    new QuickCastECSGameEffect(new ECSGameEffectInitParams(Body, QuickCastECSGameEffect.DURATION + 1000, 1));
                                    Body.DisableSkill(quickCast, 180000);

                                    casted = CheckOffensiveSpells(spellToCast);
                                }
                            }
                        }
                    }
                }
            }

            return casted || Body.IsCasting;
        }

        protected bool CanCastOffensiveSpell(Spell spell)
        {
            if (Body.GetSkillDisabledDuration(spell) <= 0)
            {
                if (spell.CastTime > 0)
                {
                    if (spell.Target is eSpellTarget.ENEMY or eSpellTarget.AREA or eSpellTarget.CONE)
                        return true;
                }
            }

            return false;
        }

        protected bool CanCastDefensiveSpell(Spell spell)
        {
            if (spell == null || spell.IsHarmful)
                return false;

            if (spell.CastTime > 0 && Body.IsBeingInterrupted && !spell.Uninterruptible)
                return false;

            if (Body.GetSkillDisabledDuration(spell) > 0)
                return false;

            return true;
        }

        protected virtual bool CheckOffensiveSpells(Spell spell)
        {
            if (spell.NeedInstrument && Body.ActiveWeaponSlot != eActiveWeaponSlot.Distance)
                Body.SwitchWeapon(eActiveWeaponSlot.Distance);

            bool casted = false;

            if (Body.TargetObject is GameLiving living && (spell.Duration == 0 || !LivingHasEffect(living, spell) || spell.SpellType == eSpellType.DirectDamageWithDebuff || spell.SpellType == eSpellType.DamageSpeedDecrease))
            {
                casted = Body.CastSpell(spell, m_mobSpellLine);
            }

            return casted;
        }

        protected virtual bool CheckInstantDefensiveSpells(Spell spell)
        {
            if (spell.HasRecastDelay && Body.GetSkillDisabledDuration(spell) > 0)
                return false;

            bool castSpell = false;

            switch (spell.SpellType)
            {
                case eSpellType.SavageCrushResistanceBuff:
                case eSpellType.SavageSlashResistanceBuff:
                case eSpellType.SavageThrustResistanceBuff:
                case eSpellType.SavageCombatSpeedBuff:
                case eSpellType.SavageDPSBuff:
                case eSpellType.SavageParryBuff:
                case eSpellType.SavageEvadeBuff:
                    if (!LivingHasEffect(Body, spell))
                        castSpell = true;
                    break;

                case eSpellType.CombatHeal:
                case eSpellType.DamageAdd:
                case eSpellType.PaladinArmorFactorBuff:
                case eSpellType.DexterityQuicknessBuff:
                case eSpellType.EnduranceRegenBuff:
                case eSpellType.CombatSpeedBuff:
                case eSpellType.AblativeArmor:
                case eSpellType.Bladeturn:
                case eSpellType.OffensiveProc:
                case eSpellType.SummonHunterPet:
                    if (spell.SpellType == eSpellType.CombatSpeedBuff)
                    {
                        if (Body.TargetObject != null && !Body.IsWithinRadius(Body.TargetObject, Body.MeleeAttackRange))
                            break;
                    }

                    if (!LivingHasEffect(Body, spell))
                        castSpell = true;
                    break;
            }

            if (castSpell)
                Body.CastSpell(spell, m_mobSpellLine);

            return castSpell;
        }

        protected virtual bool CheckInstantOffensiveSpells(Spell spell)
        {
            if (spell.HasRecastDelay && Body.GetSkillDisabledDuration(spell) > 0)
                return false;

            bool castSpell = false;

            switch (spell.SpellType)
            {
                case eSpellType.DirectDamage:
                case eSpellType.NightshadeNuke:
                case eSpellType.Lifedrain:
                case eSpellType.DexterityDebuff:
                case eSpellType.DexterityQuicknessDebuff:
                case eSpellType.StrengthDebuff:
                case eSpellType.StrengthConstitutionDebuff:
                case eSpellType.CombatSpeedDebuff:
                case eSpellType.DamageOverTime:
                case eSpellType.MeleeDamageDebuff:
                case eSpellType.AllStatsPercentDebuff:
                case eSpellType.CrushSlashThrustDebuff:
                case eSpellType.EffectivenessDebuff:
                case eSpellType.Disease:
                case eSpellType.Stun:
                case eSpellType.Mez:
                case eSpellType.Mesmerize:
                    if (spell.IsPBAoE && !Body.IsWithinRadius(Body.TargetObject, spell.Radius))
                        break;

                    if (!LivingHasEffect((GameLiving)Body.TargetObject, spell) && Body.IsWithinRadius(Body.TargetObject, spell.Range))
                        castSpell = true;
                    break;
            }

            ECSGameEffect pulseEffect = EffectListService.GetPulseEffectOnTarget(Body, spell);

            if (pulseEffect != null)
                return false;

            if (castSpell)
            {
                Body.CastSpell(spell, m_mobSpellLine);
                return true;
            }

            return false;
        }

        protected bool CheckDefensiveSpells(Spell spell)
        {
            if (!CanCastDefensiveSpell(spell))
                return false;

            bool casted = false;

            Body.TargetObject = null;

            if (spell.NeedInstrument)
                return false;

            switch (spell.SpellType)
            {
                #region Summon

                case eSpellType.SummonCommander:
                case eSpellType.SummonUnderhill:
                case eSpellType.SummonDruidPet:
                case eSpellType.SummonSimulacrum:
                case eSpellType.SummonSpiritFighter:
                    if (Body.ControlledBrain != null)
                        return false;
                    Body.TargetObject = Body;
                    break;

                case eSpellType.SummonMinion:
                    if (Body.ControlledBrain != null)
                    {
                        IControlledBrain[] icb = Body.ControlledBrain.Body.ControlledNpcList;
                        int numberofpets = 0;

                        for (int i = 0; i < icb.Length; i++)
                        {
                            if (icb[i] != null)
                                numberofpets++;
                        }

                        if (numberofpets >= icb.Length)
                            break;

                        Body.TargetObject = Body;
                    }
                    break;

                #endregion

                #region Buffs

                case eSpellType.SpeedEnhancement when spell.IsPulsing:
                    if (!LivingHasEffect(Body, spell))
                        Body.TargetObject = Body;
                    break;

                case eSpellType.BodySpiritEnergyBuff:
                case eSpellType.HeatColdMatterBuff:
                case eSpellType.SpiritResistBuff:
                case eSpellType.EnergyResistBuff:
                case eSpellType.HeatResistBuff:
                case eSpellType.ColdResistBuff:
                case eSpellType.BodyResistBuff:
                case eSpellType.MatterResistBuff:
                case eSpellType.AllMagicResistBuff:
                case eSpellType.EnduranceRegenBuff:
                case eSpellType.PowerRegenBuff:
                case eSpellType.AblativeArmor:
                case eSpellType.AcuityBuff:
                case eSpellType.AFHitsBuff:
                case eSpellType.ArmorAbsorptionBuff:
                case eSpellType.BaseArmorFactorBuff:
                case eSpellType.SpecArmorFactorBuff:
                case eSpellType.PaladinArmorFactorBuff:
                case eSpellType.Buff:
                case eSpellType.CelerityBuff:
                case eSpellType.ConstitutionBuff:
                case eSpellType.CourageBuff:
                case eSpellType.CrushSlashTrustBuff:
                case eSpellType.DexterityBuff:
                case eSpellType.DexterityQuicknessBuff:
                case eSpellType.EffectivenessBuff:
                case eSpellType.FatigueConsumptionBuff:
                case eSpellType.FlexibleSkillBuff:
                case eSpellType.HasteBuff:
                case eSpellType.HealthRegenBuff:
                case eSpellType.HeroismBuff:
                case eSpellType.KeepDamageBuff:
                case eSpellType.MagicResistBuff:
                case eSpellType.MeleeDamageBuff:
                case eSpellType.MLABSBuff:
                case eSpellType.ParryBuff:
                case eSpellType.PowerHealthEnduranceRegenBuff:
                case eSpellType.StrengthBuff:
                case eSpellType.StrengthConstitutionBuff:
                case eSpellType.SuperiorCourageBuff:
                case eSpellType.ToHitBuff:
                case eSpellType.WeaponSkillBuff:
                case eSpellType.DamageAdd:
                case eSpellType.OffensiveProc:
                case eSpellType.DefensiveProc:
                case eSpellType.DamageShield:
                case eSpellType.SpeedEnhancement when spell.Target == eSpellTarget.PET:
                case eSpellType.CombatSpeedBuff when spell.Duration > 20:
                case eSpellType.CombatSpeedBuff when spell.IsConcentration:
                case eSpellType.MesmerizeDurationBuff when !spell.IsPulsing:
                case eSpellType.Bladeturn when !spell.IsPulsing:
                {
                    if (spell.IsConcentration)
                    {
                        if (spell.Concentration > Body.Concentration)
                            break;

                        if (Body.effectListComponent.GetConcentrationEffects().Count >= 20)
                            break;
                    }

                    if (spell.Target == eSpellTarget.PET)
                    {
                        if (spell.SpellType == eSpellType.DamageShield)
                            return false;

                        if (Body.ControlledBrain?.Body != null)
                        {
                            if (!LivingHasEffect(Body.ControlledBrain.Body, spell))
                                Body.TargetObject = Body.ControlledBrain.Body;
                        }

                        break;
                    }

                    if (!LivingHasEffect(Body, spell))
                    {
                        Body.TargetObject = Body;
                        break;
                    }

                    if (Body.Group != null)
                    {
                        if (spell.Target == eSpellTarget.REALM || spell.Target == eSpellTarget.GROUP)
                        {
                            foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                            {
                                if (groupMember != Body)
                                {
                                    if (!LivingHasEffect(groupMember, spell) && Body.IsWithinRadius(groupMember, spell.Range) && groupMember.IsAlive)
                                    {
                                        Body.TargetObject = groupMember;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                break;

                #endregion

                #region Cures

                case eSpellType.CureDisease:
                    if (Body.IsDiseased)
                    {
                        Body.TargetObject = Body;
                        break;
                    }

                    if (Body.Group != null)
                    {
                        foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                        {
                            if (groupMember != Body && groupMember.IsDiseased && Body.IsWithinRadius(groupMember, spell.Range))
                            {
                                Body.TargetObject = groupMember;
                                break;
                            }
                        }
                    }
                    break;

                case eSpellType.CurePoison:
                    if (Body.IsPoisoned)
                    {
                        Body.TargetObject = Body;
                        break;
                    }

                    if (Body.Group != null)
                    {
                        foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                        {
                            if (groupMember != Body && groupMember.IsPoisoned && Body.IsWithinRadius(groupMember, spell.Range))
                            {
                                Body.TargetObject = groupMember;
                                break;
                            }
                        }
                    }
                    break;

                case eSpellType.CureMezz:
                    if (Body.Group != null)
                    {
                        foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                        {
                            if (groupMember != Body && groupMember.IsMezzed && Body.IsWithinRadius(groupMember, spell.Range))
                            {
                                Body.TargetObject = groupMember;
                                break;
                            }
                        }
                    }
                    break;

                #endregion
            }

            if (Body?.TargetObject != null)
            {
                casted = Body.CastSpell(spell, m_mobSpellLine);
            }

            return casted;
        }

        bool CheckDefensiveSpells(List<Spell> spells)
        {
            List<(Spell, GameLiving)> spellsToCast = new(spells.Count);

            foreach (Spell spell in spells)
            {
                if (CanCastDefensiveSpell(spell, out GameLiving target))
                    spellsToCast.Add((spell, target));
            }

            if (spellsToCast.Count == 0)
                return false;

            GameObject oldTarget = Body.TargetObject;
            (Spell spell, GameLiving target) spellToCast = spellsToCast[Util.Random(spellsToCast.Count - 1)];
            Body.TargetObject = spellToCast.target;
            bool cast = Body.CastSpell(spellToCast.spell, m_mobSpellLine);

            Body.TargetObject = oldTarget;
            return cast;

            bool CanCastDefensiveSpell(Spell spell, out GameLiving target)
            {
                target = null;

                if (spell.NeedInstrument || (!spell.Uninterruptible && Body.IsBeingInterrupted) ||
                    (spell.HasRecastDelay && Body.GetSkillDisabledDuration(spell) > 0))
                {
                    return false;
                }

                target = FindTargetForDefensiveSpell(spell);
                return target != null;
            }
        }

        protected virtual GameLiving FindTargetForDefensiveSpell(Spell spell)
        {
            GameLiving target = null;

            switch (spell.SpellType)
            {
                case eSpellType.SpeedEnhancement when spell.IsPulsing:
                    if (!LivingHasEffect(Body, spell))
                        target = Body;
                    break;

                case eSpellType.BodySpiritEnergyBuff:
                case eSpellType.HeatColdMatterBuff:
                case eSpellType.AblativeArmor:
                case eSpellType.AcuityBuff:
                case eSpellType.AFHitsBuff:
                case eSpellType.ArmorAbsorptionBuff:
                case eSpellType.BaseArmorFactorBuff:
                case eSpellType.SpecArmorFactorBuff:
                case eSpellType.PaladinArmorFactorBuff:
                case eSpellType.Buff:
                case eSpellType.ConstitutionBuff:
                case eSpellType.CourageBuff:
                case eSpellType.CrushSlashTrustBuff:
                case eSpellType.DexterityBuff:
                case eSpellType.DexterityQuicknessBuff:
                case eSpellType.HealthRegenBuff:
                case eSpellType.StrengthBuff:
                case eSpellType.StrengthConstitutionBuff:
                case eSpellType.SuperiorCourageBuff:
                case eSpellType.DamageAdd:
                case eSpellType.OffensiveProc:
                case eSpellType.DefensiveProc:
                case eSpellType.Bladeturn when !spell.IsPulsing:
                    if (!LivingHasEffect(Body, spell))
                    {
                        target = Body;
                        break;
                    }

                    if (Body.Group != null && (spell.Target == eSpellTarget.REALM || spell.Target == eSpellTarget.GROUP))
                    {
                        foreach (GameLiving groupMember in Body.Group.GetMembersInTheGroup())
                        {
                            if (groupMember != Body && !LivingHasEffect(groupMember, spell) && Body.IsWithinRadius(groupMember, spell.Range) && groupMember.IsAlive)
                            {
                                target = groupMember;
                                break;
                            }
                        }
                    }
                    break;
            }

            return target;
        }

        public bool LivingHasEffect(GameLiving target, Spell spell)
        {
            if (target == null)
                return true;

            eEffect spellEffect = EffectHelper.GetEffectFromSpell(spell);

            if (spellEffect is eEffect.DirectDamage or eEffect.Pet or eEffect.Unknown)
                return false;

            ISpellHandler spellHandler = Body.castingComponent.SpellHandler;

            if (spellHandler != null && spellHandler.Spell.ID == spell.ID && spellHandler.Target == target)
                return true;

            ISpellHandler queuedSpellHandler = Body.castingComponent.QueuedSpellHandler;

            if (queuedSpellHandler != null && queuedSpellHandler.Spell.ID == spell.ID && queuedSpellHandler.Target == target)
                return true;

            if (spell.SpellType is eSpellType.OffensiveProc or eSpellType.DefensiveProc)
            {
                List<ECSGameSpellEffect> existingEffects = target.effectListComponent.GetSpellEffects(spellEffect);

                foreach (ECSGameSpellEffect effect in existingEffects)
                {
                    if (effect.SpellHandler.Spell.ID == spell.ID || (spell.EffectGroup > 0 && effect.SpellHandler.Spell.EffectGroup == spell.EffectGroup))
                        return true;
                }

                return false;
            }

            ECSGameEffect pulseEffect = EffectListService.GetPulseEffectOnTarget(target, spell);

            if (pulseEffect != null)
                return true;

            return EffectListService.GetEffectOnTarget(target, spellEffect) != null || HasImmunityEffect(EffectHelper.GetImmunityEffectFromSpell(spell)) || HasImmunityEffect(EffectHelper.GetNpcImmunityEffectFromSpell(spell));

            bool HasImmunityEffect(eEffect immunityEffect)
            {
                return immunityEffect is not eEffect.Unknown && EffectListService.GetEffectOnTarget(target, immunityEffect) != null;
            }
        }

        #endregion

        #region Healing

        private long nextCureTime = 0;

        bool CheckHealSpell(Spell spell)
        {
            return spell != null
                && (!BotBody.IsBeingInterruptedByOther || spell.IsInstantCast)
                && (!spell.HasRecastDelay || BotBody.GetSkillDisabledDuration(spell) <= 0)
                && BotBody.Mana >= BotBody.PowerCost(spell);
        }

        public bool CheckHeals()
        {
            const byte ManaThreshold = 90;
            const long CureDelay = 5000;

            if (AlreadyCheckedHeals || !Body.CanCastHealSpells || Body.IsStunned || Body.IsMezzed || Body.IsSilenced)
                return false;

            AlreadyCheckedHeals = true;

            // Check if we can cast any instant heals
            bool canCastInstantHeal = CheckHealSpell(BotBody.HealInstant);
            bool canCastInstantGroupHeal = CheckHealSpell(BotBody.HealInstantGroup);

            if (BotBody.IsBeingInterruptedByOther && !canCastInstantHeal && !canCastInstantGroupHeal)
                return false;

            bool isCastingHeal = BotBody.IsCasting && BotBody.castingComponent.SpellHandler.Spell.IsHealing;

            if (isCastingHeal && !canCastInstantHeal && !canCastInstantGroupHeal)
                return true;

            // Working variables
            int amountToHeal = 0;
            int numEmergency = 0;
            int numNeedHealing = 0;
            Spell spellToCast = null;
            GameLiving spellTarget = null;
            bool startedCasting = false;

            // Check group health
            if (Body.Group != null)
            {
                foreach (GameLiving member in Body.Group.GetMembersInTheGroup())
                {
                    if (!member.IsAlive)
                        continue;

                    int deficit = member.MaxHealth - member.Health;

                    if (deficit > 0)
                    {
                        amountToHeal += deficit;

                        if (member.HealthPercent < 65)
                        {
                            numNeedHealing++;

                            if (member.HealthPercent < 40)
                                numEmergency++;

                            if (spellTarget == null || member.HealthPercent < spellTarget.HealthPercent)
                                spellTarget = member;
                        }
                        else if (IsHealer && member.HealthPercent < 80)
                        {
                            numNeedHealing++;

                            if (spellTarget == null || member.HealthPercent < spellTarget.HealthPercent)
                                spellTarget = member;
                        }
                    }
                }
            }
            else
            {
                amountToHeal = BotBody.MaxHealth - BotBody.Health;

                if (amountToHeal > 0)
                {
                    spellTarget = BotBody;

                    if (BotBody.HealthPercent < 65)
                    {
                        numNeedHealing = 1;

                        if (BotBody.HealthPercent < 40)
                            numEmergency = 1;
                    }
                }
            }

            // Emergency heal
            if (numEmergency > 0)
            {
                if (canCastInstantHeal)
                    spellToCast = BotBody.HealInstant;
                else if (canCastInstantGroupHeal)
                    spellToCast = BotBody.HealInstantGroup;
                else if (!isCastingHeal)
                {
                    if (CheckHealSpell(BotBody.HealBig))
                        spellToCast = BotBody.HealBig;
                    else if (CheckHealSpell(BotBody.HealEfficient))
                        spellToCast = BotBody.HealEfficient;
                    else if (CheckHealSpell(BotBody.HealGroup))
                        spellToCast = BotBody.HealGroup;
                }
            }

            // Cure Mezz/Disease/Poison
            if (spellToCast == null)
            {
                if (Body.Group != null)
                {
                    foreach (GameLiving member in Body.Group.GetMembersInTheGroup())
                    {
                        if (member.IsMezzed && member != Body && CheckHealSpell(BotBody.CureMezz))
                        {
                            spellToCast = BotBody.CureMezz;
                            spellTarget = member;
                            break;
                        }
                    }

                    if (spellToCast == null && nextCureTime < GameLoop.GameLoopTime)
                    {
                        foreach (GameLiving member in Body.Group.GetMembersInTheGroup())
                        {
                            if (member.IsDiseased)
                            {
                                if (CheckHealSpell(BotBody.CureDisease))
                                {
                                    spellToCast = BotBody.CureDisease;
                                    spellTarget = member;
                                    break;
                                }
                                else if (CheckHealSpell(BotBody.CureDiseaseGroup))
                                {
                                    spellToCast = BotBody.CureDiseaseGroup;
                                    spellTarget = member;
                                    break;
                                }
                            }

                            if (member.IsPoisoned)
                            {
                                if (CheckHealSpell(BotBody.CurePoison))
                                {
                                    spellToCast = BotBody.CurePoison;
                                    spellTarget = member;
                                    break;
                                }
                                else if (CheckHealSpell(BotBody.CurePoisonGroup))
                                {
                                    spellToCast = BotBody.CurePoisonGroup;
                                    spellTarget = member;
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (BotBody.IsDiseased && nextCureTime < GameLoop.GameLoopTime && CheckHealSpell(BotBody.CureDisease))
                    {
                        spellToCast = BotBody.CureDisease;
                        spellTarget = BotBody;
                    }
                    else if (BotBody.IsPoisoned && nextCureTime < GameLoop.GameLoopTime && CheckHealSpell(BotBody.CurePoison))
                    {
                        spellToCast = BotBody.CurePoison;
                        spellTarget = BotBody;
                    }
                }
            }

            // Non-emergency heal
            if (spellToCast == null && numNeedHealing > 0)
            {
                if (!BotBody.IsCasting || (numEmergency > 0 && !isCastingHeal))
                {
                    if (CheckHealSpell(BotBody.HealOverTime) && !LivingHasEffect(spellTarget, BotBody.HealOverTime))
                        spellToCast = BotBody.HealOverTime;
                    else if (BotBody.ManaPercent >= ManaThreshold && CheckHealSpell(BotBody.HealBig)
                        && (spellTarget.MaxHealth - spellTarget.Health) >= GameBot.HealAmount(BotBody.HealBig, spellTarget))
                        spellToCast = BotBody.HealBig;
                    else if (CheckHealSpell(BotBody.HealEfficient))
                        spellToCast = BotBody.HealEfficient;
                    else if (CheckHealSpell(BotBody.HealGroup))
                        spellToCast = BotBody.HealGroup;
                }
            }

            // Cast the selected spell
            if (spellToCast != null && spellTarget != null)
            {
                if (!BotBody.IsWithinRadius(spellTarget, BotBody.castingComponent.CalculateSpellRange(spellToCast)))
                {
                    BotBody.WalkTo(new Point3D(spellTarget.X, spellTarget.Y, spellTarget.Z), BotBody.MaxSpeed);
                    return true;
                }

                if (!spellToCast.IsInstantCast)
                {
                    if (BotBody.IsCasting)
                        BotBody.StopCurrentSpellcast();
                    else if (BotBody.IsAttacking)
                        BotBody.StopAttack();
                }

                GameObject oldTarget = BotBody.TargetObject;
                BotBody.TargetObject = spellTarget;
                startedCasting = BotBody.CastSpell(spellToCast, m_mobSpellLine, false);

                if (!startedCasting)
                    BotBody.TargetObject = oldTarget;
                else
                {
                    if (spellToCast.IsInstantCast)
                    {
                        BotBody.TargetObject = oldTarget;
                        startedCasting = false;
                    }
                    else if (spellToCast.SpellType == eSpellType.CureDisease || spellToCast.SpellType == eSpellType.CurePoison)
                        nextCureTime = GameLoop.GameLoopTime + CureDelay;
                }
            }

            return startedCasting || isCastingHeal;
        }

        #endregion
    }
}
