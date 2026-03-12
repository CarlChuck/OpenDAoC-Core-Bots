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

using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Effects;
using DOL.GS.PropertyCalc;
using DOL.GS.RealmAbilities;
using DOL.GS.SkillHandler;
using DOL.GS.Spells;

namespace DOL.GS.Scripts
{
    /// <summary>
    /// The Max Speed calculator
    /// BuffBonusCategory1 unused
    /// BuffBonusCategory2 unused
    /// BuffBonusCategory3 unused
    /// BuffBonusCategory4 unused
    /// BuffBonusMultCategory1 used for all multiplicative speed bonuses
    /// </summary>
    [PropertyCalculator(eProperty.MaxSpeed)]
    public class MaxSpeedCalculator : PropertyCalculator
    {
        public static readonly double SPEED1 = 1.44;
        public static readonly double SPEED2 = 1.59;
        public static readonly double SPEED3 = 1.74;
        public static readonly double SPEED4 = 1.89;
        public static readonly double SPEED5 = 2.04;

        public override int CalcValue(GameLiving living, eProperty property)
        {
            if ((living.IsMezzed || living.IsStunned) && living.effectListComponent.GetAllEffects().FirstOrDefault(x => x.GetType() == typeof(SpeedOfSoundECSEffect)) == null)
                return 0;

            double speed = living.BuffBonusMultCategory1.Get((int)property);

            if (living is GamePlayer or IGamePlayer)
            {
                // Since Dark Age of Camelot's launch, we have heard continuous feedback from our community about the movement speed in our game. The concerns over how slow
                // our movement is has continued to grow as we have added more and more areas in which to travel. Because we believe these concerns are valid, we have decided
                // to make a long requested change to the game, enhancing the movement speed of all players who are out of combat. This new run state allows the player to move
                // faster than normal run speed, provided that the player is not in any form of combat. Along with this change, we have slightly increased the speed of all
                // secondary speed buffs (see below for details). Both of these changes are noticeable but will not impinge upon the supremacy of the primary speed buffs available
                // to the Bard, Skald and Minstrel.
                // - The new run speed does not work if the player is in any form of combat. All combat timers must also be expired.
                // - The new run speed will not stack with any other run speed spell or ability, except for Sprint.
                // - Pets that are not in combat have also received the new run speed, only when they are following, to allow them to keep up with their owners.

                GamePlayer gp = living as GamePlayer;
                IGamePlayer igp = living as IGamePlayer;

                bool isOnHorse = gp?.IsOnHorse ?? igp?.IsOnHorse ?? false;
                var activeHorse = gp?.ActiveHorse ?? igp?.ActiveHorse;
                double horseSpeed = isOnHorse && activeHorse != null ? activeHorse.Speed * 0.01 : 1.0;

                if (speed > horseSpeed)
                    horseSpeed = 1.0;

                if (ServerProperties.Properties.ENABLE_PVE_SPEED)
                {
                    bool isStealthed = gp?.IsStealthed ?? igp?.IsStealthed ?? false;
                    // OF zones technically aren't in a RvR region.
                    if (speed == 1 && !living.InCombat && !isStealthed && !living.CurrentRegion.IsRvR && !living.CurrentZone.IsRvR)
                        speed *= 1.25; // New run speed is 125% when no buff.
                }

                bool isOverencumbered = gp?.IsOverencumbered ?? igp?.IsOverencumbered ?? false;
                var client = gp?.Client ?? igp?.Client;
                if (isOverencumbered && client?.Account.PrivLevel == 1 && ServerProperties.Properties.ENABLE_ENCUMBERANCE_SPEED_LOSS)
                {
                    int enc = gp?.Encumberance ?? igp?.Encumberance ?? 0;
                    int maxEnc = gp?.MaxEncumberance ?? igp?.MaxEncumberance ?? 0;

                    if (enc > maxEnc)
                    {
                        speed *= (((living.MaxSpeedBase * 1.0 / GamePlayer.PLAYER_BASE_SPEED) * (-enc)) / (maxEnc * 0.35f)) + (living.MaxSpeedBase / GamePlayer.PLAYER_BASE_SPEED) + ((living.MaxSpeedBase / GamePlayer.PLAYER_BASE_SPEED) * maxEnc / (maxEnc * 0.35));

                        if (speed <= 0)
                            speed = 0;
                    }
                    else
                    {
                        if (gp != null) gp.IsOverencumbered = false;
                        else if (igp != null) igp.IsOverencumbered = false;
                    }
                }

                bool isStealthedForSpeed = gp?.IsStealthed ?? igp?.IsStealthed ?? false;
                if (isStealthedForSpeed && client?.Account.PrivLevel == 1)
                {
                    AtlasOF_MasteryOfStealth mos = living.GetAbility<AtlasOF_MasteryOfStealth>();
                    //GameSpellEffect bloodrage = SpellHandler.FindEffectOnTarget(living, "BloodRage");
                    //VanishEffect vanish = living.EffectList.GetOfType<VanishEffect>();
                    double stealthSpec = living.GetModifiedSpecLevel(Specs.Stealth);

                    if (stealthSpec > living.Level)
                        stealthSpec = living.Level;

                    speed *= 0.3 + (stealthSpec + 10) * 0.3 / (living.Level + 10);

                    //if (vanish != null)
                    //    speed *= vanish.SpeedBonus;

                    if (mos != null)
                        speed *= 1 + mos.GetAmountForLevel(mos.Level) / 100.0;

                    //if (bloodrage != null)
                    //    speed *= 1 + (bloodrage.Spell.Value * 0.01); // 25 * 0.01 = 0.25 (a.k 25%) value should be 25.5

                    if (living.effectListComponent.ContainsEffectForEffectType(eEffect.ShadowRun))
                        speed *= 2;
                }

                if (living is GamePlayer gamePlayer && GameRelic.IsPlayerCarryingRelic(gamePlayer))
                {
                    if (speed > 1.0)
                        speed = 1.0;

                    horseSpeed = 1.0;
                }

                bool isSprinting = gp?.IsSprinting ?? igp?.IsSprinting ?? false;
                if (isSprinting)
                    speed *= 1.3;

                speed *= horseSpeed;
            }
            else if (living is GameNPC npc)
            {
                IControlledBrain brain = npc.Brain as IControlledBrain;

                if (!living.InCombat)
                {
                    if (brain?.Body != null)
                    {
                        GameLiving owner = brain.Owner;
                        if (owner != null && owner == brain.Body.FollowTarget)
                        {
                            if (owner is GameNPC && owner is not MimicNPC)
                                owner = brain.GetLivingOwner();

                            int distance = brain.Body.GetDistanceTo(owner);

                            if (distance > 20)
                                speed *= 1.25;

                            if (living is NecromancerPet && distance > 700)
                                speed *= 1.25;

                            double ownerSpeedAdjust = (double)owner.MaxSpeed / owner.MaxSpeedBase;

                            if (ownerSpeedAdjust > 1.0)
                                speed *= ownerSpeedAdjust;

                            if (owner is GamePlayer or IGamePlayer)
                            {
                                bool ownerIsOnHorse = (owner as GamePlayer)?.IsOnHorse ?? (owner as IGamePlayer)?.IsOnHorse ?? false;
                                if (ownerIsOnHorse)
                                    speed *= 3.0;

                                bool ownerIsSprinting = (owner as GamePlayer)?.IsSprinting ?? (owner as IGamePlayer)?.IsSprinting ?? false;
                                if (ownerIsSprinting)
                                    speed *= 1.4;
                            }
                        }
                    }
                }
                else
                {
                    GameLiving owner = brain?.Owner;

                    if (owner != null && owner == brain.Body.FollowTarget)
                    {
                        if (owner is GameNPC && owner is not MimicNPC)
                            owner = brain.GetPlayerOwner();
                        else if (owner is MimicNPC)
                            owner = brain.GetLivingOwner();

                        if (owner is GamePlayer or IGamePlayer && ((owner as GamePlayer)?.IsSprinting == true || (owner as IGamePlayer)?.IsSprinting == true))
                            speed *= 1.3;
                    }
                }

                double healthPercent = living.Health / (double) living.MaxHealth;

                if (healthPercent < 0.33)
                    speed *= 0.2 + healthPercent * (0.8 / 0.33); // 33% HP = full speed, 0% HP = 20% speed
            }

            speed = living.MaxSpeedBase * speed + 0.5; // 0.5 is to fix the rounding error when converting to int so root results in speed 2 ((191 * 0.01 = 1.91) + 0.5 = 2.41).

            if (speed < 0) // Fix for the rounding fix above. (???)
                return 0;

            return (int)speed;
        }
    }
}
