// SPDX-License-Identifier: GPL-3.0-or-later
#include <Windows.h>

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string_view>
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

namespace {
SKSEMessagingInterface::EventCallback messageCallback = nullptr;
SKSESerializationInterface::EventCallback saveCallback = nullptr;
SKSESerializationInterface::EventCallback loadCallback = nullptr;
SKSESerializationInterface::EventCallback revertCallback = nullptr;
UInt32 uniqueId = 0;
UInt32 recordType = 0;
UInt32 recordVersion = 0;
std::vector<std::byte> record;
bool recordAvailable = false;

void Check(bool condition, const char* description)
{
    if (!condition) throw std::runtime_error(description);
}

bool RegisterListener(PluginHandle, const char*, SKSEMessagingInterface::EventCallback callback)
{
    messageCallback = callback;
    return true;
}
void SetUniqueId(PluginHandle, UInt32 id) { uniqueId = id; }
void SetSave(PluginHandle, SKSESerializationInterface::EventCallback callback) { saveCallback = callback; }
void SetLoad(PluginHandle, SKSESerializationInterface::EventCallback callback) { loadCallback = callback; }
void SetRevert(PluginHandle, SKSESerializationInterface::EventCallback callback) { revertCallback = callback; }
bool WriteRecord(UInt32 type, UInt32 version, const void* bytes, UInt32 length)
{
    recordType = type;
    recordVersion = version;
    record.assign(static_cast<const std::byte*>(bytes), static_cast<const std::byte*>(bytes) + length);
    return true;
}
bool GetNextRecordInfo(UInt32* type, UInt32* version, UInt32* length)
{
    if (!recordAvailable) return false;
    recordAvailable = false;
    *type = recordType;
    *version = recordVersion;
    *length = static_cast<UInt32>(record.size());
    return true;
}
UInt32 ReadRecordData(void* bytes, UInt32 length)
{
    const auto count = static_cast<UInt32>(std::min<std::size_t>(length, record.size()));
    std::memcpy(bytes, record.data(), count);
    return count;
}
bool ResolveFormId(UInt32 formId, UInt32* resolved)
{
    if (!resolved || formId == 0) return false;
    *resolved = formId;
    return true;
}

SKSEMessagingInterface messaging{ SKSEMessagingInterface::kInterfaceVersion, RegisterListener };
SKSESerializationInterface serialization{};
void AddTask(TaskDelegate*) {}
SKSETaskInterface tasks{ SKSETaskInterface::kInterfaceVersion, AddTask };

UInt32 ReadU32(const std::vector<std::byte>& bytes, std::size_t offset)
{
    Check(offset + sizeof(UInt32) <= bytes.size(), "truncated SKSE co-save");
    UInt32 value = 0;
    std::memcpy(&value, bytes.data() + offset, sizeof(value));
    return value;
}

void ReadGameCosave(const std::filesystem::path& path)
{
    std::ifstream input(path, std::ios::binary | std::ios::ate);
    Check(input.good(), "open game SKSE co-save");
    const auto size = input.tellg();
    Check(size > 0 && size < 16 * 1024 * 1024, "game SKSE co-save size");
    std::vector<std::byte> bytes(static_cast<std::size_t>(size));
    input.seekg(0);
    input.read(reinterpret_cast<char*>(bytes.data()), size);
    Check(input.good(), "read game SKSE co-save");
    Check(ReadU32(bytes, 0) == 0x45534B53, "SKSE co-save signature");
    for (std::size_t offset = 4; offset + 28 <= bytes.size(); ++offset) {
        if (ReadU32(bytes, offset) != uniqueId) continue;
        const auto pluginLength = ReadU32(bytes, offset + 8);
        const auto type = ReadU32(bytes, offset + 12);
        const auto version = ReadU32(bytes, offset + 16);
        const auto length = ReadU32(bytes, offset + 20);
        if (pluginLength != length + 12 || type != 0x53525453 || version != 1 ||
            offset + 24 + length > bytes.size()) continue;
        recordType = type;
        recordVersion = version;
        record.assign(bytes.begin() + offset + 24, bytes.begin() + offset + 24 + length);
        recordAvailable = true;
        return;
    }
    throw std::runtime_error("STRS record not found in game SKSE co-save");
}

void* QueryInterface(UInt32 id)
{
    if (id == kInterface_Messaging) return &messaging;
    if (id == kInterface_Serialization) return &serialization;
    if (id == kInterface_Task) return &tasks;
    return nullptr;
}
PluginHandle GetHandle() { return 7; }
UInt32 GetReleaseIndex() { return SKSE_VERSION_RELEASEIDX; }
}

