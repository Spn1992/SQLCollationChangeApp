using System;
using System.Collections.Generic;
using System.IO;
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
        private static readonly Regex Cs0535Regex = new Regex(
            @"^'(?<class>[^']+)'\s+does not implement interface member\s+'(?<interface>[^.]+)\.(?<member>[^'(]+)(?:\((?<params>.*)\))?'",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, string?> _filePathCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _rootSearchCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _fileContentCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (int Start, int Length)> _interfaceBodyOffsetsCache = new Dictionary<string, (int Start, int Length)>(StringComparer.OrdinalIgnoreCase);

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _filePathCache.Clear();
                _rootSearchCache.Clear();
                _fileContentCache.Clear();
                _interfaceBodyOffsetsCache.Clear();
            }
        }

        public static (string ClassName, string InterfaceName, string MemberName, string ExpectedParamTypes)? ParseCs0535Message(string message)
        {
            var match = Cs0535Regex.Match(message.Trim());
            if (!match.Success)
            {
                return null;
            }

            return (
                match.Groups["class"].Value.Trim(),
                match.Groups["interface"].Value.Trim(),
                match.Groups["member"].Value.Trim(),
                match.Groups["params"].Success ? match.Groups["params"].Value.Trim() : string.Empty
            );
        }

        public InterfaceMemberInfo? ResolveInterfaceMember(
            string interfaceName,
            string memberName,
            string expectedParamTypes,
            string contextFilePath,
            string? projectRoot = null)
        {
            string? interfaceFile = FindInterfaceFile(interfaceName, contextFilePath, projectRoot);
            if (interfaceFile == null)
            {
                // Fallback: construct signature directly from diagnostic error if interface file not found
                return FallbackMemberInfo(interfaceName, memberName, expectedParamTypes);
            }

            string? content = GetOrReadFileContent(interfaceFile);
            if (content == null)
            {
                return FallbackMemberInfo(interfaceName, memberName, expectedParamTypes, interfaceFile);
            }

            var bodyOffsets = GetOrComputeInterfaceBodyOffsets(interfaceFile, interfaceName, content);
            ReadOnlySpan<char> bodySpan = content.AsSpan(bodyOffsets.Start, bodyOffsets.Length);

            var memberInfo = ExtractMemberFromInterfaceBody(bodySpan, interfaceName, memberName, expectedParamTypes);
            if (memberInfo != null)
            {
                memberInfo.InterfaceFilePath = interfaceFile;
                return memberInfo;
            }

            return FallbackMemberInfo(interfaceName, memberName, expectedParamTypes, interfaceFile);
        }

        private (int Start, int Length) GetOrComputeInterfaceBodyOffsets(string filePath, string interfaceName, string content)
        {
            string cacheKey = $"{filePath}|{interfaceName}";
            lock (_cacheLock)
            {
                if (_interfaceBodyOffsetsCache.TryGetValue(cacheKey, out var cached) &&
                    cached.Start >= 0 &&
                    cached.Length >= 0 &&
                    cached.Start + cached.Length <= content.Length)
                {
                    return cached;
                }
            }

            var computed = FindInterfaceBodyOffsets(content.AsSpan(), interfaceName);
            lock (_cacheLock)
            {
                _interfaceBodyOffsetsCache[cacheKey] = computed;
            }

            return computed;
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
            string cacheKey = $"{interfaceName}|{contextFilePath}|{root}";

            lock (_cacheLock)
            {
                if (_filePathCache.TryGetValue(cacheKey, out var cachedPath))
                {
                    return cachedPath;
                }
            }

            string? resolvedPath = ResolveInterfaceFilePathInternal(interfaceName, contextFilePath, root);

            lock (_cacheLock)
            {
                _filePathCache[cacheKey] = resolvedPath;
            }

            return resolvedPath;
        }

        private string? ResolveInterfaceFilePathInternal(string interfaceName, string contextFilePath, string root)
        {
            string fileName = $"{interfaceName}.cs";

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
            if (string.IsNullOrEmpty(sourceCode) || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            var bodyOffsets = FindInterfaceBodyOffsets(sourceCode.AsSpan(), interfaceName);
            ReadOnlySpan<char> bodySpan = sourceCode.AsSpan(bodyOffsets.Start, bodyOffsets.Length);
            return ExtractMemberFromInterfaceBody(bodySpan, interfaceName, memberName, expectedParamTypes);
        }

        private static InterfaceMemberInfo? ExtractMemberFromInterfaceBody(
            ReadOnlySpan<char> bodySpan,
            string interfaceName,
            string memberName,
            string expectedParamTypes)
        {
            if (bodySpan.IsEmpty || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            ReadOnlySpan<char> targetName = memberName.AsSpan();
            InterfaceMemberInfo? bestMethodMatch = null;
            InterfaceMemberInfo? propertyMatch = null;
            int searchStart = 0;

            while (searchStart < bodySpan.Length)
            {
                int relIdx = bodySpan.Slice(searchStart).IndexOf(targetName, StringComparison.Ordinal);
                if (relIdx < 0)
                {
                    break;
                }

                int nameIdx = searchStart + relIdx;
                searchStart = nameIdx + 1;

                // Member declaration name must be preceded by whitespace
                if (nameIdx == 0 || !char.IsWhiteSpace(bodySpan[nameIdx - 1]))
                {
                    continue;
                }

                int afterName = nameIdx + targetName.Length;
                while (afterName < bodySpan.Length && char.IsWhiteSpace(bodySpan[afterName]))
                {
                    afterName++;
                }

                if (afterName >= bodySpan.Length)
                {
                    break;
                }

                char delimiter = bodySpan[afterName];
                if (delimiter == '(')
                {
                    int paramStart = afterName + 1;
                    int closeParenRel = bodySpan.Slice(paramStart).IndexOf(')');
                    if (closeParenRel < 0)
                    {
                        continue;
                    }

                    int closeParen = paramStart + closeParenRel;
                    int afterCloseParen = closeParen + 1;
                    while (afterCloseParen < bodySpan.Length && char.IsWhiteSpace(bodySpan[afterCloseParen]))
                    {
                        afterCloseParen++;
                    }

                    // Require a terminating semicolon token so default interface implementations are ignored
                    if (afterCloseParen >= bodySpan.Length || bodySpan[afterCloseParen] != ';')
                    {
                        continue;
                    }

                    if (!TryExtractReturnTypeSpan(bodySpan, nameIdx, out ReadOnlySpan<char> returnTypeSpan))
                    {
                        continue;
                    }

                    string returnType = NormalizeReturnType(returnTypeSpan);
                    string paramList = bodySpan.Slice(paramStart, closeParenRel).Trim().ToString();

                    var info = new InterfaceMemberInfo
                    {
                        InterfaceName = interfaceName,
                        MemberName = memberName,
                        ReturnType = returnType,
                        ParameterList = paramList,
                        IsProperty = false,
                        FullSignature = $"{returnType} {memberName}({paramList})"
                    };

                    // Check parameter type matching
                    if (MatchesParameterTypes(paramList, expectedParamTypes))
                    {
                        return info;
                    }

                    bestMethodMatch ??= info;
                    searchStart = afterCloseParen + 1;
                }
                else if (delimiter == '{' && propertyMatch == null)
                {
                    int accessStart = afterName + 1;
                    int closeBraceRel = bodySpan.Slice(accessStart).IndexOf('}');
                    if (closeBraceRel < 0)
                    {
                        continue;
                    }

                    if (!TryExtractReturnTypeSpan(bodySpan, nameIdx, out ReadOnlySpan<char> propReturnSpan))
                    {
                        continue;
                    }

                    string access = bodySpan.Slice(accessStart, closeBraceRel).Trim().ToString();
                    bool hasGetter = access.Contains("get", StringComparison.Ordinal);
                    bool hasSetter = access.Contains("set", StringComparison.Ordinal);
                    bool hasInit = access.Contains("init", StringComparison.Ordinal);
                    if (!hasGetter && !hasSetter && !hasInit)
                    {
                        continue;
                    }

                    string returnType = NormalizeReturnType(propReturnSpan);
                    propertyMatch = new InterfaceMemberInfo
                    {
                        InterfaceName = interfaceName,
                        MemberName = memberName,
                        ReturnType = returnType,
                        IsProperty = true,
                        HasGetter = hasGetter,
                        HasSetter = hasSetter,
                        FullSignature = $"{returnType} {memberName} {{ {access} }}"
                    };
                    searchStart = accessStart + closeBraceRel + 1;
                }
            }

            return bestMethodMatch ?? propertyMatch;
        }

        private static bool MatchesParameterTypes(string actualParamList, string expectedParamTypes)
        {
            if (string.IsNullOrWhiteSpace(expectedParamTypes) && string.IsNullOrWhiteSpace(actualParamList))
            {
                return true;
            }

            var expectedTypes = expectedParamTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var actualParams = actualParamList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (expectedTypes.Length != actualParams.Length)
            {
                return false;
            }

            for (int i = 0; i < expectedTypes.Length; i++)
            {
                string expType = CleanTypeName(expectedTypes[i]);
                string actParam = actualParams[i].Trim();
                // actual parameter includes type and name (e.g. "string databaseName" or "CancellationToken cancellationToken = default")
                string actType = actParam.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
                actType = CleanTypeName(actType);

                if (!expType.Equals(actType, StringComparison.OrdinalIgnoreCase) &&
                    !actParam.Contains(expType, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
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

            // Reconstruct parameter names from expected types
            var paramsWithNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(expectedParamTypes))
            {
                var types = expectedParamTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int pNum = 1;
                foreach (var t in types)
                {
                    string cleanType = t.Trim();
                    string pName = cleanType.ToLowerInvariant();
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

        private static (int Start, int Length) FindInterfaceBodyOffsets(ReadOnlySpan<char> source, string interfaceName)
        {
            if (source.IsEmpty || string.IsNullOrWhiteSpace(interfaceName))
            {
                return (0, source.Length);
            }

            ReadOnlySpan<char> keyword = "interface".AsSpan();
            ReadOnlySpan<char> targetInterface = interfaceName.AsSpan();
            int searchPos = 0;
            int ifaceDeclIndex = -1;

            while (searchPos < source.Length)
            {
                int rel = source.Slice(searchPos).IndexOf(keyword, StringComparison.Ordinal);
                if (rel < 0)
                {
                    break;
                }

                int kwStart = searchPos + rel;
                int kwEnd = kwStart + keyword.Length;
                searchPos = kwEnd;

                if (kwStart > 0 && IsWordChar(source[kwStart - 1]))
                {
                    continue;
                }

                if (kwEnd >= source.Length || !char.IsWhiteSpace(source[kwEnd]))
                {
                    continue;
                }

                int nameStart = kwEnd;
                while (nameStart < source.Length && char.IsWhiteSpace(source[nameStart]))
                {
                    nameStart++;
                }

                if (nameStart + targetInterface.Length > source.Length ||
                    !source.Slice(nameStart, targetInterface.Length).Equals(targetInterface, StringComparison.Ordinal))
                {
                    continue;
                }

                int nameEnd = nameStart + targetInterface.Length;
                if (nameEnd < source.Length && IsWordChar(source[nameEnd]))
                {
                    continue;
                }

                if (IsInSingleLineComment(source, kwStart))
                {
                    continue;
                }

                ifaceDeclIndex = nameEnd;
                break;
            }

            if (ifaceDeclIndex < 0)
            {
                return (0, source.Length);
            }

            int openBraceRel = source.Slice(ifaceDeclIndex).IndexOf('{');
            if (openBraceRel < 0)
            {
                return (0, source.Length);
            }

            int openBrace = ifaceDeclIndex + openBraceRel;
            int bodyStart = openBrace + 1;
            int depth = 1;

            for (int i = bodyStart; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return (bodyStart, i - bodyStart);
                    }
                }
            }

            return (bodyStart, source.Length - bodyStart);
        }

        internal static bool TryExtractReturnTypeSpan(
            ReadOnlySpan<char> source,
            int memberNameIndex,
            out ReadOnlySpan<char> returnTypeSpan)
        {
            returnTypeSpan = default;

            int i = memberNameIndex - 1;
            while (i >= 0 && (source[i] == ' ' || source[i] == '\t'))
            {
                i--;
            }

            if (i < 0 || source[i] == '\n' || source[i] == '\r')
            {
                return false;
            }

            int returnEnd = i + 1;
            int angleDepth = 0;
            int parenDepth = 0;
            bool hasIdentifier = false;

            while (i >= 0)
            {
                char c = source[i];

                if (c == '\n' || c == '\r')
                {
                    if (angleDepth == 0)
                    {
                        break;
                    }
                    i--;
                    continue;
                }

                if (c == ' ' || c == '\t')
                {
                    i--;
                    continue;
                }

                if (c == '>')
                {
                    // Reject '=>' or '->' arrow tokens
                    if (i > 0 && (source[i - 1] == '=' || source[i - 1] == '-'))
                    {
                        break;
                    }
                    angleDepth++;
                    i--;
                    continue;
                }

                if (c == '<')
                {
                    if (angleDepth == 0)
                    {
                        break;
                    }
                    angleDepth--;
                    i--;
                    continue;
                }

                if (c == ')' && angleDepth > 0)
                {
                    parenDepth++;
                    i--;
                    continue;
                }

                if (c == '(' && angleDepth > 0)
                {
                    if (parenDepth == 0)
                    {
                        break;
                    }
                    parenDepth--;
                    i--;
                    continue;
                }

                if (c == ']')
                {
                    int k = i - 1;
                    while (k >= 0 && (source[k] == ' ' || source[k] == '\t' || source[k] == ','))
                    {
                        k--;
                    }
                    if (k >= 0 && source[k] == '[')
                    {
                        i = k - 1;
                        continue;
                    }
                    break;
                }

                if (IsWordChar(c))
                {
                    hasIdentifier = true;
                    i--;
                    continue;
                }

                if (c == '?' || c == '.')
                {
                    i--;
                    continue;
                }

                if (c == ':' && i > 0 && source[i - 1] == ':')
                {
                    i -= 2;
                    continue;
                }

                if (c == ',' && (angleDepth > 0 || parenDepth > 0))
                {
                    i--;
                    continue;
                }

                break;
            }

            if (!hasIdentifier || angleDepth != 0 || parenDepth != 0)
            {
                return false;
            }

            int returnStart = i + 1;
            if (IsInSingleLineComment(source, returnStart))
            {
                return false;
            }

            ReadOnlySpan<char> candidate = source.Slice(returnStart, returnEnd - returnStart).Trim();
            candidate = StripLeadingModifiers(candidate);
            if (candidate.IsEmpty || !ContainsIdentifierStart(candidate) || IsNonTypeKeyword(candidate))
            {
                return false;
            }

            if (HasMultipleTopLevelWords(candidate))
            {
                return false;
            }

            returnTypeSpan = candidate;
            return true;
        }

        private static string NormalizeReturnType(ReadOnlySpan<char> returnTypeSpan)
        {
            if (returnTypeSpan.IndexOfAny('\r', '\n') < 0)
            {
                return returnTypeSpan.ToString();
            }

            var sb = new System.Text.StringBuilder(returnTypeSpan.Length);
            bool inWhitespace = false;
            for (int i = 0; i < returnTypeSpan.Length; i++)
            {
                char c = returnTypeSpan[i];
                if (char.IsWhiteSpace(c))
                {
                    inWhitespace = true;
                }
                else
                {
                    if (inWhitespace && sb.Length > 0)
                    {
                        char prev = sb[sb.Length - 1];
                        if (prev != '<' && prev != '(' && prev != '.' &&
                            c != '>' && c != ')' && c != ',' && c != '?' && c != '[')
                        {
                            sb.Append(' ');
                        }
                    }
                    sb.Append(c);
                    inWhitespace = false;
                }
            }

            return sb.ToString();
        }

        private static ReadOnlySpan<char> StripLeadingModifiers(ReadOnlySpan<char> span)
        {
            while (!span.IsEmpty)
            {
                int wordLen = 0;
                while (wordLen < span.Length && IsWordChar(span[wordLen]))
                {
                    wordLen++;
                }

                if (wordLen == 0 || wordLen >= span.Length || !char.IsWhiteSpace(span[wordLen]))
                {
                    break;
                }

                ReadOnlySpan<char> word = span.Slice(0, wordLen);
                if (!IsModifierKeyword(word))
                {
                    break;
                }

                span = span.Slice(wordLen).TrimStart();
            }

            return span;
        }

        private static bool IsModifierKeyword(ReadOnlySpan<char> word)
        {
            return word.Equals("public", StringComparison.Ordinal) ||
                   word.Equals("protected", StringComparison.Ordinal) ||
                   word.Equals("internal", StringComparison.Ordinal) ||
                   word.Equals("private", StringComparison.Ordinal) ||
                   word.Equals("virtual", StringComparison.Ordinal) ||
                   word.Equals("override", StringComparison.Ordinal) ||
                   word.Equals("abstract", StringComparison.Ordinal) ||
                   word.Equals("async", StringComparison.Ordinal) ||
                   word.Equals("static", StringComparison.Ordinal) ||
                   word.Equals("sealed", StringComparison.Ordinal) ||
                   word.Equals("new", StringComparison.Ordinal) ||
                   word.Equals("partial", StringComparison.Ordinal) ||
                   word.Equals("unsafe", StringComparison.Ordinal) ||
                   word.Equals("extern", StringComparison.Ordinal) ||
                   word.Equals("ref", StringComparison.Ordinal) ||
                   word.Equals("readonly", StringComparison.Ordinal) ||
                   word.Equals("scoped", StringComparison.Ordinal) ||
                   word.Equals("required", StringComparison.Ordinal);
        }

        private static bool IsNonTypeKeyword(ReadOnlySpan<char> word)
        {
            if (IsModifierKeyword(word))
            {
                return true;
            }

            return word.Equals("return", StringComparison.Ordinal) ||
                   word.Equals("await", StringComparison.Ordinal) ||
                   word.Equals("throw", StringComparison.Ordinal) ||
                   word.Equals("yield", StringComparison.Ordinal) ||
                   word.Equals("else", StringComparison.Ordinal) ||
                   word.Equals("case", StringComparison.Ordinal) ||
                   word.Equals("goto", StringComparison.Ordinal) ||
                   word.Equals("if", StringComparison.Ordinal) ||
                   word.Equals("while", StringComparison.Ordinal) ||
                   word.Equals("for", StringComparison.Ordinal) ||
                   word.Equals("foreach", StringComparison.Ordinal) ||
                   word.Equals("switch", StringComparison.Ordinal) ||
                   word.Equals("using", StringComparison.Ordinal) ||
                   word.Equals("lock", StringComparison.Ordinal) ||
                   word.Equals("catch", StringComparison.Ordinal) ||
                   word.Equals("when", StringComparison.Ordinal) ||
                   word.Equals("nameof", StringComparison.Ordinal) ||
                   word.Equals("typeof", StringComparison.Ordinal) ||
                   word.Equals("sizeof", StringComparison.Ordinal) ||
                   word.Equals("default", StringComparison.Ordinal) ||
                   word.Equals("base", StringComparison.Ordinal) ||
                   word.Equals("this", StringComparison.Ordinal) ||
                   word.Equals("var", StringComparison.Ordinal) ||
                   word.Equals("class", StringComparison.Ordinal) ||
                   word.Equals("struct", StringComparison.Ordinal) ||
                   word.Equals("interface", StringComparison.Ordinal) ||
                   word.Equals("enum", StringComparison.Ordinal) ||
                   word.Equals("record", StringComparison.Ordinal) ||
                   word.Equals("delegate", StringComparison.Ordinal) ||
                   word.Equals("namespace", StringComparison.Ordinal) ||
                   word.Equals("event", StringComparison.Ordinal);
        }

        private static bool ContainsIdentifierStart(ReadOnlySpan<char> span)
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = span[i];
                if (char.IsLetter(c) || c == '_')
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasMultipleTopLevelWords(ReadOnlySpan<char> candidate)
        {
            int angleDepth = 0;
            bool seenIdentifier = false;
            bool sawWhitespaceAfterIdentifier = false;

            for (int i = 0; i < candidate.Length; i++)
            {
                char c = candidate[i];
                if (c == '<')
                {
                    angleDepth++;
                    sawWhitespaceAfterIdentifier = false;
                }
                else if (c == '>')
                {
                    angleDepth--;
                    sawWhitespaceAfterIdentifier = false;
                }
                else if (angleDepth == 0)
                {
                    if (IsWordChar(c))
                    {
                        if (sawWhitespaceAfterIdentifier)
                        {
                            return true;
                        }
                        seenIdentifier = true;
                    }
                    else if (c == '.' || c == ':')
                    {
                        seenIdentifier = false;
                        sawWhitespaceAfterIdentifier = false;
                    }
                    else if (char.IsWhiteSpace(c))
                    {
                        if (seenIdentifier)
                        {
                            sawWhitespaceAfterIdentifier = true;
                        }
                    }
                }
            }

            return false;
        }

        internal static bool IsInSingleLineComment(ReadOnlySpan<char> source, int pos)
        {
            int lineStart = pos;
            while (lineStart > 0 && source[lineStart - 1] != '\n' && source[lineStart - 1] != '\r')
            {
                lineStart--;
            }

            bool inString = false;
            for (int i = lineStart; i < pos - 1; i++)
            {
                char c = source[i];
                if (c == '"' && (i == lineStart || source[i - 1] != '\\'))
                {
                    inString = !inString;
                }
                else if (!inString && c == '/' && source[i + 1] == '/')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }
    }
}
