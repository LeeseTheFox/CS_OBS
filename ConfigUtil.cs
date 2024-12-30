using System;
using System.IO;
using System.Windows.Forms;

namespace CS_OBS3
{
    public class ConfigUtil
    {
        public static bool CreateDefaultConfigFile(string configFilePath)
        {
            MessageBox.Show("\"config.txt\" file was not found. A new one will be created.", "CS_OBS info", MessageBoxButtons.OK, MessageBoxIcon.Information);

            try
            {
                using StreamWriter newConfigFile = new(configFilePath);
                newConfigFile.WriteLine("# Set intervals between opening/closing the game and opening/closing OBS. 1000 = 1 second. Lower values may potentially increase CPU load");
                newConfigFile.WriteLine("# Delay before opening OBS after you have opened the game (default: 15000)");
                newConfigFile.WriteLine("GAME_PROCESS_INTERVAL=15000");
                newConfigFile.WriteLine("# Delay before closing OBS after you have closed the game (default: 5000)");
                newConfigFile.WriteLine("OBS_PROCESS_INTERVAL=5000");
                newConfigFile.WriteLine("");
                newConfigFile.WriteLine("# Path to your OBS install folder, where the \"obs64.exe\" file is located");
                newConfigFile.WriteLine("# Example:");
                newConfigFile.WriteLine("# OBS_WORKING_DIR=C:\\Program Files\\obs-studio\\bin\\64bit");
                newConfigFile.WriteLine("OBS_WORKING_DIR=");
                newConfigFile.WriteLine("");
                newConfigFile.WriteLine("# Enable automatic start on boot (1 for enabled, 0 for disabled)");
                newConfigFile.WriteLine("ADD_TO_STARTUP=0");
                newConfigFile.WriteLine("");
                newConfigFile.WriteLine("# Add the names of game executables (process file name), one per line");
                newConfigFile.WriteLine("# Make sure to include the .exe file extension");
                newConfigFile.WriteLine("");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}