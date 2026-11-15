using System;
using System.Text.RegularExpressions;

namespace Catch2OpenFolder
{
    // Catch2's __LINE__ can identify the last line of a macro invocation.
    // Preserve the reported location unless it belongs to a recognized test macro.
    internal static class TestDeclarationLocator
    {
        private static readonly Regex TestMacro = new Regex(
            @"\b(?:CATCH_)?(?:(?:TEMPLATE_(?:PRODUCT_|LIST_)?)?TEST_CASE(?:_METHOD)?(?:_SIG)?|SCENARIO(?:_METHOD)?|REGISTER_TEST_CASE|METHOD_AS_TEST_CASE)\s*\(",
            RegexOptions.CultureInvariant);

        internal static int FindAnchor(string source, int reportedLine)
        {
            if (string.IsNullOrEmpty(source) || reportedLine < 1) return -1;
            var start = 0;
            for (var line = 1; line < reportedLine; line++)
            {
                while (start < source.Length && source[start] != '\r' && source[start] != '\n') start++;
                if (start == source.Length) return -1;
                if (source[start++] == '\r' && start < source.Length && source[start] == '\n') start++;
            }
            var end = start;
            while (end < source.Length && source[end] != '\r' && source[end] != '\n') end++;
            var fallback = start;
            while (fallback < end && char.IsWhiteSpace(source[fallback])) fallback++;
            if (fallback == end) return -1;

            var code = MaskNonCode(source);
            foreach (Match match in TestMacro.Matches(code))
            {
                if (match.Index >= end) break;
                var depth = 0;
                for (var cursor = match.Index + match.Length - 1; cursor < code.Length; cursor++)
                {
                    if (code[cursor] == '(') depth++;
                    else if (code[cursor] == ')' && --depth == 0)
                    {
                        if (cursor >= start) return match.Index;
                        break;
                    }
                }
            }
            return fallback;
        }

        private static string MaskNonCode(string source)
        {
            var code = source.ToCharArray();
            for (var i = 0; i < source.Length;)
            {
                var start = i;
                var c = source[i];
                // C++ digit separators are not character literals (1'000, 0xFF'AB).
                if (char.IsDigit(c) && (i == 0 ||
                    (!char.IsLetterOrDigit(source[i - 1]) && source[i - 1] != '_')))
                {
                    while (++i < source.Length && (char.IsLetterOrDigit(source[i]) ||
                        source[i] == '\'' || source[i] == '_' || source[i] == '.')) { }
                    continue;
                }
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    i += 2;
                    while (i < source.Length)
                    {
                        if (source[i] == '\r' || source[i] == '\n')
                        {
                            if (source[i - 1] != '\\') break;
                            if (source[i++] == '\r' && i < source.Length && source[i] == '\n') i++;
                        }
                        else i++;
                    }
                }
                else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    var close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? source.Length : close + 2;
                }
                else if (c == 'R' && i + 1 < source.Length && source[i + 1] == '"' &&
                    TrySkipRawString(source, i, out var rawEnd))
                {
                    i = rawEnd;
                }
                else if (c == '"' || c == '\'')
                {
                    i++;
                    while (i < source.Length)
                    {
                        if (source[i] == '\\') i = Math.Min(i + 2, source.Length);
                        else if (source[i++] == c) break;
                    }
                }
                else { i++; continue; }
                for (var mask = start; mask < i; mask++)
                {
                    if (code[mask] != '\r' && code[mask] != '\n') code[mask] = ' ';
                }
            }
            return new string(code);
        }

        private static bool TrySkipRawString(string source, int start, out int end)
        {
            end = start;
            var open = source.IndexOf('(', start + 2);
            if (open < 0 || open - start - 2 > 16) return false;
            var delimiter = source.Substring(start + 2, open - start - 2);
            foreach (var c in delimiter)
            {
                if (char.IsWhiteSpace(c) || c == ')' || c == '\\') return false;
            }
            var terminator = ")" + delimiter + "\"";
            var close = source.IndexOf(terminator, open + 1, StringComparison.Ordinal);
            end = close < 0 ? source.Length : close + terminator.Length;
            return true;
        }
    }
}
