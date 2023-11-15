#pragma once

#include <string>

std::wstring getUserStartupFolder();
bool addToStartupFolderWithBatchFile(const std::wstring& programPath);
bool deleteStartupBatchFile();