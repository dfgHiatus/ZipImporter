using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using SkyFrost.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ZipImporter;

public class ZipImporter : ResoniteMod
{
	internal const string VERSION_CONSTANT = "2.1.1";
	public override string Name => "ZipImporter";
    public override string Author => "dfgHiatus";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/dfgHiatus/ZipImporter/";

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> enabled =
        new("importAsRawFiles", "Enable Importing Zip Files (disable to import as raw file)", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importText =
        new("importText", "Import Text", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importTexture =
        new("importTexture", "Import Textures", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importDocuments =
        new("importDocument", "Import Documents", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importMeshes =
        new("importMesh", "Import Meshes", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importPointClouds =
        new("importPointCloud", "Import Point Clouds", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> importAudio =
        new("importAudio", "Import Audio", () => true);
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> importFonts =
        new("importFont", "Import Fonts", () => true);
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> importVideos =
        new("importVideo", "Import Videos", () => true);
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> importUnknown =
        new("importUnknown", "Import Unknown", () => true);

    internal const string ZIP_FILE_EXTENSION = ".zip";

    private static ModConfiguration config;
    private static readonly string CachePath = Path.Combine(
        Engine.Current.CachePath, 
        "Cache", 
        "DecompressedZippedFiles");

    public override void OnEngineInit()
    {
        new Harmony("net.dfgHiatus.ZipImporter").PatchAll();
        config = GetConfiguration();
        Directory.CreateDirectory(CachePath);
    }

    public static async Task<string[]> ExtractZippedFile(string[] zipFiles, ProgressBarInterface pbi)
    {
        await default(ToBackground);
        var fileToHash = zipFiles.ToDictionary(file => file, GenerateMD5);
        HashSet<string> dirsToImport = new();
        HashSet<string> zippedFilesToDecompress = new();
        foreach (var element in fileToHash)
        {
            var dir = Path.Combine(CachePath, element.Value);
            if (!Directory.Exists(dir))
                zippedFilesToDecompress.Add(element.Key);
            else
                dirsToImport.Add(dir);
        }
        int counter = 1;
        foreach (var package in zippedFilesToDecompress)
        {
            var modelName = Path.GetFileNameWithoutExtension(package);
            if (ContainsUnicodeCharacter(modelName))
            {
                var msg = "Imported zip file cannot have Unicode characters in its file name.";

                await default(ToWorld);
                pbi.ProgressFail(msg);
                pbi.Slot.RunInSeconds(2.5f, delegate
                {
                    pbi.Slot.Destroy();
                });
                await default(ToBackground);

                Error(msg);
                continue;
            }

            pbi.UpdateProgress(
                counter / zippedFilesToDecompress.Count, 
                $"Extracting {counter} out of {zippedFilesToDecompress.Count} files", 
                package);

            var extractedPath = Path.Combine(CachePath, fileToHash[package]);
            ZipFile.ExtractToDirectory(package, extractedPath);
            dirsToImport.Add(extractedPath);
            counter++;
        }
        return dirsToImport.ToArray();
    }


    [HarmonyPatch(typeof(UniversalImporter), "Import", typeof(AssetClass), typeof(IEnumerable<string>),
        typeof(World), typeof(float3), typeof(floatQ), typeof(bool))]
    public class UniversalImporterPatch
    {
        static bool Prefix(IEnumerable<string> files)
        {
            if (!config.GetValue(enabled)) return true;

            List<string> hasZippedFile = new();
            List<string> notZippedFile = new();
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file).ToLower();
                if (extension == ZIP_FILE_EXTENSION)
                    hasZippedFile.Add(file);
                else
                    notZippedFile.Add(file);
            }

            var root = Engine.Current.WorldManager.FocusedWorld.RootSlot;
            root.StartGlobalTask(async delegate
            {
                ProgressBarInterface pbi = await root.World.
                    AddSlot("Import Indicator").
                    SpawnEntity<ProgressBarInterface, LegacySegmentCircleProgress>
                    (FavoriteEntity.ProgressBar);
                pbi.Slot.PositionInFrontOfUser();
                pbi.Initialize(canBeHidden: true);
                pbi.UpdateProgress(0.0f, "Detected Zip(s)! Starting extract...", string.Empty);

                await default(ToBackground);
                List<string> allDirectoriesToBatchImport = new();
                foreach (var dir in await ExtractZippedFile(hasZippedFile.ToArray(), pbi))
                    allDirectoriesToBatchImport.AddRange(Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                        .Where(ShouldImportFile).ToArray());
                await default(ToWorld);

                pbi.ProgressDone("Extract complete!");
                pbi.Slot.RunInSeconds(2.5f, delegate
                {
                    pbi.Slot.Destroy();
                });

                var slot = Engine.Current.WorldManager.FocusedWorld.AddSlot("Zip File import");
                slot.PositionInFrontOfUser();
                BatchFolderImporter.BatchImport(slot, allDirectoriesToBatchImport);
            });

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
            || (config.GetValue(importDocuments) && assetClass == AssetClass.Document)
            || (config.GetValue(importPointClouds) && assetClass == AssetClass.PointCloud)
            || (config.GetValue(importAudio) && assetClass == AssetClass.Audio)
            || (config.GetValue(importFonts) && assetClass == AssetClass.Font)
            || (config.GetValue(importVideos) && assetClass == AssetClass.Video)
            || (config.GetValue(importUnknown) && assetClass == AssetClass.Unknown)
            || (config.GetValue(importMeshes) && assetClass == AssetClass.Model && extension != ".xml")   // Handle an edge case where assimp will try to import .xml files as 3D models. Handle recursive imports below
            || extension == ZIP_FILE_EXTENSION;                                       
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