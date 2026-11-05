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
    }
}
