using System;
using System.IO;
using System.Text;
using System.Xml;
using Verse;

namespace EvolvingEnemyStrongholds;

internal static class LocalSettingsMigration
{
    internal static void ImportIfMissing(ModContentPack content)
    {
        if (!string.Equals(content.PackageId, "Local.EvolvingEnemyStrongholds.MedievalPatch", StringComparison.OrdinalIgnoreCase)) return;
        var oldPath = Path.Combine(GenFilePaths.ConfigFolderPath, "Mod_3738072776_StrongholdMod.xml");
        var localPath = Path.Combine(GenFilePaths.ConfigFolderPath,
            GenText.SanitizeFilename("Mod_" + content.FolderName + "_StrongholdMod.xml"));
        if (File.Exists(localPath) || !File.Exists(oldPath)) return;

        try
        {
            // Validate before copying so corrupt legacy settings cannot become the local settings file.
            var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(oldPath));
            if (text.StartsWith("\uFEFF", StringComparison.Ordinal)) text = text.Substring(1);
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(text),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                document.Load(reader);
            var settings = document.SelectSingleNode("/SettingsBlock/ModSettings") as XmlElement;
            if (settings == null || settings.GetAttribute("Class") != typeof(StrongholdSettings).FullName)
                throw new InvalidDataException("Legacy EES settings have an unexpected root or class.");

            var backup = Path.Combine(GenFilePaths.ConfigFolderPath, "Deprecated", "EESSettings",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(backup);
            File.Copy(oldPath, Path.Combine(backup, Path.GetFileName(oldPath)), false);
            File.Copy(oldPath, localPath, false);
            Log.Message("[EES] Imported legacy settings into " + Path.GetFileName(localPath) + "; legacy file and backup retained.");
        }
        catch (Exception exception)
        {
            Log.Warning("[EES] Legacy settings import failed; original file retained: " + exception.Message);
        }
    }
}
