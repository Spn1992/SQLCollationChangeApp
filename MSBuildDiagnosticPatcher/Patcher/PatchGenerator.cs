using System;
using System.Buffers;
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
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

            // Parse and group CS0535 diagnostics by (targetFilePath, ClassName)
            var parsedEntries = new List<(string TargetFilePath, string ClassName, string InterfaceName, string MemberName, string ExpectedParamTypes, DiagnosticSeverity Severity)>();

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

                string targetFilePath = ResolveAbsoluteFilePath(diag.FilePath, root);
                parsedEntries.Add((
                    targetFilePath,
                    parsed.Value.ClassName,
                    parsed.Value.InterfaceName,
                    parsed.Value.MemberName,
                    parsed.Value.ExpectedParamTypes,
                    diag.Severity));
            }

            var groupedByFileAndType = parsedEntries
                .GroupBy(e => (FileKey: e.TargetFilePath.ToUpperInvariant(), e.ClassName))
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

                string originalContent = File.ReadAllText(targetFilePath);
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
                            ?? ResolveProjectDirectory(diag.ProjectPath, root)
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

                var stubsBuilder = new StringBuilder();
                var overloadCache = new Dictionary<string, List<List<(string Type, string Name, bool HasDefault, string DefaultValue)>>>(StringComparer.Ordinal);
                foreach (var member in memberInfos)
                {
                    string stub = GenerateMemberStub(member, originalContent, targetClassName);
                    stubsBuilder.AppendLine(stub);
                }

                string stubsCode = stubsBuilder.ToString().TrimEnd();
                string interfaceSummary = string.Join(", ", distinctInterfaces);

                bool hasMultipleTypesInFile = typeCountPerFile.TryGetValue(group.Key.FileKey, out int count) && count > 1;
                string patchFileName = BuildUniquePatchFileName(targetFilePath, targetClassName, hasMultipleTypesInFile, usedPatchFileNames);

                var patchCandidate = CreatePatchForFile(
                    targetFilePath,
                    originalContent,
                    stubsCode,
                    targetClassName,
                    interfaceSummary,
                    root,
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
            return GenerateMemberStub(member, classSource, overloadCache: null);
        }

        private string GenerateMemberStub(
            InterfaceMemberInfo member,
            string classSource,
            Dictionary<string, List<List<(string Type, string Name, bool HasDefault, string DefaultValue)>>>? overloadCache)
        {
            var sb = new StringBuilder();

            bool omitVirtual = false;
            string searchScopeSource = CSharpLexer.StripComments(classSource);

            if (CSharpLexer.TryFindTypeScope(classSource, targetClassName, out var scope))
            {
                omitVirtual = scope.IsSealed || scope.IsStruct;
                if (!string.IsNullOrWhiteSpace(targetClassName) && scope.CloseBraceIndex > scope.OpenBraceIndex)
                {
                    searchScopeSource = searchScopeSource.Substring(
                        scope.OpenBraceIndex + 1,
                        scope.CloseBraceIndex - scope.OpenBraceIndex - 1);
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

            // Check if target class has an existing overload of the same method
            string? delegationCall = FindDelegationCall(member, searchScopeSource);

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

        private static string? FindDelegationCall(InterfaceMemberInfo member, string strippedClassSource)
        {
            var pattern = new Regex(
                $@"\b(?:public|protected|internal|private)?\s*(?:async\s+)?(?:virtual\s+)?[\w\<\>\?,\.\s\[\]\(\)]+\s+{Regex.Escape(member.MemberName)}\s*\((?<params>[^;{{}}]*?)\)\s*(?:\{{|=>)");

            var matches = pattern.Matches(strippedClassSource);
            var myParamNames = ExtractParameterNames(member.ParameterList);

            if (overloads.Count == 0)
            {
                return null;
            }

            var myParamNames = ExtractParameterNames(member.ParameterList);

                if (existingParams.Count > myParamNames.Count)
                {
                    var callArgs = new List<string>();
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

        private static List<string> ExtractParameterNames(string paramList)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(paramList)) return result;

            var parts = CSharpLexer.SplitTopLevelCommas(paramList);
            foreach (var part in parts)
            {
                string clean = StripTopLevelDefaultValue(part, out _, out _);
                string typePart = InterfaceDefinitionResolver.ExtractParameterType(clean);
                if (!string.IsNullOrEmpty(typePart) && clean.Length > typePart.Length)
                {
                    int idx = clean.LastIndexOf(' ');
                    if (idx >= 0 && idx + 1 < clean.Length)
                    {
                        result.Add(clean.Substring(idx + 1).Trim());
                        continue;
                    }
                }

                var tokens = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2)
                {
                    result.Add(tokens[tokens.Length - 1]);
                }
            }
            return result;
        }

        private static List<(string Type, string Name, bool HasDefault, string DefaultValue)> ParseParameters(string paramList)
        {
            var result = new List<(string, string, bool, string)>();
            if (string.IsNullOrWhiteSpace(paramList)) return result;

            var parts = CSharpLexer.SplitTopLevelCommas(paramList);
            foreach (var part in parts)
            {
                string clean = StripTopLevelDefaultValue(part, out bool hasDefault, out string defVal);
                string extractedType = InterfaceDefinitionResolver.ExtractParameterType(clean);
                if (!string.IsNullOrEmpty(extractedType) && clean.Length > extractedType.Length)
                {
                    int lastSpace = clean.LastIndexOf(' ');
                    if (lastSpace > 0 && lastSpace + 1 < clean.Length)
                    {
                        string name = clean.Substring(lastSpace + 1).Trim();
                        result.Add((extractedType, name, hasDefault, defVal));
                        continue;
                    }
                }

                var tokens = clean.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2)
                {
                    string name = tokens[tokens.Length - 1];
                    string type = string.Join(" ", tokens.Take(tokens.Length - 1));
                    result.Add((type, name, hasDefault, defVal));
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
            string? patchFileNameOverride = null)
        {
            int insertPos = FindClassInsertionPoint(originalContent, className);
            if (insertPos < 0)
            {
                return null;
            }

            // Format relative path for diff headers (prefer git root if found)
            string baseDir = FindRepoRoot(targetFilePath) ?? projectRoot;
            string relPath = GetRelativePath(baseDir, targetFilePath).Replace('\\', '/');
            string oldHeader = $"a/{relPath}";
            string newHeader = $"b/{relPath}";

            ReadOnlySpan<char> originalSpan = originalContent.AsSpan();
            ReadOnlySpan<char> stubsSpan = stubsCode.AsSpan();

            // Keep any preceding '\r' inside the local window so CRLF / CR breaks stay intact
            int searchBackPos = insertPos > 0 && originalSpan[insertPos - 1] == '\r'
                ? insertPos - 1
                : insertPos;

            int windowStart = FindWindowStart(originalSpan, searchBackPos, 4);
            int windowEnd = FindWindowEnd(originalSpan, searchBackPos, 5);
            int baseLineIndex = windowStart > 0 ? CountLineBreaks(originalSpan.Slice(0, windowStart)) : 0;

            if (!BuildWindowUnifiedDiff(
                    originalSpan,
                    stubsSpan,
                    insertPos,
                    windowStart,
                    windowEnd,
                    oldHeader,
                    newHeader,
                    baseLineIndex,
                    requireThreeSuffixMatches: windowEnd < originalSpan.Length,
                    out string unifiedDiff))
            {
                BuildWindowUnifiedDiff(
                    originalSpan,
                    stubsSpan,
                    insertPos,
                    windowStart,
                    originalSpan.Length,
                    oldHeader,
                    newHeader,
                    baseLineIndex,
                    requireThreeSuffixMatches: false,
                    out unifiedDiff);
            }

            string safeFileName = Path.GetFileNameWithoutExtension(targetFilePath);
            string patchFileName = string.IsNullOrWhiteSpace(patchFileNameOverride)
                ? $"{safeFileName}.patch"
                : patchFileNameOverride!;

            return new PatchCandidate
            {
                TargetFilePath = targetFilePath,
                DiagnosticCode = "CS0535",
                InterfaceName = interfaceName,
                TargetTypeName = className,
                GeneratedCode = stubsCode,
                UnifiedDiff = unifiedDiff,
                PatchFileName = patchFileName,
                Description = $"Implement missing interface members of '{interfaceName}' in '{className}'"
            };
        }

        public static int FindClassInsertionPoint(string source, string className)
        {
            if (string.IsNullOrEmpty(source))
            {
                return -1;
            }

            var scanned = CSharpLexer.Scan(source);
            string masked = scanned.CommentsAndLiteralsMasked;

            if (string.IsNullOrWhiteSpace(className))
            {
                return masked.LastIndexOf('}');
            }

            if (CSharpLexer.TryFindTypeScopeFromMasked(masked, className, out var scope))
            {
                int openBrace = scope.OpenBraceIndex;
                int closeBrace = scope.CloseBraceIndex;

                int lineStart = source.LastIndexOf('\n', closeBrace);
                if (lineStart > openBrace && IsWhitespaceOnly(source, lineStart + 1, closeBrace))
                {
                    return lineStart;
                }

                return closeBrace;
            }

            return -1;
        }

        private static bool IsWhitespaceOnly(string text, int start, int endExclusive)
        {
            for (int i = start; i < endExclusive; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
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
            TryGenerateUnifiedDiff(
                oldText.AsSpan(),
                newText.AsSpan(),
                oldHeader,
                newHeader,
                baseLineIndex: 0,
                requireThreeSuffixMatches: false,
                out string unifiedDiff);
            return unifiedDiff;
        }

        private readonly struct LineSlice
        {
            public readonly int Start;
            public readonly int Length;

            int startCommon = 0;
            while (startCommon < oldLines.Length && startCommon < newLines.Length &&
                   oldLines[startCommon] == newLines[startCommon])
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }

        private static bool TryGenerateUnifiedDiff(
            ReadOnlySpan<char> oldSpan,
            ReadOnlySpan<char> newSpan,
            string oldHeader,
            string newHeader,
            int baseLineIndex,
            bool requireThreeSuffixMatches,
            out string unifiedDiff)
        {
            int commonPrefixChars = oldSpan.CommonPrefixLength(newSpan);
            if (commonPrefixChars > 0 && oldSpan[commonPrefixChars - 1] == '\r')
            {
                commonPrefixChars--;
            }

            int contextBefore = Math.Max(0, startCommon - 3);
            int contextAfterOld = Math.Min(oldLines.Length, endCommonOld + 4);
            int contextAfterNew = Math.Min(newLines.Length, endCommonNew + 4);

            Span<LineSlice> beforeBuf = stackalloc LineSlice[3];
            int beforeCollected = 0;

            int oldPos = prefixScanStart;
            bool oldHasMore = true;
            int newPos = prefixScanStart;
            bool newHasMore = true;

            int oldDiffStart = oldPos;
            int newDiffStart = newPos;
            bool foundDifference = false;

            for (int i = contextBefore; i < startCommon; i++)
            {
                int lineStartOld = oldPos;
                int lineStartNew = newPos;
                ReadOnlySpan<char> oldLine = ReadNextLine(oldSpan, ref oldPos, ref oldHasMore, out LineSlice oldSlice);
                ReadOnlySpan<char> newLine = ReadNextLine(newSpan, ref newPos, ref newHasMore, out _);

                if (oldLine.SequenceEqual(newLine))
                {
                    if (beforeCollected < 3)
                    {
                        beforeBuf[beforeCollected++] = oldSlice;
                    }
                    else
                    {
                        beforeBuf[0] = beforeBuf[1];
                        beforeBuf[1] = beforeBuf[2];
                        beforeBuf[2] = oldSlice;
                    }

                    startCommon++;
                }
                else
                {
                    oldDiffStart = lineStartOld;
                    newDiffStart = lineStartNew;
                    foundDifference = true;
                    break;
                }
            }

            ReadOnlySpan<char> oldRem = default;
            int oldEnd = 0;
            bool oldRemHasMore = false;

            ReadOnlySpan<char> newRem = default;
            int newEnd = 0;
            bool newRemHasMore = false;

            Span<LineSlice> afterBuf = stackalloc LineSlice[3];
            int afterCollected = 0;
            int matchedSuffixCount = 0;

            if (foundDifference)
            {
                oldRem = oldSpan.Slice(oldDiffStart);
                newRem = newSpan.Slice(newDiffStart);
                oldRemHasMore = true;
                newRemHasMore = true;

                int commonSuffixChars = CommonSuffixLength(oldRem, newRem);
                if (commonSuffixChars > 0 && oldRem[oldRem.Length - commonSuffixChars] == '\n')
                {
                    commonSuffixChars--;
                }

                ReadOnlySpan<char> commonSuffix = oldRem.Slice(oldRem.Length - commonSuffixChars);
                int suffixCutInCommon = FindWindowEnd(commonSuffix, 0, 4);
                if (suffixCutInCommon < commonSuffix.Length)
                {
                    oldEnd = (oldRem.Length - commonSuffixChars) + suffixCutInCommon;
                    newEnd = (newRem.Length - commonSuffixChars) + suffixCutInCommon;
                    matchedSuffixCount = 1;
                }
                else
                {
                    oldEnd = oldRem.Length;
                    newEnd = newRem.Length;
                    matchedSuffixCount = 0;
                }

                while (oldRemHasMore && newRemHasMore)
                {
                    int nextOldEnd = oldEnd;
                    bool nextOldHasMore = oldRemHasMore;
                    int nextNewEnd = newEnd;
                    bool nextNewHasMore = newRemHasMore;

                    ReadOnlySpan<char> oldLine = ReadPrevLine(oldRem, ref nextOldEnd, ref nextOldHasMore, out LineSlice oldSlice);
                    ReadOnlySpan<char> newLine = ReadPrevLine(newRem, ref nextNewEnd, ref nextNewHasMore, out _);

                    if (oldLine.SequenceEqual(newLine))
                    {
                        oldEnd = nextOldEnd;
                        oldRemHasMore = nextOldHasMore;
                        newEnd = nextNewEnd;
                        newRemHasMore = nextNewHasMore;

                        if (afterCollected < 3)
                        {
                            for (int i = afterCollected; i > 0; i--)
                            {
                                afterBuf[i] = afterBuf[i - 1];
                            }
                            afterBuf[0] = oldSlice;
                            afterCollected++;
                        }
                        else
                        {
                            afterBuf[2] = afterBuf[1];
                            afterBuf[1] = afterBuf[0];
                            afterBuf[0] = oldSlice;
                        }

                        matchedSuffixCount++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            else if (oldHasMore)
            {
                oldRem = oldSpan.Slice(oldPos);
                oldEnd = oldRem.Length;
                oldRemHasMore = true;
            }
            else if (newHasMore)
            {
                newRem = newSpan.Slice(newPos);
                newEnd = newRem.Length;
                newRemHasMore = true;
            }

            if (requireThreeSuffixMatches && matchedSuffixCount < 3)
            {
                unifiedDiff = string.Empty;
                return false;
            }

            ReadOnlySpan<char> removedSpan = oldRemHasMore ? oldRem.Slice(0, oldEnd) : default;
            ReadOnlySpan<char> addedSpan = newRemHasMore ? newRem.Slice(0, newEnd) : default;

            int removedCount = oldRemHasMore ? 1 + CountLineBreaks(removedSpan) : 0;
            int addedCount = newRemHasMore ? 1 + CountLineBreaks(addedSpan) : 0;

            int contextBefore = startCommon - beforeCollected;
            int hunkOldStart = baseLineIndex + contextBefore + 1;
            int hunkOldCount = beforeCollected + removedCount + afterCollected;
            int hunkNewStart = hunkOldStart;
            int hunkNewCount = beforeCollected + addedCount + afterCollected;

            int estimatedCapacity = oldHeader.Length + newHeader.Length + 64
                + removedSpan.Length + addedSpan.Length
                + (beforeCollected + removedCount + addedCount + afterCollected) * 3
                + 128;

            var sb = new StringBuilder(estimatedCapacity);
            sb.Append("--- ").AppendLine(oldHeader);
            sb.Append("+++ ").AppendLine(newHeader);
            sb.Append("@@ -").Append(hunkOldStart).Append(',').Append(hunkOldCount)
              .Append(" +").Append(hunkNewStart).Append(',').Append(hunkNewCount)
              .AppendLine(" @@");

            for (int i = 0; i < beforeCollected; i++)
            {
                LineSlice slice = beforeBuf[i];
                sb.Append(' ').Append(oldSpan.Slice(slice.Start, slice.Length)).AppendLine();
            }

            for (int i = startCommon; i <= endCommonOld; i++)
            {
                int remPos = 0;
                bool remHasMore = true;
                while (remHasMore)
                {
                    ReadOnlySpan<char> line = ReadNextLine(removedSpan, ref remPos, ref remHasMore, out _);
                    sb.Append('-').Append(line).AppendLine();
                }
            }

            for (int i = startCommon; i <= endCommonNew; i++)
            {
                int addPos = 0;
                bool addHasMore = true;
                while (addHasMore)
                {
                    ReadOnlySpan<char> line = ReadNextLine(addedSpan, ref addPos, ref addHasMore, out _);
                    sb.Append('+').Append(line).AppendLine();
                }
            }

            int afterLinesCount = Math.Min(oldLines.Length - (endCommonOld + 1), 3);
            for (int i = 0; i < afterLinesCount; i++)
            {
                LineSlice slice = afterBuf[i];
                sb.Append(' ').Append(oldRem.Slice(slice.Start, slice.Length)).AppendLine();
            }

            unifiedDiff = sb.ToString();
            return true;
        }

        private static ReadOnlySpan<char> ReadNextLine(
            ReadOnlySpan<char> span,
            ref int pos,
            ref bool hasMore,
            out LineSlice slice)
        {
            int idx = span.Slice(pos).IndexOfAny('\r', '\n');
            if (idx < 0)
            {
                int len = span.Length - pos;
                slice = new LineSlice(pos, len);
                ReadOnlySpan<char> line = span.Slice(pos, len);
                pos = span.Length;
                hasMore = false;
                return line;
            }
            else
            {
                slice = new LineSlice(pos, idx);
                ReadOnlySpan<char> line = span.Slice(pos, idx);
                int breakPos = pos + idx;
                pos = breakPos + 1;
                if (span[breakPos] == '\r' && pos < span.Length && span[pos] == '\n')
                {
                    pos++;
                }

                return line;
            }
        }

        private static ReadOnlySpan<char> ReadPrevLine(
            ReadOnlySpan<char> span,
            ref int end,
            ref bool hasMore,
            out LineSlice slice)
        {
            int idx = span.Slice(0, end).LastIndexOfAny('\r', '\n');
            if (idx < 0)
            {
                slice = new LineSlice(0, end);
                ReadOnlySpan<char> line = span.Slice(0, end);
                end = 0;
                hasMore = false;
                return line;
            }
            else
            {
                int lineStart = idx + 1;
                int len = end - lineStart;
                slice = new LineSlice(lineStart, len);
                ReadOnlySpan<char> line = span.Slice(lineStart, len);
                if (span[idx] == '\n' && idx > 0 && span[idx - 1] == '\r')
                {
                    end = idx - 1;
                }
                else
                {
                    end = idx;
                }

                return line;
            }
        }

        private static int FindWindowStart(ReadOnlySpan<char> content, int pos, int maxBreaks)
        {
            int end = pos;
            for (int b = 0; b < maxBreaks; b++)
            {
                if (end <= 0)
                {
                    return 0;
                }

                int idx = content.Slice(0, end).LastIndexOfAny('\r', '\n');
                if (idx < 0)
                {
                    return 0;
                }

                if (b == maxBreaks - 1)
                {
                    return idx + 1;
                }

                if (content[idx] == '\n' && idx > 0 && content[idx - 1] == '\r')
                {
                    end = idx - 1;
                }
                else
                {
                    end = idx;
                }
            }

            return 0;
        }

        private static int FindWindowEnd(ReadOnlySpan<char> content, int startPos, int maxBreaks)
        {
            int pos = startPos;
            for (int b = 0; b < maxBreaks; b++)
            {
                if (pos >= content.Length)
                {
                    return content.Length;
                }

                int idx = content.Slice(pos).IndexOfAny('\r', '\n');
                if (idx < 0)
                {
                    return content.Length;
                }

                int breakStart = pos + idx;
                if (b == maxBreaks - 1)
                {
                    return breakStart;
                }

                pos = breakStart + 1;
                if (content[breakStart] == '\r' && pos < content.Length && content[pos] == '\n')
                {
                    pos++;
                }
            }

            return content.Length;
        }

        private static int CountLineBreaks(ReadOnlySpan<char> span)
        {
            int count = 0;
            int pos = 0;
            while (pos < span.Length)
            {
                int idx = span.Slice(pos).IndexOfAny('\r', '\n');
                if (idx < 0)
                {
                    break;
                }

                pos += idx + 1;
                if (span[pos - 1] == '\r' && pos < span.Length && span[pos] == '\n')
                {
                    pos++;
                }

                count++;
            }

            return count;
        }

        private static int CommonSuffixLength(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            int max = Math.Min(a.Length, b.Length);
            int matched = 0;

            while (matched + 256 <= max &&
                   a.Slice(a.Length - matched - 256, 256).SequenceEqual(b.Slice(b.Length - matched - 256, 256)))
            {
                matched += 256;
            }

            while (matched + 16 <= max &&
                   a.Slice(a.Length - matched - 16, 16).SequenceEqual(b.Slice(b.Length - matched - 16, 16)))
            {
                matched += 16;
            }

            while (matched < max && a[a.Length - 1 - matched] == b[b.Length - 1 - matched])
            {
                matched++;
            }

            return matched;
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
