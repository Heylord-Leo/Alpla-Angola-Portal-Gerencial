using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace AlplaPortal.Infrastructure.Helpers;

public class PathResolutionResult
{
    public string ConfigKeyUsed { get; set; } = string.Empty;
    public string ConfiguredValue { get; set; } = string.Empty;
    public bool IsConfigValueAbsolute { get; set; }
    public string ContentRootPath { get; set; } = string.Empty;
    public List<string> AttemptedPaths { get; set; } = new();
    public string ResolvedPath { get; set; } = string.Empty;
    public bool Exists { get; set; }
}

public static class PathResolutionHelper
{
    /// <summary>
    /// Resolves a physical path using a secure fallback strategy:
    /// 1. Explicit appsettings configuration (absolute or relative to ContentRootPath)
    /// 2. IIS / Deployed structure (relative to ContentRootPath)
    /// 3. Local DEV structure (climbing up out of src/backend/AlplaPortal.Api)
    /// 4. Legacy fallback (../../..)
    /// Returns diagnostic information suitable for structured logging.
    /// </summary>
    public static PathResolutionResult ResolvePath(
        IWebHostEnvironment env,
        IConfiguration config,
        string configKey,
        string defaultRelativePath,
        bool isDirectory = true)
    {
        var result = new PathResolutionResult
        {
            ConfigKeyUsed = configKey,
            ContentRootPath = env.ContentRootPath
        };

        var configVal = config[configKey];
        if (!string.IsNullOrWhiteSpace(configVal))
        {
            result.ConfiguredValue = configVal;
            result.IsConfigValueAbsolute = Path.IsPathRooted(configVal);

            var explicitPath = result.IsConfigValueAbsolute 
                ? configVal 
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configVal));
            
            result.AttemptedPaths.Add($"[1. Config] {explicitPath}");
            
            if (CheckExists(explicitPath, isDirectory))
            {
                result.ResolvedPath = explicitPath;
                result.Exists = true;
                return result;
            }
        }

        // Fallback 1: Try ContentRootPath (IIS deployment style)
        var contentRootAttempt = Path.GetFullPath(Path.Combine(env.ContentRootPath, defaultRelativePath));
        result.AttemptedPaths.Add($"[2. IIS] {contentRootAttempt}");
        
        if (CheckExists(contentRootAttempt, isDirectory))
        {
            result.ResolvedPath = contentRootAttempt;
            result.Exists = true;
            return result;
        }

        // Fallback 2: DEV environment structure (climbing up out of src/backend/AlplaPortal.Api)
        string devRoot = env.ContentRootPath;
        var sep = Path.DirectorySeparatorChar.ToString();
        var srcToken = $"{sep}src{sep}";
        var srcIdx = devRoot.IndexOf(srcToken, StringComparison.OrdinalIgnoreCase);
        
        if (srcIdx > 0)
        {
            devRoot = devRoot.Substring(0, srcIdx);
            var devAttempt = Path.GetFullPath(Path.Combine(devRoot, defaultRelativePath));
            result.AttemptedPaths.Add($"[3. DEV] {devAttempt}");
            
            if (CheckExists(devAttempt, isDirectory))
            {
                result.ResolvedPath = devAttempt;
                result.Exists = true;
                return result;
            }
        }

        // Ultimate fallback (the legacy assumption)
        var legacyAttempt = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "..", defaultRelativePath));
        result.AttemptedPaths.Add($"[4. Legacy] {legacyAttempt}");
        
        result.ResolvedPath = legacyAttempt;
        result.Exists = CheckExists(legacyAttempt, isDirectory);
        
        return result;
    }

    private static bool CheckExists(string path, bool isDirectory)
    {
        return isDirectory ? Directory.Exists(path) : File.Exists(path);
    }
}
