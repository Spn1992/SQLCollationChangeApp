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
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

            // Group CS0535 diagnostics by target file
            var cs0535Diags = diagnostics
                .Where(d => d.Code.Equals("CS0535", StringComparison.OrdinalIgnoreCase))
                .GroupBy(d => ResolveAbsoluteFilePath(d.FilePath, root, d.ProjectPath), pathComparer);

            foreach (var group in cs0535Diags)
            {
                string targetFilePath = group.Key;
                if (!File.Exists(targetFilePath))
                {
                    continue;
                }

                var (contentWithoutBom, _, hasUtf8Bom) = ReadFileWithEncoding(targetFilePath);
                string newline = DetectNewline(contentWithoutBom);

                var memberInfos = new List<InterfaceMemberInfo>();
                var seenMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string targetClassName = string.Empty;
                string interfaceName = string.Empty;

                foreach (var diag in group)
                {
                    var parsed = InterfaceDefinitionResolver.ParseCs0535Message(diag.Message);
                    if (parsed == null) continue;

                    string memberKey = $"{parsed.Value.InterfaceName}.{parsed.Value.MemberName}({parsed.Value.ExpectedParamTypes})";
                    if (!seenMembers.Add(memberKey)) continue;

                    targetClassName = parsed.Value.ClassName;
                    interfaceName = parsed.Value.InterfaceName;

                    string interfaceSearchRoot = hasExplicitProjectRoot
                        ? root
                        : FindRepoRoot(targetFilePath)
                            ?? ResolveProjectDirectory(diag.ProjectPath, root)
                            ?? root;

                    var member = _resolver.ResolveInterfaceMember(
                        interfaceName,
                        parsed.Value.MemberName,
                        parsed.Value.ExpectedParamTypes,
                        targetFilePath,
                        interfaceSearchRoot);

                    if (member != null)
                    {
                        memberInfos.Add(member);
                    }
                }

                if (memberInfos.Count == 0) continue;

                // Generate stubs and per-member metadata for the missing members
                var memberEntries = new List<PatchMemberEntry>(memberInfos.Count);
                foreach (var member in memberInfos)
                {
                    string rawStub = GenerateMemberStub(member, contentWithoutBom).Trim('\r', '\n');
                    string normalizedStub = NormalizeNewlines(rawStub, newline);
                    string signature = BuildMemberSignature(member);

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

                // Compute unified diff
                var patchCandidate = CreatePatchForFile(
                    targetFilePath,
                    diffSourceContent,
                    stubsCode,
                    targetClassName,
                    interfaceName,
                    root,
                    memberEntries);

                if (patchCandidate != null)
                {
                    patchCandidate.ResolvedErrorCount = memberInfos.Count;
                    candidates.Add(patchCandidate);
                }
            }

            return candidates;
        }

        public string GenerateMemberStub(InterfaceMemberInfo member, string classSource)
        {
            return GenerateMemberStub(member, classSource, overloadCache: null);
        }

        private string GenerateMemberStub(
            InterfaceMemberInfo member,
            string classSource,
            Dictionary<string, List<List<(string Type, string Name, bool HasDefault, string DefaultValue)>>>? overloadCache)
        {
            var sb = new StringBuilder();

            if (member.IsProperty)
            {
                sb.AppendLine($"        public virtual {member.ReturnType} {member.MemberName}");
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

            // Check if class has an existing overload of the same method
            string? delegationCall = FindDelegationCall(member, classSource, overloadCache);

            sb.AppendLine($"        public virtual {member.ReturnType} {member.MemberName}({member.ParameterList})");
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

        private static string BuildMemberSignature(InterfaceMemberInfo member)
        {
            if (member.IsProperty)
            {
                return $"public virtual {member.ReturnType} {member.MemberName}";
            }
            return $"public virtual {member.ReturnType} {member.MemberName}({member.ParameterList})";
        }

        private static string? FindDelegationCall(InterfaceMemberInfo member, string classSource)
        {
            if (string.IsNullOrWhiteSpace(member.MemberName) || string.IsNullOrEmpty(classSource))
            {
                return null;
            }

            var matches = pattern.Matches(classSource);
            var myParamNames = ParseParameters(member.ParameterList).Select(p => p.Name).ToList();

            if (overloads.Count == 0)
            {
                return null;
            }

            var myParamNames = ExtractParameterNames(member.ParameterList);

            foreach (var existingParams in overloads)
            {
                // If existing method has strictly more parameters than the missing member
                if (existingParams.Count > myParamNames.Count)
                {
                    var callArgs = new List<string>();
                    bool canDelegate = true;

                    for (int i = 0; i < existingParams.Count; i++)
                    {
                        var (pType, pName, hasDefault, defaultValue) = existingParams[i];

                        // Match by name or type with missing member's parameters
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
                            // If parameter has a default, pass default value or fallback expression
                            callArgs.Add(!string.IsNullOrWhiteSpace(defaultValue) ? defaultValue : "default");
                        }
                        else if (pType.EndsWith("?"))
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

            var parts = SplitTopLevelParameters(paramList);
            foreach (var part in parts)
            {
                string clean = StripTopLevelDefaultValue(part).Trim();
                var tokens = clean.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
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

            var parts = SplitTopLevelParameters(paramList);
            foreach (var part in parts)
            {
                string clean = part.Trim();
                int eqIdx = FindTopLevelEquals(clean);
                bool hasDefault = eqIdx >= 0;
                string defVal = string.Empty;
                if (hasDefault)
                {
                    defVal = clean.Substring(eqIdx + 1).Trim();
                    clean = clean.Substring(0, eqIdx).Trim();
                }

                var tokens = clean.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2)
                {
                    string name = tokens[tokens.Length - 1];
                    string type = string.Join(" ", tokens.Take(tokens.Length - 1));
                    result.Add((type, name, hasDefault, defVal));
                }
            }
            return result;
        }

        public PatchCandidate? CreatePatchForFile(
            string targetFilePath,
            string originalContent,
            string stubsCode,
            string className,
            string interfaceName,
            string projectRoot,
            IReadOnlyList<PatchMemberEntry>? members = null)
        {
            string effectiveOriginal = originalContent;
            if (!effectiveOriginal.StartsWith('\uFEFF') && HasUtf8BomOnDisk(targetFilePath))
            {
                effectiveOriginal = "\uFEFF" + effectiveOriginal;
            }

            // Find insertion point before class closing brace
            int insertPos = FindClassInsertionPoint(effectiveOriginal, className);
            if (insertPos < 0)
            {
                return null;
            }

            string newline = DetectNewline(effectiveOriginal);
            string normalizedStubs = NormalizeNewlines(stubsCode.Trim('\r', '\n'), newline);
            string newContent = SpliceCodeAtInsertionPoint(effectiveOriginal, insertPos, normalizedStubs, newline);

            // Format relative path for diff headers (prefer git root if found)
            string baseDir = FindRepoRoot(targetFilePath) ?? projectRoot;
            string relPath = GetRelativePath(baseDir, targetFilePath).Replace('\\', '/');

            string unifiedDiff = GenerateUnifiedDiff(
                effectiveOriginal,
                newContent,
                $"a/{relPath}",
                $"b/{relPath}");

            string safeFileName = Path.GetFileNameWithoutExtension(targetFilePath);
            string patchFileName = $"{safeFileName}.patch";

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

            string masked = MaskCommentsAndStrings(source);

            Match classMatch = Match.Empty;
            if (!string.IsNullOrWhiteSpace(className))
            {
                classMatch = Regex.Match(masked, $@"\bclass\s+{Regex.Escape(className)}\b");
                if (!classMatch.Success && className.Contains('.'))
                {
                    string simpleName = className.Substring(className.LastIndexOf('.') + 1);
                    if (!string.IsNullOrWhiteSpace(simpleName))
                    {
                        classMatch = Regex.Match(masked, $@"\bclass\s+{Regex.Escape(simpleName)}\b");
                    }
                }
            }

            if (!classMatch.Success)
            {
                int fallbackBrace = masked.LastIndexOf('}');
                if (fallbackBrace < 0)
                {
                    return source.LastIndexOf('}');
                }
                return ResolveClosingBraceInsertionIndex(source, -1, fallbackBrace);
            }

            // Find class body braces: find opening brace after class keyword
            int openBrace = masked.IndexOf('{', classMatch.Index + classMatch.Length);
            if (openBrace < 0)
            {
                int fallbackBrace = masked.LastIndexOf('}');
                return fallbackBrace >= 0 ? ResolveClosingBraceInsertionIndex(source, -1, fallbackBrace) : source.LastIndexOf('}');
            }

            int lastInnerBrace = FindMatchingClosingBrace(masked, openBrace);
            if (lastInnerBrace >= 0)
            {
                return ResolveClosingBraceInsertionIndex(source, openBrace, lastInnerBrace);
            }

            int lastBrace = masked.LastIndexOf('}');
            return lastBrace >= 0 ? lastBrace : source.LastIndexOf('}');
        }

        internal static int FindMatchingClosingBrace(string maskedSource, int openBrace)
        {
            int depth = 1;
            for (int i = openBrace + 1; i < maskedSource.Length; i++)
            {
                if (maskedSource[i] == '{')
                {
                    depth++;
                }
                else if (maskedSource[i] == '}')
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

        private static int ResolveClosingBraceInsertionIndex(string source, int openBrace, int closingBrace)
        {
            // Scan backwards to find the line break (\r\n, \n, or \r) preceding closingBrace
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
                // Advance past the line break (\n of \r\n, bare \n, or bare \r)
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
                ArrayPool<char>.Shared.Return(rented);
            }
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
                commonPrefixChars--;
            }

            // Context lines (up to 3 lines before and after)
            int contextBefore = Math.Max(0, startCommon - 3);
            int contextAfterOld = Math.Min(oldLines.Count, endCommonOld + 4);
            int contextAfterNew = Math.Min(newLines.Count, endCommonNew + 4);

            int hunkOldCount = contextAfterOld - contextBefore;
            int hunkNewCount = contextAfterNew - contextBefore;
            int hunkOldStart = hunkOldCount == 0 ? 0 : contextBefore + 1;
            int hunkNewStart = hunkNewCount == 0 ? 0 : contextBefore + 1;

            sb.Append($"@@ -{hunkOldStart},{hunkOldCount} +{hunkNewStart},{hunkNewCount} @@\n");

            while (oldHasMore && newHasMore)
            {
                AppendDiffLine(sb, ' ', oldLines[i]);
            }

            if (oldRemHasMore)
            {
                AppendDiffLine(sb, '-', oldLines[i]);
            }

            if (newRemHasMore)
            {
                AppendDiffLine(sb, '+', newLines[i]);
            }

            // Context lines after
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

        /// <summary>
        /// Replaces characters inside C# comments, character literals, and string literals
        /// (including verbatim, raw, and interpolated strings) with spaces while preserving
        /// exact string length and line terminators (\r and \n).
        /// </summary>
        internal static string MaskCommentsAndStrings(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            char[] chars = source.ToCharArray();
            int n = source.Length;
            var interpolationStack = new Stack<(bool IsVerbatim, int BraceDepth)>();

            int i = 0;
            while (i < n)
            {
                // If we are inside an interpolated string literal (and not currently inside an { expression } hole)
                if (interpolationStack.Count > 0 && interpolationStack.Peek().BraceDepth == 0)
                {
                    bool isVerbatim = interpolationStack.Peek().IsVerbatim;
                    char ch = source[i];

                    if (!isVerbatim && ch == '\\' && i + 1 < n)
                    {
                        MaskChar(chars, i);
                        MaskChar(chars, i + 1);
                        i += 2;
                        continue;
                    }

                    if (isVerbatim && ch == '"' && i + 1 < n && source[i + 1] == '"')
                    {
                        chars[i] = ' ';
                        chars[i + 1] = ' ';
                        i += 2;
                        continue;
                    }

                    if ((ch == '{' && i + 1 < n && source[i + 1] == '{') ||
                        (ch == '}' && i + 1 < n && source[i + 1] == '}'))
                    {
                        chars[i] = ' ';
                        chars[i + 1] = ' ';
                        i += 2;
                        continue;
                    }

                    if (ch == '{')
                    {
                        chars[i] = ' ';
                        var top = interpolationStack.Pop();
                        interpolationStack.Push((top.IsVerbatim, 1));
                        i++;
                        continue;
                    }

                    if (ch == '"' || (!isVerbatim && (ch == '\r' || ch == '\n')))
                    {
                        MaskChar(chars, i);
                        interpolationStack.Pop();
                        i++;
                        continue;
                    }

                    MaskChar(chars, i);
                    i++;
                    continue;
                }

                // Normal code mode (or inside an interpolation hole { ... })
                char c = source[i];

                // Track braces for active interpolation hole
                if (interpolationStack.Count > 0 && interpolationStack.Peek().BraceDepth > 0)
                {
                    if (c == '{')
                    {
                        chars[i] = ' ';
                        var top = interpolationStack.Pop();
                        interpolationStack.Push((top.IsVerbatim, top.BraceDepth + 1));
                        i++;
                        continue;
                    }
                    if (c == '}')
                    {
                        chars[i] = ' ';
                        var top = interpolationStack.Pop();
                        interpolationStack.Push((top.IsVerbatim, top.BraceDepth - 1));
                        i++;
                        continue;
                    }
                }

                // Single-line comment //
                if (c == '/' && i + 1 < n && source[i + 1] == '/')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i += 2;
                    while (i < n && source[i] != '\r' && source[i] != '\n')
                    {
                        chars[i] = ' ';
                        i++;
                    }
                    continue;
                }

                // Multi-line block comment /* ... */
                if (c == '/' && i + 1 < n && source[i + 1] == '*')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i += 2;
                    while (i < n)
                    {
                        if (source[i] == '*' && i + 1 < n && source[i + 1] == '/')
                        {
                            chars[i] = ' ';
                            chars[i + 1] = ' ';
                            i += 2;
                            break;
                        }
                        MaskChar(chars, i);
                        i++;
                    }
                    continue;
                }

                // Character literal '...'
                if (c == '\'')
                {
                    chars[i] = ' ';
                    i++;
                    while (i < n && source[i] != '\r' && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < n)
                        {
                            MaskChar(chars, i);
                            MaskChar(chars, i + 1);
                            i += 2;
                            continue;
                        }
                        if (source[i] == '\'')
                        {
                            chars[i] = ' ';
                            i++;
                            break;
                        }
                        MaskChar(chars, i);
                        i++;
                    }
                    continue;
                }

                // Interpolated verbatim string $@"..." or @$"..."
                if ((c == '$' && i + 2 < n && source[i + 1] == '@' && source[i + 2] == '"') ||
                    (c == '@' && i + 2 < n && source[i + 1] == '$' && source[i + 2] == '"'))
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    chars[i + 2] = ' ';
                    interpolationStack.Push((IsVerbatim: true, BraceDepth: 0));
                    i += 3;
                    continue;
                }

                // Interpolated regular or raw string $"..." or $"""..."""
                if (c == '$' && i + 1 < n && source[i + 1] == '"')
                {
                    if (i + 3 < n && source[i + 2] == '"' && source[i + 3] == '"')
                    {
                        chars[i] = ' ';
                        i = ConsumeRawStringLiteral(source, chars, i + 1);
                        continue;
                    }
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    interpolationStack.Push((IsVerbatim: false, BraceDepth: 0));
                    i += 2;
                    continue;
                }

                // Verbatim string @"..."
                if (c == '@' && i + 1 < n && source[i + 1] == '"')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i += 2;
                    while (i < n)
                    {
                        if (source[i] == '"' && i + 1 < n && source[i + 1] == '"')
                        {
                            chars[i] = ' ';
                            chars[i + 1] = ' ';
                            i += 2;
                            continue;
                        }
                        if (source[i] == '"')
                        {
                            chars[i] = ' ';
                            i++;
                            break;
                        }
                        MaskChar(chars, i);
                        i++;
                    }
                    continue;
                }

                // Raw string """...""" or regular string "..."
                if (c == '"')
                {
                    if (i + 2 < n && source[i + 1] == '"' && source[i + 2] == '"')
                    {
                        i = ConsumeRawStringLiteral(source, chars, i);
                        continue;
                    }

                    chars[i] = ' ';
                    i++;
                    while (i < n && source[i] != '\r' && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < n)
                        {
                            MaskChar(chars, i);
                            MaskChar(chars, i + 1);
                            i += 2;
                            continue;
                        }
                        if (source[i] == '"')
                        {
                            chars[i] = ' ';
                            i++;
                            break;
                        }
                        MaskChar(chars, i);
                        i++;
                    }
                    continue;
                }

                i++;
            }

            return new string(chars);
        }

        private static int ConsumeRawStringLiteral(string source, char[] chars, int quoteStart)
        {
            int n = source.Length;
            int qCount = 0;
            int i = quoteStart;
            while (i < n && source[i] == '"')
            {
                chars[i] = ' ';
                qCount++;
                i++;
            }

            while (i < n)
            {
                if (source[i] == '"')
                {
                    int run = 0;
                    while (i + run < n && source[i + run] == '"')
                    {
                        run++;
                    }
                    for (int k = 0; k < run; k++)
                    {
                        chars[i + k] = ' ';
                    }
                    i += run;
                    if (run >= qCount)
                    {
                        break;
                    }
                    continue;
                }

                MaskChar(chars, i);
                i++;
            }

            return i;
        }

        private static void MaskChar(char[] chars, int index)
        {
            if (chars[index] != '\r' && chars[index] != '\n')
            {
                chars[index] = ' ';
            }
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

            // Exclude type declarations and events
            if (Regex.IsMatch(clean, @"\b(?:class|struct|interface|record|enum|event|delegate|namespace)\b"))
            {
                return null;
            }

            // Strip trailing generic constraints: "where T : ..."
            clean = StripTrailingGenericConstraints(clean).Trim();

            if (clean.EndsWith(")"))
            {
                int openParen = FindMatchingOpenParen(clean, clean.Length - 1);
                if (openParen < 0)
                {
                    return null;
                }

                string beforeParen = clean.Substring(0, openParen).Trim();
                // Field/delegate assignment check: '=' before '(' means field initializer, not method
                if (beforeParen.Contains('='))
                {
                    return null;
                }

                string rawParams = clean.Substring(openParen + 1, clean.Length - openParen - 2).Trim();

                // Check for generic type parameter list on method name, e.g., Foo<T1, T2>
                string genericAritySuffix = string.Empty;
                if (beforeParen.EndsWith(">"))
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

                // Strip explicit interface prefix if present (e.g., IDatabaseService.BackupDatabaseAsync)
                string simpleMemberName = ExtractUnqualifiedMemberName(rawMemberName) + genericAritySuffix;
                var normalizedParamTypes = ExtractNormalizedParameterTypes(rawParams);

                return (simpleMemberName, returnType, false, rawParams, normalizedParamTypes);
            }
            else
            {
                // Property header (no parentheses, no '=' field assignment)
                if (clean.Contains('=') || clean.Contains('['))
                {
                    // Allow array return types like byte[] Prop, reject indexers this[...]
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

            // Read trailing qualified identifier (e.g. "Greet" or "IDatabaseService.Greet")
            int end = trimmed.Length;
            int pos = end - 1;
            while (pos >= 0 && (char.IsLetterOrDigit(trimmed[pos]) || trimmed[pos] == '_' || trimmed[pos] == '.' || trimmed[pos] == '<' || trimmed[pos] == '>' || trimmed[pos] == ',' || trimmed[pos] == ' '))
            {
                // Only continue across '.' if part of explicit interface name; stop at whitespace before identifier
                if (trimmed[pos] == ' ' || trimmed[pos] == '>' || trimmed[pos] == '<' || trimmed[pos] == ',')
                {
                    break;
                }
                pos--;
            }

            // If explicit interface name had generic args (e.g. IRepo<int>.Get), step over IRepo<int>.
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

            // Strip leading modifiers from prefix to verify a real return type exists (excludes constructors)
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
            // Match " where Identifier :" after ')'
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

                // Handle parameter modifiers
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

            // If parameter ends with an identifier preceded by a valid type (e.g. "string name", "List<int> items", "IProgress<int>? p"),
            // strip the trailing parameter name; otherwise (e.g. "string", "List<int>", "string?"), the entire token is the type.
            int end = s.Length - 1;
            if (char.IsLetterOrDigit(s[end]) || s[end] == '_')
            {
                int idStart = end;
                while (idStart >= 0 && (char.IsLetterOrDigit(s[idStart]) || s[idStart] == '_' || s[idStart] == '@'))
                {
                    idStart--;
                }

                string beforeId = s.Substring(0, idStart + 1).Trim();
                if (!string.IsNullOrEmpty(beforeId) && ! beforeId.EndsWith(".", StringComparison.Ordinal) && !beforeId.EndsWith("::", StringComparison.Ordinal))
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

            // Handle array rank suffixes like [], [,]
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

            // Handle tuple types (T1 Name1, T2 Name2) -> (NormT1,NormT2)
            if (t.StartsWith("(", StringComparison.Ordinal) && t.EndsWith(")", StringComparison.Ordinal) &&
                FindMatchingOpenParen(t, t.Length - 1) == 0)
            {
                string inner = t.Substring(1, t.Length - 2);
                var elems = SplitTopLevelParameters(inner)
                    .Select(e => NormalizeTypeName(ExtractTypeFromParameterDeclaration(e)));
                return "(" + string.Join(",", elems) + ")";
            }

            // Handle generic types Outer<Arg1, Arg2>
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
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(list))
            {
                return result;
            }

            int angleDepth = 0;
            int parenDepth = 0;
            int bracketDepth = 0;
            int start = 0;

            for (int i = 0; i < list.Length; i++)
            {
                char c = list[i];
                if (c == '<') angleDepth++;
                else if (c == '>' && angleDepth > 0) angleDepth--;
                else if (c == '(') parenDepth++;
                else if (c == ')' && parenDepth > 0) parenDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']' && bracketDepth > 0) bracketDepth--;
                else if (c == ',' && angleDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                {
                    result.Add(list.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }

            string tail = list.Substring(start).Trim();
            if (!string.IsNullOrEmpty(tail))
            {
                result.Add(tail);
            }

            return result;
        }

        private static string StripTopLevelDefaultValue(string param)
        {
            int eqIdx = FindTopLevelEquals(param);
            return eqIdx >= 0 ? param.Substring(0, eqIdx).Trim() : param;
        }

        private static int FindTopLevelEquals(string param)
        {
            int angleDepth = 0;
            int parenDepth = 0;
            int bracketDepth = 0;
            for (int i = 0; i < param.Length; i++)
            {
                char c = param[i];
                if (c == '<') angleDepth++;
                else if (c == '>' && angleDepth > 0) angleDepth--;
                else if (c == '(') parenDepth++;
                else if (c == ')' && parenDepth > 0) parenDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']' && bracketDepth > 0) bracketDepth--;
                else if (c == '=' && angleDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                {
                    return i;
                }
            }
            return -1;
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

        private static string ResolveAbsoluteFilePath(string path, string root)
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
