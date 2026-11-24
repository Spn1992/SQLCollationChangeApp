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

            var patchedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedPatchFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var affectedInterfaces = new Dictionary<string, (string SampleContextFile, List<InterfaceMemberInfo> DiagnosticMembers)>(
                StringComparer.Ordinal);

            // Parse and group CS0535 diagnostics by (targetFilePath, className)
            var parsedCs0535 = new List<(CompilerDiagnostic Diag, string TargetFilePath, string ClassName, string InterfaceName, string MemberName, string ExpectedParamTypes)>();
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

                string targetFilePath = ResolveDiagnosticFilePath(diag, root);
                parsedCs0535.Add((
                    diag,
                    targetFilePath,
                    parsed.Value.ClassName,
                    parsed.Value.InterfaceName,
                    parsed.Value.MemberName,
                    parsed.Value.ExpectedParamTypes));
            }

            foreach (var group in parsedCs0535.GroupBy(
                         x => $"{Path.GetFullPath(x.TargetFilePath)}|{x.ClassName}",
                         StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                string targetFilePath = first.TargetFilePath;
                if (!File.Exists(targetFilePath))
                {
                    continue;
                }

                string normalizedTargetPath = Path.GetFullPath(targetFilePath);
                string targetClassName = first.ClassName;
                string primaryInterfaceName = first.InterfaceName;
                patchedClasses.Add($"{normalizedTargetPath}|{targetClassName}");

                string originalContent = File.ReadAllText(targetFilePath);
                var memberInfos = new List<InterfaceMemberInfo>();
                var seenMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in group)
                {
                    string memberKey = $"{item.InterfaceName}.{item.MemberName}({item.ExpectedParamTypes})";
                    if (!seenMembers.Add(memberKey)) continue;

                    primaryInterfaceName = item.InterfaceName;

                    var member = _resolver.ResolveInterfaceMember(
                        item.InterfaceName,
                        item.MemberName,
                        item.ExpectedParamTypes,
                        targetFilePath,
                        root);

                    if (member != null)
                    {
                        memberInfos.Add(member);

                        if (!affectedInterfaces.TryGetValue(item.InterfaceName, out var ifaceEntry))
                        {
                            ifaceEntry = (targetFilePath, new List<InterfaceMemberInfo>());
                            affectedInterfaces[item.InterfaceName] = ifaceEntry;
                        }
                        ifaceEntry.DiagnosticMembers.Add(member);
                    }
                }

                if (memberInfos.Count == 0) continue;

                // Generate stubs for the missing members
                string classBodySource = ExtractClassBodyOrFallback(originalContent, targetClassName);
                var stubsBuilder = new StringBuilder();
                foreach (var member in memberInfos)
                {
                    string stub = GenerateMemberStub(member, classBodySource);
                    stubsBuilder.AppendLine(stub);
                }

                string stubsCode = stubsBuilder.ToString().TrimEnd();

                // Compute unified diff
                var patchCandidate = CreatePatchForFile(
                    targetFilePath,
                    originalContent,
                    stubsCode,
                    targetClassName,
                    primaryInterfaceName,
                    root);

                if (patchCandidate != null)
                {
                    EnsureUniquePatchFileName(patchCandidate, usedPatchFileNames);
                    patchCandidate.ResolvedErrorCount = memberInfos.Count;
                    candidates.Add(patchCandidate);
                }
            }

            // Discover uncompiled downstream classes implementing affected interfaces (e.g. MockDatabaseService in tests)
            if (affectedInterfaces.Count > 0 && Directory.Exists(root))
            {
                DiscoverDownstreamCs0535Patches(
                    root,
                    affectedInterfaces,
                    patchedClasses,
                    usedPatchFileNames,
                    candidates);
            }

            return candidates;
        }

        private void DiscoverDownstreamCs0535Patches(
            string root,
            Dictionary<string, (string SampleContextFile, List<InterfaceMemberInfo> DiagnosticMembers)> affectedInterfaces,
            HashSet<string> patchedClasses,
            HashSet<string> usedPatchFileNames,
            List<PatchCandidate> candidates)
        {
            // Build combined required member list per affected interface (from interface definition + diagnostic fallback)
            var interfaceMembersMap = new Dictionary<string, List<InterfaceMemberInfo>>(StringComparer.Ordinal);
            foreach (var kvp in affectedInterfaces)
            {
                string interfaceName = kvp.Key;
                var combined = new List<InterfaceMemberInfo>();
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);

                var requiredFromSource = _resolver.ResolveRequiredInterfaceMembers(
                    interfaceName,
                    kvp.Value.SampleContextFile,
                    root);

                foreach (var m in requiredFromSource)
                {
                    if (seenKeys.Add(CSharpSourceSanitizer.BuildMemberIdentityKey(m)))
                    {
                        combined.Add(m);
                    }
                }

                foreach (var m in kvp.Value.DiagnosticMembers)
                {
                    if (seenKeys.Add(CSharpSourceSanitizer.BuildMemberIdentityKey(m)))
                    {
                        combined.Add(m);
                    }
                }

                if (combined.Count > 0)
                {
                    interfaceMembersMap[interfaceName] = combined;
                }
            }

            if (interfaceMembersMap.Count == 0)
            {
                return;
            }

            foreach (string csFile in EnumerateSourceFiles(root))
            {
                string normalizedPath = Path.GetFullPath(csFile);
                string content;
                try
                {
                    content = File.ReadAllText(csFile);
                }
                catch
                {
                    continue;
                }

                // Quick pre-filter: skip files that do not mention any affected interface name
                bool mentionsAnyInterface = false;
                foreach (string ifaceName in interfaceMembersMap.Keys)
                {
                    if (content.Contains(ifaceName, StringComparison.Ordinal))
                    {
                        mentionsAnyInterface = true;
                        break;
                    }
                }

                if (!mentionsAnyInterface)
                {
                    continue;
                }

                string sanitized = CSharpSourceSanitizer.SanitizePreserveLength(content);
                var discoveredClasses = FindClassesImplementingInterfaces(content, sanitized, interfaceMembersMap.Keys);

                foreach (var cls in discoveredClasses)
                {
                    string classKey = $"{normalizedPath}|{cls.ClassName}";
                    if (!patchedClasses.Add(classKey))
                    {
                        continue;
                    }

                    var (implementedMethods, implementedProperties) = ExtractImplementedClassMembers(
                        cls.OriginalBody,
                        cls.SanitizedBody);

                    var missingMembers = new List<InterfaceMemberInfo>();
                    var seenMissing = new HashSet<string>(StringComparer.Ordinal);
                    string primaryInterface = cls.ImplementedInterfaces[0];

                    foreach (string ifaceName in cls.ImplementedInterfaces)
                    {
                        if (!interfaceMembersMap.TryGetValue(ifaceName, out var reqMembers))
                        {
                            continue;
                        }

                        foreach (var req in reqMembers)
                        {
                            if (IsMemberImplemented(req, implementedMethods, implementedProperties))
                            {
                                continue;
                            }

                            string key = CSharpSourceSanitizer.BuildMemberIdentityKey(req);
                            if (seenMissing.Add(key))
                            {
                                primaryInterface = ifaceName;
                                missingMembers.Add(req);
                            }
                        }
                    }

                    if (missingMembers.Count == 0)
                    {
                        continue;
                    }

                    var stubsBuilder = new StringBuilder();
                    foreach (var member in missingMembers)
                    {
                        string stub = GenerateMemberStub(member, cls.OriginalBody);
                        stubsBuilder.AppendLine(stub);
                    }

                    string stubsCode = stubsBuilder.ToString().TrimEnd();
                    var patchCandidate = CreatePatchForFile(
                        csFile,
                        content,
                        stubsCode,
                        cls.ClassName,
                        primaryInterface,
                        root);

                    if (patchCandidate != null)
                    {
                        EnsureUniquePatchFileName(patchCandidate, usedPatchFileNames);
                        // Uncompiled downstream classes did not emit errors in the parsed log
                        patchCandidate.ResolvedErrorCount = 0;
                        candidates.Add(patchCandidate);
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateSourceFiles(string root)
        {
            var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git",
                "bin",
                "obj",
                ".vs",
                ".idea",
                "node_modules",
                "build-diagnostics"
            };

            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string currentDir = stack.Pop();
                string[] files;
                try
                {
                    files = Directory.GetFiles(currentDir, "*.cs", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    yield return file;
                }

                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(currentDir, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                Array.Sort(subDirs, StringComparer.OrdinalIgnoreCase);
                for (int i = subDirs.Length - 1; i >= 0; i--)
                {
                    string dirName = Path.GetFileName(subDirs[i]);
                    if (!excludedDirs.Contains(dirName))
                    {
                        stack.Push(subDirs[i]);
                    }
                }
            }
        }

        private static List<(string ClassName, List<string> ImplementedInterfaces, string OriginalBody, string SanitizedBody)> FindClassesImplementingInterfaces(
            string original,
            string sanitized,
            IEnumerable<string> targetInterfaces)
        {
            var targetSet = new HashSet<string>(targetInterfaces, StringComparer.Ordinal);
            var results = new List<(string, List<string>, string, string)>();

            var classMatches = Regex.Matches(sanitized, @"\bclass\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)");
            foreach (Match match in classMatches)
            {
                // Ignore generic constraints such as 'where T : class'
                int prev = match.Index - 1;
                while (prev >= 0 && char.IsWhiteSpace(sanitized[prev])) prev--;
                if (prev >= 0 && sanitized[prev] == ':')
                {
                    continue;
                }

                string className = match.Groups["name"].Value.TrimStart('@');
                int cursor = match.Index + match.Length;

                // Skip optional generic type parameters <...>
                while (cursor < sanitized.Length && char.IsWhiteSpace(sanitized[cursor])) cursor++;
                if (cursor < sanitized.Length && sanitized[cursor] == '<')
                {
                    int angleDepth = 1;
                    cursor++;
                    while (cursor < sanitized.Length && angleDepth > 0)
                    {
                        if (sanitized[cursor] == '<') angleDepth++;
                        else if (sanitized[cursor] == '>') angleDepth--;
                        cursor++;
                    }
                }

                // Skip optional C# 12 primary constructor parameter list (...)
                while (cursor < sanitized.Length && char.IsWhiteSpace(sanitized[cursor])) cursor++;
                if (cursor < sanitized.Length && sanitized[cursor] == '(')
                {
                    int closeParen = CSharpSourceSanitizer.FindMatchingParen(sanitized, cursor);
                    if (closeParen < 0) continue;
                    cursor = closeParen + 1;
                }

                while (cursor < sanitized.Length && char.IsWhiteSpace(sanitized[cursor])) cursor++;
                if (cursor >= sanitized.Length || sanitized[cursor] != ':')
                {
                    continue;
                }

                int colonIdx = cursor;
                // Locate opening '{' of class body (outside <...>)
                int openBrace = -1;
                int depthAngle = 0;
                for (int i = colonIdx + 1; i < sanitized.Length; i++)
                {
                    if (sanitized[i] == '<') depthAngle++;
                    else if (sanitized[i] == '>' && depthAngle > 0) depthAngle--;
                    else if (sanitized[i] == '{' && depthAngle == 0)
                    {
                        openBrace = i;
                        break;
                    }
                    else if (sanitized[i] == ';' && depthAngle == 0)
                    {
                        break;
                    }
                }

                if (openBrace < 0)
                {
                    continue;
                }

                string baseClause = sanitized.Substring(colonIdx + 1, openBrace - colonIdx - 1);
                var whereMatch = Regex.Match(baseClause, @"\bwhere\b");
                if (whereMatch.Success)
                {
                    baseClause = baseClause.Substring(0, whereMatch.Index);
                }

                var matchedInterfaces = new List<string>();
                foreach (string rawBaseType in SplitBaseTypes(baseClause))
                {
                    string cleanBase = Regex.Replace(rawBaseType.Trim(), @"\s+", "");
                    cleanBase = cleanBase.Replace("global::", "");
                    // Strip namespace prefix on outer type while preserving generic suffix (<...>)
                    int lt = cleanBase.IndexOf('<');
                    string outerPart = lt >= 0 ? cleanBase.Substring(0, lt) : cleanBase;
                    string genericSuffix = lt >= 0 ? cleanBase.Substring(lt) : string.Empty;
                    int lastDot = outerPart.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        outerPart = outerPart.Substring(lastDot + 1);
                    }
                    string unqualifiedBase = outerPart + genericSuffix;

                    if (targetSet.Contains(unqualifiedBase) && !matchedInterfaces.Contains(unqualifiedBase))
                    {
                        matchedInterfaces.Add(unqualifiedBase);
                    }
                }

                if (matchedInterfaces.Count == 0)
                {
                    continue;
                }

                int closeBrace = CSharpSourceSanitizer.FindMatchingBrace(sanitized, openBrace);
                if (closeBrace < 0)
                {
                    continue;
                }

                string origBody = original.Substring(openBrace + 1, closeBrace - openBrace - 1);
                string sanBody = sanitized.Substring(openBrace + 1, closeBrace - openBrace - 1);
                results.Add((className, matchedInterfaces, origBody, sanBody));
            }

            return results;
        }

        private static List<string> SplitBaseTypes(string baseClause)
        {
            var list = new List<string>();
            int angleDepth = 0;
            int start = 0;
            for (int i = 0; i < baseClause.Length; i++)
            {
                if (baseClause[i] == '<') angleDepth++;
                else if (baseClause[i] == '>' && angleDepth > 0) angleDepth--;
                else if (baseClause[i] == ',' && angleDepth == 0)
                {
                    list.Add(baseClause.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < baseClause.Length)
            {
                list.Add(baseClause.Substring(start));
            }
            return list;
        }

        private static (List<(string Name, List<string> ParamTypes)> Methods, HashSet<string> Properties) ExtractImplementedClassMembers(
            string origBody,
            string sanBody)
        {
            var methods = new List<(string Name, List<string> ParamTypes)>();
            var properties = new HashSet<string>(StringComparer.Ordinal);

            var spans = CSharpSourceSanitizer.SplitTopLevelMemberSpans(sanBody, 0, sanBody.Length);
            foreach (var (spanStart, spanEnd) in spans)
            {
                int declStart = CSharpSourceSanitizer.StripLeadingAttributes(sanBody, spanStart, spanEnd + 1);
                if (declStart > spanEnd) continue;

                string sanDecl = sanBody.Substring(declStart, spanEnd - declStart + 1);
                string origDecl = origBody.Substring(declStart, spanEnd - declStart + 1);

                // Find first top-level delimiter: '(', '{', '=>', or '='
                int delimiterIdx = -1;
                string delimiterKind = string.Empty;
                int bracketDepth = 0;

                for (int i = 0; i < sanDecl.Length; i++)
                {
                    char c = sanDecl[i];
                    if (c == '[') bracketDepth++;
                    else if (c == ']' && bracketDepth > 0) bracketDepth--;
                    else if (bracketDepth == 0)
                    {
                        if (c == '(')
                        {
                            delimiterIdx = i;
                            delimiterKind = "(";
                            break;
                        }
                        if (c == '{')
                        {
                            delimiterIdx = i;
                            delimiterKind = "{";
                            break;
                        }
                        if (c == '=' && i + 1 < sanDecl.Length && sanDecl[i + 1] == '>')
                        {
                            delimiterIdx = i;
                            delimiterKind = "=>";
                            break;
                        }
                        if (c == '=')
                        {
                            delimiterIdx = i;
                            delimiterKind = "=";
                            break;
                        }
                    }
                }

                if (delimiterIdx < 0 || delimiterKind == "=")
                {
                    // Field or incomplete declaration
                    continue;
                }

                string headerSan = sanDecl.Substring(0, delimiterIdx).Trim();
                string headerOrig = origDecl.Substring(0, delimiterIdx).Trim();

                if (delimiterKind == "(")
                {
                    int closeParen = CSharpSourceSanitizer.FindMatchingParen(sanDecl, delimiterIdx);
                    if (closeParen < 0) continue;

                    string? mName = ExtractDeclarationMemberName(headerOrig);
                    if (string.IsNullOrWhiteSpace(mName)) continue;

                    string paramList = origDecl.Substring(delimiterIdx + 1, closeParen - delimiterIdx - 1);
                    var paramTypes = CSharpSourceSanitizer.ExtractNormalizedParameterTypes(paramList, hasParameterNames: true);
                    methods.Add((mName, paramTypes));
                }
                else if (delimiterKind == "{" || delimiterKind == "=>")
                {
                    if (Regex.IsMatch(headerSan, @"\b(?:class|struct|interface|enum|record|event)\b") ||
                        Regex.IsMatch(headerSan, @"\bthis\s*\["))
                    {
                        continue;
                    }

                    string? pName = ExtractDeclarationMemberName(headerOrig);
                    if (!string.IsNullOrWhiteSpace(pName))
                    {
                        properties.Add(pName);
                    }
                }
            }

            return (methods, properties);
        }

        private static string? ExtractDeclarationMemberName(string headerOrig)
        {
            string cleaned = headerOrig.Trim();

            // Strip generic method type parameters <T>
            if (cleaned.EndsWith(">", StringComparison.Ordinal))
            {
                int depth = 0;
                for (int i = cleaned.Length - 1; i >= 0; i--)
                {
                    if (cleaned[i] == '>') depth++;
                    else if (cleaned[i] == '<')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            cleaned = cleaned.Substring(0, i).TrimEnd();
                            break;
                        }
                    }
                }
            }

            // Match trailing member name (including explicit interface implementation like IDatabaseService.Foo)
            var match = Regex.Match(cleaned, @"(?:[A-Za-z_][A-Za-z0-9_<>]*\.)?(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\s*$");
            if (!match.Success)
            {
                return null;
            }

            return match.Groups["name"].Value.TrimStart('@');
        }

        private static bool IsMemberImplemented(
            InterfaceMemberInfo required,
            List<(string Name, List<string> ParamTypes)> implementedMethods,
            HashSet<string> implementedProperties)
        {
            if (required.IsProperty)
            {
                return implementedProperties.Contains(required.MemberName);
            }

            var reqTypes = CSharpSourceSanitizer.ExtractNormalizedParameterTypes(required.ParameterList, hasParameterNames: true);
            foreach (var (name, implTypes) in implementedMethods)
            {
                if (!name.Equals(required.MemberName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (reqTypes.Count != implTypes.Count)
                {
                    continue;
                }

                bool allMatch = true;
                for (int i = 0; i < reqTypes.Count; i++)
                {
                    if (!reqTypes[i].Equals(implTypes[i], StringComparison.Ordinal))
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureUniquePatchFileName(PatchCandidate candidate, HashSet<string> usedPatchFileNames)
        {
            if (usedPatchFileNames.Add(candidate.PatchFileName))
            {
                return;
            }

            string baseName = Path.GetFileNameWithoutExtension(candidate.PatchFileName);
            string disambiguated = $"{baseName}.{candidate.TargetTypeName}.patch";
            int suffix = 2;
            while (!usedPatchFileNames.Add(disambiguated))
            {
                disambiguated = $"{baseName}.{candidate.TargetTypeName}.{suffix++}.patch";
            }
            candidate.PatchFileName = disambiguated;
        }

        private static string ResolveDiagnosticFilePath(CompilerDiagnostic diag, string root)
        {
            string direct = ResolveAbsoluteFilePath(diag.FilePath, root);
            if (File.Exists(direct))
            {
                return direct;
            }

            if (!Path.IsPathRooted(diag.FilePath) && !string.IsNullOrWhiteSpace(diag.ProjectPath))
            {
                string projFullPath = ResolveAbsoluteFilePath(diag.ProjectPath, root);
                if (File.Exists(projFullPath))
                {
                    string? projDir = Path.GetDirectoryName(projFullPath);
                    if (!string.IsNullOrEmpty(projDir))
                    {
                        string projRelative = Path.GetFullPath(Path.Combine(projDir, diag.FilePath));
                        if (File.Exists(projRelative))
                        {
                            return projRelative;
                        }
                    }
                }
            }

            return direct;
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
            string sanitized = CSharpSourceSanitizer.SanitizePreserveLength(source);

            // Search for class declaration in sanitized source
            var classMatch = Regex.Match(sanitized, $@"\bclass\s+@?{Regex.Escape(className)}\b");
            if (!classMatch.Success)
            {
                // Fallback: search last closing brace
                return sanitized.LastIndexOf('}');
            }

            // Find class body braces: find opening brace after class keyword
            int openBrace = sanitized.IndexOf('{', classMatch.Index);
            if (openBrace < 0) return sanitized.LastIndexOf('}');

            int lastInnerBrace = CSharpSourceSanitizer.FindMatchingBrace(sanitized, openBrace);
            if (lastInnerBrace >= 0)
            {
                // Find start of line for the closing brace
                int lineStart = source.LastIndexOf('\n', lastInnerBrace);
                return lineStart >= 0 ? lineStart : lastInnerBrace;
            }

            return sanitized.LastIndexOf('}');
        }

        private static string ExtractClassBodyOrFallback(string source, string className)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                return source;
            }

            string sanitized = CSharpSourceSanitizer.SanitizePreserveLength(source);
            var classMatch = Regex.Match(sanitized, $@"\bclass\s+@?{Regex.Escape(className)}\b");
            if (!classMatch.Success)
            {
                return source;
            }

            int openBrace = sanitized.IndexOf('{', classMatch.Index);
            if (openBrace < 0)
            {
                return source;
            }

            int closeBrace = CSharpSourceSanitizer.FindMatchingBrace(sanitized, openBrace);
            if (closeBrace < 0)
            {
                return source;
            }

            return source.Substring(openBrace, closeBrace - openBrace + 1);
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