int wmain(int argc, wchar_t** argv)
{
    try {
        Check(argc >= 2 && argc <= 4,
            "pass native economy package plugin directory [game .skse co-save] [--expect-catalog-mismatch]");
        Check(argc != 4 || std::wstring_view(argv[3]) == L"--expect-catalog-mismatch",
            "unknown co-save test option");
        const auto folder = std::filesystem::path(argv[1]);
        const auto dll = LoadLibraryW((folder / L"SimulatedTradeRoutes.dll").c_str());
        Check(dll != nullptr, "load SKSE DLL");
        const auto load = reinterpret_cast<bool (*)(const SKSEInterface*)>(GetProcAddress(dll, "SKSEPlugin_Load"));
        Check(load != nullptr, "SKSEPlugin_Load export");
        serialization.version = SKSESerializationInterface::kVersion;
        serialization.SetUniqueID = SetUniqueId;
        serialization.SetSaveCallback = SetSave;
        serialization.SetLoadCallback = SetLoad;
        serialization.SetRevertCallback = SetRevert;
        serialization.WriteRecord = WriteRecord;
        serialization.GetNextRecordInfo = GetNextRecordInfo;
        serialization.ReadRecordData = ReadRecordData;
        serialization.ResolveFormId = ResolveFormId;
        SKSEInterface skse{};
        skse.runtimeVersion = RUNTIME_VERSION_1_7_104;
        skse.QueryInterface = QueryInterface;
        skse.GetPluginHandle = GetHandle;
        skse.GetReleaseIndex = GetReleaseIndex;
        Check(load(&skse), "register SKSE callbacks");
        Check(uniqueId != 0 && messageCallback && saveCallback && loadCallback && revertCallback,
            "all serialization callbacks registered");
        SKSEMessagingInterface::Message message{ "SKSE", SKSEMessagingInterface::kMessage_DataLoaded, 0, nullptr };
        messageCallback(&message);

        const auto aot = GetModuleHandleW(L"SkyrimTreader.NativeEconomy.dll");
        Check(aot != nullptr, "Native AOT DLL initialized by SKSE message");
        const auto advance = reinterpret_cast<int (*)(int)>(GetProcAddress(aot, "STR_AdvanceToDay"));
        const auto price = reinterpret_cast<int (*)(const char*, const char*)>(GetProcAddress(aot, "STR_GetPrice"));
        Check(advance && price, "native economy entry points");
        Check(advance(5) == 5, "advance to day 5");
        Check(price("markarth", "05ACE4:Skyrim.esm") == 6, "day 5 price");
        saveCallback(&serialization);
        Check(recordType == 0x53525453 && recordVersion == 1 && !record.empty(), "versioned co-save record");
        Check(advance(6) == 6, "advance to day 6");
        recordAvailable = true;
        loadCallback(&serialization);
        Check(price("markarth", "05ACE4:Skyrim.esm") == 6, "co-save restored day 5");
        revertCallback(&serialization);
        Check(price("markarth", "05ACE4:Skyrim.esm") == 7, "revert resets state");
        Check(advance(5) == 5, "advance again after revert");
        loadCallback(&serialization);
        Check(price("markarth", "05ACE4:Skyrim.esm") == 7, "missing co-save resets state");
        if (argc >= 3) {
            ReadGameCosave(std::filesystem::path(argv[2]));
            loadCallback(&serialization);
            const auto syncWorldDay = reinterpret_cast<int (*)(int)>(GetProcAddress(aot, "STR_SyncWorldDay"));
            Check(syncWorldDay != nullptr, "world day export available");
            if (argc == 4) {
                Check(price("markarth", "05ACE4:Skyrim.esm") == 7,
                    "changed catalog leaves initial price after rejected co-save");
                Check(syncWorldDay(0) == 0, "changed catalog leaves initial day after rejected co-save");
                std::cout << "PASS changed catalog: old game co-save rejected without partial state\n";
            } else {
                Check(price("markarth", "05ACE4:Skyrim.esm") == 6, "real game co-save restores day 5 price");
                Check(syncWorldDay(0) == 5, "real game co-save ignores rewound clock");
                Check(syncWorldDay(6) == 6, "real game co-save advances from restored origin");
                std::cout << "PASS real game SKSE co-save: restored day 5 and advanced to day 6\n";
            }
        }
        std::cout << "PASS SKSE serialization: register, save, restore, revert, missing record\n";
        // Native AOT and SKSE DLL remain loaded until process exit.
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL " << error.what() << '\n';
        return 1;
    }
}
