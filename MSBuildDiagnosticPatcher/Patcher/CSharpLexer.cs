using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MSBuildDiagnosticPatcher.Patcher
{
    internal readonly struct ScannedSource
    {
        public string Original { get; }
        public string CommentStripped { get; }
        public string CommentsAndLiteralsMasked { get; }
        public bool[] LiteralMask { get; }

        public ScannedSource(
            string original,
            string commentStripped,
            string commentsAndLiteralsMasked,
            bool[] literalMask)
        {
            Original = original;
            CommentStripped = commentStripped;
            CommentsAndLiteralsMasked = commentsAndLiteralsMasked;
            LiteralMask = literalMask;
        }
    }

    internal readonly struct TypeScopeInfo
    {
        public string Name { get; }
        public string Kind { get; }
        public bool IsSealed { get; }
        public bool IsStruct { get; }
        public int OpenBraceIndex { get; }
        public int CloseBraceIndex { get; }

        public TypeScopeInfo(
            string name,
            string kind,
            bool isSealed,
            bool isStruct,
            int openBraceIndex,
            int closeBraceIndex)
        {
            Name = name;
            Kind = kind;
            IsSealed = isSealed;
            IsStruct = isStruct;
            OpenBraceIndex = openBraceIndex;
            CloseBraceIndex = closeBraceIndex;
        }
    }

    internal static class CSharpLexer
    {
        private static readonly HashSet<string> TypeModifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal", "file",
            "new", "abstract", "sealed", "static", "readonly",
            "ref", "unsafe", "partial"
        };

        private static readonly HashSet<string> DisallowedTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "where", "class", "struct", "record", "interface", "namespace", "default", "new",
            "public", "private", "protected", "internal", "sealed", "abstract", "static",
            "readonly", "ref", "unsafe", "partial", "void", "return", "in", "out"
        };

        public static ScannedSource Scan(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return new ScannedSource(string.Empty, string.Empty, string.Empty, Array.Empty<bool>());
            }

            char[] stripped = source.ToCharArray();
            char[] masked = source.ToCharArray();
            bool[] literalMask = new bool[source.Length];

            ScanRange(source, stripped, masked, literalMask, 0, source.Length, closingBraceRunRequired: 0, out _);

            return new ScannedSource(source, new string(stripped), new string(masked), literalMask);
        }

        public static string StripComments(string source)
        {
            return Scan(source).CommentStripped;
        }

        public static string MaskCommentsAndLiterals(string source)
        {
            return Scan(source).CommentsAndLiteralsMasked;
        }

        private static void ScanRange(
            string src,
            char[] stripped,
            char[] masked,
            bool[] literalMask,
            int start,
            int end,
            int closingBraceRunRequired,
            out int nextIndex)
        {
            int i = start;
            int holeBraceDepth = 0;

            while (i < end)
            {
                char c = src[i];

                // Handle interpolation hole brace tracking when inside an interpolated hole
                if (closingBraceRunRequired > 0)
                {
                    if (c == '{')
                    {
                        holeBraceDepth++;
                        i++;
                        continue;
                    }

                    if (c == '}')
                    {
                        if (holeBraceDepth == 0)
                        {
                            int closeRun = CountCharRun(src, i, end, '}');
                            if (closeRun >= closingBraceRunRequired)
                            {
                                nextIndex = i + closingBraceRunRequired;
                                return;
                            }
                        }
                        else
                        {
                            holeBraceDepth--;
                        }
                        i++;
                        continue;
                    }
                }

                // 1. Single-line comment // or ///
                if (c == '/' && i + 1 < end && src[i + 1] == '/')
                {
                    while (i < end && src[i] != '\r' && src[i] != '\n')
                    {
                        stripped[i] = ' ';
                        masked[i] = ' ';
                        i++;
                    }
                    continue;
                }

                // 2. Multi-line block comment /* ... */
                if (c == '/' && i + 1 < end && src[i + 1] == '*')
                {
                    stripped[i] = ' ';
                    masked[i] = ' ';
                    stripped[i + 1] = ' ';
                    masked[i + 1] = ' ';
                    i += 2;

                    while (i < end)
                    {
                        if (src[i] == '*' && i + 1 < end && src[i + 1] == '/')
                        {
                            stripped[i] = ' ';
                            masked[i] = ' ';
                            stripped[i + 1] = ' ';
                            masked[i + 1] = ' ';
                            i += 2;
                            break;
                        }

                        if (src[i] != '\r' && src[i] != '\n')
                        {
                            stripped[i] = ' ';
                            masked[i] = ' ';
                        }
                        i++;
                    }
                    continue;
                }

                // 3. Character literal '...'
                if (c == '\'')
                {
                    int litStart = i;
                    i++;
                    while (i < end && src[i] != '\r' && src[i] != '\n')
                    {
                        if (src[i] == '\\' && i + 1 < end)
                        {
                            i += 2;
                            continue;
                        }
                        if (src[i] == '\'')
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    MaskLiteralSpan(src, masked, literalMask, litStart, i, '\'');
                    continue;
                }

                // 4. Interpolated verbatim strings $@"..." or @$"..."
                if ((c == '$' && i + 2 < end && src[i + 1] == '@' && src[i + 2] == '"') ||
                    (c == '@' && i + 2 < end && src[i + 1] == '$' && src[i + 2] == '"'))
                {
                    int litStart = i;
                    i += 3;
                    while (i < end)
                    {
                        if (src[i] == '"' && i + 1 < end && src[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        if ((src[i] == '{' && i + 1 < end && src[i + 1] == '{') ||
                            (src[i] == '}' && i + 1 < end && src[i + 1] == '}'))
                        {
                            i += 2;
                            continue;
                        }
                        if (src[i] == '{')
                        {
                            ScanRange(src, stripped, masked, literalMask, i + 1, end, closingBraceRunRequired: 1, out i);
                            continue;
                        }
                        if (src[i] == '"')
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    MaskLiteralSpan(src, masked, literalMask, litStart, i, '"');
                    continue;
                }

                // 5. Verbatim string @"..."
                if (c == '@' && i + 1 < end && src[i + 1] == '"')
                {
                    int litStart = i;
                    i += 2;
                    while (i < end)
                    {
                        if (src[i] == '"' && i + 1 < end && src[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        if (src[i] == '"')
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    MaskLiteralSpan(src, masked, literalMask, litStart, i, '"');
                    continue;
                }

                // 6. Raw / Interpolated Raw / Interpolated Regular / Regular strings starting with $* "+
                if (c == '$' || c == '"')
                {
                    int dollarCount = CountCharRun(src, i, end, '$');
                    int quoteStart = i + dollarCount;
                    int quoteCount = quoteStart < end ? CountCharRun(src, quoteStart, end, '"') : 0;

                    if (quoteCount >= 3)
                    {
                        // Raw or interpolated raw string literal ("""...""" or $"""...""" / $$"""...""")
                        int litStart = i;
                        i = quoteStart + quoteCount;
                        while (i < end)
                        {
                            if (src[i] == '"')
                            {
                                int closeQuotes = CountCharRun(src, i, end, '"');
                                if (closeQuotes >= quoteCount)
                                {
                                    i += closeQuotes;
                                    break;
                                }
                                i += closeQuotes;
                                continue;
                            }

                            if (dollarCount > 0 && src[i] == '{')
                            {
                                int openBraces = CountCharRun(src, i, end, '{');
                                if (openBraces >= dollarCount)
                                {
                                    i += openBraces;
                                    ScanRange(src, stripped, masked, literalMask, i, end, closingBraceRunRequired: dollarCount, out i);
                                    continue;
                                }
                                i += openBraces;
                                continue;
                            }

                            i++;
                        }
                        MaskLiteralSpan(src, masked, literalMask, litStart, i, '"');
                        continue;
                    }

                    if (dollarCount == 1 && quoteCount > 0)
                    {
                        // Regular interpolated string $"..." (including $"")
                        int litStart = i;
                        i = quoteStart + 1;
                        while (i < end && src[i] != '\r' && src[i] != '\n')
                        {
                            if (src[i] == '\\' && i + 1 < end)
                            {
                                i += 2;
                                continue;
                            }
                            if ((src[i] == '{' && i + 1 < end && src[i + 1] == '{') ||
                                (src[i] == '}' && i + 1 < end && src[i + 1] == '}'))
                            {
                                i += 2;
                                continue;
                            }
                            if (src[i] == '{')
                            {
                                ScanRange(src, stripped, masked, literalMask, i + 1, end, closingBraceRunRequired: 1, out i);
                                continue;
                            }
                            if (src[i] == '"')
                            {
                                i++;
                                break;
                            }
                            i++;
                        }
                        MaskLiteralSpan(src, masked, literalMask, litStart, i, '"');
                        continue;
                    }

                    if (dollarCount == 0 && quoteCount > 0)
                    {
                        // Regular string "..." (including "")
                        int litStart = i;
                        i++;
                        while (i < end && src[i] != '\r' && src[i] != '\n')
                        {
                            if (src[i] == '\\' && i + 1 < end)
                            {
                                i += 2;
                                continue;
                            }
                            if (src[i] == '"')
                            {
                                i++;
                                break;
                            }
                            i++;
                        }
                        MaskLiteralSpan(src, masked, literalMask, litStart, i, '"');
                        continue;
                    }
                }

                i++;
            }

            nextIndex = end;
        }

        private static void MaskLiteralSpan(
            string src,
            char[] masked,
            bool[] literalMask,
            int start,
            int endExclusive,
            char delimiter)
        {
            if (endExclusive <= start) return;

            for (int k = start; k < endExclusive; k++)
            {
                literalMask[k] = true;
                if (src[k] != '\r' && src[k] != '\n')
                {
                    masked[k] = ' ';
                }
            }

            masked[start] = delimiter;
            if (endExclusive - 1 > start && src[endExclusive - 1] != '\r' && src[endExclusive - 1] != '\n')
            {
                masked[endExclusive - 1] = delimiter;
            }
        }

        private static int CountCharRun(string src, int index, int end, char target)
        {
            int count = 0;
            while (index + count < end && src[index + count] == target)
            {
                count++;
            }
            return count;
        }

        public static int FindMatchingBrace(string masked, int openBraceIndex, int limitExclusive = -1)
        {
            int end = limitExclusive < 0 || limitExclusive > masked.Length ? masked.Length : limitExclusive;
            if (openBraceIndex < 0 || openBraceIndex >= end || masked[openBraceIndex] != '{')
            {
                return -1;
            }

            int depth = 1;
            for (int i = openBraceIndex + 1; i < end; i++)
            {
                if (masked[i] == '{')
                {
                    depth++;
                }
                else if (masked[i] == '}')
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

        public static List<string> SplitTopLevelCommas(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            string masked = MaskCommentsAndLiterals(text);
            int angleDepth = 0;
            int bracketDepth = 0;
            int parenDepth = 0;
            int braceDepth = 0;
            bool inDefaultValue = false;
            int start = 0;

            for (int i = 0; i < masked.Length; i++)
            {
                char c = masked[i];
                switch (c)
                {
                    case '(':
                        parenDepth++;
                        break;
                    case ')':
                        if (parenDepth > 0) parenDepth--;
                        break;
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        if (bracketDepth > 0) bracketDepth--;
                        break;
                    case '{':
                        braceDepth++;
                        break;
                    case '}':
                        if (braceDepth > 0) braceDepth--;
                        break;
                    case '=':
                        if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && angleDepth == 0)
                        {
                            inDefaultValue = true;
                        }
                        break;
                    case '<':
                        if (!inDefaultValue)
                        {
                            angleDepth++;
                        }
                        break;
                    case '>':
                        if (!inDefaultValue && angleDepth > 0)
                        {
                            angleDepth--;
                        }
                        break;
                    case ',':
                        if (angleDepth == 0 && bracketDepth == 0 && parenDepth == 0 && braceDepth == 0)
                        {
                            string item = text.Substring(start, i - start).Trim();
                            if (item.Length > 0)
                            {
                                result.Add(item);
                            }
                            start = i + 1;
                            inDefaultValue = false;
                        }
                        break;
                }
            }

            string tail = text.Substring(start).Trim();
            if (tail.Length > 0)
            {
                result.Add(tail);
            }

            return result;
        }

        public static string CollapseWhitespacePreservingLiterals(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var scanned = Scan(text);
            bool[] literalMask = scanned.LiteralMask;
            var sb = new StringBuilder(text.Length);
            bool prevSpace = false;

            for (int i = 0; i < text.Length; i++)
            {
                if (literalMask[i])
                {
                    prevSpace = false;
                    sb.Append(text[i]);
                    continue;
                }

                if (char.IsWhiteSpace(text[i]))
                {
                    if (!prevSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        prevSpace = true;
                    }
                }
                else
                {
                    prevSpace = false;
                    sb.Append(text[i]);
                }
            }

            return sb.ToString().Trim();
        }

        public static List<string> SplitQualifiedTypeSegments(string typeName)
        {
            var segments = new List<string>();
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return segments;
            }

            int angleDepth = 0;
            int start = 0;
            for (int i = 0; i < typeName.Length; i++)
            {
                char c = typeName[i];
                if (c == '<') angleDepth++;
                else if (c == '>' && angleDepth > 0) angleDepth--;
                else if (c == '.' && angleDepth == 0)
                {
                    string seg = StripGenericArity(typeName.Substring(start, i - start).Trim());
                    if (seg.Length > 0) segments.Add(seg);
                    start = i + 1;
                }
            }

            string last = StripGenericArity(typeName.Substring(start).Trim());
            if (last.Length > 0)
            {
                segments.Add(last);
            }

            return segments;
        }

        public static string StripGenericArity(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return string.Empty;
            }

            int lt = identifier.IndexOf('<');
            if (lt >= 0)
            {
                identifier = identifier.Substring(0, lt);
            }
            int tick = identifier.IndexOf('`');
            if (tick >= 0)
            {
                identifier = identifier.Substring(0, tick);
            }
            return identifier.Trim();
        }

        public static bool TryFindTypeScope(string source, string? targetTypeName, out TypeScopeInfo scopeInfo)
        {
            var scanned = Scan(source);
            return TryFindTypeScopeFromMasked(scanned.CommentsAndLiteralsMasked, targetTypeName, out scopeInfo);
        }

        public static bool TryFindTypeScopeFromMasked(string masked, string? targetTypeName, out TypeScopeInfo scopeInfo)
        {
            scopeInfo = default;

            if (string.IsNullOrWhiteSpace(masked))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetTypeName))
            {
                var topTypes = EnumerateChildTypeScopes(masked, 0, masked.Length, allowNamespaceBlocks: true);
                if (topTypes.Count > 0)
                {
                    scopeInfo = topTypes[0];
                    return true;
                }
                return false;
            }

            var segments = SplitQualifiedTypeSegments(targetTypeName!);
            if (segments.Count == 0)
            {
                return false;
            }

            int searchStart = 0;
            int searchEnd = masked.Length;
            int segOffset = ConsumeMatchingNamespacePrefix(masked, segments, ref searchStart, ref searchEnd);

            for (int startIdx = segOffset; startIdx < segments.Count; startIdx++)
            {
                if (TryWalkTypeSegments(
                    masked,
                    segments,
                    startIdx,
                    searchStart,
                    searchEnd,
                    allowNamespaceAtRoot: startIdx == segOffset || segOffset == 0,
                    out scopeInfo))
                {
                    return true;
                }

                // If segments[startIdx] exists as an outer type in current scope, do not skip past it
                var candidateOuter = EnumerateChildTypeScopes(masked, searchStart, searchEnd, allowNamespaceBlocks: true);
                bool outerExists = false;
                foreach (var t in candidateOuter)
                {
                    if (t.Name.Equals(segments[startIdx], StringComparison.Ordinal))
                    {
                        outerExists = true;
                        break;
                    }
                }
                if (outerExists)
                {
                    break;
                }
            }

            // Fallback when caller passed a single unqualified leaf name (e.g. "Inner") for a nested type
            if (segments.Count == 1)
            {
                if (TryFindAnyDepthTypeScope(masked, segments[0], searchStart, searchEnd, out scopeInfo))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryWalkTypeSegments(
            string masked,
            List<string> segments,
            int startSegmentIndex,
            int rangeStart,
            int rangeEnd,
            bool allowNamespaceAtRoot,
            out TypeScopeInfo result)
        {
            result = default;
            int curStart = rangeStart;
            int curEnd = rangeEnd;
            TypeScopeInfo current = default;
            bool foundAny = false;

            for (int s = startSegmentIndex; s < segments.Count; s++)
            {
                string targetSeg = segments[s];
                bool allowNs = (s == startSegmentIndex) && allowNamespaceAtRoot;
                var children = EnumerateChildTypeScopes(masked, curStart, curEnd, allowNamespaceBlocks: allowNs);

                bool matched = false;
                foreach (var child in children)
                {
                    if (child.Name.Equals(targetSeg, StringComparison.Ordinal))
                    {
                        current = child;
                        curStart = child.OpenBraceIndex + 1;
                        curEnd = child.CloseBraceIndex;
                        matched = true;
                        foundAny = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return false;
                }
            }

            if (foundAny)
            {
                result = current;
                return true;
            }

            return false;
        }

        private static bool TryFindAnyDepthTypeScope(
            string masked,
            string targetName,
            int start,
            int end,
            out TypeScopeInfo result)
        {
            var top = EnumerateChildTypeScopes(masked, start, end, allowNamespaceBlocks: true);
            foreach (var child in top)
            {
                if (child.Name.Equals(targetName, StringComparison.Ordinal))
                {
                    result = child;
                    return true;
                }
                if (TryFindAnyDepthTypeScope(masked, targetName, child.OpenBraceIndex + 1, child.CloseBraceIndex, out result))
                {
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static int ConsumeMatchingNamespacePrefix(
            string masked,
            List<string> segments,
            ref int searchStart,
            ref int searchEnd)
        {
            var nsRegex = new Regex(@"\bnamespace\s+(?<ns>[A-Za-z_@\w\.]*)\s*(?<term>[{;])");
            var matches = nsRegex.Matches(masked, searchStart);

            foreach (Match m in matches)
            {
                if (m.Index >= searchEnd) break;
                string nsName = m.Groups["ns"].Value.Trim();
                var nsParts = nsName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (nsParts.Length == 0 || nsParts.Length >= segments.Count)
                {
                    continue;
                }

                bool prefixMatch = true;
                for (int k = 0; k < nsParts.Length; k++)
                {
                    if (!nsParts[k].Equals(segments[k], StringComparison.Ordinal))
                    {
                        prefixMatch = false;
                        break;
                    }
                }

                if (prefixMatch)
                {
                    string term = m.Groups["term"].Value;
                    int termIdx = m.Groups["term"].Index;
                    if (term == "{")
                    {
                        int closeBrace = FindMatchingBrace(masked, termIdx, searchEnd);
                        if (closeBrace > termIdx)
                        {
                            searchStart = termIdx + 1;
                            searchEnd = closeBrace;
                            return nsParts.Length;
                        }
                    }
                    else
                    {
                        searchStart = termIdx + 1;
                        return nsParts.Length;
                    }
                }
            }

            return 0;
        }

        public static List<TypeScopeInfo> EnumerateChildTypeScopes(
            string masked,
            int rangeStart,
            int rangeEnd,
            bool allowNamespaceBlocks)
        {
            var results = new List<TypeScopeInfo>();
            int i = rangeStart;

            while (i < rangeEnd)
            {
                char c = masked[i];

                // Skip attributes [...] at current level
                if (c == '[')
                {
                    i = SkipBalanced(masked, i, rangeEnd, '[', ']');
                    continue;
                }

                // Skip unrelated blocks (e.g. method or property bodies)
                if (c == '{')
                {
                    int close = FindMatchingBrace(masked, i, rangeEnd);
                    i = close > i ? close + 1 : rangeEnd;
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    int wordStart = i;
                    int wordEnd = ReadWord(masked, i, rangeEnd);
                    string word = masked.Substring(wordStart, wordEnd - wordStart);

                    if (allowNamespaceBlocks && word == "namespace")
                    {
                        int braceOrSemi = FindNextTopLevelChar(masked, wordEnd, rangeEnd, '{', ';');
                        if (braceOrSemi >= 0 && masked[braceOrSemi] == '{')
                        {
                            int nsClose = FindMatchingBrace(masked, braceOrSemi, rangeEnd);
                            if (nsClose > braceOrSemi)
                            {
                                results.AddRange(EnumerateChildTypeScopes(masked, braceOrSemi + 1, nsClose, allowNamespaceBlocks: true));
                                i = nsClose + 1;
                                continue;
                            }
                        }
                        i = braceOrSemi >= 0 ? braceOrSemi + 1 : wordEnd;
                        continue;
                    }

                    if (word == "class" || word == "struct" || word == "record")
                    {
                        if (IsValidTypeDeclarationHeader(masked, rangeStart, wordStart, out bool isSealed))
                        {
                            int afterKind = wordEnd;
                            string kind = word;

                            if (word == "record")
                            {
                                int nextTokStart = SkipWhitespace(masked, afterKind, rangeEnd);
                                if (nextTokStart < rangeEnd && IsIdentifierStart(masked[nextTokStart]))
                                {
                                    int nextTokEnd = ReadWord(masked, nextTokStart, rangeEnd);
                                    string nextWord = masked.Substring(nextTokStart, nextTokEnd - nextTokStart);
                                    if (nextWord == "class" || nextWord == "struct")
                                    {
                                        kind = $"record {nextWord}";
                                        afterKind = nextTokEnd;
                                    }
                                }
                            }

                            int nameStart = SkipWhitespace(masked, afterKind, rangeEnd);
                            if (nameStart < rangeEnd && IsIdentifierStart(masked[nameStart]))
                            {
                                int nameEnd = ReadWord(masked, nameStart, rangeEnd);
                                string rawName = masked.Substring(nameStart, nameEnd - nameStart);
                                string cleanName = rawName.TrimStart('@');

                                if (rawName.StartsWith("@", StringComparison.Ordinal) || !DisallowedTypeNames.Contains(cleanName))
                                {
                                    int bodyDelim = FindTypeBodyDelimiter(masked, nameEnd, rangeEnd);
                                    if (bodyDelim >= 0 && masked[bodyDelim] == '{')
                                    {
                                        int closeBrace = FindMatchingBrace(masked, bodyDelim, rangeEnd);
                                        if (closeBrace > bodyDelim)
                                        {
                                            bool isStruct = kind == "struct" || kind == "record struct";

                                            results.Add(new TypeScopeInfo(
                                                cleanName,
                                                kind,
                                                isSealed,
                                                isStruct,
                                                bodyDelim,
                                                closeBrace));

                                            i = closeBrace + 1;
                                            continue;
                                        }
                                    }
                                    else if (bodyDelim >= 0 && masked[bodyDelim] == ';')
                                    {
                                        i = bodyDelim + 1;
                                        continue;
                                    }
                                }
                            }
                        }
                    }

                    i = wordEnd;
                    continue;
                }

                i++;
            }

            return results;
        }

        private static bool IsValidTypeDeclarationHeader(
            string masked,
            int boundStart,
            int keywordIndex,
            out bool isSealed)
        {
            isSealed = false;
            int headerStart = boundStart;

            for (int k = keywordIndex - 1; k >= boundStart; k--)
            {
                char ch = masked[k];
                if (ch == '{' || ch == '}' || ch == ';' || ch == ']')
                {
                    headerStart = k + 1;
                    break;
                }
            }

            int pos = headerStart;
            while (pos < keywordIndex)
            {
                pos = SkipWhitespace(masked, pos, keywordIndex);
                if (pos >= keywordIndex) break;

                if (!IsIdentifierStart(masked[pos]))
                {
                    return false;
                }

                int end = ReadWord(masked, pos, keywordIndex);
                string word = masked.Substring(pos, end - pos);
                if (!TypeModifiers.Contains(word))
                {
                    return false;
                }

                if (word.Equals("sealed", StringComparison.Ordinal))
                {
                    isSealed = true;
                }

                pos = end;
            }

            return true;
        }

        private static int FindTypeBodyDelimiter(string masked, int start, int end)
        {
            int angleDepth = 0;
            int parenDepth = 0;
            int bracketDepth = 0;

            for (int i = start; i < end; i++)
            {
                char c = masked[i];
                switch (c)
                {
                    case '<':
                        angleDepth++;
                        break;
                    case '>':
                        if (angleDepth > 0) angleDepth--;
                        break;
                    case '(':
                        parenDepth++;
                        break;
                    case ')':
                        if (parenDepth > 0) parenDepth--;
                        break;
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        if (bracketDepth > 0) bracketDepth--;
                        break;
                    case '{':
                    case ';':
                        if (angleDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                        {
                            return i;
                        }
                        break;
                }
            }

            return -1;
        }

        private static int FindNextTopLevelChar(string masked, int start, int end, char target1, char target2)
        {
            for (int i = start; i < end; i++)
            {
                if (masked[i] == target1 || masked[i] == target2)
                {
                    return i;
                }
            }
            return -1;
        }

        public static int SkipBalanced(string masked, int start, int end, char open, char close)
        {
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                if (masked[i] == open) depth++;
                else if (masked[i] == close)
                {
                    depth--;
                    if (depth == 0) return i + 1;
                }
            }
            return end;
        }

        public static int SkipWhitespace(string text, int pos, int end)
        {
            while (pos < end && (char.IsWhiteSpace(text[pos]) || text[pos] == '\uFEFF'))
            {
                pos++;
            }
            return pos;
        }

        public static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_' || c == '@';
        }

        public static int ReadWord(string text, int start, int end)
        {
            int i = start;
            if (i < end && text[i] == '@') i++;
            while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
            {
                i++;
            }
            return i;
        }
    }
}
