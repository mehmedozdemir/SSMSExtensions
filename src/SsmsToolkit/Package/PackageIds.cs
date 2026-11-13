using System;

namespace SsmsToolkit.Package
{
    /// <summary>GUIDs and command IDs. Must stay in sync with ToolkitPackage.vsct.</summary>
    internal static class PackageGuids
    {
        public const string PackageString = "f0914648-f08f-4329-b5b0-290413696d6f";
        public const string CommandSetString = "c8bf28e8-0341-40d6-ab4a-41d2119ba000";

        public static readonly Guid Package = new Guid(PackageString);
        public static readonly Guid CommandSet = new Guid(CommandSetString);
    }

    internal static class PackageIds
    {
        public const int SettingsCommand = 0x0100;
        public const int OpenLogFolderCommand = 0x0101;
        public const int AboutCommand = 0x0102;
        public const int InspectTableCommand = 0x0200;
    }
}
