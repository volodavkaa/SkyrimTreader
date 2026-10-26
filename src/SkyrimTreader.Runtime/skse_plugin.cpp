// SPDX-License-Identifier: GPL-3.0-or-later
#include <Windows.h>

#include <cassert>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>
#include <system_error>
#include <thread>
#include <vector>

using UInt8 = std::uint8_t;
using UInt16 = std::uint16_t;
using UInt32 = std::uint32_t;
using UInt64 = std::uint64_t;
using SInt8 = std::int8_t;
using SInt16 = std::int16_t;
using SInt32 = std::int32_t;
using SInt64 = std::int64_t;
#include <skse64/PluginAPI.h>
#include <skse64_common/skse_version.h>
#ifdef STR_NATIVE_ECONOMY
#include "native_economy_bridge.h"
#endif
#ifdef STR_LIVE_PRICES
#include "regional_prices.h"
#endif
#if defined(STR_DIAGNOSTIC_FORMS) || defined(STR_NATIVE_ECONOMY)
#define STATIC_ASSERT(condition) static_assert(condition)
#define ASSERT(condition) assert(condition)
#define _MESSAGE(...) ((void)0)
#undef InterlockedDecrement
#define InterlockedDecrement(pointer) _InterlockedDecrement(reinterpret_cast<volatile long*>(pointer))
#include <skse64/GameForms.h>
#include <skse64/GameObjects.h>
#endif
#ifdef STR_NATIVE_ECONOMY
#include <skse64/gamethreads.h>
#endif

