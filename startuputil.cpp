#include "startuputil.h"
#include <windows.h>
#include <shlobj.h>
#include <fstream>
#include <Shlwapi.h>

#pragma comment(lib, "Shlwapi.lib")

// Function to get the user's startup folder path
std::wstring getUserStartupFolder() {
    wchar_t startupFolder[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPath(NULL, CSIDL_STARTUP, NULL, 0, startupFolder))) {
        return std::wstring(startupFolder);
    }
    return L"";
}

bool addToStartupFolderWithBatchFile(const std::wstring& programPath) {
    std::wstring startupFolder = getUserStartupFolder();
    if (!startupFolder.empty()) {
        // Construct the full path to the batch file
        std::wstring batchFilePath = startupFolder + L"\\CS_OBS_Startup.bat";

        std::wofstream batchFile(batchFilePath);
        if (batchFile) {
            // Get the current working directory (where the program is located)
            wchar_t currentDir[MAX_PATH];
            GetCurrentDirectory(MAX_PATH, currentDir);

            // Add commands to the batch file to run the program as administrator from its current location
            batchFile << L"@echo off" << std::endl;
            batchFile << L"cd /d \"" << currentDir << L"\"" << std::endl;
            batchFile << L"start \"\" \"CS_OBS.exe\"" << std::endl;
            batchFile << L"exit" << std::endl;
            batchFile.close();

            return true;
        }
    }

    return false;
}

bool deleteStartupBatchFile() {
    std::wstring startupFolder = getUserStartupFolder();
    if (!startupFolder.empty()) {
        std::wstring batchFilePath = startupFolder + L"\\CS_OBS_Startup.bat";
        return DeleteFile(batchFilePath.c_str()) == TRUE;
    }
    return false;
}