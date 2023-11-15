#include "configutil.h"
#include <fstream>
#include <Windows.h>

bool createDefaultConfigFile(const std::wstring& configFilePath) {
    // Display an info pop-up and create a new config.txt file if it doesn't exist
    MessageBox(NULL, L"\"config.txt\" file was not found. A new one will be created. Configure it and relaunch the app.", L"CS_OBS Info", MB_ICONINFORMATION);

    // Create a new config.txt file with default content
    std::wofstream newConfigFile(configFilePath);
    if (newConfigFile) {
        newConfigFile << L"# Set intervals between opening/closing the game and opening/closing OBS. 1000 = 1 second. Lower values may potentially increase CPU load" << std::endl;
        newConfigFile << L"# Delay before opening OBS after you have opened the game (default: 15000)" << std::endl;
        newConfigFile << L"GAME_PROCESS_INTERVAL=15000" << std::endl;
        newConfigFile << L"# Delay before closing OBS after you have closed the game (default: 5000)" << std::endl;
        newConfigFile << L"OBS_PROCESS_INTERVAL=5000" << std::endl;
        newConfigFile << L"" << std::endl;
        newConfigFile << L"# Path to your OBS install folder, where the \"obs64.exe\" file is located" << std::endl;
        newConfigFile << L"# Example:" << std::endl;
        newConfigFile << L"# OBS_WORKING_DIR=C:\\Program Files\\obs-studio\\bin\\64bit" << std::endl;
        newConfigFile << L"OBS_WORKING_DIR=" << std::endl;
        newConfigFile << L"" << std::endl;
        newConfigFile << L"# Enable automatic start on boot (1 for enabled, 0 for disabled)" << std::endl;
        newConfigFile << L"ADD_TO_STARTUP=0" << std::endl;
        newConfigFile << L"" << std::endl;
        newConfigFile << L"# Add the names of game executables (process file name), one per line" << std::endl;
        newConfigFile << L"# Make sure to include the .exe file extension" << std::endl;
        newConfigFile << L"" << std::endl;
        newConfigFile.close();
        return true;
    }

    return false;
}