namespace {
#ifdef STR_NATIVE_ECONOMY
constexpr UInt32 kSerializationId = 0x45525453; // STRE
constexpr UInt32 kStateRecord = 0x53525453; // STRS
constexpr UInt32 kStateVersion = 1;
constexpr UInt32 kMaximumStateBytes = 16 * 1024 * 1024;
std::vector<std::byte> pendingState;
bool pendingReset = false;
bool gameLoaded = false;
int lastLoggedWorldDay = -1;
const SKSETaskInterface* taskInterface = nullptr;
std::atomic<bool> tickQueued = false;
std::jthread tickThread;
#endif
std::filesystem::path PluginDirectory()
{
    const auto module = GetModuleHandleW(L"SimulatedTradeRoutes.dll");
    if (!module) return {};
    wchar_t buffer[32768]{};
    const auto length = GetModuleFileNameW(module, buffer, static_cast<DWORD>(std::size(buffer)));
    if (length == 0 || length == std::size(buffer)) return {};
    return std::filesystem::path(buffer).parent_path();
}

void Log(const std::string& message)
{
    const auto folder = PluginDirectory();
    if (folder.empty()) return;
    std::ofstream log(folder / "SimulatedTradeRoutes.log", std::ios::app);
    if (log) log << message << '\n';
}

#ifdef STR_NATIVE_ECONOMY
void TickEconomy()
{
    if (!gameLoaded || !NativeEconomy::Ready()) return;
    // LookupFormByID comes from the installed SKSE 2.3.1 source for Skyrim 1.7.104.
    constexpr std::uintptr_t lookupOffset = 0x001E5860;
    const auto gameBase = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    if (!gameBase) return;
    const auto lookup = reinterpret_cast<_LookupFormByID>(gameBase + lookupOffset);
    TESForm* form = lookup(0x00000039); // GameDaysPassed in Skyrim.esm.
    if (!form || form->formType != kFormType_Global) return;
    const auto* global = static_cast<TESGlobal*>(form);
    float fractionalDay = 0;
    std::memcpy(&fractionalDay, &global->unk34, sizeof(fractionalDay));
    if (!std::isfinite(fractionalDay) || fractionalDay < 0 || fractionalDay > 100000) return;
    const int worldDay = static_cast<int>(std::floor(fractionalDay));
    if (worldDay == lastLoggedWorldDay) return;
    const int economyDay = NativeEconomy::SyncWorldDay(worldDay);
    if (economyDay < 0) {
        Log("World day sync failed: " + NativeEconomy::LastError());
        return;
    }
    lastLoggedWorldDay = worldDay;
    Log("World day " + std::to_string(worldDay) + " -> economy day " + std::to_string(economyDay) + ".");
}

class EconomyTickTask final : public TaskDelegate
{
public:
    void Run() override
    {
        TickEconomy();
#ifdef STR_LIVE_PRICES
        if (gameLoaded) {
            const auto changes = RegionalPrices::Update();
            if (!changes.empty()) Log("Regional prices: " + changes);
        }
#endif
        tickQueued = false;
    }
    void Dispose() override { delete this; }
};

void StartEconomyTicker()
{
    if (!taskInterface || tickThread.joinable()) return;
    tickThread = std::jthread([](std::stop_token stop) {
        while (!stop.stop_requested()) {
            std::this_thread::sleep_for(std::chrono::seconds(1));
            if (stop.stop_requested()) break;
            if (!tickQueued.exchange(true)) taskInterface->AddTask(new EconomyTickTask());
        }
    });
}

void ApplyPendingState()
{
    if (!NativeEconomy::Ready()) return;
    if (pendingReset) {
        if (NativeEconomy::Reset() < 0) {
            Log("Economy reset failed: " + NativeEconomy::LastError());
            return;
        }
        pendingReset = false;
    }
    if (!pendingState.empty()) {
        const int day = NativeEconomy::LoadState(pendingState);
        if (day < 0) Log("Co-save state rejected: " + NativeEconomy::LastError());
        else Log("Co-save state restored at day " + std::to_string(day) + ".");
        pendingState.clear();
    }
}

void OnSave(SKSESerializationInterface* serialization)
{
    if (!serialization || !serialization->WriteRecord) return;
    ApplyPendingState();
    TickEconomy();
#ifdef STR_LIVE_PRICES
    if (gameLoaded) RegionalPrices::Update();
#endif
    std::vector<std::byte> bytes;
    if (!NativeEconomy::CopyState(bytes)) {
        Log("Co-save state unavailable: " + NativeEconomy::LastError());
        return;
    }
    if (!serialization->WriteRecord(kStateRecord, kStateVersion, bytes.data(), static_cast<UInt32>(bytes.size())))
        Log("Failed to write economy co-save record.");
    else
        Log("Economy co-save written (" + std::to_string(bytes.size()) + " bytes).");
}

void OnLoad(SKSESerializationInterface* serialization)
{
#ifdef STR_LIVE_PRICES
    RegionalPrices::Restore();
#endif
    gameLoaded = false;
    lastLoggedWorldDay = -1;
    pendingState.clear();
    pendingReset = true;
    if (!serialization || !serialization->GetNextRecordInfo || !serialization->ReadRecordData) return;
    UInt32 type = 0, version = 0, length = 0;
    while (serialization->GetNextRecordInfo(&type, &version, &length)) {
        if (type != kStateRecord) continue;
        if (version != kStateVersion || length == 0 || length > kMaximumStateBytes) {
            Log("Skipped incompatible economy co-save record.");
            continue;
        }
        std::vector<std::byte> bytes(length);
        if (serialization->ReadRecordData(bytes.data(), length) != length) {
            Log("Truncated economy co-save record.");
            continue;
        }
        pendingState = std::move(bytes);
    }
    if (pendingState.empty()) Log("No economy co-save record; starting from initial state.");
    ApplyPendingState();
}

void OnRevert(SKSESerializationInterface*)
{
#ifdef STR_LIVE_PRICES
    RegionalPrices::Restore();
#endif
    gameLoaded = false;
    lastLoggedWorldDay = -1;
    pendingState.clear();
    pendingReset = true;
    ApplyPendingState();
}
#endif

#ifdef STR_DIAGNOSTIC_FORMS
void LogIronIngotValue(const char* stage)
{
    // SKSE 2.3.1 GameForms.cpp offset, valid only for the runtime checked in SKSEPlugin_Load.
    constexpr std::uintptr_t lookupOffset = 0x001E5860;
    const auto gameBase = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    if (!gameBase) {
        Log(std::string(stage) + ": game module unavailable.");
        return;
    }
    const auto lookup = reinterpret_cast<_LookupFormByID>(gameBase + lookupOffset);
    TESForm* form = lookup(0x0005ACE4);
    if (!form || form->formType != kFormType_Misc) {
        Log(std::string(stage) + ": iron ingot missing or wrong form type.");
        return;
    }
    const auto* ingot = static_cast<TESObjectMISC*>(form);
    Log(std::string(stage) + ": live iron ingot value=" + std::to_string(ingot->value.value));
}

void LogGameDaysPassed()
{
    // SKSE 2.3.1 GameForms.cpp offset; used only in the opt-in diagnostic build.
    constexpr std::uintptr_t lookupEditorOffset = 0x001E5A30;
    const auto gameBase = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    if (!gameBase) return;
    const auto lookup = reinterpret_cast<_LookupFormByEditorID>(gameBase + lookupEditorOffset);
    TESForm* form = lookup("GameDaysPassed");
    if (!form || form->formType != kFormType_Global) {
        Log("GameDaysPassed global was not found by EditorID.");
        return;
    }
    const auto* global = static_cast<TESGlobal*>(form);
    float days = 0;
    std::memcpy(&days, &global->unk34, sizeof(days));
    Log("GameDaysPassed global form=" + std::to_string(form->formID) +
        " raw=" + std::to_string(global->unk34) + " float=" + std::to_string(days));
}
#endif

void OnSkseMessage(SKSEMessagingInterface::Message* message)
{
    if (!message) return;
#ifdef STR_NATIVE_ECONOMY
    if (message->type == SKSEMessagingInterface::kMessage_PreLoadGame) {
        gameLoaded = false;
        return;
    }
    if (message->type == SKSEMessagingInterface::kMessage_PostLoadGame &&
        reinterpret_cast<std::uintptr_t>(message->data) != 0) {
        ApplyPendingState();
        gameLoaded = true;
        TickEconomy();
#ifdef STR_LIVE_PRICES
        const auto changes = RegionalPrices::Update();
        if (!changes.empty()) Log("Regional prices: " + changes);
#endif
    }
    if (message->type == SKSEMessagingInterface::kMessage_NewGame) {
        gameLoaded = true;
        lastLoggedWorldDay = -1;
    }
#endif
#ifdef STR_DIAGNOSTIC_FORMS
    if (message->type == SKSEMessagingInterface::kMessage_PostLoadGame &&
        reinterpret_cast<std::uintptr_t>(message->data) != 0) {
        LogIronIngotValue("Save loaded");
        LogGameDaysPassed();
        return;
    }
#endif
    if (message->type != SKSEMessagingInterface::kMessage_DataLoaded) return;
#ifdef STR_DIAGNOSTIC_FORMS
    LogIronIngotValue("Data loaded");
#endif
    const auto folder = PluginDirectory();
    if (folder.empty()) return;
#ifdef STR_NATIVE_ECONOMY
    Log(NativeEconomy::Initialize(folder));
    ApplyPendingState();
#ifdef STR_LIVE_PRICES
    Log(RegionalPrices::Initialize());
#endif
    if (NativeEconomy::Ready()) StartEconomyTicker();
#endif
#ifndef STR_NATIVE_ECONOMY
    const auto snapshotPath = folder / "STRPrices.json";
    std::error_code error;
    const auto size = std::filesystem::file_size(snapshotPath, error);
    if (error)
        Log("Data loaded; STRPrices.json is missing or unreadable.");
    else
        Log("Data loaded; STRPrices.json found (" + std::to_string(size) + " bytes). Parsing and price application are pending.");
#endif
}
}

