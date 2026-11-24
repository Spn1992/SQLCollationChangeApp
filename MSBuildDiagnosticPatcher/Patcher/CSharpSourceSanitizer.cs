using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MSBuildDiagnosticPatcher.Patcher
{
    internal static class CSharpSourceSanitizer
    {
        private static readonly Dictionary<string, string> BuiltInTypeAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Boolean"] = "bool",
            ["Byte"] = "byte",
            ["SByte"] = "sbyte",
            ["Char"] = "char",
            ["Decimal"] = "decimal",
            ["Double"] = "double",
            ["Single"] = "float",
            ["Int32"] = "int",
            ["UInt32"] = "uint",
            ["Int64"] = "long",
            ["UInt64"] = "ulong",
            ["Int16"] = "short",
            ["UInt16"] = "ushort",
            ["Object"] = "object",
            ["String"] = "string",
            ["Void"] = "void"
        };

        /// <summary>
        /// Replaces C# comments, string literals, and character literals with spaces while
        /// preserving exact string length and newline characters (\r and \n).
        /// </summary>
        public static string SanitizePreserveLength(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            char[] buf = source.ToCharArray();
            int n = buf.Length;
            int i = 0;

            while (i < n)
            {
                // 1. Line comment: // ...
                if (buf[i] == '/' && i + 1 < n && buf[i + 1] == '/')
                {
                    while (i < n && buf[i] != '\r' && buf[i] != '\n')
                    {
                        buf[i++] = ' ';
                    }
                    continue;
                }

                // 2. Block comment: /* ... */
                if (buf[i] == '/' && i + 1 < n && buf[i + 1] == '*')
                {
                    buf[i++] = ' ';
                    buf[i++] = ' ';
                    while (i < n)
                    {
                        if (buf[i] == '*' && i + 1 < n && buf[i + 1] == '/')
                        {
                            buf[i++] = ' ';
                            buf[i++] = ' ';
                            break;
                        }
                        if (buf[i] != '\r' && buf[i] != '\n')
                        {
                            buf[i] = ' ';
                        }
                        i++;
                    }
                    continue;
                }

                // 3. String literals: "...", @"...", $"...", $@"...", @$ "...", raw """..."""
                if (TryGetStringLiteralPrefix(buf, i, out int prefixLen, out bool isVerbatim, out int quoteCount))
                {
                    for (int p = 0; p < prefixLen; p++)
                    {
                        buf[i++] = ' ';
                    }

                    if (quoteCount >= 3)
                    {
                        // Raw string literal: closes on quoteCount consecutive '"'
                        while (i < n)
                        {
                            int run = 0;
                            while (i + run < n && buf[i + run] == '"')
                            {
                                run++;
                            }
                            if (run >= quoteCount)
                            {
                                for (int q = 0; q < run; q++)
                                {
                                    buf[i++] = ' ';
                                }
                                break;
                            }
                            if (buf[i] != '\r' && buf[i] != '\n')
                            {
                                buf[i] = ' ';
                            }
                            i++;
                        }
                    }
                    else if (isVerbatim)
                    {
                        // Verbatim string literal: "" is escaped quote
                        while (i < n)
                        {
                            if (buf[i] == '"')
                            {
                                if (i + 1 < n && buf[i + 1] == '"')
                                {
                                    buf[i++] = ' ';
                                    buf[i++] = ' ';
                                    continue;
                                }
                                buf[i++] = ' ';
                                break;
                            }
                            if (buf[i] != '\r' && buf[i] != '\n')
                            {
                                buf[i] = ' ';
                            }
                            i++;
                        }
                    }
                    else
                    {
                        // Regular / single-line interpolated string literal: \" and \\ escaped
                        while (i < n && buf[i] != '\r' && buf[i] != '\n')
                        {
                            if (buf[i] == '\\' && i + 1 < n && buf[i + 1] != '\r' && buf[i + 1] != '\n')
                            {
                                buf[i++] = ' ';
                                buf[i++] = ' ';
                                continue;
                            }
                            if (buf[i] == '"')
                            {
                                buf[i++] = ' ';
                                break;
                            }
                            buf[i++] = ' ';
                        }
                    }
                    continue;
                }

                // 4. Character literal: 'x' or '\x'
                if (buf[i] == '\'')
                {
                    buf[i++] = ' ';
                    while (i < n && buf[i] != '\r' && buf[i] != '\n')
                    {
                        if (buf[i] == '\\' && i + 1 < n && buf[i + 1] != '\r' && buf[i + 1] != '\n')
                        {
                            buf[i++] = ' ';
                            buf[i++] = ' ';
                            continue;
                        }
                        if (buf[i] == '\'')
                        {
                            buf[i++] = ' ';
                            break;
                        }
                        buf[i++] = ' ';
                    }
                    continue;
                }

                i++;
            }

            return new string(buf);
        }

        private static bool TryGetStringLiteralPrefix(
            char[] buf,
            int index,
            out int prefixLen,
            out bool isVerbatim,
            out int quoteCount)
        {
            prefixLen = 0;
            isVerbatim = false;
            quoteCount = 0;

            int n = buf.Length;
            int j = index;
            bool sawAt = false;
            int dollarCount = 0;

            while (j < n && (buf[j] == '$' || buf[j] == '@'))
            {
                if (buf[j] == '@')
                {
                    if (sawAt) break;
                    sawAt = true;
                }
                else
                {
                    dollarCount++;
                }
                j++;
            }

            if (j >= n || buf[j] != '"')
            {
                return false;
            }

            int quotes = 0;
            while (j + quotes < n && buf[j + quotes] == '"')
            {
                quotes++;
            }

            // Note: "" at start of regular empty string "" (with no @ prefix) has quotes == 2,
            // which is an opening quote + immediate closing quote (quoteCount = 1), whereas
            // raw string literals have >= 3 quotes.
            if (quotes >= 3 && !sawAt)
            {
                quoteCount = quotes;
                prefixLen = (j - index) + quotes;
                isVerbatim = false;
                return true;
            }

            quoteCount = 1;
            isVerbatim = sawAt;
            prefixLen = (j - index) + 1;
            return true;
        }

        public static int FindMatchingBrace(string sanitized, int openBraceIndex)
        {
            if (openBraceIndex < 0 || openBraceIndex >= sanitized.Length || sanitized[openBraceIndex] != '{')
            {
                return -1;
            }

            int depth = 1;
            for (int i = openBraceIndex + 1; i < sanitized.Length; i++)
            {
                if (sanitized[i] == '{') depth++;
                else if (sanitized[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public static int FindMatchingParen(string sanitized, int openParenIndex)
        {
            if (openParenIndex < 0 || openParenIndex >= sanitized.Length || sanitized[openParenIndex] != '(')
            {
                return -1;
            }

            int depth = 1;
            for (int i = openParenIndex + 1; i < sanitized.Length; i++)
            {
                if (sanitized[i] == '(') depth++;
                else if (sanitized[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public static List<(int Start, int EndInclusive)> SplitTopLevelMemberSpans(
            string sanitized,
            int start,
            int endExclusive)
        {
            var spans = new List<(int Start, int EndInclusive)>();
            int parenDepth = 0;
            int braceDepth = 0;
            int bracketDepth = 0;
            int memberStart = start;

            for (int i = start; i < endExclusive; i++)
            {
                char c = sanitized[i];
                if (c == '(') parenDepth++;
                else if (c == ')' && parenDepth > 0) parenDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']' && bracketDepth > 0) bracketDepth--;
                else if (c == '{') braceDepth++;
                else if (c == '}' && braceDepth > 0)
                {
                    braceDepth--;
                    if (braceDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                    {
                        int k = i + 1;
                        while (k < endExclusive && char.IsWhiteSpace(sanitized[k])) k++;
                        if (k < endExclusive && sanitized[k] == '=')
                        {
                            // Auto-property initializer: e.g. { get; set; } = new();
                            continue;
                        }
                        spans.Add((memberStart, i));
                        memberStart = i + 1;
                    }
                }
                else if (c == ';' && braceDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                {
                    spans.Add((memberStart, i));
                    memberStart = i + 1;
                }
            }

            return spans;
        }

        public static int StripLeadingAttributes(string sanitized, int start, int endExclusive)
        {
            int idx = start;
            while (idx < endExclusive)
            {
                while (idx < endExclusive && char.IsWhiteSpace(sanitized[idx])) idx++;
                if (idx >= endExclusive || sanitized[idx] != '[') break;

                int depth = 1;
                idx++;
                while (idx < endExclusive && depth > 0)
                {
                    if (sanitized[idx] == '[') depth++;
                    else if (sanitized[idx] == ']') depth--;
                    idx++;
                }
            }
            return idx;
        }

        public static List<string> ExtractNormalizedParameterTypes(string parameterList, bool hasParameterNames = true)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(parameterList))
            {
                return result;
            }

            string sanitized = SanitizePreserveLength(parameterList);
            var paramSpans = SplitAtTopLevelCommas(sanitized);

            foreach (var (start, length) in paramSpans)
            {
                string origSlice = parameterList.Substring(start, length);
                string sanSlice = sanitized.Substring(start, length);

                // Strip default value '= ...' at top level
                int eqIdx = FindTopLevelEquals(sanSlice);
                if (eqIdx >= 0)
                {
                    origSlice = origSlice.Substring(0, eqIdx);
                    sanSlice = sanSlice.Substring(0, eqIdx);
                }

                int attrEnd = StripLeadingAttributes(sanSlice, 0, sanSlice.Length);
                string clean = origSlice.Substring(attrEnd).Trim();
                if (string.IsNullOrWhiteSpace(clean))
                {
                    continue;
                }

                // Strip parameter modifiers
                clean = Regex.Replace(clean, @"^(?:(?:this|ref|out|in|params|scoped|readonly)\s+)+", "");

                string typePart = clean;
                if (hasParameterNames)
                {
                    var trailingNameMatch = Regex.Match(clean, @"(?<=[\s>\?\)\]])@?[A-Za-z_][A-Za-z0-9_]*\s*$");
                    if (trailingNameMatch.Success)
                    {
                        typePart = clean.Substring(0, trailingNameMatch.Index).Trim();
                    }
                }

                result.Add(NormalizeTypeName(typePart));
            }

            return result;
        }

        public static string NormalizeTypeName(string rawType)
        {
            if (string.IsNullOrWhiteSpace(rawType))
            {
                return string.Empty;
            }

            // Collapse whitespace and nullable annotations
            string t = Regex.Replace(rawType, @"\s+", "");
            t = t.Replace("?", "");
            t = t.Replace("global::", "");

            // Strip namespace prefixes: e.g. System.Threading.CancellationToken -> CancellationToken
            t = Regex.Replace(t, @"\b(?:[A-Za-z_][A-Za-z0-9_]*\.)+([A-Za-z_][A-Za-z0-9_]*)", "$1");

            // Normalize CLR type names to C# keyword aliases
            t = Regex.Replace(t, @"\b[A-Za-z_][A-Za-z0-9_]*\b", match =>
            {
                return BuiltInTypeAliases.TryGetValue(match.Value, out string? alias) ? alias : match.Value;
            });

            return t;
        }

        public static string BuildMemberIdentityKey(InterfaceMemberInfo member)
        {
            if (member.IsProperty)
            {
                return $"prop:{member.MemberName}";
            }

            var normTypes = ExtractNormalizedParameterTypes(member.ParameterList, hasParameterNames: true);
            return $"method:{member.MemberName}({string.Join(",", normTypes)})";
        }

        private static List<(int Start, int Length)> SplitAtTopLevelCommas(string sanitized)
        {
            var parts = new List<(int Start, int Length)>();
            int angleDepth = 0;
            int parenDepth = 0;
            int bracketDepth = 0;
            int start = 0;

            for (int i = 0; i < sanitized.Length; i++)
            {
                char c = sanitized[i];
                if (c == '<') angleDepth++;
                else if (c == '>' && angleDepth > 0) angleDepth--;
                else if (c == '(') parenDepth++;
                else if (c == ')' && parenDepth > 0) parenDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']' && bracketDepth > 0) bracketDepth--;
                else if (c == ',' && angleDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                {
                    parts.Add((start, i - start));
                    start = i + 1;
                }
            }

            if (start <= sanitized.Length)
            {
                parts.Add((start, sanitized.Length - start));
            }

            return parts;
        }

        private static int FindTopLevelEquals(string sanitized)
        {
            int angleDepth = 0;
            int parenDepth = 0;
            int bracketDepth = 0;

            for (int i = 0; i < sanitized.Length; i++)
            {
                char c = sanitized[i];
                if (c == '<') angleDepth++;
                else if (c == '>' && angleDepth > 0) angleDepth--;
                else if (c == '(') parenDepth++;
                else if (c == ')' && parenDepth > 0) parenDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']' && bracketDepth > 0) bracketDepth--;
                else if (c == '=' && angleDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                {
                    // Guard against '=>'
                    if (i + 1 < sanitized.Length && sanitized[i + 1] == '>')
                    {
                        i++;
                        continue;
                    }
                    return i;
                }
            }

            return -1;
        }
    }
}
