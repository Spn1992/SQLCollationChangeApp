using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

            string source = File.ReadAllText(candidate.TargetFilePath);

            // Scope duplicate guard to the target type body when available so multi-class files work cleanly
            string checkRegion = source;
            if (!string.IsNullOrWhiteSpace(candidate.TargetTypeName) &&
                CSharpLexer.TryFindTypeScope(source, candidate.TargetTypeName, out var scope) &&
                scope.CloseBraceIndex > scope.OpenBraceIndex)
            {
                checkRegion = source.Substring(scope.OpenBraceIndex, scope.CloseBraceIndex - scope.OpenBraceIndex + 1);
            }

            if ((!string.IsNullOrWhiteSpace(candidate.MemberSignature) && checkRegion.Contains(candidate.MemberSignature)) ||
                (!string.IsNullOrWhiteSpace(candidate.GeneratedCode) && checkRegion.Contains(candidate.GeneratedCode.Trim())))
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
