using System;
using System.IO;
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

            string source = File.ReadAllText(candidate.TargetFilePath);

            // Guard: Check if member is already present to prevent duplicate insertions
            if ((!string.IsNullOrWhiteSpace(candidate.MemberSignature) && source.Contains(candidate.MemberSignature)) ||
                (!string.IsNullOrWhiteSpace(candidate.GeneratedCode) && source.Contains(candidate.GeneratedCode.Trim())))
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

            string updatedSource = source.Insert(insertPos, "\n" + candidate.GeneratedCode + "\n");
            File.WriteAllText(candidate.TargetFilePath, updatedSource);

            candidate.IsApplied = true;
            Console.WriteLine($"[SUCCESS] Successfully applied patch to '{candidate.TargetFilePath}'.");
            return true;
        }

        public bool ApplyPatchFile(string patchFilePath, string? workingDir = null, bool requireConfirmation = true)
        {
            if (!File.Exists(patchFilePath))
            {
                Console.WriteLine($"[ERROR] Patch file not found: {patchFilePath}");
                return false;
            }

            string patchContent = File.ReadAllText(patchFilePath);
            // Parse target file from "+++ b/<path>"
            var match = System.Text.RegularExpressions.Regex.Match(patchContent, @"\+\+\+\s+b/(?<path>[^\r\n]+)");
            if (!match.Success)
            {
                Console.WriteLine("[ERROR] Invalid unified diff format: missing +++ b/ header.");
                return false;
            }

            string relPath = match.Groups["path"].Value.Trim();
            string root = string.IsNullOrWhiteSpace(workingDir) ? Directory.GetCurrentDirectory() : workingDir;
            string targetPath = Path.Combine(root, relPath);

            if (!File.Exists(targetPath))
            {
                Console.WriteLine($"[ERROR] Target file not found for patch: {targetPath}");
                return false;
            }

            if (requireConfirmation)
            {
                Console.Write($"Apply patch file '{patchFilePath}' to '{targetPath}'? [y/N]: ");
                string? response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response != "y" && response != "yes")
                {
                    Console.WriteLine("[INFO] Patch application skipped by user.");
                    return false;
                }
            }

            // Extract added lines from the patch hunk
            var lines = patchContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var addedLines = new System.Collections.Generic.List<string>();
            bool inHunk = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("@@"))
                {
                    inHunk = true;
                    continue;
                }
                if (inHunk && line.StartsWith("+") && !line.StartsWith("+++"))
                {
                    addedLines.Add(line.Substring(1));
                }
            }

            if (addedLines.Count == 0)
            {
                Console.WriteLine("[WARNING] No added lines found in patch.");
                return false;
            }

            string stubsCode = string.Join("\n", addedLines);
            string source = File.ReadAllText(targetPath);

            int lastBrace = source.LastIndexOf('}');
            if (lastBrace < 0)
            {
                Console.WriteLine("[ERROR] Could not find class closing brace.");
                return false;
            }

            int lineStart = source.LastIndexOf('\n', lastBrace);
            int insertPos = lineStart >= 0 ? lineStart : lastBrace;

            string updatedSource = source.Insert(insertPos, "\n" + stubsCode + "\n");
            File.WriteAllText(targetPath, updatedSource);

            Console.WriteLine($"[SUCCESS] Successfully applied patch to '{targetPath}'.");
            return true;
        }
    }
}
