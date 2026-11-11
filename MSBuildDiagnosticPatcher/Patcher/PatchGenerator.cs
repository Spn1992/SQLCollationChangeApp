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

            // Group CS0535 diagnostics by target file
            var cs0535Diags = diagnostics
                .Where(d => d.Code.Equals("CS0535", StringComparison.OrdinalIgnoreCase))
                .GroupBy(d => ResolveAbsoluteFilePath(d.FilePath, root));

            foreach (var group in cs0535Diags)
            {
                string targetFilePath = group.Key;
                if (!File.Exists(targetFilePath))
                {
                    continue;
                }

                string originalContent = File.ReadAllText(targetFilePath);
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

                    var member = _resolver.ResolveInterfaceMember(
                        interfaceName,
                        parsed.Value.MemberName,
                        parsed.Value.ExpectedParamTypes,
                        targetFilePath,
                        root);

                    if (member != null)
                    {
                        memberInfos.Add(member);
                    }
                }

                if (memberInfos.Count == 0) continue;

                // Generate stubs for the missing members
                var stubsBuilder = new StringBuilder();
                foreach (var member in memberInfos)
                {
                    string stub = GenerateMemberStub(member, originalContent);
                    stubsBuilder.AppendLine(stub);
                }

                string stubsCode = stubsBuilder.ToString().TrimEnd();

                // Compute unified diff
                var patchCandidate = CreatePatchForFile(
                    targetFilePath,
                    originalContent,
                    stubsCode,
                    targetClassName,
                    interfaceName,
                    root);

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
            string? delegationCall = FindDelegationCall(member, classSource);

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

        private static string? FindDelegationCall(InterfaceMemberInfo member, string classSource)
        {
            // Look for existing method in class with the same name:
            // e.g. BackupDatabaseAsync(string databaseName, string backupDirectory, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            var pattern = new Regex(
                $@"\b(?:public|protected|internal|private)?\s*(?:async\s+)?(?:virtual\s+)?[\w\<\>\?,\.\s]+\s+{Regex.Escape(member.MemberName)}\s*\((?<params>[^)]*)\)");

            var matches = pattern.Matches(classSource);
            var myParamNames = ExtractParameterNames(member.ParameterList);

            foreach (Match match in matches)
            {
                string existingParamList = match.Groups["params"].Value.Trim();
                var existingParams = ParseParameters(existingParamList);

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

            var parts = paramList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                string clean = part.Trim();
                if (clean.Contains('='))
                {
                    clean = clean.Substring(0, clean.IndexOf('=')).Trim();
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

            var parts = paramList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                string clean = part.Trim();
                bool hasDefault = false;
                string defVal = "";
                if (clean.Contains('='))
                {
                    hasDefault = true;
                    int eqIdx = clean.IndexOf('=');
                    defVal = clean.Substring(eqIdx + 1).Trim();
                    clean = clean.Substring(0, eqIdx).Trim();
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

        public PatchCandidate? CreatePatchForFile(
            string targetFilePath,
            string originalContent,
            string stubsCode,
            string className,
            string interfaceName,
            string projectRoot)
        {
            // Find insertion point before class closing brace
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
            string patchFileName = $"{safeFileName}.patch";

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
            // Search for class declaration
            var classMatch = Regex.Match(source, $@"\bclass\s+{Regex.Escape(className)}\b");
            if (!classMatch.Success)
            {
                // Fallback: search last closing brace
                return source.LastIndexOf('}');
            }

            // Find class body braces: find opening brace after class keyword
            int openBrace = source.IndexOf('{', classMatch.Index);
            if (openBrace < 0) return source.LastIndexOf('}');

            int depth = 1;
            int lastInnerBrace = -1;

            for (int i = openBrace + 1; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        // Found closing brace of class
                        lastInnerBrace = i;
                        break;
                    }
                }
            }

            if (lastInnerBrace >= 0)
            {
                // Find start of line for the closing brace
                int lineStart = source.LastIndexOf('\n', lastInnerBrace);
                return lineStart >= 0 ? lineStart : lastInnerBrace;
            }

            return source.LastIndexOf('}');
        }

        public static string GenerateUnifiedDiff(
            string oldText,
            string newText,
            string oldHeader,
            string newHeader)
        {
            var oldLines = oldText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var newLines = newText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            // Simple Myers diff or prefix/suffix common line detection for clean unified patch
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

            // Context lines (3 lines before and after)
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

            // Context lines before
            for (int i = contextBefore; i < startCommon; i++)
            {
                sb.AppendLine($" {oldLines[i]}");
            }

            // Removed lines
            for (int i = startCommon; i <= endCommonOld; i++)
            {
                sb.AppendLine($"-{oldLines[i]}");
            }

            // Added lines
            for (int i = startCommon; i <= endCommonNew; i++)
            {
                sb.AppendLine($"+{newLines[i]}");
            }

            // Context lines after
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
