using Newtonsoft.Json;

namespace BijiSDK
{
    /// <summary>Enforces standard JSON syntax before Json.NET's permissive reader.</summary>
    internal sealed class StrictJsonSyntax
    {
        private readonly string text;
        private int position;
        private StrictJsonSyntax(string text) { this.text = text; }

        internal static void Validate(string text)
        {
            var parser = new StrictJsonSyntax(text);
            parser.Value(0);
            parser.Space();
            if (parser.position != text.Length) parser.Fail();
        }

        private void Value(int depth)
        {
            if (depth > 64) Fail();
            Space();
            if (position >= text.Length) Fail();
            switch (text[position])
            {
                case '{':
                    position++;
                    Space();
                    if (Take('}')) return;
                    do
                    {
                        Space();
                        String();
                        Space();
                        Require(':');
                        Value(depth + 1);
                        Space();
                        if (Take('}')) return;
                        Require(',');
                    } while (true);
                case '[':
                    position++;
                    Space();
                    if (Take(']')) return;
                    do
                    {
                        Value(depth + 1);
                        Space();
                        if (Take(']')) return;
                        Require(',');
                    } while (true);
                case '"': String(); return;
                case 't': Literal("true"); return;
                case 'f': Literal("false"); return;
                case 'n': Literal("null"); return;
                default: Number(); return;
            }
        }

        private void String()
        {
            Require('"');
            while (position < text.Length)
            {
                char current = text[position++];
                if (current == '"') return;
                if (current < 0x20) Fail();
                if (current != '\\') continue;
                if (position == text.Length) Fail();
                char escaped = text[position++];
                if (escaped == 'u')
                {
                    for (int index = 0; index < 4; index++)
                    {
                        if (position == text.Length || !Hex(text[position++])) Fail();
                    }
                }
                else if (escaped != '"' && escaped != '\\' && escaped != '/' &&
                         escaped != 'b' && escaped != 'f' && escaped != 'n' &&
                         escaped != 'r' && escaped != 't')
                    Fail();
            }
            Fail();
        }

        private void Number()
        {
            Take('-');
            if (!Take('0'))
            {
                if (position == text.Length || text[position] < '1' || text[position] > '9') Fail();
                Digits();
            }
            if (Take('.'))
            {
                RequireDigit();
                Digits();
            }
            if (Take('e') || Take('E'))
            {
                if (!Take('+')) Take('-');
                RequireDigit();
                Digits();
            }
        }

        private void RequireDigit()
        {
            if (position == text.Length || text[position] < '0' || text[position] > '9') Fail();
        }

        private void Digits()
        {
            while (position < text.Length && text[position] >= '0' && text[position] <= '9')
                position++;
        }

        private void Literal(string expected)
        {
            foreach (char current in expected) Require(current);
        }

        private void Space()
        {
            while (position < text.Length)
            {
                char current = text[position];
                if (current != ' ' && current != '\t' && current != '\r' && current != '\n') return;
                position++;
            }
        }

        private bool Take(char expected)
        {
            if (position >= text.Length || text[position] != expected) return false;
            position++;
            return true;
        }

        private void Require(char expected)
        {
            if (!Take(expected)) Fail();
        }

        private static bool Hex(char value)
        {
            return (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') ||
                   (value >= 'A' && value <= 'F');
        }

        private void Fail()
        {
            throw new JsonReaderException("Nonstandard or incomplete JSON at character " + position + ".");
        }
    }
}
