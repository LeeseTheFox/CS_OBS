#include "startuputil.h"
#include "configutil.h"
#include <iostream>
#include <fstream>
#include <windows.h>
#include <tlhelp32.h>
#include <thread>
#include <string>
#include <vector>
#include <Shlwapi.h>
#include <shlobj.h>
#include <algorithm>
#include <shellapi.h>
#include <tchar.h>

#pragma comment(lib, "Shlwapi.lib")

// Function to delete a file
bool deleteFile(const std::wstring& filePath) {
    return DeleteFile(filePath.c_str()) == TRUE;
}

// Function to get the process ID of a process given its name
DWORD getProcessID(const std::wstring& processName) {
    PROCESSENTRY32 pe32;
    pe32.dwSize = sizeof(PROCESSENTRY32);
    HANDLE hProcessSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

    if (hProcessSnap == INVALID_HANDLE_VALUE) {
        return -1;
    }

    if (Process32First(hProcessSnap, &pe32)) {
        do {
            if (_wcsicmp(PathFindFileName(pe32.szExeFile), processName.c_str()) == 0) {
                CloseHandle(hProcessSnap);
                return pe32.th32ProcessID;
            }
        } while (Process32Next(hProcessSnap, &pe32));
    }

    CloseHandle(hProcessSnap);
    return -1;
}

// Function to close a process given its ID
void closeProcess(DWORD processID) {
    HANDLE hProcess = OpenProcess(PROCESS_TERMINATE, FALSE, processID);
    if (hProcess != nullptr) {
        TerminateProcess(hProcess, 0);
        CloseHandle(hProcess);
    }
}

// Function to launch a process given its path and arguments
bool launchProcess(const std::wstring& path, const std::wstring& arguments, const std::wstring& workingDir) {
    SHELLEXECUTEINFO info = { sizeof(SHELLEXECUTEINFO) };
    info.fMask = SEE_MASK_NOCLOSEPROCESS;
    info.lpFile = path.c_str();
    info.lpParameters = arguments.c_str();
    info.lpDirectory = workingDir.c_str();
    info.nShow = SW_HIDE;
    return ShellExecuteEx(&info) == TRUE;
}

// Function to check if a process is running given its name
bool isProcessRunning(const std::wstring& processName) {
    PROCESSENTRY32 pe32;
    pe32.dwSize = sizeof(PROCESSENTRY32);
    HANDLE hProcessSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

    if (hProcessSnap == INVALID_HANDLE_VALUE) {
        return false;
    }

    if (Process32First(hProcessSnap, &pe32)) {
        do {
            if (_wcsicmp(PathFindFileName(pe32.szExeFile), processName.c_str()) == 0) {
                CloseHandle(hProcessSnap);
                return true;
            }
        } while (Process32Next(hProcessSnap, &pe32));
    }

    CloseHandle(hProcessSnap);
    return false;
}

// Function to read game process names, intervals, OBS working directory, and startup setting from a config file
bool readConfigFile(std::wstring& obsWorkingDir, int& gameCheckInterval, int& obsCheckInterval, std::vector<std::wstring>& gameProcessNames, bool& addToStartup) {
    // Get the full path of the directory where the program is located
    wchar_t programPath[MAX_PATH];
    GetModuleFileName(NULL, programPath, MAX_PATH);
    PathRemoveFileSpec(programPath); // Remove the filename to get the directory path
    std::wstring programDirectory(programPath);

    // Construct the full path to the config.txt file
    std::wstring configFilePath = programDirectory + L"\\config.txt";

    std::wifstream configFile(configFilePath);
    if (!configFile) {
        if (!createDefaultConfigFile(configFilePath)) {
            exit(0); // Terminate the program
        }
    }

    gameProcessNames.clear();
    std::wstring line;
    while (std::getline(configFile, line)) {
        std::wstring prefix = line.substr(0, line.find(L"="));
        if (prefix == L"GAME_PROCESS_INTERVAL") {
            gameCheckInterval = std::stoi(line.substr(line.find(L"=") + 1));
        }
        else if (prefix == L"OBS_PROCESS_INTERVAL") {
            obsCheckInterval = std::stoi(line.substr(line.find(L"=") + 1));
        }
        else if (prefix == L"OBS_WORKING_DIR") {
            obsWorkingDir = line.substr(line.find(L"=") + 1);
        }
        else if (prefix == L"ADD_TO_STARTUP") {
            addToStartup = (line.substr(line.find(L"=") + 1) == L"1");
        }
        else if (!line.empty()) {
            gameProcessNames.push_back(line);
        }
    }

    return true;
}

int main() {
    // Hide the program window
    HWND console = GetConsoleWindow();
    ShowWindow(console, SW_HIDE);

    bool isObsRunning = false;
    std::vector<bool> isGameRunning;

    int gameCheckInterval = 0;
    int obsCheckInterval = 0;
    std::wstring obsWorkingDir;
    std::vector<std::wstring> gameProcessNames;
    bool addToStartup = false;

    if (readConfigFile(obsWorkingDir, gameCheckInterval, obsCheckInterval, gameProcessNames, addToStartup)) {
        isGameRunning.resize(gameProcessNames.size(), false);
    }
    else {
        exit(0); // Terminate the program
    }

    if (addToStartup) {
        wchar_t programPath[MAX_PATH];
        GetModuleFileName(NULL, programPath, MAX_PATH);

        if (!addToStartupFolderWithBatchFile(programPath)) {
            exit(0); // Terminate the program
        }
    }
    else {
        // Delete the batch file from the startup folder if "ADD_TO_STARTUP" is 0
        deleteStartupBatchFile();
    }

    while (true) {
        isGameRunning.resize(gameProcessNames.size(), false);

        for (size_t i = 0; i < gameProcessNames.size(); i++) {
            isGameRunning[i] = isProcessRunning(gameProcessNames[i]);
        }

        if (std::any_of(isGameRunning.begin(), isGameRunning.end(), [](bool b) { return b; }) && !isObsRunning) {
            std::wstring obsExePath = obsWorkingDir + L"\\obs64.exe";
            std::wstring launchOptions = L"--startreplaybuffer --disable-shutdown-check";

            launchProcess(obsExePath, launchOptions, obsWorkingDir);
            isObsRunning = true;
        }
        else if (!std::any_of(isGameRunning.begin(), isGameRunning.end(), [](bool b) { return b; }) && isObsRunning) {
            DWORD obsPID = getProcessID(L"obs64.exe");
            if (obsPID != -1) {
                closeProcess(obsPID);
            }
            isObsRunning = false;
        }

        int sleepInterval = (isObsRunning) ? obsCheckInterval : gameCheckInterval;
        std::this_thread::sleep_for(std::chrono::milliseconds(sleepInterval));
    }

    return 0;
}