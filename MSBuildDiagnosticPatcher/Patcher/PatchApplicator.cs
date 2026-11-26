using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MSBuildDiagnosticPatcher.Models;

namespace MSBuildDiagnosticPatcher.Patcher
{
    public class PatchApplicator
    {
        public bool ApplyPatch(PatchCandidate candidate, bool requireConfirmation = true)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));

            if (!File.Exists(candidate.TargetFilePath))
            {
                Console.WriteLine($"[ERROR] Target file not found: {candidate.TargetFilePath}");
                return false;
            }

            if (requireConfirmation)
            {
                Console.Write($"Apply patch to '{candidate.TargetFilePath}'? [y/N]: ");
                string? response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response != "y" && response != "yes")
                {
                    Console.WriteLine("[INFO] Patch application skipped by user.");
                    return false;
                }
            }

            var (source, encoding, _) = PatchGenerator.ReadFileWithEncoding(candidate.TargetFilePath);
            string newline = PatchGenerator.DetectNewline(source);

            var (existingMembers, checkRegion) = ExtractExistingClassMembers(source, candidate.TargetTypeName);
            var candidateEntries = ResolveCandidateMembers(candidate);

            var missingEntries = new List<PatchMemberEntry>();
            foreach (var entry in candidateEntries)
            {
                if (!IsMemberAlreadyPresent(entry, checkRegion, existingMembers))
                {
                    missingEntries.Add(entry);
                }
            }

            // Guard: If all candidate members are already present, skip modifying file
            if (missingEntries.Count == 0)
            {
                Console.WriteLine($"[INFO] Target file already contains implementation. Skipped: {candidate.TargetFilePath}");
                candidate.IsApplied = true;
                return true;
            }

            int insertPos = PatchGenerator.FindClassInsertionPoint(source, candidate.TargetTypeName);
            if (insertPos < 0)
            {
                Console.WriteLine($"[ERROR] Could not find class '{candidate.TargetTypeName}' insertion point in {candidate.TargetFilePath}");
                return false;
            }

            string normalizedStubs = string.Join(
                newline + newline,
                missingEntries
                    .Select(m => PatchGenerator.NormalizeNewlines(m.GeneratedCode.Trim('\r', '\n'), newline))
                    .Where(code => !string.IsNullOrWhiteSpace(code)));

            if (string.IsNullOrWhiteSpace(normalizedStubs))
            {
                Console.WriteLine($"[INFO] Target file already contains implementation. Skipped: {candidate.TargetFilePath}");
                candidate.IsApplied = true;
                return true;
            }

            string updatedSource = PatchGenerator.SpliceCodeAtInsertionPoint(source, insertPos, normalizedStubs, newline);
            File.WriteAllText(candidate.TargetFilePath, updatedSource, encoding);

            candidate.IsApplied = true;
            Console.WriteLine($"[SUCCESS] Successfully applied patch to '{candidate.TargetFilePath}'.");
            return true;
        }

        private static List<PatchMemberEntry> ResolveCandidateMembers(PatchCandidate candidate)
        {
            if (candidate.Members != null && candidate.Members.Count > 0)
            {
                return candidate.Members;
            }

            return PatchGenerator.ParseMemberEntriesFromCode(candidate.GeneratedCode, candidate.MemberSignature);
        }

        private static bool IsMemberAlreadyPresent(
            PatchMemberEntry entry,
            string checkRegion,
            ExistingClassMembers existing)
        {
            if (!string.IsNullOrWhiteSpace(entry.MemberName))
            {
                if (entry.IsProperty)
                {
                    if (existing.Properties.Contains(entry.MemberName))
                    {
                        return true;
                    }
                }
                else
                {
                    var expectedParams = entry.ParameterTypes != null && entry.ParameterTypes.Count > 0
                        ? entry.ParameterTypes
                        : PatchGenerator.ExtractNormalizedParameterTypes(entry.ParameterList);

                    string methodKey = BuildMethodSignatureKey(entry.MemberName, expectedParams);
                    if (existing.MethodSignatureKeys.Contains(methodKey))
                    {
                        return true;
                    }
                }
            }

            // Fallback checks for manually constructed PatchCandidate instances with custom snippets/signatures
            string collapsedSource = PatchGenerator.CollapseWhitespace(checkRegion);

            if (!string.IsNullOrWhiteSpace(entry.MemberSignature))
            {
                string collapsedSig = PatchGenerator.CollapseWhitespace(entry.MemberSignature);
                if (!string.IsNullOrWhiteSpace(collapsedSig) &&
                    collapsedSource.Contains(collapsedSig, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.GeneratedCode))
            {
                string collapsedCode = PatchGenerator.CollapseWhitespace(entry.GeneratedCode);
                if (!string.IsNullOrWhiteSpace(collapsedCode) &&
                    collapsedSource.Contains(collapsedCode, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class ExistingClassMembers
        {
            public HashSet<string> MethodSignatureKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Properties { get; } = new HashSet<string>(StringComparer.Ordinal);
        }

        private static (ExistingClassMembers Members, string CheckRegion) ExtractExistingClassMembers(string source, string targetClassName)
        {
            var result = new ExistingClassMembers();
            if (string.IsNullOrWhiteSpace(source))
            {
                return (result, source);
            }

            string masked = PatchGenerator.MaskCommentsAndStrings(source);
            var (bodyStart, bodyEnd) = FindTargetClassBodyBounds(source, masked, targetClassName);
            if (bodyStart < 0 || bodyEnd <= bodyStart)
            {
                return (result, source);
            }

            int sliceStart = Math.Max(0, bodyStart - 1);
            int sliceLen = Math.Min(source.Length - sliceStart, bodyEnd - sliceStart + 1);
            string checkRegion = source.Substring(sliceStart, sliceLen);

            int headerStart = bodyStart;
            int parenDepth = 0;

            for (int i = bodyStart; i < bodyEnd; i++)
            {
                char c = masked[i];
                if (c == '(')
                {
                    parenDepth++;
                }
                else if (c == ')' && parenDepth > 0)
                {
                    parenDepth--;
                }
                else if (parenDepth == 0 && c == '=' && i + 1 < bodyEnd && masked[i + 1] == '>')
                {
                    // Expression-bodied member header ends before '=>'
                    string header = masked.Substring(headerStart, i - headerStart);
                    RecordParsedHeader(header, result);

                    // Advance past expression body up to ';' at member level
                    i += 2;
                    int exprBraceDepth = 0;
                    int exprParenDepth = 0;
                    while (i < bodyEnd)
                    {
                        char ec = masked[i];
                        if (ec == '{') exprBraceDepth++;
                        else if (ec == '}' && exprBraceDepth > 0) exprBraceDepth--;
                        else if (ec == '(') exprParenDepth++;
                        else if (ec == ')' && exprParenDepth > 0) exprParenDepth--;
                        else if (ec == ';' && exprBraceDepth == 0 && exprParenDepth == 0)
                        {
                            break;
                        }
                        i++;
                    }
                    headerStart = i + 1;
                }
                else if (parenDepth == 0 && c == ';')
                {
                    // Field, event, or abstract/partial/extern method declaration
                    string header = masked.Substring(headerStart, i - headerStart);
                    RecordParsedHeader(header, result);
                    headerStart = i + 1;
                }
                else if (parenDepth == 0 && c == '{')
                {
                    // Block-bodied member or nested type declaration
                    string header = masked.Substring(headerStart, i - headerStart);
                    RecordParsedHeader(header, result);

                    int closeBrace = PatchGenerator.FindMatchingClosingBrace(masked, i);
                    if (closeBrace < 0 || closeBrace >= bodyEnd)
                    {
                        break;
                    }
                    i = closeBrace;
                    headerStart = i + 1;
                }
            }

            return (result, checkRegion);
        }

        private static void RecordParsedHeader(string maskedHeader, ExistingClassMembers result)
        {
            var parsed = PatchGenerator.TryParseMemberHeader(maskedHeader);
            if (parsed == null)
            {
                return;
            }

            if (parsed.Value.IsProperty)
            {
                result.Properties.Add(parsed.Value.MemberName);
            }
            else
            {
                string key = BuildMethodSignatureKey(parsed.Value.MemberName, parsed.Value.NormalizedParamTypes);
                result.MethodSignatureKeys.Add(key);
            }
        }

        private static string BuildMethodSignatureKey(string memberName, IReadOnlyList<string> normalizedParamTypes)
        {
            return $"{memberName}({string.Join(",", normalizedParamTypes)})";
        }

        private static (int BodyStart, int BodyEnd) FindTargetClassBodyBounds(string source, string masked, string targetClassName)
        {
            if (!string.IsNullOrWhiteSpace(targetClassName) &&
                CSharpLexer.TryFindTypeScope(source, targetClassName, out var scope) &&
                scope.CloseBraceIndex > scope.OpenBraceIndex)
            {
                return (scope.OpenBraceIndex + 1, scope.CloseBraceIndex);
            }

            Match classMatch = Match.Empty;
            if (!string.IsNullOrWhiteSpace(targetClassName))
            {
                classMatch = Regex.Match(masked, $@"\bclass\s+{Regex.Escape(targetClassName)}\b");
                if (!classMatch.Success && targetClassName.Contains('.'))
                {
                    string simpleName = targetClassName.Substring(targetClassName.LastIndexOf('.') + 1);
                    if (!string.IsNullOrWhiteSpace(simpleName))
                    {
                        classMatch = Regex.Match(masked, $@"\bclass\s+{Regex.Escape(simpleName)}\b");
                    }
                }
            }

            if (!classMatch.Success)
            {
                // Fallback: first top-level class declaration in file
                classMatch = Regex.Match(masked, @"\bclass\s+\w+\b");
                if (!classMatch.Success)
                {
                    return (-1, -1);
                }
            }

            int openBrace = masked.IndexOf('{', classMatch.Index + classMatch.Length);
            if (openBrace < 0)
            {
                return (-1, -1);
            }

            int closeBrace = PatchGenerator.FindMatchingClosingBrace(masked, openBrace);
            if (closeBrace < 0)
            {
                return (openBrace + 1, masked.Length);
            }

            return (openBrace + 1, closeBrace);
        }

        public static string? FindEnclosingProject(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                string fullFilePath = Path.GetFullPath(filePath);
                string tempRoot = Path.GetFullPath(Path.GetTempPath())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                string? dir = Path.GetDirectoryName(fullFilePath);
                while (!string.IsNullOrEmpty(dir))
                {
                    string normalizedDir = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.IsNullOrEmpty(normalizedDir) ||
                        normalizedDir.Equals(tempRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    if (Directory.Exists(dir))
                    {
                        var csprojFiles = Directory.GetFiles(dir, "*.csproj");
                        if (csprojFiles.Length > 0)
                        {
                            Array.Sort(csprojFiles, StringComparer.OrdinalIgnoreCase);
                            return csprojFiles[0];
                        }
                    }

                    dir = Directory.GetParent(dir)?.FullName;
                }
            }
            catch
            {
                // Ignore path/permission errors and return null
            }

            return null;
        }

        public bool VerifyProjectBuild(string projectFilePath, out string buildOutput)
        {
            buildOutput = string.Empty;
            if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("build");
                psi.ArgumentList.Add(projectFilePath);
                psi.ArgumentList.Add("-p:UseSharedCompilation=false");
                psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
                psi.Environment["DOTNET_NOLOGO"] = "1";

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return false;
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Task.WaitAll(stdoutTask, stderrTask);

                buildOutput = (stdoutTask.Result + "\n" + stderrTask.Result).Trim();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                buildOutput = ex.Message;
                return false;
            }
        }
    }
}
