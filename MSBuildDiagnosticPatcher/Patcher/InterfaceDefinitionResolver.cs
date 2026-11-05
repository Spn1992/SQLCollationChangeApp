using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MSBuildDiagnosticPatcher.Patcher
{
    public class InterfaceMemberInfo
    {
        public string InterfaceName { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string ReturnType { get; set; } = "void";
        public string ParameterList { get; set; } = string.Empty;
        public bool IsProperty { get; set; }
        public bool HasGetter { get; set; } = true;
        public bool HasSetter { get; set; }
        public string FullSignature { get; set; } = string.Empty;
        public string InterfaceFilePath { get; set; } = string.Empty;
    }

    public class InterfaceDefinitionResolver
    {
        private static readonly Regex Cs0535Regex = new Regex(
            @"^'(?<class>[^']+)'\s+does not implement interface member\s+'(?<interface>[^.]+)\.(?<member>[^'(]+)(?:\((?<params>.*)\))?'",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, string?> _filePathCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _rootSearchCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _fileContentCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _filePathCache.Clear();
                _rootSearchCache.Clear();
                _fileContentCache.Clear();
            }
        }

        public static (string ClassName, string InterfaceName, string MemberName, string ExpectedParamTypes)? ParseCs0535Message(string message)
        {
            var match = Cs0535Regex.Match(message.Trim());
            if (!match.Success)
            {
                return null;
            }

            return (
                match.Groups["class"].Value.Trim(),
                match.Groups["interface"].Value.Trim(),
                match.Groups["member"].Value.Trim(),
                match.Groups["params"].Success ? match.Groups["params"].Value.Trim() : string.Empty
            );
        }

        public InterfaceMemberInfo? ResolveInterfaceMember(
            string interfaceName,
            string memberName,
            string expectedParamTypes,
            string contextFilePath,
            string? projectRoot = null)
        {
            string? interfaceFile = FindInterfaceFile(interfaceName, contextFilePath, projectRoot);
            if (interfaceFile == null)
            {
                // Fallback: construct signature directly from diagnostic error if interface file not found
                return FallbackMemberInfo(interfaceName, memberName, expectedParamTypes);
            }

            string? content = GetOrReadFileContent(interfaceFile);
            if (content == null)
            {
                return FallbackMemberInfo(interfaceName, memberName, expectedParamTypes, interfaceFile);
            }

            var memberInfo = ExtractMemberFromInterfaceSource(content, interfaceName, memberName, expectedParamTypes);
            if (memberInfo != null)
            {
                memberInfo.InterfaceFilePath = interfaceFile;
                return memberInfo;
            }

            return FallbackMemberInfo(interfaceName, memberName, expectedParamTypes, interfaceFile);
        }

        private string? GetOrReadFileContent(string filePath)
        {
            lock (_cacheLock)
            {
                if (_fileContentCache.TryGetValue(filePath, out var content))
                {
                    return content;
                }
            }

            if (!File.Exists(filePath))
            {
                return null;
            }

            string diskContent = File.ReadAllText(filePath);
            lock (_cacheLock)
            {
                _fileContentCache[filePath] = diskContent;
            }
            return diskContent;
        }

        public string? FindInterfaceFile(string interfaceName, string contextFilePath, string? projectRoot)
        {
            string root = string.IsNullOrWhiteSpace(projectRoot) ? Directory.GetCurrentDirectory() : projectRoot;
            string cacheKey = $"{interfaceName}|{contextFilePath}|{root}";

            lock (_cacheLock)
            {
                if (_filePathCache.TryGetValue(cacheKey, out var cachedPath))
                {
                    return cachedPath;
                }
            }

            string? resolvedPath = ResolveInterfaceFilePathInternal(interfaceName, contextFilePath, root);

            lock (_cacheLock)
            {
                _filePathCache[cacheKey] = resolvedPath;
            }

            return resolvedPath;
        }

        private string? ResolveInterfaceFilePathInternal(string interfaceName, string contextFilePath, string root)
        {
            string fileName = $"{interfaceName}.cs";

            // 1. Same directory as context file
            string? dir = Path.GetDirectoryName(contextFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                string sameDirCandidate = Path.Combine(dir, fileName);
                if (File.Exists(sameDirCandidate)) return sameDirCandidate;

                // 2. Parent directory
                string? parent = Directory.GetParent(dir)?.FullName;
                if (!string.IsNullOrEmpty(parent))
                {
                    string parentCandidate = Path.Combine(parent, fileName);
                    if (File.Exists(parentCandidate)) return parentCandidate;

                    // Sibling Services/ directory
                    string siblingDir = Path.Combine(parent, "Services", fileName);
                    if (File.Exists(siblingDir)) return siblingDir;
                }
            }

            // 3. Search under project root
            string rootSearchKey = $"{fileName}|{root}";
            lock (_cacheLock)
            {
                if (_rootSearchCache.TryGetValue(rootSearchKey, out var cachedRootPath))
                {
                    return cachedRootPath;
                }
            }

            string? foundRootPath = null;
            try
            {
                var files = Directory.GetFiles(root, fileName, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    foundRootPath = files[0];
                }
            }
            catch
            {
                // Ignore search permission exceptions
            }

            lock (_cacheLock)
            {
                _rootSearchCache[rootSearchKey] = foundRootPath;
            }

            return foundRootPath;
        }

        public InterfaceMemberInfo? ExtractMemberFromInterfaceSource(
            string sourceCode,
            string interfaceName,
            string memberName,
            string expectedParamTypes)
        {
            string searchBody = ExtractInterfaceBody(sourceCode, interfaceName);

            // Pattern for method declaration in interface:
            // e.g.: Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, CancellationToken cancellationToken = default);
            var methodRegex = new Regex(
                $@"(?<return>[\w\<\>\?,\.\s]+?)\s+{Regex.Escape(memberName)}\s*\((?<params>[^)]*)\)\s*;");

            var matches = methodRegex.Matches(searchBody);
            InterfaceMemberInfo? bestMatch = null;

            foreach (Match match in matches)
            {
                string returnType = match.Groups["return"].Value.Trim();
                string paramList = match.Groups["params"].Value.Trim();

                var info = new InterfaceMemberInfo
                {
                    InterfaceName = interfaceName,
                    MemberName = memberName,
                    ReturnType = returnType,
                    ParameterList = paramList,
                    IsProperty = false,
                    FullSignature = $"{returnType} {memberName}({paramList})"
                };

                // Check parameter type matching
                if (MatchesParameterTypes(paramList, expectedParamTypes))
                {
                    return info;
                }

                bestMatch ??= info;
            }

            if (bestMatch != null)
            {
                return bestMatch;
            }

            // Pattern for property declaration in interface:
            // e.g.: string ConnectionString { get; set; }
            var propRegex = new Regex(
                $@"(?<return>[\w\<\>\?,\.\s]+?)\s+{Regex.Escape(memberName)}\s*\{{\s*(?<access>[^}}]*)\s*\}}");

            var propMatch = propRegex.Match(searchBody);
            if (propMatch.Success)
            {
                string returnType = propMatch.Groups["return"].Value.Trim();
                string access = propMatch.Groups["access"].Value.Trim();
                return new InterfaceMemberInfo
                {
                    InterfaceName = interfaceName,
                    MemberName = memberName,
                    ReturnType = returnType,
                    IsProperty = true,
                    HasGetter = access.Contains("get"),
                    HasSetter = access.Contains("set"),
                    FullSignature = $"{returnType} {memberName} {{ {access} }}"
                };
            }

            return null;
        }

        private static bool MatchesParameterTypes(string actualParamList, string expectedParamTypes)
        {
            if (string.IsNullOrWhiteSpace(expectedParamTypes) && string.IsNullOrWhiteSpace(actualParamList))
            {
                return true;
            }

            var expectedTypes = expectedParamTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var actualParams = actualParamList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (expectedTypes.Length != actualParams.Length)
            {
                return false;
            }

            for (int i = 0; i < expectedTypes.Length; i++)
            {
                string expType = CleanTypeName(expectedTypes[i]);
                string actParam = actualParams[i].Trim();
                // actual parameter includes type and name (e.g. "string databaseName" or "CancellationToken cancellationToken = default")
                string actType = actParam.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
                actType = CleanTypeName(actType);

                if (!expType.Equals(actType, StringComparison.OrdinalIgnoreCase) &&
                    !actParam.Contains(expType, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static string CleanTypeName(string typeName)
        {
            return typeName.Trim().TrimEnd('?').Replace("System.", "");
        }

        private static InterfaceMemberInfo FallbackMemberInfo(
            string interfaceName,
            string memberName,
            string expectedParamTypes,
            string? interfaceFile = null)
        {
            bool isAsync = memberName.EndsWith("Async", StringComparison.OrdinalIgnoreCase);
            string returnType = isAsync ? "Task" : "void";

            // Reconstruct parameter names from expected types
            var paramsWithNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(expectedParamTypes))
            {
                var types = expectedParamTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int pNum = 1;
                foreach (var t in types)
                {
                    string cleanType = t.Trim();
                    string pName = cleanType.ToLowerInvariant();
                    if (cleanType.Equals("CancellationToken", StringComparison.OrdinalIgnoreCase))
                    {
                        paramsWithNames.Add("CancellationToken cancellationToken = default");
                    }
                    else
                    {
                        paramsWithNames.Add($"{cleanType} param{pNum++}");
                    }
                }
            }

            string paramList = string.Join(", ", paramsWithNames);
            return new InterfaceMemberInfo
            {
                InterfaceName = interfaceName,
                MemberName = memberName,
                ReturnType = returnType,
                ParameterList = paramList,
                IsProperty = false,
                FullSignature = $"{returnType} {memberName}({paramList})",
                InterfaceFilePath = interfaceFile ?? string.Empty
            };
        }

        private static string ExtractInterfaceBody(string sourceCode, string interfaceName)
        {
            var ifaceMatch = Regex.Match(sourceCode, $@"\binterface\s+{Regex.Escape(interfaceName)}\b");
            if (!ifaceMatch.Success) return sourceCode;

            int openBrace = sourceCode.IndexOf('{', ifaceMatch.Index);
            if (openBrace < 0) return sourceCode;

            int depth = 1;
            for (int i = openBrace + 1; i < sourceCode.Length; i++)
            {
                if (sourceCode[i] == '{') depth++;
                else if (sourceCode[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return sourceCode.Substring(openBrace + 1, i - openBrace - 1);
                    }
                }
            }

            return sourceCode.Substring(openBrace + 1);
        }
    }
}
