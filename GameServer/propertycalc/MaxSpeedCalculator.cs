using System;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Effects;
using DOL.GS.RealmAbilities;

namespace DOL.GS.PropertyCalc
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
        public const double SPEED1 = 1.44;
        public const double SPEED2 = 1.59;
        public const double SPEED3 = 1.74;
        public const double SPEED4 = 1.89;
        public const double SPEED5 = 2.04;
        private const double SPRINT = 1.3;

        public override int CalcValue(GameLiving living, eProperty property)
        {
            if (living.IsCrowdControlled && living.effectListComponent.GetEffects().FirstOrDefault(x => x.GetType() == typeof(SpeedOfSoundECSEffect)) == null)
                return 0;

            double speedIncrease = living.BuffBonusMultCategory1.Get((int)property);
            double maxSpeedBase = living.MaxSpeedBase;

            if (living is IGamePlayer igp)
            {
                double horseSpeed = igp.IsOnHorse && igp.ActiveHorse != null ? igp.ActiveHorse.Speed * 0.01 : 1.0;

                if (speedIncrease > horseSpeed)
                    horseSpeed = 1.0;

                if (ServerProperties.Properties.ENABLE_PVE_SPEED)
                {
                    if (speedIncrease == 1 && !igp.InCombat && !igp.IsStealthed && !living.CurrentRegion.IsRvR && !living.CurrentZone.IsRvR)
                        speedIncrease *= 1.25;
                }

                // Encumbrance and priv level checks only apply to real players
                if (living is GamePlayer gp)
                {
                    if (gp.IsEncumbered && gp.Client.Account.PrivLevel == 1 && ServerProperties.Properties.ENABLE_ENCUMBERANCE_SPEED_LOSS)
                    {
                        speedIncrease *= gp.MaxSpeedModifierFromEncumbrance;

                        if (speedIncrease <= 0)
                            speedIncrease = 0;
                    }

                    if (gp.IsStealthed && gp.Client.Account.PrivLevel == 1)
                    {
                        AtlasOF_MasteryOfStealth mos = gp.GetAbility<AtlasOF_MasteryOfStealth>();
                        double stealthSpec = gp.GetModifiedSpecLevel(Specs.Stealth);

                        if (stealthSpec > gp.Level)
                            stealthSpec = gp.Level;

                        speedIncrease *= 0.3 + (stealthSpec + 10) * 0.3 / (gp.Level + 10);

                        if (mos != null)
                            speedIncrease *= 1 + mos.GetAmountForLevel(mos.Level) / 100.0;

                        if (gp.effectListComponent.ContainsEffectForEffectType(eEffect.ShadowRun))
                            speedIncrease *= 2;
                    }

                    if (GameRelic.IsPlayerCarryingRelic(gp))
                    {
                        if (speedIncrease > 1.0)
                            speedIncrease = 1.0;

                        horseSpeed = 1.0;
                    }
                }
                else if (igp.IsStealthed)
                {
                    // Bot stealth speed (simplified — no priv level check)
                    double stealthSpec = living.GetModifiedSpecLevel(Specs.Stealth);

                    if (stealthSpec > igp.Level)
                        stealthSpec = igp.Level;

                    speedIncrease *= 0.3 + (stealthSpec + 10) * 0.3 / (igp.Level + 10);

                    if (living.effectListComponent.ContainsEffectForEffectType(eEffect.ShadowRun))
                        speedIncrease *= 2;
                }

                if (igp.IsSprinting)
                    speedIncrease *= SPRINT;

                speedIncrease *= horseSpeed;
            }
            else if (living is GameNPC npc)
            {
                // Special handling for pets.
                if (npc.Brain is IControlledBrain brain)
                {
                    GameLiving owner = brain.Owner;

                    if (owner != null && owner == brain.Body.FollowTarget)
                    {
                        // Increase the pet's speed to match its owner's (using mount speed if applicable) when both are out of combat
                        if (!living.InCombat && !owner.InCombat)
                        {
                            GamePlayer playerOwner = brain.GetPlayerOwner();

                            if (playerOwner != null)
                            {
                                GameNPC steed = playerOwner.Steed;

                                if (steed != null)
                                    owner = steed;
                            }

                            // Only increase the pet's speed, never decrease it, so take the max of the two.
                            // Overwrite whatever speed bonuses the pet has at this point.
                            maxSpeedBase = Math.Max(maxSpeedBase, owner.MaxSpeedBase);
                            speedIncrease = Math.Max(1, (double) owner.MaxSpeed / owner.MaxSpeedBase);
                        }

                        // A pet following its owner moves at sprint speed (+30%),
                        // even in combat (This behavior has been confirmed on Uthgard for Necromancer and Cabalist pets),
                        // even if the catch up mechanic applies (to ensure it stays within the catch up radius).
                        speedIncrease *= SPRINT;
                    }
                }

                double healthPercent = living.Health / (double) living.MaxHealth;

                if (healthPercent < 0.33)
                    speedIncrease *= 0.2 + healthPercent * (0.8 / 0.33); // 33% HP = full speed, 0% HP = 20% speed
            }

            return (int) Math.Round(maxSpeedBase * speedIncrease);
        }
    }
}
