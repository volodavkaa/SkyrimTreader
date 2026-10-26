// SPDX-License-Identifier: GPL-3.0-or-later
#include "native_economy_bridge.h"

#include <Windows.h>

#include <array>
#include <cstddef>
#include <filesystem>
#include <span>
#include <string>
#include <utility>
#include <vector>

namespace {
using InitializeFn = int(__cdecl*)(const char*, const char*, const char*);
using AdvanceFn = int(__cdecl*)(int);
using PriceFn = int(__cdecl*)(const char*, const char*);
using TradeFn = int(__cdecl*)(long long, const char*, const char*, int);
using CopyErrorFn = int(__cdecl*)(char*, int);
using CopyStateFn = int(__cdecl*)(char*, int);
using LoadStateFn = int(__cdecl*)(const char*, int);
using CaravanLossFn = int(__cdecl*)(long long, long long, int);
using CaravanArrivalFn = int(__cdecl*)(long long, long long);
using TrackedCountFn = int(__cdecl*)();
using TrackedValueFn = int(__cdecl*)(int);
using TrackedKeyFn = int(__cdecl*)(int, char*, int);

HMODULE library = nullptr;  // Native AOT stays loaded until process exit.
InitializeFn initialize = nullptr;
AdvanceFn advance = nullptr;
AdvanceFn syncWorldDay = nullptr;
PriceFn price = nullptr;
TradeFn trade = nullptr;
CopyErrorFn copyError = nullptr;
CopyStateFn copyState = nullptr;
LoadStateFn loadState = nullptr;
CopyStateFn copyManifest = nullptr;
CaravanLossFn caravanLoss = nullptr;
CaravanArrivalFn caravanArrival = nullptr;
TrackedCountFn trackedCount = nullptr;
TrackedValueFn trackedBase = nullptr;
TrackedValueFn trackedKind = nullptr;
TrackedKeyFn trackedKey = nullptr;
std::string configFile;
std::string catalogFile;
bool ready = false;

std::string Utf8Path(const std::filesystem::path& path)
{
    const auto utf8 = path.u8string();
    return { reinterpret_cast<const char*>(utf8.data()), utf8.size() };
}

std::string ReadLastError()
{
    if (!copyError) return "Native economy entry points are unavailable.";
    std::array<char, 1024> buffer{};
    copyError(buffer.data(), static_cast<int>(buffer.size()));
    return buffer.data();
}
}

