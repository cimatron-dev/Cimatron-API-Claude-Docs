using System;
using System.IO;
using System.Reflection;

namespace ApiName
{
    // Cimatron discovers this class by interface at load time and calls
    // AppendCommand() to register the plugin's toolbar entry.
    public class ApiNamePlugin : CimUIInfrastructure.PlugIn.ICimApiCommandPlugin
    {
        public CimUIInfrastructure.PlugIn.ApiCommand AppendCommand()
        {
            string pluginDir = GetExecutionPath();
            string icoPath = Path.Combine(pluginDir, "ApiName.ico");
            EnsureToolbarIconCache(icoPath);

            var command = new CimUIInfrastructure.PlugIn.ApiCommand
            {
                Name = "ApiName",
                ToolbarName = "APIs",
                MenuPath = "__MENU_PATH__",
                Caption = "ApiName",
                ToolTip = "ApiName",
                Description = "ApiName plugin command.",
                Application =
                    CimUIInfrastructure.PlugIn.ApiApplications.Assembly |
                    CimUIInfrastructure.PlugIn.ApiApplications.Part,
                IconSource = new CimWpfContracts.WpfImageIdentifier(
                    icoPath,
                    CimWpfContracts.ImageSize.Small),
                ExecuteCommand = new ApiNameCommand()
            };

            return command;
        }

        public static string GetExecutionPath()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var location = assembly?.Location;
            return string.IsNullOrEmpty(location)
                ? AppDomain.CurrentDomain.BaseDirectory
                : Path.GetDirectoryName(location);
        }

        // Cimatron's WpfImageIdentifier renders the toolbar icon from a sibling
        // 32x32 .png cached next to the source .ico. When the cache is absent on
        // first launch after a fresh install, Cimatron's regeneration path (under
        // @1 INI re-read) has been observed to leave the toolbar button blank.
        // Materializing the cache here makes first-launch behavior deterministic
        // regardless of how the plugin was deployed (installer, manual copy, F5).
        // Read-only Program Files is handled by silently falling through; in that
        // case Cimatron's own cache regen still runs as before.
        private static void EnsureToolbarIconCache(string icoPath)
        {
            string pngPath = Path.ChangeExtension(icoPath, ".png");
            if (File.Exists(pngPath) || !File.Exists(icoPath)) return;
            try
            {
                using (var icon = new System.Drawing.Icon(icoPath, 32, 32))
                using (var bmp = icon.ToBitmap())
                {
                    bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch
            {
                // Plugin runs at the user's integrity level; the seed .ico may
                // be malformed (PNG-in-ICO trips Icon.ToBitmap) or Program Files
                // may be read-only. Either way, fall through and let Cimatron
                // attempt its own cache regen.
            }
        }
    }
}
