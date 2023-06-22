using BaseX;
using FrooxEngine;
using HarmonyLib;
using NeosModLoader;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System;
using CodeX;

namespace Unzipper;

public class Unzipper : NeosMod
{
    public override string Name => "Unzipper";
    public override string Author => "dfgHiatus";
    public override string Version => "1.0.0";
    public override string Link => "https://github.com/dfgHiatus/Unzipper/";

    public override void DefineConfiguration(ModConfigurationDefinitionBuilder builder)
    {
        builder
            .Version(new Version(1, 0, 0))
            .AutoSave(true);
    }

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importAsRawFiles =
        new("importAsRawFiles",
        "Import files into Neos as raw files",
        () => false);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importText =
        new("importText", "Import Text", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importTexture =
        new("importTexture", "Import Textures", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importDocument =
        new("importDocument", "Import Documents", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importMesh =
        new("importMesh", "Import Meshes", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importPointCloud =
        new("importPointCloud", "Import Point Clouds", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importAudio =
        new("importAudio", "Import Audio", () => true);
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> importFont =
        new("importFont", "Import Fonts", () => true);
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> importVideo =
        new("importVideo", "Import Videos", () => true);
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> importUnknown =
        new("importUnknown", "Import Videos", () => true);

    internal static HashSet<string> SupportedZippedFiles = new()
    {
        ".zip"
    };

    //internal static HashSet<string> SupportedTarFiles = new()
    //{
    //    ".tar"
    //};

    //internal static HashSet<string> SupportedTarGZFiles = new()
    //{
    //    ".gz", ".tgz", // ".xz", ".txz", ".bz2", ".tbz2", ".tbz", ".lzma", ".tlz",
    //};

    private static ModConfiguration config;
    private static string cachePath = Path.Combine(Engine.Current.CachePath, "Cache", "DecompressedZippedFiles");

    public override void OnEngineInit()
    {
        new Harmony("net.dfgHiatus.Unzipper").PatchAll();
        config = GetConfiguration();
        Directory.CreateDirectory(cachePath);
    }

    public static string[] DecomposeZippedFile(string[] files)
    {
        var fileToHash = files.ToDictionary(file => file, GenerateMD5);
        HashSet<string> dirsToImport = new();
        HashSet<string> zippedFilesToDecompress = new();
        foreach (var element in fileToHash)
        {
            var dir = Path.Combine(cachePath, element.Value);
            if (!Directory.Exists(dir))
                zippedFilesToDecompress.Add(element.Key);
            else
                dirsToImport.Add(dir);
        }
        foreach (var package in zippedFilesToDecompress)
        {
            var modelName = Path.GetFileNameWithoutExtension(package);
            if (ContainsUnicodeCharacter(modelName))
            {
                Error("Imported zip file cannot have Unicode characters in its file name.");
                continue;
            }
            var extractedPath = Path.Combine(cachePath, fileToHash[package]);
            Extractor.Unpack(package, extractedPath);
            dirsToImport.Add(extractedPath);
        }
        return dirsToImport.ToArray();
    }


    [HarmonyPatch(typeof(UniversalImporter), "Import", typeof(AssetClass), typeof(IEnumerable<string>),
        typeof(World), typeof(float3), typeof(floatQ), typeof(bool))]
    public class UniversalImporterPatch
    {
        static bool Prefix(ref IEnumerable<string> files)
        {
            List<string> hasZippedFile = new();
            List<string> notZippedFile = new();
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file).ToLower();
                if (SupportedZippedFiles.Contains(extension))
                    // || SupportedTarFiles.Contains(extension) 
                    // || SupportedTarGZFiles.Contains(extension))
                    hasZippedFile.Add(file);
                else
                    notZippedFile.Add(file);
            }

            List<string> allDirectoriesToBatchImport = new();
            foreach (var dir in DecomposeZippedFile(hasZippedFile.ToArray()))
                allDirectoriesToBatchImport.AddRange(Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(ShouldImportFile).ToArray());

            var slot = Engine.Current.WorldManager.FocusedWorld.AddSlot("Zipped file import");
            slot.PositionInFrontOfUser();
            BatchFolderImporter.BatchImport(slot, allDirectoriesToBatchImport, config.GetValue(importAsRawFiles));

            if (notZippedFile.Count <= 0) return false;
            files = notZippedFile.ToArray();
            return true;
        }
    }

    private static bool ShouldImportFile(string file)
    {
        var extension = Path.GetExtension(file).ToLower();
        var assetClass = AssetHelper.ClassifyExtension(Path.GetExtension(file));
        return (config.GetValue(importText) && assetClass == AssetClass.Text)
            || (config.GetValue(importTexture) && assetClass == AssetClass.Texture)
            || (config.GetValue(importDocument) && assetClass == AssetClass.Document)
            || (config.GetValue(importPointCloud) && assetClass == AssetClass.PointCloud)
            || (config.GetValue(importAudio) && assetClass == AssetClass.Audio)
            || (config.GetValue(importFont) && assetClass == AssetClass.Font)
            || (config.GetValue(importVideo) && assetClass == AssetClass.Video)
            || (config.GetValue(importUnknown) && assetClass == AssetClass.Unknown)
            || (config.GetValue(importMesh) && assetClass == AssetClass.Model && extension != ".xml")   // Handle an edge case where assimp will try to import .xml files as 3D models. Handle recursive imports below
            || SupportedZippedFiles.Contains(extension);
            // || SupportedTarFiles.Contains(extension)
            // || SupportedTarGZFiles.Contains(extension);                                              
    }

    private static bool ContainsUnicodeCharacter(string input)
    {
        const int MaxAnsiCode = 255;
        return input.Any(c => c > MaxAnsiCode);
    }

    // Credit to delta for this method https://github.com/XDelta/
    private static string GenerateMD5(string filepath)
    {
        using var hasher = MD5.Create();
        using var stream = File.OpenRead(filepath);
        var hash = hasher.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "");
    }
}