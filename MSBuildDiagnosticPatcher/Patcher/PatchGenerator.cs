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

        public PatchGenerator(InterfaceDefinitionResolver? resolver = null)
        {
            _resolver = resolver ?? new InterfaceDefinitionResolver();
        }

        public List<PatchCandidate> GeneratePatches(
            IEnumerable<CompilerDiagnostic> diagnostics,
            string? projectRoot = null)
        {
            var candidates = new List<PatchCandidate>();
            string root = string.IsNullOrWhiteSpace(projectRoot) ? Directory.GetCurrentDirectory() : projectRoot;

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

                    var member = _resolver.ResolveInterfaceMember(
                        entry.InterfaceName,
                        entry.MemberName,
                        entry.ExpectedParamTypes,
                        targetFilePath,
                        root);

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

            foreach (Match match in matches)
            {
                string existingParamList = match.Groups["params"].Value.Trim();
                var existingParams = ParseParameters(existingParamList);

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

                var tokens = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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

            string newContent = originalContent.Insert(insertPos, "\n" + stubsCode + "\n");

            // Format relative path for diff headers (prefer git root if found)
            string baseDir = FindRepoRoot(targetFilePath) ?? projectRoot;
            string relPath = GetRelativePath(baseDir, targetFilePath).Replace('\\', '/');

            string unifiedDiff = GenerateUnifiedDiff(
                originalContent,
                newContent,
                $"a/{relPath}",
                $"b/{relPath}");

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
            var oldLines = oldText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var newLines = newText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int startCommon = 0;
            while (startCommon < oldLines.Length && startCommon < newLines.Length &&
                   oldLines[startCommon] == newLines[startCommon])
            {
                startCommon++;
            }

            int endCommonOld = oldLines.Length - 1;
            int endCommonNew = newLines.Length - 1;
            while (endCommonOld >= startCommon && endCommonNew >= startCommon &&
                   oldLines[endCommonOld] == newLines[endCommonNew])
            {
                endCommonOld--;
                endCommonNew--;
            }

            int contextBefore = Math.Max(0, startCommon - 3);
            int contextAfterOld = Math.Min(oldLines.Length, endCommonOld + 4);
            int contextAfterNew = Math.Min(newLines.Length, endCommonNew + 4);

            int hunkOldStart = contextBefore + 1;
            int hunkOldCount = contextAfterOld - contextBefore;
            int hunkNewStart = contextBefore + 1;
            int hunkNewCount = contextAfterNew - contextBefore;

            var sb = new StringBuilder();
            sb.AppendLine($"--- {oldHeader}");
            sb.AppendLine($"+++ {newHeader}");
            sb.AppendLine($"@@ -{hunkOldStart},{hunkOldCount} +{hunkNewStart},{hunkNewCount} @@");

            for (int i = contextBefore; i < startCommon; i++)
            {
                sb.AppendLine($" {oldLines[i]}");
            }

            for (int i = startCommon; i <= endCommonOld; i++)
            {
                sb.AppendLine($"-{oldLines[i]}");
            }

            for (int i = startCommon; i <= endCommonNew; i++)
            {
                sb.AppendLine($"+{newLines[i]}");
            }

            int afterLinesCount = Math.Min(oldLines.Length - (endCommonOld + 1), 3);
            for (int i = 0; i < afterLinesCount; i++)
            {
                sb.AppendLine($" {oldLines[endCommonOld + 1 + i]}");
            }

            return sb.ToString();
        }

        private static string ResolveAbsoluteFilePath(string path, string root)
        {
            if (Path.IsPathRooted(path)) return path;
            return Path.GetFullPath(Path.Combine(root, path));
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
                    if (Directory.Exists(Path.Combine(dir, ".git")))
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
