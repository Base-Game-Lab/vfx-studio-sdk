namespace BaseGameLab.Vfx.Editor
{
    /// <summary>The file version rule: read every 0.x we know, and stop with a plain sentence on anything newer.</summary>
    internal static class VfxVersion
    {
        public const int KnownMajor = 0, KnownMinor = 4;
        public static string Known => $"{KnownMajor}.{KnownMinor}";

        /// <summary>Null when this package can read the document, otherwise what to tell the user.</summary>
        public static string Problem(J doc, string fileName)
        {
            if (doc["format"].S() != "vfx-native") return $"{fileName} is not a VFX Studio effect file.";
            var v = doc["version"].S("");
            var parts = v.Split('.');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
                return $"{fileName} has no readable version (found \"{v}\"). Export it again from VFX Studio.";
            if (major != KnownMajor || minor > KnownMinor)
                return $"{fileName} is format {v}, but this package reads up to {Known}. Update the package in Package Manager; nothing else needs to change.";
            return null;
        }
    }
}