namespace NativeEconomy {
std::string Initialize(const std::filesystem::path& pluginDirectory)
{
    if (ready) return "Native economy already initialized.";
    const auto libraryPath = pluginDirectory / L"SkyrimTreader.NativeEconomy.dll";
    const auto configPath = pluginDirectory / L"STRConfig.json";
    const auto catalogPath = pluginDirectory / L"STRCatalog.json";
    if (!std::filesystem::is_regular_file(libraryPath) || !std::filesystem::is_regular_file(configPath) ||
        !std::filesystem::is_regular_file(catalogPath))
        return "Native economy files are incomplete; expected DLL, STRConfig.json and STRCatalog.json.";

    if (!library) library = LoadLibraryW(libraryPath.c_str());
    if (!library) return "LoadLibraryW failed for SkyrimTreader.NativeEconomy.dll.";
    initialize = reinterpret_cast<InitializeFn>(GetProcAddress(library, "STR_Initialize"));
    advance = reinterpret_cast<AdvanceFn>(GetProcAddress(library, "STR_AdvanceToDay"));
    syncWorldDay = reinterpret_cast<AdvanceFn>(GetProcAddress(library, "STR_SyncWorldDay"));
    price = reinterpret_cast<PriceFn>(GetProcAddress(library, "STR_GetPrice"));
    trade = reinterpret_cast<TradeFn>(GetProcAddress(library, "STR_ApplyTrade"));
    copyError = reinterpret_cast<CopyErrorFn>(GetProcAddress(library, "STR_CopyLastError"));
    copyState = reinterpret_cast<CopyStateFn>(GetProcAddress(library, "STR_CopyState"));
    loadState = reinterpret_cast<LoadStateFn>(GetProcAddress(library, "STR_LoadState"));
    copyManifest = reinterpret_cast<CopyStateFn>(GetProcAddress(library, "STR_CopyCaravanManifest"));
    caravanLoss = reinterpret_cast<CaravanLossFn>(GetProcAddress(library, "STR_ReportCaravanLoss"));
    caravanArrival = reinterpret_cast<CaravanArrivalFn>(GetProcAddress(library, "STR_ReportCaravanArrival"));
    trackedCount = reinterpret_cast<TrackedCountFn>(GetProcAddress(library, "STR_GetTrackedItemCount"));
    trackedBase = reinterpret_cast<TrackedValueFn>(GetProcAddress(library, "STR_GetTrackedItemBaseValue"));
    trackedKind = reinterpret_cast<TrackedValueFn>(GetProcAddress(library, "STR_GetTrackedItemKind"));
    trackedKey = reinterpret_cast<TrackedKeyFn>(GetProcAddress(library, "STR_CopyTrackedItemKey"));
    if (!initialize || !advance || !syncWorldDay || !price || !trade || !copyError || !copyState || !loadState ||
        !copyManifest || !caravanLoss || !caravanArrival || !trackedCount || !trackedBase || !trackedKind || !trackedKey)
        return "Native economy DLL lacks one or more required entry points.";
    configFile = Utf8Path(configPath);
    catalogFile = Utf8Path(catalogPath);
    const auto day = initialize(configFile.c_str(), catalogFile.c_str(), "");
    ready = day >= 0;
    return day >= 0 ? "Native economy initialized at day " + std::to_string(day) + "." :
        "Native economy initialization failed: " + ReadLastError();
}

bool Ready() { return ready; }
int Reset()
{
    if (!ready) return -1;
    const int day = initialize(configFile.c_str(), catalogFile.c_str(), "");
    ready = day >= 0;
    return day;
}
std::string LastError() { return ReadLastError(); }
bool CopyState(std::vector<std::byte>& output)
{
    if (!ready) return false;
    const int required = copyState(nullptr, 0);
    if (required <= 0 || required > 16 * 1024 * 1024) return false;
    output.resize(required);
    const int actual = copyState(reinterpret_cast<char*>(output.data()), required);
    return actual == required;
}
bool CopyCaravanManifest(std::vector<std::byte>& output)
{
    if (!ready) return false;
    const int required = copyManifest(nullptr, 0);
    if (required <= 0 || required > 16 * 1024 * 1024) return false;
    output.resize(required);
    const int actual = copyManifest(reinterpret_cast<char*>(output.data()), required);
    return actual == required;
}
bool GetTrackedItems(std::vector<TrackedItem>& output)
{
    if (!ready) return false;
    const int count = trackedCount();
    if (count < 0 || count > 4096) return false;
    std::vector<TrackedItem> items;
    items.reserve(count);
    for (int index = 0; index < count; ++index) {
        const int length = trackedKey(index, nullptr, 0);
        if (length <= 0 || length > 255) return false;
        std::string key(length, '\0');
        if (trackedKey(index, key.data(), length) != length) return false;
        const int base = trackedBase(index);
        const int kind = trackedKind(index);
        if (base < 0 || kind < 1 || kind > 4) return false;
        items.push_back({ std::move(key), base, kind });
    }
    output = std::move(items);
    return true;
}
int LoadState(std::span<const std::byte> bytes)
{
    if (!ready || bytes.empty() || bytes.size() > 16 * 1024 * 1024) return -1;
    return loadState(reinterpret_cast<const char*>(bytes.data()), static_cast<int>(bytes.size()));
}
int AdvanceToDay(int day) { return advance ? advance(day) : -1; }
int SyncWorldDay(int absoluteDay) { return syncWorldDay ? syncWorldDay(absoluteDay) : -1; }
int GetPrice(const char* region, const char* formKey)
{
    return price && region && formKey ? price(region, formKey) : -1;
}
int ApplyTrade(long long sequence, const char* region, const char* formKey, int netSoldToMerchant)
{
    return trade && region && formKey ? trade(sequence, region, formKey, netSoldToMerchant) : -1;
}
int ReportCaravanLoss(long long sequence, long long shipmentId, int lostQuantity)
{
    return caravanLoss ? caravanLoss(sequence, shipmentId, lostQuantity) : -1;
}
int ReportCaravanArrival(long long sequence, long long shipmentId)
{
    return caravanArrival ? caravanArrival(sequence, shipmentId) : -1;
}
}
