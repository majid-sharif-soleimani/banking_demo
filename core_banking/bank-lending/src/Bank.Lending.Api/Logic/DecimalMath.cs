namespace Bank.Lending.Api.Logic;

internal static class DecimalMath
{
    /// <summary>x^n by repeated squaring in decimal, so the annuity factor never passes through double.</summary>
    public static decimal Pow(decimal x, int n)
    {
        var result = 1m;
        while (n > 0)
        {
            if ((n & 1) == 1)
            {
                result *= x;
            }

            x *= x;
            n >>= 1;
        }

        return result;
    }
}
