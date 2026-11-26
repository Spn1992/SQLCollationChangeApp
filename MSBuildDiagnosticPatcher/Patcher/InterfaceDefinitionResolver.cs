using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MSBuildDiagnosticPatcher.Patcher
{
    public class InterfaceMemberInfo
    {
        public string InterfaceName { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string ReturnType { get; set; } = "void";
        public string ParameterList { get; set; } = string.Empty;
        public bool IsProperty { get; set; }
        public bool HasGetter { get; set; } = true;
        public bool HasSetter { get; set; }
        public string FullSignature { get; set; } = string.Empty;
        public string InterfaceFilePath { get; set; } = string.Empty;
    }

    public class InterfaceDefinitionResolver
    {
        private static readonly Regex Cs0535OuterRegex = new Regex(
            @"^'(?<class>[^']+)'\s+does not implement interface member\s+'(?<memberSpec>[^']+)'",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> MemberLeadingModifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "protected", "internal", "private",
            "new", "unsafe", "virtual", "abstract", "static"
        };

        private static readonly HashSet<string> ParameterModifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "ref", "out", "in", "params", "this", "scoped", "readonly"
        };

        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, string?> _filePathCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _rootSearchCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _fileContentCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string BodyStripped, string BodyMasked, Dictionary<string, string>? TypeParamMap)> _interfaceBodyCache =
            new Dictionary<string, (string, string, Dictionary<string, string>?)>(StringComparer.OrdinalIgnoreCase);

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _filePathCache.Clear();
                _rootSearchCache.Clear();
                _fileContentCache.Clear();
                _interfaceBodyCache.Clear();
            }
        }

        public static (string ClassName, string InterfaceName, string MemberName, string ExpectedParamTypes)? ParseCs0535Message(string message)
        {
            var match = Cs0535OuterRegex.Match(message.Trim());
            if (!match.Success)
            {
                return null;
            }

            string className = match.Groups["class"].Value.Trim();
            string memberSpec = match.Groups["memberSpec"].Value.Trim();

            // Split memberSpec into head (Interface.Member) and optional (params) at angle depth 0
            int angleDepth = 0;
            int openParen = -1;
            for (int i = 0; i < memberSpec.Length; i++)
            {
                char c = memberSpec[i];
                if (c == '<') angleDepth++;
                else if (c == '>' && angleDepth > 0) angleDepth--;
                else if (c == '(' && angleDepth == 0)
                {
                    openParen = i;
                    break;
                }
            }

            string head;
            string expectedParams = string.Empty;
            if (openParen >= 0 && memberSpec.EndsWith(")", StringComparison.Ordinal))
            {
                head = memberSpec.Substring(0, openParen).Trim();
                expectedParams = memberSpec.Substring(openParen + 1, memberSpec.Length - openParen - 2).Trim();
            }
            else
            {
                head = memberSpec;
            }

            // Find last '.' at angle depth 0 separating InterfaceName and MemberName
            angleDepth = 0;
            int lastDot = -1;
            for (int i = 0; i < head.Length; i++)
            {
                char c = head[i];
                if (c == '<') angleDepth++;
                else if (c == '>' && angleDepth > 0) angleDepth--;
                else if (c == '.' && angleDepth == 0)
                {
                    lastDot = i;
                }
            }

            if (lastDot <= 0 || lastDot >= head.Length - 1)
            {
                return null;
            }

            string interfaceName = head.Substring(0, lastDot).Trim();
            string memberName = head.Substring(lastDot + 1).Trim();

            return (className, interfaceName, memberName, expectedParams);
        }

        public InterfaceMemberInfo? ResolveInterfaceMember(
            string interfaceName,
            string memberName,
            string expectedParamTypes,
            string contextFilePath,
            string? projectRoot = null)
        {
            string? interfaceFile = FindInterfaceFile(interfaceName, contextFilePath, projectRoot);
            if (interfaceFile != null)
            {
                string? content = GetOrReadFileContent(interfaceFile);
                if (content != null)
                {
                    var memberInfo = ExtractMemberFromInterfaceSourceCore(
                        content,
                        interfaceName,
                        memberName,
                        expectedParamTypes,
                        cacheKey: $"{interfaceFile}|{interfaceName}");
                    if (memberInfo != null)
                    {
                        memberInfo.InterfaceFilePath = interfaceFile;
                        return memberInfo;
                    }
                }
            }

            // Check contextFilePath when the file declares the interface alongside the implementing type
            if (!string.IsNullOrWhiteSpace(contextFilePath))
            {
                string? contextContent = GetOrReadFileContent(contextFilePath);
                if (contextContent != null && ContainsInterfaceDeclaration(contextContent, interfaceName))
                {
                    var contextMember = ExtractMemberFromInterfaceSourceCore(
                        contextContent,
                        interfaceName,
                        memberName,
                        expectedParamTypes,
                        cacheKey: $"{contextFilePath}|{interfaceName}");
                    if (contextMember != null)
                    {
                        contextMember.InterfaceFilePath = contextFilePath;
                        return contextMember;
                    }
                }
            }

            // Fallback: construct signature directly from diagnostic error if interface definition is not found
            return FallbackMemberInfo(interfaceName, memberName, expectedParamTypes, interfaceFile);
        }

        private static bool ContainsInterfaceDeclaration(string sourceCode, string interfaceName)
        {
            string shortName = GetShortInterfaceName(interfaceName);
            if (string.IsNullOrEmpty(shortName))
            {
                return false;
            }

            string masked = CSharpLexer.MaskCommentsAndLiterals(sourceCode);
            return Regex.IsMatch(masked, $@"\binterface\s+@?{Regex.Escape(shortName)}\b");
        }

        private static string GetShortInterfaceName(string interfaceName)
        {
            var segments = CSharpLexer.SplitQualifiedTypeSegments(interfaceName);
            return segments.Count > 0 ? segments[segments.Count - 1].TrimStart('@') : CSharpLexer.StripGenericArity(interfaceName).TrimStart('@');
        }

        private string? GetOrReadFileContent(string filePath)
        {
            lock (_cacheLock)
            {
                if (_fileContentCache.TryGetValue(filePath, out var content))
                {
                    return content;
                }
            }

            if (!File.Exists(filePath))
            {
                return null;
            }

            string diskContent = File.ReadAllText(filePath);
            lock (_cacheLock)
            {
                _fileContentCache[filePath] = diskContent;
            }
            return diskContent;
        }

        public string? FindInterfaceFile(string interfaceName, string contextFilePath, string? projectRoot)
        {
            string root = string.IsNullOrWhiteSpace(projectRoot) ? Directory.GetCurrentDirectory() : projectRoot;
            string shortName = GetShortInterfaceName(interfaceName);
            string cacheKey = $"{shortName}|{contextFilePath}|{root}";

            lock (_cacheLock)
            {
                if (_filePathCache.TryGetValue(cacheKey, out var cachedPath))
                {
                    return cachedPath;
                }
            }

            string? resolvedPath = ResolveInterfaceFilePathInternal(shortName, contextFilePath, root);

            lock (_cacheLock)
            {
                _filePathCache[cacheKey] = resolvedPath;
            }

            return resolvedPath;
        }

        private string? ResolveInterfaceFilePathInternal(string shortInterfaceName, string contextFilePath, string root)
        {
            string fileName = $"{shortInterfaceName}.cs";

            // 1. Same directory as context file
            string? dir = Path.GetDirectoryName(contextFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                string sameDirCandidate = Path.Combine(dir, fileName);
                if (File.Exists(sameDirCandidate)) return sameDirCandidate;

                // 2. Parent directory
                string? parent = Directory.GetParent(dir)?.FullName;
                if (!string.IsNullOrEmpty(parent))
                {
                    string parentCandidate = Path.Combine(parent, fileName);
                    if (File.Exists(parentCandidate)) return parentCandidate;

                    // Sibling Services/ directory
                    string siblingDir = Path.Combine(parent, "Services", fileName);
                    if (File.Exists(siblingDir)) return siblingDir;
                }
            }

            // 3. Search under project root
            string rootSearchKey = $"{fileName}|{root}";
            lock (_cacheLock)
            {
                if (_rootSearchCache.TryGetValue(rootSearchKey, out var cachedRootPath))
                {
                    return cachedRootPath;
                }
            }

            string? foundRootPath = null;
            try
            {
                var files = Directory.GetFiles(root, fileName, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    foundRootPath = files[0];
                }
            }
            catch
            {
                // Ignore search permission exceptions
            }

            lock (_cacheLock)
            {
                _rootSearchCache[rootSearchKey] = foundRootPath;
            }

            return foundRootPath;
        }

        public InterfaceMemberInfo? ExtractMemberFromInterfaceSource(
            string sourceCode,
            string interfaceName,
            string memberName,
            string expectedParamTypes)
        {
            return ExtractMemberFromInterfaceSourceCore(sourceCode, interfaceName, memberName, expectedParamTypes, cacheKey: null);
        }

        private InterfaceMemberInfo? ExtractMemberFromInterfaceSourceCore(
            string sourceCode,
            string interfaceName,
            string memberName,
            string expectedParamTypes,
            string? cacheKey)
        {
            if (string.IsNullOrWhiteSpace(sourceCode) || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            (string bodyStripped, string bodyMasked, Dictionary<string, string>? typeParamMap) bodySlices =
                (string.Empty, string.Empty, null);
            bool foundCached = false;

            if (!string.IsNullOrEmpty(cacheKey))
            {
                lock (_cacheLock)
                {
                    if (_interfaceBodyCache.TryGetValue(cacheKey, out var cachedSlices))
                    {
                        bodySlices = cachedSlices;
                        foundCached = true;
                    }
                }
            }

            if (!foundCached)
            {
                var scanned = CSharpLexer.Scan(sourceCode);
                bodySlices = ExtractInterfaceBodySlices(scanned, interfaceName);
                if (!string.IsNullOrEmpty(cacheKey))
                {
                    lock (_cacheLock)
                    {
                        _interfaceBodyCache[cacheKey] = bodySlices;
                    }
                }
            }

            var (bodyStripped, bodyMasked, typeParamMap) = bodySlices;

            var declarations = SplitInterfaceDeclarations(bodyStripped, bodyMasked);
            string targetBaseMemberName = CSharpLexer.StripGenericArity(memberName).TrimStart('@');

            InterfaceMemberInfo? bestMethodMatch = null;

            foreach (var (declStripped, declMasked) in declarations)
            {
                if (TryParseMethodDeclaration(declStripped, declMasked, interfaceName, targetBaseMemberName, typeParamMap, out var methodInfo))
                {
                    if (MatchesParameterTypes(methodInfo.ParameterList, expectedParamTypes))
                    {
                        return methodInfo;
                    }
                    bestMethodMatch ??= methodInfo;
                }
            }

            if (bestMethodMatch != null)
            {
                return bestMethodMatch;
            }

            foreach (var (declStripped, declMasked) in declarations)
            {
                if (TryParsePropertyDeclaration(declStripped, declMasked, interfaceName, targetBaseMemberName, typeParamMap, out var propInfo))
                {
                    return propInfo;
                }
            }

            return null;
        }

        private static List<(string Stripped, string Masked)> SplitInterfaceDeclarations(string bodyStripped, string bodyMasked)
        {
            var list = new List<(string, string)>();
            int braceDepth = 0;
            int parenDepth = 0;
            int bracketDepth = 0;
            int start = 0;

            for (int i = 0; i < bodyMasked.Length; i++)
            {
                char c = bodyMasked[i];
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
                        if (braceDepth > 0)
                        {
                            braceDepth--;
                            if (braceDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                            {
                                AddSliceIfNonEmpty(list, bodyStripped, bodyMasked, start, i + 1);
                                start = i + 1;
                            }
                        }
                        break;
                    case ';':
                        if (braceDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                        {
                            AddSliceIfNonEmpty(list, bodyStripped, bodyMasked, start, i + 1);
                            start = i + 1;
                        }
                        break;
                }
            }

            if (start < bodyMasked.Length)
            {
                AddSliceIfNonEmpty(list, bodyStripped, bodyMasked, start, bodyMasked.Length);
            }

            return list;
        }

        private static void AddSliceIfNonEmpty(
            List<(string, string)> list,
            string stripped,
            string masked,
            int start,
            int endExclusive)
        {
            while (start < endExclusive && char.IsWhiteSpace(masked[start])) start++;
            while (endExclusive > start && char.IsWhiteSpace(masked[endExclusive - 1])) endExclusive--;

            if (endExclusive > start)
            {
                list.Add((stripped.Substring(start, endExclusive - start), masked.Substring(start, endExclusive - start)));
            }
        }

        private static int SkipAttributesAndMemberModifiers(string declMasked, out bool isEvent)
        {
            isEvent = false;
            int pos = 0;
            int len = declMasked.Length;

            while (pos < len)
            {
                pos = CSharpLexer.SkipWhitespace(declMasked, pos, len);
                if (pos >= len) break;

                if (declMasked[pos] == '[')
                {
                    pos = CSharpLexer.SkipBalanced(declMasked, pos, len, '[', ']');
                    continue;
                }

                if (CSharpLexer.IsIdentifierStart(declMasked[pos]))
                {
                    int wordEnd = CSharpLexer.ReadWord(declMasked, pos, len);
                    string word = declMasked.Substring(pos, wordEnd - pos);

                    if (word == "event")
                    {
                        isEvent = true;
                        return wordEnd;
                    }

                    if (MemberLeadingModifiers.Contains(word))
                    {
                        pos = wordEnd;
                        continue;
                    }
                }

                break;
            }

            return pos;
        }

        private static bool TryParseMethodDeclaration(
            string declStripped,
            string declMasked,
            string interfaceName,
            string targetBaseMemberName,
            Dictionary<string, string>? typeParamMap,
            out InterfaceMemberInfo info)
        {
            info = null!;
            if (!declMasked.EndsWith(";", StringComparison.Ordinal))
            {
                return false;
            }

            int pos = SkipAttributesAndMemberModifiers(declMasked, out bool isEvent);
            if (isEvent || pos >= declMasked.Length - 1)
            {
                return false;
            }

            // Reject expression-bodied default interface methods ('=> ...;')
            int sigEnd = FindTopLevelArrowOrEnd(declMasked, pos, declMasked.Length - 1);
            if (sigEnd < declMasked.Length - 1)
            {
                return false;
            }

            // Collect all top-level (...) groups in [pos .. sigEnd)
            var parenGroups = new List<(int Open, int Close)>();
            int angleDepth = 0;
            int bracketDepth = 0;
            int parenDepth = 0;
            int currentOpenParen = -1;

            for (int i = pos; i < sigEnd; i++)
            {
                char c = declMasked[i];
                switch (c)
                {
                    case '<':
                        if (parenDepth == 0 && bracketDepth == 0) angleDepth++;
                        break;
                    case '>':
                        if (parenDepth == 0 && bracketDepth == 0 && angleDepth > 0) angleDepth--;
                        break;
                    case '[':
                        if (parenDepth == 0) bracketDepth++;
                        break;
                    case ']':
                        if (parenDepth == 0 && bracketDepth > 0) bracketDepth--;
                        break;
                    case '(':
                        if (parenDepth == 0 && bracketDepth == 0 && angleDepth == 0)
                        {
                            currentOpenParen = i;
                        }
                        parenDepth++;
                        break;
                    case ')':
                        if (parenDepth > 0)
                        {
                            parenDepth--;
                            if (parenDepth == 0 && bracketDepth == 0 && angleDepth == 0 && currentOpenParen >= 0)
                            {
                                parenGroups.Add((currentOpenParen, i));
                                currentOpenParen = -1;
                            }
                        }
                        break;
                    case '{':
                    case '}':
                        if (parenDepth == 0 && bracketDepth == 0 && angleDepth == 0)
                        {
                            return false;
                        }
                        break;
                }
            }

            if (parenGroups.Count == 0)
            {
                return false;
            }

            // Find the parameter list (...) — normally the last top-level (...) unless a 'where T : new()' constraint follows it
            int paramGroupIdx = parenGroups.Count - 1;
            while (paramGroupIdx > 0)
            {
                int prevClose = parenGroups[paramGroupIdx - 1].Close;
                int thisOpen = parenGroups[paramGroupIdx].Open;
                string between = declMasked.Substring(prevClose + 1, thisOpen - prevClose - 1);
                if (Regex.IsMatch(between, @"\bwhere\b"))
                {
                    paramGroupIdx--;
                }
                else
                {
                    break;
                }
            }

            var (openParen, closeParen) = parenGroups[paramGroupIdx];

            // Verify trailing tokens after closeParen are only whitespace or 'where' constraints
            string trailing = declMasked.Substring(closeParen + 1, sigEnd - closeParen - 1).Trim();
            if (trailing.Length > 0 && !trailing.StartsWith("where", StringComparison.Ordinal))
            {
                return false;
            }

            // Walk backward before openParen for optional generic <T> and method identifier
            int cursor = TrimRightIndex(declMasked, pos, openParen);
            string methodTypeParams = string.Empty;

            if (cursor > pos && declMasked[cursor - 1] == '>')
            {
                int ltIdx = FindMatchingOpenAngle(declMasked, pos, cursor - 1);
                if (ltIdx > pos)
                {
                    methodTypeParams = CSharpLexer.CollapseWhitespacePreservingLiterals(
                        declStripped.Substring(ltIdx, cursor - ltIdx));
                    cursor = TrimRightIndex(declMasked, pos, ltIdx);
                }
            }

            int nameStart = ReadIdentifierBackward(declMasked, pos, cursor);
            if (nameStart < 0 || nameStart >= cursor)
            {
                return false;
            }

            string parsedName = declStripped.Substring(nameStart, cursor - nameStart).Trim();
            string cleanParsedName = parsedName.TrimStart('@');
            if (!cleanParsedName.Equals(targetBaseMemberName, StringComparison.Ordinal))
            {
                return false;
            }

            string rawReturnType = declStripped.Substring(pos, nameStart - pos);
            string returnType = FormatReturnType(rawReturnType);
            if (string.IsNullOrWhiteSpace(returnType) || !IsBalancedTypeTokens(returnType))
            {
                return false;
            }

            string rawParams = declStripped.Substring(openParen + 1, closeParen - openParen - 1);
            string paramList = CSharpLexer.CollapseWhitespacePreservingLiterals(rawParams);

            if (typeParamMap != null && typeParamMap.Count > 0)
            {
                returnType = SubstituteTypeParameters(returnType, typeParamMap);
                paramList = SubstituteTypeParameters(paramList, typeParamMap);
            }

            string emittedMemberName = string.IsNullOrEmpty(methodTypeParams)
                ? parsedName
                : $"{parsedName}{methodTypeParams}";

            info = new InterfaceMemberInfo
            {
                InterfaceName = interfaceName,
                MemberName = emittedMemberName,
                ReturnType = returnType,
                ParameterList = paramList,
                IsProperty = false,
                FullSignature = $"{returnType} {emittedMemberName}({paramList})"
            };
            return true;
        }

        private static bool TryParsePropertyDeclaration(
            string declStripped,
            string declMasked,
            string interfaceName,
            string targetBaseMemberName,
            Dictionary<string, string>? typeParamMap,
            out InterfaceMemberInfo info)
        {
            info = null!;
            if (!declMasked.EndsWith("}", StringComparison.Ordinal))
            {
                return false;
            }

            int pos = SkipAttributesAndMemberModifiers(declMasked, out bool isEvent);
            if (isEvent || pos >= declMasked.Length - 1)
            {
                return false;
            }

            // Locate top-level '{' of property accessors
            int angleDepth = 0;
            int bracketDepth = 0;
            int parenDepth = 0;
            int openBrace = -1;

            for (int i = pos; i < declMasked.Length; i++)
            {
                char c = declMasked[i];
                switch (c)
                {
                    case '<':
                        if (parenDepth == 0 && bracketDepth == 0) angleDepth++;
                        break;
                    case '>':
                        if (parenDepth == 0 && bracketDepth == 0 && angleDepth > 0) angleDepth--;
                        break;
                    case '[':
                        if (parenDepth == 0) bracketDepth++;
                        break;
                    case ']':
                        if (parenDepth == 0 && bracketDepth > 0) bracketDepth--;
                        break;
                    case '(':
                        parenDepth++;
                        break;
                    case ')':
                        if (parenDepth > 0) parenDepth--;
                        break;
                    case '{':
                        if (angleDepth == 0 && bracketDepth == 0 && parenDepth == 0)
                        {
                            openBrace = i;
                        }
                        break;
                }
                if (openBrace >= 0) break;
            }

            if (openBrace <= pos)
            {
                return false;
            }

            int closeBrace = CSharpLexer.FindMatchingBrace(declMasked, openBrace);
            if (closeBrace != declMasked.Length - 1)
            {
                return false;
            }

            int cursor = TrimRightIndex(declMasked, pos, openBrace);
            int nameStart = ReadIdentifierBackward(declMasked, pos, cursor);
            if (nameStart < 0 || nameStart >= cursor)
            {
                return false;
            }

            string parsedName = declStripped.Substring(nameStart, cursor - nameStart).Trim();
            string cleanParsedName = parsedName.TrimStart('@');
            if (!cleanParsedName.Equals(targetBaseMemberName, StringComparison.Ordinal))
            {
                return false;
            }

            string rawReturnType = declStripped.Substring(pos, nameStart - pos);
            string returnType = FormatReturnType(rawReturnType);
            if (string.IsNullOrWhiteSpace(returnType) || !IsBalancedTypeTokens(returnType))
            {
                return false;
            }

            string access = CSharpLexer.CollapseWhitespacePreservingLiterals(
                declStripped.Substring(openBrace + 1, closeBrace - openBrace - 1));

            bool hasGetter = Regex.IsMatch(access, @"\bget\b");
            bool hasSetter = Regex.IsMatch(access, @"\bset\b");
            bool hasInit = Regex.IsMatch(access, @"\binit\b");
            if (!hasGetter && !hasSetter && !hasInit)
            {
                return false;
            }

            if (typeParamMap != null && typeParamMap.Count > 0)
            {
                returnType = SubstituteTypeParameters(returnType, typeParamMap);
            }

            info = new InterfaceMemberInfo
            {
                InterfaceName = interfaceName,
                MemberName = parsedName,
                ReturnType = returnType,
                IsProperty = true,
                HasGetter = hasGetter,
                HasSetter = hasSetter || hasInit,
                FullSignature = $"{returnType} {parsedName} {{ {access} }}"
            };
            return true;
        }

        private static string FormatReturnType(string rawReturnType)
        {
            string collapsed = CSharpLexer.CollapseWhitespacePreservingLiterals(rawReturnType);
            if (string.IsNullOrEmpty(collapsed))
            {
                return collapsed;
            }

            var sb = new StringBuilder(collapsed.Length);
            for (int i = 0; i < collapsed.Length; i++)
            {
                char c = collapsed[i];
                if (c == ' ')
                {
                    char prev = sb.Length > 0 ? sb[sb.Length - 1] : '\0';
                    char next = i + 1 < collapsed.Length ? collapsed[i + 1] : '\0';
                    if (prev == '<' || prev == '(' || prev == '[' || prev == '.' ||
                        next == '>' || next == ')' || next == ']' || next == ',' || next == '[' || next == '?')
                    {
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool IsBalancedTypeTokens(string typeText)
        {
            int angle = 0;
            int bracket = 0;
            int paren = 0;

            for (int i = 0; i < typeText.Length; i++)
            {
                switch (typeText[i])
                {
                    case '<': angle++; break;
                    case '>':
                        angle--;
                        if (angle < 0) return false;
                        break;
                    case '[': bracket++; break;
                    case ']':
                        bracket--;
                        if (bracket < 0) return false;
                        break;
                    case '(': paren++; break;
                    case ')':
                        paren--;
                        if (paren < 0) return false;
                        break;
                    case ';':
                    case '{':
                    case '}':
                        return false;
                }
            }

            return angle == 0 && bracket == 0 && paren == 0;
        }

        private static int FindTopLevelArrowOrEnd(string masked, int start, int end)
        {
            int paren = 0;
            int bracket = 0;
            int angle = 0;

            for (int i = start; i < end - 1; i++)
            {
                char c = masked[i];
                if (c == '(') paren++;
                else if (c == ')' && paren > 0) paren--;
                else if (c == '[') bracket++;
                else if (c == ']' && bracket > 0) bracket--;
                else if (c == '<' && paren == 0) angle++;
                else if (c == '>' && paren == 0 && angle > 0) angle--;
                else if (c == '=' && masked[i + 1] == '>' && paren == 0 && bracket == 0 && angle == 0)
                {
                    return i;
                }
            }

            return end;
        }

        private static int TrimRightIndex(string text, int minIndex, int endExclusive)
        {
            while (endExclusive > minIndex && char.IsWhiteSpace(text[endExclusive - 1]))
            {
                endExclusive--;
            }
            return endExclusive;
        }

        private static int FindMatchingOpenAngle(string masked, int minIndex, int closeAngleIndex)
        {
            int depth = 0;
            for (int i = closeAngleIndex; i >= minIndex; i--)
            {
                if (masked[i] == '>') depth++;
                else if (masked[i] == '<')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static int ReadIdentifierBackward(string masked, int minIndex, int endExclusive)
        {
            int i = endExclusive;
            while (i > minIndex && (char.IsLetterOrDigit(masked[i - 1]) || masked[i - 1] == '_'))
            {
                i--;
            }
            if (i == endExclusive)
            {
                return -1;
            }
            if (i > minIndex && masked[i - 1] == '@')
            {
                i--;
            }
            return CSharpLexer.IsIdentifierStart(masked[i]) ? i : -1;
        }

        private static bool MatchesParameterTypes(string actualParamList, string expectedParamTypes)
        {
            if (string.IsNullOrWhiteSpace(expectedParamTypes) && string.IsNullOrWhiteSpace(actualParamList))
            {
                return true;
            }

            var expectedTypes = CSharpLexer.SplitTopLevelCommas(expectedParamTypes);
            var actualParams = CSharpLexer.SplitTopLevelCommas(actualParamList);

            if (expectedTypes.Count != actualParams.Count)
            {
                return false;
            }

            for (int i = 0; i < expectedTypes.Count; i++)
            {
                string expNorm = NormalizeTypeForComparison(expectedTypes[i]);
                string actRawType = ExtractParameterType(actualParams[i]);
                string actNorm = NormalizeTypeForComparison(actRawType);

                if (!expNorm.Equals(actNorm, StringComparison.OrdinalIgnoreCase) &&
                    !actualParams[i].Contains(CleanTypeName(expectedTypes[i]), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        internal static string ExtractParameterType(string paramDecl)
        {
            if (string.IsNullOrWhiteSpace(paramDecl))
            {
                return string.Empty;
            }

            string masked = CSharpLexer.MaskCommentsAndLiterals(paramDecl);

            // 1. Strip default value '= ...' at top level
            int angle = 0;
            int bracket = 0;
            int paren = 0;
            int eqIdx = -1;

            for (int i = 0; i < masked.Length; i++)
            {
                char c = masked[i];
                if (c == '<') angle++;
                else if (c == '>' && angle > 0) angle--;
                else if (c == '[') bracket++;
                else if (c == ']' && bracket > 0) bracket--;
                else if (c == '(') paren++;
                else if (c == ')' && paren > 0) paren--;
                else if (c == '=' && angle == 0 && bracket == 0 && paren == 0)
                {
                    eqIdx = i;
                    break;
                }
            }

            int end = eqIdx >= 0 ? eqIdx : paramDecl.Length;
            int pos = 0;

            // 2. Skip leading parameter attributes [...] and modifiers (ref, out, in, params, this, scoped)
            while (pos < end)
            {
                pos = CSharpLexer.SkipWhitespace(masked, pos, end);
                if (pos >= end) break;

                if (masked[pos] == '[')
                {
                    pos = CSharpLexer.SkipBalanced(masked, pos, end, '[', ']');
                    continue;
                }

                if (CSharpLexer.IsIdentifierStart(masked[pos]))
                {
                    int wEnd = CSharpLexer.ReadWord(masked, pos, end);
                    string word = masked.Substring(pos, wEnd - pos);
                    if (ParameterModifiers.Contains(word))
                    {
                        pos = wEnd;
                        continue;
                    }
                }

                break;
            }

            // 3. Remove trailing parameter identifier if followed by nothing else
            int trimmedEnd = TrimRightIndex(masked, pos, end);
            int paramNameStart = ReadIdentifierBackward(masked, pos, trimmedEnd);
            if (paramNameStart > pos)
            {
                return paramDecl.Substring(pos, paramNameStart - pos).Trim();
            }

            return paramDecl.Substring(pos, trimmedEnd - pos).Trim();
        }

        private static string NormalizeTypeForComparison(string typeText)
        {
            string s = CleanTypeName(typeText);
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            // Strip parameter modifiers if present in expected CS0535 type
            foreach (var mod in ParameterModifiers)
            {
                if (s.StartsWith(mod + " ", StringComparison.Ordinal))
                {
                    s = s.Substring(mod.Length + 1).TrimStart();
                }
            }

            // Normalize tuple element names recursively: (string Host, int Port)[] -> (string,int)[]
            s = StripTupleElementNames(s);

            // Remove all whitespace and '?' nullability markers for canonical comparison
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (!char.IsWhiteSpace(c) && c != '?')
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static string StripTupleElementNames(string typeText)
        {
            int openParen = typeText.IndexOf('(');
            if (openParen < 0)
            {
                return typeText;
            }

            var sb = new StringBuilder(typeText.Length);
            int i = 0;
            while (i < typeText.Length)
            {
                if (typeText[i] == '(')
                {
                    int closeParen = CSharpLexer.SkipBalanced(typeText, i, typeText.Length, '(', ')') - 1;
                    if (closeParen > i && closeParen < typeText.Length && typeText[closeParen] == ')')
                    {
                        string inner = typeText.Substring(i + 1, closeParen - i - 1);
                        var elements = CSharpLexer.SplitTopLevelCommas(inner);
                        var normalizedElements = new List<string>(elements.Count);
                        foreach (var elem in elements)
                        {
                            string elemType = ExtractTupleElementType(elem);
                            normalizedElements.Add(StripTupleElementNames(CleanTypeName(elemType)));
                        }
                        sb.Append('(').Append(string.Join(",", normalizedElements)).Append(')');
                        i = closeParen + 1;
                        continue;
                    }
                }

                sb.Append(typeText[i]);
                i++;
            }

            return sb.ToString();
        }

        private static string ExtractTupleElementType(string elem)
        {
            string trimmed = elem.Trim();
            if (string.IsNullOrEmpty(trimmed)) return trimmed;

            int nameStart = ReadIdentifierBackward(trimmed, 0, trimmed.Length);
            if (nameStart > 0)
            {
                string before = trimmed.Substring(0, nameStart).Trim();
                // Ensure 'before' is a non-empty valid type (e.g. "string" in "string Host", or "(int, int)" in "(int, int) Pair")
                if (before.Length > 0 && IsBalancedTypeTokens(before))
                {
                    return before;
                }
            }
            return trimmed;
        }

        private static string CleanTypeName(string typeName)
        {
            return typeName.Trim().TrimEnd('?').Replace("System.", "");
        }

        private static InterfaceMemberInfo FallbackMemberInfo(
            string interfaceName,
            string memberName,
            string expectedParamTypes,
            string? interfaceFile = null)
        {
            bool isAsync = memberName.EndsWith("Async", StringComparison.OrdinalIgnoreCase);
            string returnType = isAsync ? "Task" : "void";

            // Reconstruct parameter names from expected types using balanced comma splitting
            var paramsWithNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(expectedParamTypes))
            {
                var types = CSharpLexer.SplitTopLevelCommas(expectedParamTypes);
                int pNum = 1;
                foreach (var t in types)
                {
                    string cleanType = t.Trim();
                    if (cleanType.Equals("CancellationToken", StringComparison.OrdinalIgnoreCase))
                    {
                        paramsWithNames.Add("CancellationToken cancellationToken = default");
                    }
                    else
                    {
                        paramsWithNames.Add($"{cleanType} param{pNum++}");
                    }
                }
            }

            string paramList = string.Join(", ", paramsWithNames);
            return new InterfaceMemberInfo
            {
                InterfaceName = interfaceName,
                MemberName = memberName,
                ReturnType = returnType,
                ParameterList = paramList,
                IsProperty = false,
                FullSignature = $"{returnType} {memberName}({paramList})",
                InterfaceFilePath = interfaceFile ?? string.Empty
            };
        }

        private static (string BodyStripped, string BodyMasked, Dictionary<string, string>? TypeParamMap) ExtractInterfaceBodySlices(
            ScannedSource scanned,
            string interfaceName)
        {
            string shortName = GetShortInterfaceName(interfaceName);
            string stripped = scanned.CommentStripped;
            string masked = scanned.CommentsAndLiteralsMasked;

            if (string.IsNullOrEmpty(shortName))
            {
                return (stripped, masked, null);
            }

            var ifaceMatch = Regex.Match(masked, $@"\binterface\s+@?{Regex.Escape(shortName)}\b");
            if (!ifaceMatch.Success)
            {
                return (stripped, masked, null);
            }

            int afterName = ifaceMatch.Index + ifaceMatch.Length;
            afterName = CSharpLexer.SkipWhitespace(masked, afterName, masked.Length);

            List<string>? declaredTypeParams = null;
            if (afterName < masked.Length && masked[afterName] == '<')
            {
                int gtEnd = CSharpLexer.SkipBalanced(masked, afterName, masked.Length, '<', '>');
                if (gtEnd > afterName + 1)
                {
                    string rawParams = stripped.Substring(afterName + 1, gtEnd - afterName - 2);
                    declaredTypeParams = new List<string>();
                    foreach (var tp in CSharpLexer.SplitTopLevelCommas(rawParams))
                    {
                        // Strip variance modifiers 'in' / 'out'
                        string cleanTp = Regex.Replace(tp.Trim(), @"^(?:in|out)\s+", "");
                        if (cleanTp.Length > 0) declaredTypeParams.Add(cleanTp);
                    }
                }
            }

            // Find opening '{' of interface body at angle/paren/bracket depth 0
            int angleDepth = 0;
            int parenDepth = 0;
            int bracketDepth = 0;
            int openBrace = -1;

            for (int i = afterName; i < masked.Length; i++)
            {
                char c = masked[i];
                if (c == '<') angleDepth++;
                else if (c == '>' && angleDepth > 0) angleDepth--;
                else if (c == '(') parenDepth++;
                else if (c == ')' && parenDepth > 0) parenDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']' && bracketDepth > 0) bracketDepth--;
                else if (c == '{' && angleDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                {
                    openBrace = i;
                    break;
                }
            }

            if (openBrace < 0)
            {
                return (stripped, masked, null);
            }

            var typeParamMap = BuildInterfaceTypeArgumentMap(interfaceName, declaredTypeParams);

            int closeBrace = CSharpLexer.FindMatchingBrace(masked, openBrace);
            if (closeBrace > openBrace)
            {
                int len = closeBrace - openBrace - 1;
                return (stripped.Substring(openBrace + 1, len), masked.Substring(openBrace + 1, len), typeParamMap);
            }

            return (stripped.Substring(openBrace + 1), masked.Substring(openBrace + 1), typeParamMap);
        }

        private static Dictionary<string, string>? BuildInterfaceTypeArgumentMap(
            string interfaceName,
            List<string>? declaredTypeParams)
        {
            if (declaredTypeParams == null || declaredTypeParams.Count == 0)
            {
                return null;
            }

            // Extract last top-level <...> on interfaceName if present
            int lt = interfaceName.IndexOf('<');
            int gt = interfaceName.LastIndexOf('>');
            if (lt < 0 || gt <= lt)
            {
                return null;
            }

            var suppliedArgs = CSharpLexer.SplitTopLevelCommas(interfaceName.Substring(lt + 1, gt - lt - 1));
            if (suppliedArgs.Count != declaredTypeParams.Count)
            {
                return null;
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < declaredTypeParams.Count; i++)
            {
                if (!declaredTypeParams[i].Equals(suppliedArgs[i], StringComparison.Ordinal))
                {
                    map[declaredTypeParams[i]] = suppliedArgs[i];
                }
            }

            return map.Count > 0 ? map : null;
        }

        private static string SubstituteTypeParameters(string text, Dictionary<string, string> typeParamMap)
        {
            string result = text;
            foreach (var kvp in typeParamMap)
            {
                result = Regex.Replace(result, $@"\b{Regex.Escape(kvp.Key)}\b", kvp.Value);
            }
            return result;
        }
    }
}
