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
bool CopyStatus(std::vector<std::byte>& output);
bool CopyVisibleCaravanPlan(const char* region, std::vector<std::byte>& output);
bool CopyPhysicalCaravanPlan(const char* region, std::vector<std::byte>& output);
int ConfirmVisibleCaravanSpawn(long long shipmentId, const char* region, unsigned int leaderFormId);
int ConfirmPhysicalCaravanSpawn(long long shipmentId, const char* region, unsigned int leaderFormId);
int ConfirmPhysicalCaravanGroup(long long shipmentId, const char* region, unsigned int leaderFormId,
    unsigned int packFormId, unsigned int guardFormId);
int UpdateVisibleWaypoint(long long shipmentId, unsigned int leaderFormId, int waypointIndex, int anchorCount);
int GetPhysicalCaravanGroup(long long& shipmentId, unsigned int& leaderFormId,
    unsigned int& packFormId, unsigned int& guardFormId);
int RebindPhysicalCaravanGroup(long long shipmentId, unsigned int oldLeaderFormId,
    unsigned int leaderFormId, unsigned int packFormId, unsigned int guardFormId);
int ReleaseVisibleCaravan(long long shipmentId, unsigned int leaderFormId);
bool GetTrackedItems(std::vector<TrackedItem>& output);
int LoadState(std::span<const std::byte> bytes);
int AdvanceToDay(int day);
int SyncWorldDay(int absoluteDay);
int GetPrice(const char* region, const char* formKey);
int ApplyTrade(long long sequence, const char* region, const char* formKey, int netSoldToMerchant);
long long ApplyTradeAuto(const char* region, const char* formKey, int netSoldToMerchant);
int ReportCaravanLoss(long long sequence, long long shipmentId, int lostQuantity);
int ReportCaravanArrival(long long sequence, long long shipmentId);
}
