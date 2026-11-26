using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MSBuildDiagnosticPatcher.Models;

namespace MSBuildDiagnosticPatcher.Patcher
{
    public class PatchGenerator
    {
        private readonly InterfaceDefinitionResolver _resolver;

        private static readonly HashSet<string> ValueTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "bool", "byte", "sbyte", "char", "decimal", "double", "float",
            "int", "uint", "long", "ulong", "short", "ushort", "nint", "nuint",
            "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "CancellationToken"
        };

        private static readonly HashSet<string> MemberModifierKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal", "static", "virtual",
            "override", "abstract", "sealed", "async", "extern", "unsafe",
            "new", "partial", "readonly", "volatile", "required"
        };

        private static readonly HashSet<string> NonReturnTypeKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal", "static", "virtual",
            "override", "abstract", "sealed", "async", "extern", "unsafe",
            "new", "partial", "readonly", "volatile", "required",
            "return", "throw", "await", "yield", "if", "else", "while", "for",
            "foreach", "switch", "case", "do", "using", "lock", "catch", "when",
            "where", "in", "out", "ref", "class", "struct", "interface", "record",
            "enum", "namespace", "event", "delegate"
        };

        internal readonly struct DiffLine : IEquatable<DiffLine>
        {
            public string Content { get; }
            public string Terminator { get; }

            public DiffLine(string content, string terminator)
            {
                Content = content;
                Terminator = terminator;
            }

            public bool Equals(DiffLine other) =>
                string.Equals(Content, other.Content, StringComparison.Ordinal) &&
                string.Equals(Terminator, other.Terminator, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is DiffLine other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Content, Terminator);
        }

        public PatchGenerator(InterfaceDefinitionResolver? resolver = null)
        {
            _resolver = resolver ?? new InterfaceDefinitionResolver();
        }

        public List<PatchCandidate> GeneratePatches(
            IEnumerable<CompilerDiagnostic> diagnostics,
            string? projectRoot = null)
        {
            var candidates = new List<PatchCandidate>();
            bool hasExplicitProjectRoot = !string.IsNullOrWhiteSpace(projectRoot);
            string root = hasExplicitProjectRoot ? Path.GetFullPath(projectRoot!) : Directory.GetCurrentDirectory();
            bool isCaseInsensitiveFs = OperatingSystem.IsWindows();

            var parsedEntries = new List<(
                string TargetFilePath,
                string ClassName,
                string InterfaceName,
                string MemberName,
                string ExpectedParamTypes,
                DiagnosticSeverity Severity,
                string ProjectPath)>();

            foreach (var diag in diagnostics)
            {
                if (!diag.Code.Equals("CS0535", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parsed = InterfaceDefinitionResolver.ParseCs0535Message(diag.Message);
                if (parsed == null)
                {
                    continue;
                }

                string targetFilePath = ResolveAbsoluteFilePath(diag.FilePath, root, diag.ProjectPath);
                parsedEntries.Add((
                    targetFilePath,
                    parsed.Value.ClassName,
                    parsed.Value.InterfaceName,
                    parsed.Value.MemberName,
                    parsed.Value.ExpectedParamTypes,
                    diag.Severity,
                    diag.ProjectPath));
            }

            var groupedByFileAndType = parsedEntries
                .GroupBy(e => (
                    FileKey: isCaseInsensitiveFs ? e.TargetFilePath.ToUpperInvariant() : e.TargetFilePath,
                    e.ClassName))
                .ToList();

            var typeCountPerFile = groupedByFileAndType
                .GroupBy(g => g.Key.FileKey)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            var usedPatchFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedByFileAndType)
            {
                string targetFilePath = group.First().TargetFilePath;
                string targetClassName = group.Key.ClassName;

                if (!File.Exists(targetFilePath))
                {
                    continue;
                }

                var (contentWithoutBom, _, hasUtf8Bom) = ReadFileWithEncoding(targetFilePath);
                string newline = DetectNewline(contentWithoutBom);
                var scannedClass = CSharpLexer.Scan(contentWithoutBom);

                var memberInfos = new List<InterfaceMemberInfo>();
                var seenMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var resolvedMemberKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var distinctInterfaces = new List<string>();
                int resolvedDiagnosticCount = 0;

                foreach (var entry in group)
                {
                    string memberKey = $"{targetClassName}|{entry.InterfaceName}.{entry.MemberName}({entry.ExpectedParamTypes})";
                    if (!seenMembers.Add(memberKey))
                    {
                        if (entry.Severity == DiagnosticSeverity.Error && resolvedMemberKeys.Contains(memberKey))
                        {
                            resolvedDiagnosticCount++;
                        }
                        continue;
                    }

                    if (!distinctInterfaces.Contains(entry.InterfaceName, StringComparer.Ordinal))
                    {
                        distinctInterfaces.Add(entry.InterfaceName);
                    }

                    string interfaceSearchRoot = hasExplicitProjectRoot
                        ? root
                        : FindRepoRoot(targetFilePath)
                            ?? ResolveProjectDirectory(entry.ProjectPath, root)
                            ?? root;

                    var member = _resolver.ResolveInterfaceMember(
                        entry.InterfaceName,
                        entry.MemberName,
                        entry.ExpectedParamTypes,
                        targetFilePath,
                        interfaceSearchRoot);

                    if (member != null)
                    {
                        memberInfos.Add(member);
                        resolvedMemberKeys.Add(memberKey);
                        if (entry.Severity == DiagnosticSeverity.Error)
                        {
                            resolvedDiagnosticCount++;
                        }
                    }
                }

                if (memberInfos.Count == 0)
                {
                    continue;
                }

                var overloadCache = new Dictionary<string, List<List<(string Type, string Name, bool HasDefault, string DefaultValue)>>>(StringComparer.Ordinal);
                var memberEntries = new List<PatchMemberEntry>(memberInfos.Count);

                foreach (var member in memberInfos)
                {
                    string rawStub = GenerateMemberStubCore(member, scannedClass, targetClassName, overloadCache, out bool omitVirtual).Trim('\r', '\n');
                    string normalizedStub = NormalizeNewlines(rawStub, newline);
                    string signature = BuildMemberSignature(member, omitVirtual);

                    memberEntries.Add(new PatchMemberEntry
                    {
                        MemberName = member.MemberName,
                        ReturnType = member.ReturnType,
                        ParameterList = member.ParameterList,
                        ParameterTypes = ExtractNormalizedParameterTypes(member.ParameterList),
                        IsProperty = member.IsProperty,
                        MemberSignature = signature,
                        GeneratedCode = normalizedStub
                    });
                }

                string stubsCode = string.Join(newline + newline, memberEntries.Select(m => m.GeneratedCode));
                string diffSourceContent = hasUtf8Bom ? "\uFEFF" + contentWithoutBom : contentWithoutBom;
                string interfaceSummary = string.Join(", ", distinctInterfaces);

                bool hasMultipleTypesInFile = typeCountPerFile.TryGetValue(group.Key.FileKey, out int count) && count > 1;
                string patchFileName = BuildUniquePatchFileName(targetFilePath, targetClassName, hasMultipleTypesInFile, usedPatchFileNames);

                var patchCandidate = CreatePatchForFile(
                    targetFilePath,
                    diffSourceContent,
                    stubsCode,
                    targetClassName,
                    interfaceSummary,
                    root,
                    memberEntries,
                    patchFileName);

                if (patchCandidate != null)
                {
                    patchCandidate.ResolvedErrorCount = resolvedDiagnosticCount;
                    candidates.Add(patchCandidate);
                }
            }

            return candidates;
        }

        private static string BuildUniquePatchFileName(
            string targetFilePath,
            string targetClassName,
            bool hasMultipleTypesInFile,
            HashSet<string> usedPatchFileNames)
        {
            string safeFileName = Path.GetFileNameWithoutExtension(targetFilePath);
            string sanitizedType = SanitizeFileNameSegment(targetClassName);

            string baseCandidate = (!hasMultipleTypesInFile || string.IsNullOrEmpty(sanitizedType))
                ? $"{safeFileName}.patch"
                : $"{safeFileName}.{sanitizedType}.patch";

            if (usedPatchFileNames.Add(baseCandidate))
            {
                return baseCandidate;
            }

            if (!string.IsNullOrEmpty(sanitizedType))
            {
                string typedCandidate = $"{safeFileName}.{sanitizedType}.patch";
                if (usedPatchFileNames.Add(typedCandidate))
                {
                    return typedCandidate;
                }
            }

            int suffix = 2;
            string stem = Path.GetFileNameWithoutExtension(baseCandidate);
            while (true)
            {
                string numbered = $"{stem}.{suffix}.patch";
                if (usedPatchFileNames.Add(numbered))
                {
                    return numbered;
                }
                suffix++;
            }
        }

        private static string SanitizeFileNameSegment(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            return sb.ToString().Trim('.', '_');
        }

        public string GenerateMemberStub(InterfaceMemberInfo member, string classSource, string? targetClassName = null)
        {
            var scannedClass = CSharpLexer.Scan(classSource);
            return GenerateMemberStubCore(member, scannedClass, targetClassName, overloadCache: null, out _);
        }

        private static string GenerateMemberStubCore(
            InterfaceMemberInfo member,
            ScannedSource scannedClass,
            string? targetClassName,
            Dictionary<string, List<List<(string Type, string Name, bool HasDefault, string DefaultValue)>>>? overloadCache,
            out bool omitVirtual)
        {
            var sb = new StringBuilder();
            omitVirtual = false;

            int scopeStart = 0;
            int scopeEnd = scannedClass.CommentsAndLiteralsMasked.Length;

            if (CSharpLexer.TryFindTypeScopeFromMasked(scannedClass.CommentsAndLiteralsMasked, targetClassName, out var scope))
            {
                omitVirtual = scope.IsSealed || scope.IsStruct;
                if (!string.IsNullOrWhiteSpace(targetClassName) && scope.CloseBraceIndex > scope.OpenBraceIndex)
                {
                    scopeStart = scope.OpenBraceIndex + 1;
                    scopeEnd = scope.CloseBraceIndex;
                }
            }

            string modifierPrefix = omitVirtual ? "public" : "public virtual";

            if (member.IsProperty)
            {
                sb.AppendLine($"        {modifierPrefix} {member.ReturnType} {member.MemberName}");
                sb.AppendLine("        {");
                if (member.HasGetter)
                {
                    sb.AppendLine("            get => throw new NotImplementedException();");
                }
                if (member.HasSetter)
                {
                    sb.AppendLine("            set => throw new NotImplementedException();");
                }
                sb.AppendLine("        }");
                return sb.ToString();
            }

            string? delegationCall = FindDelegationCall(member, scannedClass, scopeStart, scopeEnd, overloadCache);

            sb.AppendLine($"        {modifierPrefix} {member.ReturnType} {member.MemberName}({member.ParameterList})");
            sb.AppendLine("        {");

            if (delegationCall != null)
            {
                if (member.ReturnType.Equals("void", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"            {delegationCall};");
                }
                else
                {
                    sb.AppendLine($"            return {delegationCall};");
                }
            }
            else
            {
                sb.AppendLine("            throw new NotImplementedException();");
            }

            sb.AppendLine("        }");
            return sb.ToString();
        }

        private static string BuildMemberSignature(InterfaceMemberInfo member, bool omitVirtual)
        {
            string modifierPrefix = omitVirtual ? "public" : "public virtual";
            if (member.IsProperty)
            {
                return $"{modifierPrefix} {member.ReturnType} {member.MemberName}";
            }
            return $"{modifierPrefix} {member.ReturnType} {member.MemberName}({member.ParameterList})";
        }

        private static string? FindDelegationCall(
            InterfaceMemberInfo member,
            ScannedSource scannedClass,
            int scopeStart,
            int scopeEnd,
            Dictionary<string, List<List<(string Type, string Name, bool HasDefault, string DefaultValue)>>>? overloadCache)
        {
            string baseName = CSharpLexer.StripGenericArity(member.MemberName).TrimStart('@');
            if (string.IsNullOrEmpty(baseName))
            {
                return null;
            }

            List<List<(string Type, string Name, bool HasDefault, string DefaultValue)>>? overloads = null;
            if (overloadCache == null || !overloadCache.TryGetValue(baseName, out overloads))
            {
                overloads = ExtractMethodOverloadParameters(scannedClass, scopeStart, scopeEnd, baseName);
                if (overloadCache != null)
                {
                    overloadCache[baseName] = overloads;
                }
            }

            if (overloads.Count == 0)
            {
                return null;
            }

            var myParamNames = ExtractParameterNames(member.ParameterList);

            foreach (var existingParams in overloads)
            {
                if (existingParams.Count > myParamNames.Count)
                {
                    var callArgs = new List<string>(existingParams.Count);
                    bool canDelegate = true;

                    for (int i = 0; i < existingParams.Count; i++)
                    {
                        var (pType, pName, hasDefault, defaultValue) = existingParams[i];

                        var matchMyParam = myParamNames.FirstOrDefault(p =>
                            p.Equals(pName, StringComparison.OrdinalIgnoreCase) ||
                            (p.EndsWith("token", StringComparison.OrdinalIgnoreCase) && pName.EndsWith("token", StringComparison.OrdinalIgnoreCase)) ||
                            (p.EndsWith("directory", StringComparison.OrdinalIgnoreCase) && pName.EndsWith("directory", StringComparison.OrdinalIgnoreCase)) ||
                            (p.EndsWith("database", StringComparison.OrdinalIgnoreCase) && pName.EndsWith("database", StringComparison.OrdinalIgnoreCase)));

                        if (matchMyParam != null)
                        {
                            callArgs.Add(matchMyParam);
                        }
                        else if (hasDefault)
                        {
                            callArgs.Add(!string.IsNullOrWhiteSpace(defaultValue) ? defaultValue : "default");
                        }
                        else if (pType.EndsWith("?", StringComparison.Ordinal))
                        {
                            callArgs.Add("null");
                        }
                        else
                        {
                            canDelegate = false;
                            break;
                        }
                    }

                    if (canDelegate)
                    {
                        return $"{member.MemberName}({string.Join(", ", callArgs)})";
                    }
                }
            }

            return null;
        }

        private static List<List<(string Type, string Name, bool HasDefault, string DefaultValue)>> ExtractMethodOverloadParameters(
            ScannedSource scannedClass,
            int scopeStart,
            int scopeEnd,
            string baseMemberName)
        {
            var results = new List<List<(string Type, string Name, bool HasDefault, string DefaultValue)>>();
            string masked = scannedClass.CommentsAndLiteralsMasked;
            string stripped = scannedClass.CommentStripped;
            ReadOnlySpan<char> maskedSpan = masked.AsSpan();
            ReadOnlySpan<char> targetName = baseMemberName.AsSpan();
            int searchStart = scopeStart;

            while (searchStart < scopeEnd)
            {
                int relIdx = maskedSpan.Slice(searchStart, scopeEnd - searchStart).IndexOf(targetName, StringComparison.Ordinal);
                if (relIdx < 0)
                {
                    break;
                }

                int nameIdx = searchStart + relIdx;
                searchStart = nameIdx + 1;

                if (nameIdx <= scopeStart || !char.IsWhiteSpace(masked[nameIdx - 1]))
                {
                    continue;
                }

                int afterName = nameIdx + targetName.Length;
                if (afterName < scopeEnd && (char.IsLetterOrDigit(masked[afterName]) || masked[afterName] == '_'))
                {
                    continue;
                }

                afterName = CSharpLexer.SkipWhitespace(masked, afterName, scopeEnd);
                if (afterName < scopeEnd && masked[afterName] == '<')
                {
                    afterName = CSharpLexer.SkipBalanced(masked, afterName, scopeEnd, '<', '>');
                    afterName = CSharpLexer.SkipWhitespace(masked, afterName, scopeEnd);
                }

                if (afterName >= scopeEnd || masked[afterName] != '(')
                {
                    continue;
                }

                if (!HasPrecedingReturnType(masked, scopeStart, nameIdx))
                {
                    continue;
                }

                int afterCloseParen = CSharpLexer.SkipBalanced(masked, afterName, scopeEnd, '(', ')');
                if (afterCloseParen <= afterName + 1 || afterCloseParen > scopeEnd || masked[afterCloseParen - 1] != ')')
                {
                    continue;
                }

                int closeParen = afterCloseParen - 1;
                if (!HasMethodBodyOpenerAfterParens(masked, afterCloseParen, scopeEnd))
                {
                    continue;
                }

                string existingParamList = stripped.Substring(afterName + 1, closeParen - afterName - 1).Trim();
                results.Add(ParseParameters(existingParamList));
                searchStart = afterCloseParen;
            }

            return results;
        }

        private static bool HasPrecedingReturnType(string masked, int minIndex, int nameIdx)
        {
            int i = nameIdx - 1;
            while (i >= minIndex && char.IsWhiteSpace(masked[i]))
            {
                i--;
            }

            if (i < minIndex)
            {
                return false;
            }

            // Unwrap trailing '?' nullability markers
            while (i >= minIndex && masked[i] == '?')
            {
                i--;
                while (i >= minIndex && char.IsWhiteSpace(masked[i]))
                {
                    i--;
                }
            }

            if (i < minIndex)
            {
                return false;
            }

            char endChar = masked[i];

            // Reject '=>' or '->' arrows immediately before method name
            if (endChar == '>')
            {
                if (i > minIndex && (masked[i - 1] == '=' || masked[i - 1] == '-'))
                {
                    return false;
                }

                int openAngle = FindMatchingOpenAngleInRange(masked, minIndex, i);
                return openAngle > minIndex;
            }

            if (endChar == ']')
            {
                int openBracket = FindMatchingOpenBracketInRange(masked, minIndex, i);
                return openBracket > minIndex;
            }

            if (endChar == ')')
            {
                int openParen = FindMatchingOpenParenInRange(masked, minIndex, i);
                return openParen >= minIndex;
            }

            if (char.IsLetterOrDigit(endChar) || endChar == '_')
            {
                int wordStart = i;
                while (wordStart > minIndex && (char.IsLetterOrDigit(masked[wordStart - 1]) || masked[wordStart - 1] == '_'))
                {
                    wordStart--;
                }

                string word = masked.Substring(wordStart, i - wordStart + 1);
                return !NonReturnTypeKeywords.Contains(word);
            }

            return false;
        }

        private static bool HasMethodBodyOpenerAfterParens(string masked, int pos, int scopeEnd)
        {
            int cur = CSharpLexer.SkipWhitespace(masked, pos, scopeEnd);
            if (cur >= scopeEnd)
            {
                return false;
            }

            if (masked[cur] == '{')
            {
                return true;
            }

            if (masked[cur] == '=' && cur + 1 < scopeEnd && masked[cur + 1] == '>')
            {
                return true;
            }

            // Allow optional generic 'where T : ...' constraints before '{' or '=>'
            if (cur + 5 <= scopeEnd &&
                masked.AsSpan(cur, 5).Equals("where".AsSpan(), StringComparison.Ordinal) &&
                (cur + 5 == scopeEnd || char.IsWhiteSpace(masked[cur + 5])))
            {
                int i = cur + 5;
                int parenDepth = 0;
                int angleDepth = 0;
                while (i < scopeEnd)
                {
                    char c = masked[i];
                    if (c == '(') parenDepth++;
                    else if (c == ')' && parenDepth > 0) parenDepth--;
                    else if (c == '<' && parenDepth == 0) angleDepth++;
                    else if (c == '>' && parenDepth == 0 && angleDepth > 0) angleDepth--;
                    else if (parenDepth == 0 && angleDepth == 0)
                    {
                        if (c == '{') return true;
                        if (c == '=' && i + 1 < scopeEnd && masked[i + 1] == '>') return true;
                        if (c == ';') return false;
                    }
                    i++;
                }
            }

            return false;
        }

        private static int FindMatchingOpenAngleInRange(string text, int minIndex, int closeIndex)
        {
            int depth = 0;
            for (int i = closeIndex; i >= minIndex; i--)
            {
                if (text[i] == '>')
                {
                    if (i > minIndex && (text[i - 1] == '=' || text[i - 1] == '-'))
                    {
                        return -1;
                    }
                    depth++;
                }
                else if (text[i] == '<')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static int FindMatchingOpenBracketInRange(string text, int minIndex, int closeIndex)
        {
            int depth = 0;
            for (int i = closeIndex; i >= minIndex; i--)
            {
                if (text[i] == ']') depth++;
                else if (text[i] == '[')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static int FindMatchingOpenParenInRange(string text, int minIndex, int closeIndex)
        {
            int depth = 0;
            for (int i = closeIndex; i >= minIndex; i--)
            {
                if (text[i] == ')') depth++;
                else if (text[i] == '(')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static List<string> ExtractParameterNames(string? paramList)
        {
            return ParseParameters(paramList)
                .Select(p => p.Name)
                .ToList();
        }

        private static List<(string Type, string Name, bool HasDefault, string DefaultValue)> ParseParameters(string? paramList)
        {
            var result = new List<(string, string, bool, string)>();
            if (string.IsNullOrWhiteSpace(paramList))
            {
                return result;
            }

            var parts = CSharpLexer.SplitTopLevelCommas(paramList!);
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                string clean = StripTopLevelDefaultValue(part, out bool hasDefault, out string defVal);
                if (string.IsNullOrWhiteSpace(clean))
                {
                    continue;
                }

                int end = clean.Length;
                int nameStart = end;
                while (nameStart > 0 && (char.IsLetterOrDigit(clean[nameStart - 1]) || clean[nameStart - 1] == '_'))
                {
                    nameStart--;
                }
                if (nameStart < end && nameStart > 0 && clean[nameStart - 1] == '@')
                {
                    nameStart--;
                }

                if (nameStart > 0 && nameStart < end)
                {
                    string name = clean.Substring(nameStart).Trim();
                    string rawType = clean.Substring(0, nameStart);
                    string collapsedType = CollapseWhitespace(rawType);
                    if (!string.IsNullOrEmpty(collapsedType) && !string.IsNullOrEmpty(name))
                    {
                        result.Add((collapsedType, name, hasDefault, defVal));
                    }
                }
            }

            return result;
        }

        private static string StripTopLevelDefaultValue(string paramText, out bool hasDefault, out string defaultValue)
        {
            hasDefault = false;
            defaultValue = string.Empty;

            string masked = CSharpLexer.MaskCommentsAndLiterals(paramText);
            int angle = 0;
            int bracket = 0;
            int paren = 0;

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
                    hasDefault = true;
                    defaultValue = paramText.Substring(i + 1).Trim();
                    return paramText.Substring(0, i).Trim();
                }
            }

            return paramText.Trim();
        }

        public PatchCandidate? CreatePatchForFile(
            string targetFilePath,
            string originalContent,
            string stubsCode,
            string className,
            string interfaceName,
            string projectRoot,
            IReadOnlyList<PatchMemberEntry>? members = null,
            string? patchFileNameOverride = null)
        {
            string effectiveOriginal = originalContent;
            if (!effectiveOriginal.StartsWith('\uFEFF') && HasUtf8BomOnDisk(targetFilePath))
            {
                effectiveOriginal = "\uFEFF" + effectiveOriginal;
            }

            int insertPos = FindClassInsertionPoint(effectiveOriginal, className);
            if (insertPos < 0)
            {
                return null;
            }

            string newline = DetectNewline(effectiveOriginal);
            string normalizedStubs = NormalizeNewlines(stubsCode.Trim('\r', '\n'), newline);
            string newContent = SpliceCodeAtInsertionPoint(effectiveOriginal, insertPos, normalizedStubs, newline);

            string baseDir = FindRepoRoot(targetFilePath) ?? projectRoot;
            string relPath = GetRelativePath(baseDir, targetFilePath).Replace('\\', '/');

            string unifiedDiff = GenerateUnifiedDiff(
                effectiveOriginal,
                newContent,
                $"a/{relPath}",
                $"b/{relPath}");

            string safeFileName = Path.GetFileNameWithoutExtension(targetFilePath);
            string patchFileName = string.IsNullOrWhiteSpace(patchFileNameOverride)
                ? $"{safeFileName}.patch"
                : patchFileNameOverride!;

            List<PatchMemberEntry> resolvedMembers = members != null && members.Count > 0
                ? members.Select(m => new PatchMemberEntry
                {
                    MemberName = m.MemberName,
                    ReturnType = m.ReturnType,
                    ParameterList = m.ParameterList,
                    ParameterTypes = new List<string>(m.ParameterTypes),
                    IsProperty = m.IsProperty,
                    MemberSignature = m.MemberSignature,
                    GeneratedCode = NormalizeNewlines(m.GeneratedCode.Trim('\r', '\n'), newline)
                }).ToList()
                : ParseMemberEntriesFromCode(normalizedStubs, null);

            string combinedSignature = resolvedMembers.Count > 0
                ? string.Join("; ", resolvedMembers
                    .Select(m => m.MemberSignature)
                    .Where(s => !string.IsNullOrWhiteSpace(s)))
                : string.Empty;

            return new PatchCandidate
            {
                TargetFilePath = targetFilePath,
                DiagnosticCode = "CS0535",
                InterfaceName = interfaceName,
                TargetTypeName = className,
                MemberSignature = combinedSignature,
                GeneratedCode = normalizedStubs,
                Members = resolvedMembers,
                UnifiedDiff = unifiedDiff,
                PatchFileName = patchFileName,
                Description = $"Implement missing interface members of '{interfaceName}' in '{className}'"
            };
        }

        internal static string SpliceCodeAtInsertionPoint(
            string source,
            int insertPos,
            string normalizedStubs,
            string newline)
        {
            return source.Insert(insertPos, newline + normalizedStubs + newline);
        }

        public static int FindClassInsertionPoint(string source, string className)
        {
            if (string.IsNullOrEmpty(source))
            {
                return -1;
            }

            string masked = CSharpLexer.MaskCommentsAndLiterals(source);

            if (!string.IsNullOrWhiteSpace(className) &&
                CSharpLexer.TryFindTypeScopeFromMasked(masked, className, out var scope))
            {
                return ResolveClosingBraceInsertionIndex(source, scope.OpenBraceIndex, scope.CloseBraceIndex);
            }

            int fallbackBrace = masked.LastIndexOf('}');
            if (fallbackBrace < 0)
            {
                return -1;
            }

            return ResolveClosingBraceInsertionIndex(source, -1, fallbackBrace);
        }

        internal static int FindMatchingClosingBrace(string maskedSource, int openBrace)
        {
            return CSharpLexer.FindMatchingBrace(maskedSource, openBrace);
        }

        private static int ResolveClosingBraceInsertionIndex(string source, int openBrace, int closingBrace)
        {
            int lineBreakPos = -1;
            for (int i = closingBrace - 1; i >= 0; i--)
            {
                if (source[i] == '\n' || source[i] == '\r')
                {
                    lineBreakPos = i;
                    break;
                }
            }

            if (lineBreakPos >= 0)
            {
                int lineStart = lineBreakPos + 1;
                if (lineStart > openBrace && IsHorizontalWhitespaceOnly(source, lineStart, closingBrace))
                {
                    return lineStart;
                }
            }

            return closingBrace;
        }

        private static bool IsHorizontalWhitespaceOnly(string text, int start, int endExclusive)
        {
            for (int i = start; i < endExclusive; i++)
            {
                if (text[i] != ' ' && text[i] != '\t')
                {
                    return false;
                }
            }
            return true;
        }

        public static string GenerateUnifiedDiff(
            string oldText,
            string newText,
            string oldHeader,
            string newHeader)
        {
            var oldLines = SplitLinesWithTerminators(oldText);
            var newLines = SplitLinesWithTerminators(newText);

            int startCommon = 0;
            while (startCommon < oldLines.Count && startCommon < newLines.Count &&
                   oldLines[startCommon].Equals(newLines[startCommon]))
            {
                startCommon++;
            }

            var sb = new StringBuilder();
            sb.Append("--- ").Append(oldHeader).Append('\n');
            sb.Append("+++ ").Append(newHeader).Append('\n');

            if (startCommon == oldLines.Count && startCommon == newLines.Count)
            {
                return sb.ToString();
            }

            int endCommonOld = oldLines.Count - 1;
            int endCommonNew = newLines.Count - 1;
            while (endCommonOld >= startCommon && endCommonNew >= startCommon &&
                   oldLines[endCommonOld].Equals(newLines[endCommonNew]))
            {
                endCommonOld--;
                endCommonNew--;
            }

            int contextBefore = Math.Max(0, startCommon - 3);
            int contextAfterOld = Math.Min(oldLines.Count, endCommonOld + 4);
            int contextAfterNew = Math.Min(newLines.Count, endCommonNew + 4);

            int hunkOldCount = contextAfterOld - contextBefore;
            int hunkNewCount = contextAfterNew - contextBefore;
            int hunkOldStart = hunkOldCount == 0 ? 0 : contextBefore + 1;
            int hunkNewStart = hunkNewCount == 0 ? 0 : contextBefore + 1;

            sb.Append($"@@ -{hunkOldStart},{hunkOldCount} +{hunkNewStart},{hunkNewCount} @@\n");

            for (int i = contextBefore; i < startCommon; i++)
            {
                AppendDiffLine(sb, ' ', oldLines[i]);
            }

            for (int i = startCommon; i <= endCommonOld; i++)
            {
                AppendDiffLine(sb, '-', oldLines[i]);
            }

            for (int i = startCommon; i <= endCommonNew; i++)
            {
                AppendDiffLine(sb, '+', newLines[i]);
            }

            for (int i = endCommonOld + 1; i < contextAfterOld; i++)
            {
                AppendDiffLine(sb, ' ', oldLines[i]);
            }

            return sb.ToString();
        }

        private static void AppendDiffLine(StringBuilder sb, char prefix, DiffLine line)
        {
            sb.Append(prefix);
            sb.Append(line.Content);
            if (line.Terminator.Length > 0)
            {
                sb.Append(line.Terminator);
            }
            else
            {
                sb.Append('\n');
                sb.Append("\\ No newline at end of file\n");
            }
        }

        internal static List<DiffLine> SplitLinesWithTerminators(string text)
        {
            var lines = new List<DiffLine>();
            if (string.IsNullOrEmpty(text))
            {
                return lines;
            }

            int i = 0;
            int n = text.Length;
            while (i < n)
            {
                int lineStart = i;
                while (i < n && text[i] != '\r' && text[i] != '\n')
                {
                    i++;
                }

                string content = text.Substring(lineStart, i - lineStart);
                string terminator;
                if (i < n)
                {
                    if (text[i] == '\r')
                    {
                        if (i + 1 < n && text[i + 1] == '\n')
                        {
                            terminator = "\r\n";
                            i += 2;
                        }
                        else
                        {
                            terminator = "\r";
                            i += 1;
                        }
                    }
                    else
                    {
                        terminator = "\n";
                        i += 1;
                    }
                }
                else
                {
                    terminator = string.Empty;
                }

                lines.Add(new DiffLine(content, terminator));
            }

            return lines;
        }

        internal static (string ContentWithoutBom, Encoding Encoding, bool HasUtf8Bom) ReadFileWithEncoding(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);

            // UTF-8 BOM: EF BB BF
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                return (utf8Bom.GetString(bytes, 3, bytes.Length - 3), utf8Bom, true);
            }

            // UTF-32 LE BOM: FF FE 00 00
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                var utf32Le = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
                return (utf32Le.GetString(bytes, 4, bytes.Length - 4), utf32Le, false);
            }

            // UTF-32 BE BOM: 00 00 FE FF
            if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            {
                var utf32Be = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
                return (utf32Be.GetString(bytes, 4, bytes.Length - 4), utf32Be, false);
            }

            // UTF-16 LE BOM: FF FE
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                var utf16Le = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
                return (utf16Le.GetString(bytes, 2, bytes.Length - 2), utf16Le, false);
            }

            // UTF-16 BE BOM: FE FF
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                var utf16Be = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
                return (utf16Be.GetString(bytes, 2, bytes.Length - 2), utf16Be, false);
            }

            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return (utf8NoBom.GetString(bytes), utf8NoBom, false);
        }

        private static bool HasUtf8BomOnDisk(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    return false;
                }

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < 3)
                {
                    return false;
                }

                int b0 = fs.ReadByte();
                int b1 = fs.ReadByte();
                int b2 = fs.ReadByte();
                return b0 == 0xEF && b1 == 0xBB && b2 == 0xBF;
            }
            catch
            {
                return false;
            }
        }

        internal static string DetectNewline(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "\n";
            }

            int crlfCount = 0;
            int lfOnlyCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        crlfCount++;
                        i++;
                    }
                }
                else if (text[i] == '\n')
                {
                    lfOnlyCount++;
                }
            }

            return crlfCount > 0 && crlfCount >= lfOnlyCount ? "\r\n" : "\n";
        }

        internal static string NormalizeNewlines(string text, string newline)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(text.Length + 16);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    sb.Append(newline);
                }
                else if (text[i] == '\n')
                {
                    sb.Append(newline);
                }
                else
                {
                    sb.Append(text[i]);
                }
            }
            return sb.ToString();
        }

        internal static string MaskCommentsAndStrings(string source)
        {
            return CSharpLexer.MaskCommentsAndLiterals(source);
        }

        internal static List<PatchMemberEntry> ParseMemberEntriesFromCode(string generatedCode, string? fallbackSignature)
        {
            var entries = new List<PatchMemberEntry>();
            if (string.IsNullOrWhiteSpace(generatedCode))
            {
                if (!string.IsNullOrWhiteSpace(fallbackSignature))
                {
                    var sigEntry = CreateEntryFromSignatureOnly(fallbackSignature!);
                    if (sigEntry != null)
                    {
                        entries.Add(sigEntry);
                    }
                }
                return entries;
            }

            string masked = MaskCommentsAndStrings(generatedCode);
            int n = masked.Length;
            int blockStart = 0;
            int braceDepth = 0;
            int parenDepth = 0;
            int headerEnd = -1;

            for (int i = 0; i < n; i++)
            {
                char c = masked[i];
                if (braceDepth == 0)
                {
                    if (c == '(') parenDepth++;
                    else if (c == ')' && parenDepth > 0) parenDepth--;
                    else if (c == '{')
                    {
                        headerEnd = i;
                        braceDepth = 1;
                    }
                    else if (parenDepth == 0 && c == ';')
                    {
                        string rawBlock = TrimBlankOuterLines(generatedCode.Substring(blockStart, i - blockStart + 1));
                        if (!string.IsNullOrWhiteSpace(rawBlock))
                        {
                            string headerText = masked.Substring(blockStart, i - blockStart);
                            int arrowIdx = FindArrowOutsideParens(headerText);
                            if (arrowIdx >= 0)
                            {
                                headerText = headerText.Substring(0, arrowIdx);
                            }
                            entries.Add(BuildEntryFromHeaderAndBlock(headerText, rawBlock));
                        }
                        blockStart = i + 1;
                        headerEnd = -1;
                    }
                }
                else
                {
                    if (c == '{') braceDepth++;
                    else if (c == '}')
                    {
                        braceDepth--;
                        if (braceDepth == 0)
                        {
                            string rawBlock = TrimBlankOuterLines(generatedCode.Substring(blockStart, i - blockStart + 1));
                            string headerText = headerEnd >= blockStart
                                ? masked.Substring(blockStart, headerEnd - blockStart)
                                : string.Empty;
                            if (!string.IsNullOrWhiteSpace(rawBlock))
                            {
                                entries.Add(BuildEntryFromHeaderAndBlock(headerText, rawBlock));
                            }
                            blockStart = i + 1;
                            headerEnd = -1;
                        }
                    }
                }
            }

            if (entries.Count == 0)
            {
                string trimmed = TrimBlankOuterLines(generatedCode);
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    var fallbackEntry = !string.IsNullOrWhiteSpace(fallbackSignature)
                        ? BuildEntryFromHeaderAndBlock(MaskCommentsAndStrings(fallbackSignature!), trimmed)
                        : new PatchMemberEntry { GeneratedCode = trimmed };
                    if (!string.IsNullOrWhiteSpace(fallbackSignature))
                    {
                        fallbackEntry.MemberSignature = fallbackSignature!.Trim();
                    }
                    entries.Add(fallbackEntry);
                }
            }
            else if (entries.Count == 1 && !string.IsNullOrWhiteSpace(fallbackSignature))
            {
                if (string.IsNullOrWhiteSpace(entries[0].MemberSignature))
                {
                    entries[0].MemberSignature = fallbackSignature!.Trim();
                }
                if (string.IsNullOrWhiteSpace(entries[0].MemberName))
                {
                    var parsedSig = TryParseMemberHeader(MaskCommentsAndStrings(fallbackSignature!));
                    if (parsedSig != null)
                    {
                        entries[0].MemberName = parsedSig.Value.MemberName;
                        entries[0].IsProperty = parsedSig.Value.IsProperty;
                        entries[0].ParameterList = parsedSig.Value.RawParamList;
                        entries[0].ParameterTypes = parsedSig.Value.NormalizedParamTypes;
                    }
                }
            }

            return entries;
        }

        private static PatchMemberEntry? CreateEntryFromSignatureOnly(string signature)
        {
            var parsed = TryParseMemberHeader(MaskCommentsAndStrings(signature));
            return new PatchMemberEntry
            {
                MemberName = parsed?.MemberName ?? string.Empty,
                ParameterList = parsed?.RawParamList ?? string.Empty,
                ParameterTypes = parsed?.NormalizedParamTypes ?? new List<string>(),
                IsProperty = parsed?.IsProperty ?? false,
                MemberSignature = signature.Trim(),
                GeneratedCode = string.Empty
            };
        }

        private static PatchMemberEntry BuildEntryFromHeaderAndBlock(string maskedHeader, string rawBlock)
        {
            string cleanHeader = CollapseWhitespace(StripLeadingAttributes(maskedHeader));
            var parsed = TryParseMemberHeader(maskedHeader);
            return new PatchMemberEntry
            {
                MemberName = parsed?.MemberName ?? string.Empty,
                ReturnType = parsed?.ReturnType ?? string.Empty,
                ParameterList = parsed?.RawParamList ?? string.Empty,
                ParameterTypes = parsed?.NormalizedParamTypes ?? new List<string>(),
                IsProperty = parsed?.IsProperty ?? false,
                MemberSignature = cleanHeader,
                GeneratedCode = rawBlock
            };
        }

        internal static (string MemberName, string ReturnType, bool IsProperty, string RawParamList, List<string> NormalizedParamTypes)?
            TryParseMemberHeader(string maskedHeader)
        {
            string clean = StripLeadingAttributes(maskedHeader).Trim();
            if (string.IsNullOrWhiteSpace(clean))
            {
                return null;
            }

            if (Regex.IsMatch(clean, @"\b(?:class|struct|interface|record|enum|event|delegate|namespace)\b"))
            {
                return null;
            }

            clean = StripTrailingGenericConstraints(clean).Trim();

            if (clean.EndsWith(")", StringComparison.Ordinal))
            {
                int openParen = FindMatchingOpenParen(clean, clean.Length - 1);
                if (openParen < 0)
                {
                    return null;
                }

                string beforeParen = clean.Substring(0, openParen).Trim();
                if (beforeParen.Contains('='))
                {
                    return null;
                }

                string rawParams = clean.Substring(openParen + 1, clean.Length - openParen - 2).Trim();

                string genericAritySuffix = string.Empty;
                if (beforeParen.EndsWith(">", StringComparison.Ordinal))
                {
                    int openAngle = FindMatchingOpenAngle(beforeParen, beforeParen.Length - 1);
                    if (openAngle > 0)
                    {
                        string typeParams = beforeParen.Substring(openAngle + 1, beforeParen.Length - openAngle - 2);
                        int arity = SplitTopLevelParameters(typeParams).Count;
                        genericAritySuffix = $"`{arity}";
                        beforeParen = beforeParen.Substring(0, openAngle).Trim();
                    }
                }

                var (returnType, rawMemberName) = SplitReturnTypeAndMemberName(beforeParen);
                if (string.IsNullOrWhiteSpace(rawMemberName) || string.IsNullOrWhiteSpace(returnType))
                {
                    return null;
                }

                string simpleMemberName = ExtractUnqualifiedMemberName(rawMemberName) + genericAritySuffix;
                var normalizedParamTypes = ExtractNormalizedParameterTypes(rawParams);

                return (simpleMemberName, returnType, false, rawParams, normalizedParamTypes);
            }
            else
            {
                if (clean.Contains('=') || clean.Contains('['))
                {
                    if (Regex.IsMatch(clean, @"\bthis\s*\["))
                    {
                        return null;
                    }
                    if (clean.Contains('='))
                    {
                        return null;
                    }
                }

                var (returnType, rawMemberName) = SplitReturnTypeAndMemberName(clean);
                if (string.IsNullOrWhiteSpace(rawMemberName) || string.IsNullOrWhiteSpace(returnType))
                {
                    return null;
                }

                string simpleMemberName = ExtractUnqualifiedMemberName(rawMemberName);
                return (simpleMemberName, returnType, true, string.Empty, new List<string>());
            }
        }

        private static (string ReturnType, string MemberName) SplitReturnTypeAndMemberName(string declarationBeforeParams)
        {
            string trimmed = declarationBeforeParams.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return (string.Empty, string.Empty);
            }

            int end = trimmed.Length;
            int pos = end - 1;
            while (pos >= 0 && (char.IsLetterOrDigit(trimmed[pos]) || trimmed[pos] == '_' || trimmed[pos] == '.' || trimmed[pos] == '<' || trimmed[pos] == '>' || trimmed[pos] == ',' || trimmed[pos] == ' '))
            {
                if (trimmed[pos] == ' ' || trimmed[pos] == '>' || trimmed[pos] == '<' || trimmed[pos] == ',')
                {
                    break;
                }
                pos--;
            }

            while (pos >= 0 && trimmed[pos] == '>' && pos + 1 < trimmed.Length && trimmed[pos + 1] == '.')
            {
                int openAngle = FindMatchingOpenAngle(trimmed, pos);
                if (openAngle <= 0) break;
                pos = openAngle - 1;
                while (pos >= 0 && (char.IsLetterOrDigit(trimmed[pos]) || trimmed[pos] == '_' || trimmed[pos] == '.'))
                {
                    pos--;
                }
            }

            int nameStart = pos + 1;
            if (nameStart <= 0 || nameStart >= end)
            {
                return (string.Empty, string.Empty);
            }

            string memberName = trimmed.Substring(nameStart).Trim();
            string prefix = trimmed.Substring(0, nameStart).Trim();

            string returnType = StripLeadingModifiers(prefix);
            if (string.IsNullOrWhiteSpace(returnType))
            {
                return (string.Empty, string.Empty);
            }

            return (CollapseWhitespace(returnType), memberName);
        }

        private static string ExtractUnqualifiedMemberName(string rawName)
        {
            int lastDot = rawName.LastIndexOf('.');
            return lastDot >= 0 ? rawName.Substring(lastDot + 1).Trim() : rawName.Trim();
        }

        private static string StripLeadingModifiers(string text)
        {
            int i = 0;
            int n = text.Length;
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(text[i])) i++;
                int wordStart = i;
                while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                if (i == wordStart)
                {
                    return text.Substring(wordStart).Trim();
                }

                string word = text.Substring(wordStart, i - wordStart);
                if (MemberModifierKeywords.Contains(word) && i < n && char.IsWhiteSpace(text[i]))
                {
                    continue;
                }

                return text.Substring(wordStart).Trim();
            }
            return string.Empty;
        }

        internal static string StripLeadingAttributes(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                return string.Empty;
            }

            int i = 0;
            int n = header.Length;
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(header[i])) i++;
                if (i < n && header[i] == '[')
                {
                    int depth = 1;
                    int j = i + 1;
                    bool hasLetter = false;
                    while (j < n && depth > 0)
                    {
                        if (char.IsLetter(header[j])) hasLetter = true;
                        if (header[j] == '[') depth++;
                        else if (header[j] == ']') depth--;
                        j++;
                    }
                    if (depth == 0 && hasLetter)
                    {
                        i = j;
                        continue;
                    }
                }
                break;
            }

            return header.Substring(i);
        }

        private static string StripTrailingGenericConstraints(string header)
        {
            int closeParen = header.LastIndexOf(')');
            if (closeParen < 0 || closeParen + 1 >= header.Length)
            {
                return header;
            }

            string afterParen = header.Substring(closeParen + 1);
            var whereMatch = Regex.Match(afterParen, @"^\s+where\s+\w+\s*:");
            if (whereMatch.Success)
            {
                return header.Substring(0, closeParen + 1);
            }
            return header;
        }

        private static int FindMatchingOpenParen(string text, int closeParenIndex)
        {
            int depth = 0;
            for (int i = closeParenIndex; i >= 0; i--)
            {
                if (text[i] == ')') depth++;
                else if (text[i] == '(')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static int FindMatchingOpenAngle(string text, int closeAngleIndex)
        {
            int depth = 0;
            for (int i = closeAngleIndex; i >= 0; i--)
            {
                if (text[i] == '>') depth++;
                else if (text[i] == '<')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        internal static int FindArrowOutsideParens(string text)
        {
            int parenDepth = 0;
            int angleDepth = 0;
            for (int i = 0; i < text.Length - 1; i++)
            {
                char c = text[i];
                if (c == '(') parenDepth++;
                else if (c == ')' && parenDepth > 0) parenDepth--;
                else if (c == '<') angleDepth++;
                else if (c == '>' && angleDepth > 0) angleDepth--;
                else if (parenDepth == 0 && angleDepth == 0 && c == '=' && text[i + 1] == '>')
                {
                    return i;
                }
            }
            return -1;
        }

        internal static List<string> ExtractNormalizedParameterTypes(string paramList)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(paramList))
            {
                return result;
            }

            var parts = SplitTopLevelParameters(paramList);
            foreach (var rawPart in parts)
            {
                string part = StripLeadingAttributes(rawPart);
                part = StripTopLevelDefaultValue(part);
                part = CollapseWhitespace(part);
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                bool isByRef = false;
                while (true)
                {
                    if (part.StartsWith("this ", StringComparison.Ordinal))
                    {
                        part = part.Substring(5).TrimStart();
                    }
                    else if (part.StartsWith("params ", StringComparison.Ordinal))
                    {
                        part = part.Substring(7).TrimStart();
                    }
                    else if (part.StartsWith("scoped ", StringComparison.Ordinal))
                    {
                        part = part.Substring(7).TrimStart();
                    }
                    else if (part.StartsWith("ref readonly ", StringComparison.Ordinal))
                    {
                        isByRef = true;
                        part = part.Substring(13).TrimStart();
                    }
                    else if (part.StartsWith("ref ", StringComparison.Ordinal))
                    {
                        isByRef = true;
                        part = part.Substring(4).TrimStart();
                    }
                    else if (part.StartsWith("out ", StringComparison.Ordinal))
                    {
                        isByRef = true;
                        part = part.Substring(4).TrimStart();
                    }
                    else if (part.StartsWith("in ", StringComparison.Ordinal))
                    {
                        isByRef = true;
                        part = part.Substring(3).TrimStart();
                    }
                    else
                    {
                        break;
                    }
                }

                string rawType = ExtractTypeFromParameterDeclaration(part);
                string normalizedType = NormalizeTypeName(rawType);
                result.Add(isByRef ? "byref:" + normalizedType : normalizedType);
            }

            return result;
        }

        private static string ExtractTypeFromParameterDeclaration(string paramWithoutModifiersOrDefault)
        {
            string s = paramWithoutModifiersOrDefault.Trim();
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            int end = s.Length - 1;
            if (char.IsLetterOrDigit(s[end]) || s[end] == '_')
            {
                int idStart = end;
                while (idStart >= 0 && (char.IsLetterOrDigit(s[idStart]) || s[idStart] == '_' || s[idStart] == '@'))
                {
                    idStart--;
                }

                string beforeId = s.Substring(0, idStart + 1).Trim();
                if (!string.IsNullOrEmpty(beforeId) &&
                    !beforeId.EndsWith(".", StringComparison.Ordinal) &&
                    !beforeId.EndsWith("::", StringComparison.Ordinal))
                {
                    return beforeId;
                }
            }

            return s;
        }

        internal static string NormalizeTypeName(string rawType)
        {
            if (string.IsNullOrWhiteSpace(rawType))
            {
                return string.Empty;
            }

            string t = CollapseWhitespace(rawType);
            if (t.StartsWith("global::", StringComparison.Ordinal))
            {
                t = t.Substring(8).TrimStart();
            }

            if (t.EndsWith("?", StringComparison.Ordinal))
            {
                string withoutQ = t.Substring(0, t.Length - 1).TrimEnd();
                string normBase = NormalizeTypeName(withoutQ);
                return IsKnownValueType(normBase) ? normBase + "?" : normBase;
            }

            if (t.EndsWith("]", StringComparison.Ordinal))
            {
                int openBracket = t.LastIndexOf('[');
                if (openBracket > 0)
                {
                    string elemType = t.Substring(0, openBracket).TrimEnd();
                    string rank = Regex.Replace(t.Substring(openBracket), @"\s+", "");
                    return NormalizeTypeName(elemType) + rank;
                }
            }

            if (t.StartsWith("(", StringComparison.Ordinal) && t.EndsWith(")", StringComparison.Ordinal) &&
                FindMatchingOpenParen(t, t.Length - 1) == 0)
            {
                string inner = t.Substring(1, t.Length - 2);
                var elems = SplitTopLevelParameters(inner)
                    .Select(e => NormalizeTypeName(ExtractTypeFromParameterDeclaration(e)));
                return "(" + string.Join(",", elems) + ")";
            }

            if (t.EndsWith(">", StringComparison.Ordinal))
            {
                int openAngle = FindMatchingOpenAngle(t, t.Length - 1);
                if (openAngle > 0)
                {
                    string outerRaw = t.Substring(0, openAngle).Trim();
                    string outerSimple = StripNamespaceAndMapBuiltin(outerRaw);
                    string innerRaw = t.Substring(openAngle + 1, t.Length - openAngle - 2);
                    var normArgs = SplitTopLevelParameters(innerRaw).Select(NormalizeTypeName).ToList();

                    if (outerSimple == "Nullable" && normArgs.Count == 1)
                    {
                        return normArgs[0].EndsWith("?", StringComparison.Ordinal) ? normArgs[0] : normArgs[0] + "?";
                    }

                    return $"{outerSimple}<{string.Join(",", normArgs)}>";
                }
            }

            return StripNamespaceAndMapBuiltin(t);
        }

        private static bool IsKnownValueType(string normalizedBaseType)
        {
            if (normalizedBaseType.StartsWith("(", StringComparison.Ordinal) && normalizedBaseType.EndsWith(")", StringComparison.Ordinal))
            {
                return true;
            }
            return ValueTypeNames.Contains(normalizedBaseType);
        }

        private static string StripNamespaceAndMapBuiltin(string typeName)
        {
            string clean = Regex.Replace(typeName, @"\s+", "");
            if (clean.StartsWith("global::", StringComparison.Ordinal))
            {
                clean = clean.Substring(8);
            }

            int lastDot = clean.LastIndexOf('.');
            string simple = lastDot >= 0 ? clean.Substring(lastDot + 1) : clean;

            return simple switch
            {
                "Boolean" => "bool",
                "Byte" => "byte",
                "SByte" => "sbyte",
                "Char" => "char",
                "Decimal" => "decimal",
                "Double" => "double",
                "Single" => "float",
                "Int32" => "int",
                "UInt32" => "uint",
                "Int64" => "long",
                "UInt64" => "ulong",
                "Int16" => "short",
                "UInt16" => "ushort",
                "IntPtr" => "nint",
                "UIntPtr" => "nuint",
                "Object" => "object",
                "String" => "string",
                "Void" => "void",
                _ => simple
            };
        }

        internal static List<string> SplitTopLevelParameters(string list)
        {
            return CSharpLexer.SplitTopLevelCommas(list);
        }

        private static string StripTopLevelDefaultValue(string param)
        {
            return StripTopLevelDefaultValue(param, out _, out _);
        }

        internal static string CollapseWhitespace(string text)
        {
            return Regex.Replace(text.Trim(), @"\s+", " ");
        }

        private static string TrimBlankOuterLines(string block)
        {
            if (string.IsNullOrEmpty(block))
            {
                return string.Empty;
            }

            string trimmedEnd = block.TrimEnd();
            int lineStart = 0;
            int i = 0;
            while (i < trimmedEnd.Length)
            {
                if (trimmedEnd[i] == ' ' || trimmedEnd[i] == '\t')
                {
                    i++;
                }
                else if (trimmedEnd[i] == '\r')
                {
                    i += (i + 1 < trimmedEnd.Length && trimmedEnd[i + 1] == '\n') ? 2 : 1;
                    lineStart = i;
                }
                else if (trimmedEnd[i] == '\n')
                {
                    i++;
                    lineStart = i;
                }
                else
                {
                    break;
                }
            }

            return trimmedEnd.Substring(lineStart);
        }

        private static string ResolveAbsoluteFilePath(string path, string root, string? projectPath = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string normalizedPath = NormalizeRelativePath(path);
            string? projectDir = ResolveProjectDirectory(projectPath, root);
            if (!string.IsNullOrEmpty(projectDir))
            {
                try
                {
                    string projectCandidate = Path.GetFullPath(Path.Combine(projectDir, normalizedPath));
                    if (File.Exists(projectCandidate))
                    {
                        return projectCandidate;
                    }
                }
                catch
                {
                    // Fall back to root below
                }
            }

            return Path.GetFullPath(Path.Combine(root, normalizedPath));
        }

        private static string? ResolveProjectDirectory(string? projectPath, string root)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return null;
            }

            try
            {
                string resolvedProjectPath = Path.IsPathRooted(projectPath)
                    ? Path.GetFullPath(projectPath)
                    : Path.GetFullPath(Path.Combine(root, NormalizeRelativePath(projectPath)));

                if (Directory.Exists(resolvedProjectPath))
                {
                    return resolvedProjectPath;
                }

                return Path.GetDirectoryName(resolvedProjectPath);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeRelativePath(string path)
        {
            if (!OperatingSystem.IsWindows() &&
                path.Contains('\\') &&
                !Regex.IsMatch(path, @"^[a-zA-Z]:[\\/]"))
            {
                return path.Replace('\\', Path.DirectorySeparatorChar);
            }

            return path;
        }

        private static string GetRelativePath(string relativeTo, string path)
        {
            try
            {
                return Path.GetRelativePath(relativeTo, path);
            }
            catch
            {
                return path;
            }
        }

        private static string? FindRepoRoot(string filePath)
        {
            try
            {
                string? dir = Path.GetDirectoryName(filePath);
                while (!string.IsNullOrEmpty(dir))
                {
                    string gitPath = Path.Combine(dir, ".git");
                    if (Directory.Exists(gitPath) || File.Exists(gitPath))
                    {
                        return dir;
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch
            {
                // Fallback to null
            }
            return null;
        }
    }
}
