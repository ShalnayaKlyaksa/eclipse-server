using System.Globalization;

namespace Content.Shared.PDA;

/// <summary>Decimal arithmetic with precedence, parentheses and contextual percentages.</summary>
public static class PdaCalculator
{
    public static bool TryEvaluate(string expression, out decimal result)
    {
        result = 0;
        if (expression.Length is 0 or > 256) return false;
        try
        {
            var parser = new Parser(expression.Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace(',', '.'));
            result = parser.Sum(); parser.Skip();
            return parser.Position == expression.Length;
        }
        // The content sandbox does not expose OverflowException. Keep the catch
        // around parsing/arithmetic only so malformed input never escapes into UI.
        catch (Exception) { return false; }
    }
    private sealed class Parser(string text)
    {
        public int Position;
        public void Skip() { while (Position < text.Length && char.IsWhiteSpace(text[Position])) Position++; }
        private bool Take(char value) { Skip(); if (Position >= text.Length || text[Position] != value) return false; Position++; return true; }
        public decimal Sum()
        {
            var value = Product(1);
            while (true)
            {
                if (Take('+')) value += Product(value);
                else if (Take('-')) value -= Product(value);
                else return value;
            }
        }
        private decimal Product(decimal percentBase)
        {
            var value = Factor(percentBase);
            while (true)
            {
                if (Take('*')) value *= Factor(1);
                else if (Take('/')) value /= Factor(1);
                else return value;
            }
        }
        private decimal Factor(decimal percentBase)
        {
            if (Take('+')) return Factor(percentBase);
            if (Take('-')) return -Factor(percentBase);
            decimal value;
            if (Take('(')) { value = Sum(); if (!Take(')')) throw new InvalidOperationException(); }
            else
            {
                Skip(); var start = Position;
                while (Position < text.Length && (char.IsAsciiDigit(text[Position]) || text[Position] == '.')) Position++;
                if (!decimal.TryParse(text.AsSpan(start, Position - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) throw new InvalidOperationException();
            }
            if (Take('%')) value = value / 100 * percentBase;
            return value;
        }
    }
}
