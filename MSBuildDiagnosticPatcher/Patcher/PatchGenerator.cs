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

            var patchedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedPatchFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var affectedInterfaces = new Dictionary<string, (string SampleContextFile, string SearchRoot, List<InterfaceMemberInfo> DiagnosticMembers)>(
                StringComparer.Ordinal);
            var downstreamScanRoots = new HashSet<string>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            if (hasExplicitProjectRoot)
            {
                downstreamScanRoots.Add(root);
            }

            // Parse and group CS0535 diagnostics by (targetFilePath, ClassName)
            var parsedEntries = new List<(string TargetFilePath, string FileKey, string ClassName, string InterfaceName, string MemberName, string ExpectedParamTypes, DiagnosticSeverity Severity, string ProjectPath)>();

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
                string fullTarget = Path.GetFullPath(targetFilePath);
                string fileKey = OperatingSystem.IsWindows() ? fullTarget.ToUpperInvariant() : fullTarget;

                parsedEntries.Add((
                    targetFilePath,
                    fileKey,
                    parsed.Value.ClassName,
                    parsed.Value.InterfaceName,
                    parsed.Value.MemberName,
                    parsed.Value.ExpectedParamTypes,
                    diag.Severity,
                    diag.ProjectPath));
            }

            var groupedByFileAndType = parsedEntries
                .GroupBy(e => (e.FileKey, e.ClassName))
                .ToList();

            var typeCountPerFile = groupedByFileAndType
                .GroupBy(g => g.Key.FileKey)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            foreach (var group in groupedByFileAndType)
            {
                var first = group.First();
                string targetFilePath = first.TargetFilePath;
                string targetClassName = group.Key.ClassName;

                if (!File.Exists(targetFilePath))
                {
                    continue;
                }

                string normalizedTargetPath = Path.GetFullPath(targetFilePath);
                patchedClasses.Add($"{normalizedTargetPath}|{targetClassName}");

                var (contentWithoutBom, _, hasUtf8Bom) = ReadFileWithEncoding(targetFilePath);
                string newline = DetectNewline(contentWithoutBom);

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

                    if (!hasExplicitProjectRoot && !string.IsNullOrWhiteSpace(interfaceSearchRoot))
                    {
                        downstreamScanRoots.Add(interfaceSearchRoot);
                    }

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

                        if (!affectedInterfaces.TryGetValue(entry.InterfaceName, out var ifaceEntry))
                        {
                            ifaceEntry = (targetFilePath, interfaceSearchRoot, new List<InterfaceMemberInfo>());
                            affectedInterfaces[entry.InterfaceName] = ifaceEntry;
                        }
                        ifaceEntry.DiagnosticMembers.Add(member);
                    }
                }

                if (memberInfos.Count == 0)
                {
                    continue;
                }

                bool omitVirtual = ShouldOmitVirtual(contentWithoutBom, targetClassName);
                var memberEntries = new List<PatchMemberEntry>(memberInfos.Count);
                foreach (var member in memberInfos)
                {
                    string rawStub = GenerateMemberStub(member, contentWithoutBom, targetClassName).Trim('\r', '\n');
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

            // Discover uncompiled downstream classes implementing affected interfaces (e.g. MockDatabaseService in tests)
            if (affectedInterfaces.Count > 0)
            {
                foreach (string scanRoot in downstreamScanRoots)
                {
                    if (Directory.Exists(scanRoot))
                    {
                        DiscoverDownstreamCs0535Patches(
                            scanRoot,
                            affectedInterfaces,
                            patchedClasses,
                            usedPatchFileNames,
                            candidates);
                    }
                }
            }

            return candidates;
        }

        private void DiscoverDownstreamCs0535Patches(
            string scanRoot,
            Dictionary<string, (string SampleContextFile, string SearchRoot, List<InterfaceMemberInfo> DiagnosticMembers)> affectedInterfaces,
            HashSet<string> patchedClasses,
            HashSet<string> usedPatchFileNames,
            List<PatchCandidate> candidates)
        {
            var interfaceMembersMap = new Dictionary<string, List<InterfaceMemberInfo>>(StringComparer.Ordinal);
            foreach (var kvp in affectedInterfaces)
            {
                string interfaceName = kvp.Key;
                var combined = new List<InterfaceMemberInfo>();
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);

                var requiredFromSource = _resolver.ResolveRequiredInterfaceMembers(
                    interfaceName,
                    kvp.Value.SampleContextFile,
                    kvp.Value.SearchRoot);

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

            foreach (string csFile in EnumerateSourceFiles(scanRoot))
            {
                string normalizedPath = Path.GetFullPath(csFile);
                string contentWithoutBom;
                bool hasUtf8Bom;
                try
                {
                    (contentWithoutBom, _, hasUtf8Bom) = ReadFileWithEncoding(csFile);
                }
                catch
                {
                    continue;
                }

                bool mentionsAnyInterface = false;
                foreach (string ifaceName in interfaceMembersMap.Keys)
                {
                    if (contentWithoutBom.Contains(ifaceName, StringComparison.Ordinal))
                    {
                        mentionsAnyInterface = true;
                        break;
                    }
                }

                if (!mentionsAnyInterface)
                {
                    continue;
                }

                string sanitized = CSharpSourceSanitizer.SanitizePreserveLength(contentWithoutBom);
                var discoveredClasses = FindClassesImplementingInterfaces(contentWithoutBom, sanitized, interfaceMembersMap.Keys);

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

                    string newline = DetectNewline(contentWithoutBom);
                    bool omitVirtual = ShouldOmitVirtual(contentWithoutBom, cls.ClassName);
                    var memberEntries = new List<PatchMemberEntry>(missingMembers.Count);

                    foreach (var member in missingMembers)
                    {
                        string rawStub = GenerateMemberStub(member, contentWithoutBom, cls.ClassName).Trim('\r', '\n');
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
                    string patchFileName = BuildUniquePatchFileName(csFile, cls.ClassName, hasMultipleTypesInFile: false, usedPatchFileNames);

                    var patchCandidate = CreatePatchForFile(
                        csFile,
                        diffSourceContent,
                        stubsCode,
                        cls.ClassName,
                        primaryInterface,
                        scanRoot,
                        memberEntries,
                        patchFileName);

                    if (patchCandidate != null)
                    {
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

        private static bool ShouldOmitVirtual(string classSource, string? targetClassName)
        {
            if (CSharpLexer.TryFindTypeScope(classSource, targetClassName, out var scope))
            {
                return scope.IsSealed || scope.IsStruct;
            }

            return false;
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

        private static string BuildMemberSignature(InterfaceMemberInfo member, bool omitVirtual = false)
        {
            string modifierPrefix = omitVirtual ? "public" : "public virtual";
            if (member.IsProperty)
            {
                return $"{modifierPrefix} {member.ReturnType} {member.MemberName}";
            }
            return $"{modifierPrefix} {member.ReturnType} {member.MemberName}({member.ParameterList})";
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
            IReadOnlyList<PatchMemberEntry>? members = null,
            string? patchFileNameOverride = null)
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

            string scanInput = source[0] == '\uFEFF' ? " " + source.Substring(1) : source;
            var scanned = CSharpLexer.Scan(scanInput);
            string masked = scanned.CommentsAndLiteralsMasked;

            if (string.IsNullOrWhiteSpace(className))
            {
                int fallbackBrace = masked.LastIndexOf('}');
                if (fallbackBrace < 0)
                {
                    return source.LastIndexOf('}');
                }
                return ResolveClosingBraceInsertionIndex(source, -1, fallbackBrace);
            }

            if (CSharpLexer.TryFindTypeScopeFromMasked(masked, className, out var scope))
            {
                return ResolveClosingBraceInsertionIndex(source, scope.OpenBraceIndex, scope.CloseBraceIndex);
            }

            return -1;
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

            // Context lines (up to 3 lines before and after)
            int contextBefore = Math.Max(0, startCommon - 3);
            int contextAfterOld = Math.Min(oldLines.Count, endCommonOld + 4);
            int contextAfterNew = Math.Min(newLines.Count, endCommonNew + 4);

            int hunkOldCount = contextAfterOld - contextBefore;
            int hunkNewCount = contextAfterNew - contextBefore;
            int hunkOldStart = hunkOldCount == 0 ? 0 : contextBefore + 1;
            int hunkNewStart = hunkNewCount == 0 ? 0 : contextBefore + 1;

            sb.Append($"@@ -{hunkOldStart},{hunkOldCount} +{hunkNewStart},{hunkNewCount} @@\n");

            // Context lines before
            for (int i = contextBefore; i < startCommon; i++)
            {
                AppendDiffLine(sb, ' ', oldLines[i]);
            }

            // Removed lines
            for (int i = startCommon; i <= endCommonOld; i++)
            {
                AppendDiffLine(sb, '-', oldLines[i]);
            }

            // Added lines
            for (int i = startCommon; i <= endCommonNew; i++)
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

                char c = source[i];

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

            if (Regex.IsMatch(clean, @"\b(?:class|struct|interface|record|enum|event|delegate|namespace)\b"))
            {
                return null;
            }

            clean = StripTrailingGenericConstraints(clean).Trim();

            if (clean.EndsWith(")"))
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
                if (!string.IsNullOrEmpty(beforeId) && !beforeId.EndsWith(".", StringComparison.Ordinal) && !beforeId.EndsWith("::", StringComparison.Ordinal))
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

        private static string ResolveAbsoluteFilePath(string path, string root, string? projectPath = null)
        {
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