extern "C" {
__declspec(dllexport) SKSEPluginVersionData SKSEPlugin_Version = {
    SKSEPluginVersionData::kVersion,
    1,
    "SimulatedTradeRoutes",
    "Simulated Trade Routes contributors",
    "",
    0,
    0,
    {RUNTIME_VERSION_1_7_104, 0},
    PACKED_SKSE_VERSION
};

__declspec(dllexport) bool SKSEPlugin_Load(const SKSEInterface* skse)
{
    if (!skse || skse->isEditor || skse->runtimeVersion != RUNTIME_VERSION_1_7_104 ||
        !skse->QueryInterface || !skse->GetPluginHandle || !skse->GetReleaseIndex ||
        skse->GetReleaseIndex() < SKSE_VERSION_RELEASEIDX)
        return false;

    const auto* messaging = static_cast<SKSEMessagingInterface*>(skse->QueryInterface(kInterface_Messaging));
    if (!messaging || messaging->interfaceVersion < SKSEMessagingInterface::kInterfaceVersion ||
        !messaging->RegisterListener)
        return false;

    const auto handle = skse->GetPluginHandle();
    if (handle == kPluginHandle_Invalid || !messaging->RegisterListener(handle, "SKSE", OnSkseMessage))
        return false;
#ifdef STR_NATIVE_ECONOMY
    taskInterface = static_cast<SKSETaskInterface*>(skse->QueryInterface(kInterface_Task));
    if (!taskInterface || taskInterface->interfaceVersion < SKSETaskInterface::kInterfaceVersion ||
        !taskInterface->AddTask)
        return false;
    const auto* serialization = static_cast<SKSESerializationInterface*>(skse->QueryInterface(kInterface_Serialization));
    if (!serialization || serialization->version < SKSESerializationInterface::kVersion ||
        !serialization->SetUniqueID || !serialization->SetSaveCallback ||
        !serialization->SetLoadCallback || !serialization->SetRevertCallback ||
        !serialization->WriteRecord || !serialization->GetNextRecordInfo || !serialization->ReadRecordData)
        return false;
    serialization->SetUniqueID(handle, kSerializationId);
    serialization->SetSaveCallback(handle, OnSave);
    serialization->SetLoadCallback(handle, OnLoad);
    serialization->SetRevertCallback(handle, OnRevert);
#endif

    Log("Plugin loaded; waiting for SKSE data-loaded message.");
    return true;
}
}
