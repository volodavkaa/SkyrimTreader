// SPDX-License-Identifier: GPL-3.0-or-later
#include <Windows.h>

#include <cassert>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>
#include <system_error>

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
#ifdef STR_DIAGNOSTIC_FORMS
#define STATIC_ASSERT(condition) static_assert(condition)
#define ASSERT(condition) assert(condition)
#define _MESSAGE(...) ((void)0)
#define InterlockedDecrement(pointer) _InterlockedDecrement(reinterpret_cast<volatile long*>(pointer))
#include <skse64/GameForms.h>
#include <skse64/GameObjects.h>
#endif

namespace {
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
#endif

void OnSkseMessage(SKSEMessagingInterface::Message* message)
{
    if (!message) return;
#ifdef STR_DIAGNOSTIC_FORMS
    if (message->type == SKSEMessagingInterface::kMessage_PostLoadGame &&
        reinterpret_cast<std::uintptr_t>(message->data) != 0) {
        LogIronIngotValue("Save loaded");
        return;
    }
#endif
    if (message->type != SKSEMessagingInterface::kMessage_DataLoaded) return;
#ifdef STR_DIAGNOSTIC_FORMS
    LogIronIngotValue("Data loaded");
#endif
    const auto folder = PluginDirectory();
    if (folder.empty()) return;
    const auto snapshotPath = folder / "STRPrices.json";
    std::error_code error;
    const auto size = std::filesystem::file_size(snapshotPath, error);
    if (error)
        Log("Data loaded; STRPrices.json is missing or unreadable.");
    else
        Log("Data loaded; STRPrices.json found (" + std::to_string(size) + " bytes). Parsing and price application are pending.");
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

    Log("Plugin loaded; waiting for SKSE data-loaded message.");
    return true;
}
}
