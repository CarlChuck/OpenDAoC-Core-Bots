namespace DOL.GS.PropertyCalc
{
    /// <summary>
    /// The Concentration point calculator
    /// 
    /// BuffBonusCategory1 unused
    /// BuffBonusCategory2 unused
    /// BuffBonusCategory3 unused
    /// BuffBonusCategory4 unused
    /// BuffBonusMultCategory1 unused
    /// </summary>
    [PropertyCalculator(eProperty.MaxConcentration)]
    public class MaxConcentrationCalculator : PropertyCalculator
    {
        public MaxConcentrationCalculator() { }

        public override int CalcValue(GameLiving living, eProperty property) 
        {
            if (living is IGamePlayer igp)
            {
                if (igp.CharacterClass.ManaStat is eStat.UNDEFINED)
                    return 1000000;

                int concBase = (int) (igp.Level * 4 * 2.2);
                int stat = living.GetModified((eProperty) igp.CharacterClass.ManaStat);
                var statConc = (stat - 50) * 2.8;
                int conc = (concBase + (int) statConc) / 2;
                conc = (int) (igp.Effectiveness * conc);

                if (conc < 0)
                    conc = 0;

                if (igp.GetSpellLine("Perfecter") != null && igp.MLLevel >= 4)
                    conc += 20 * conc / 100;

                return conc;
            }
            else
                return 1000000;
        }
    }
}
