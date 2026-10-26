// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include <cstddef>
#include <filesystem>
#include <span>
#include <string>
#include <vector>

namespace NativeEconomy {
struct TrackedItem
{
    std::string formKey;
    int baseValue = 0;
    int kind = 0; // 1 MISC, 2 ALCH, 3 WEAP, 4 ARMO
};
std::string Initialize(const std::filesystem::path& pluginDirectory);
bool Ready();
int Reset();
std::string LastError();
bool CopyState(std::vector<std::byte>& output);
bool CopyCaravanManifest(std::vector<std::byte>& output);
bool GetTrackedItems(std::vector<TrackedItem>& output);
int LoadState(std::span<const std::byte> bytes);
int AdvanceToDay(int day);
int SyncWorldDay(int absoluteDay);
int GetPrice(const char* region, const char* formKey);
int ApplyTrade(long long sequence, const char* region, const char* formKey, int netSoldToMerchant);
int ReportCaravanLoss(long long sequence, long long shipmentId, int lostQuantity);
int ReportCaravanArrival(long long sequence, long long shipmentId);
}
