// SPDX-License-Identifier: GPL-3.0-or-later
#include "../../src/SkyrimTreader.Runtime/native_economy_bridge.h"

#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <vector>

void Check(bool condition, const char* description)
{
    if (!condition) throw std::runtime_error(description);
}

int wmain(int argc, wchar_t** argv)
{
    try {
        Check(argc == 2, "pass folder containing Native AOT DLL and JSON inputs");
        std::cout << NativeEconomy::Initialize(std::filesystem::path(argv[1])) << '\n';
        Check(NativeEconomy::Ready(), "bridge initialize");
        std::vector<NativeEconomy::TrackedItem> tracked;
        Check(NativeEconomy::GetTrackedItems(tracked) && tracked.size() == 4, "tracked catalog items");
        Check(tracked.front().formKey == "05ACE4:Skyrim.esm" && tracked.front().baseValue == 7 &&
            tracked.front().kind == 1, "iron ingot metadata");
        Check(NativeEconomy::GetPrice("markarth", "05ACE4:Skyrim.esm") == 7, "initial ingot price");
        Check(NativeEconomy::AdvanceToDay(5) == 5, "day advancement");
        Check(NativeEconomy::GetPrice("markarth", "05ACE4:Skyrim.esm") == 6, "regional price");
        Check(NativeEconomy::SyncWorldDay(200) == 5, "world day origin");
        Check(NativeEconomy::ApplyTrade(1, "markarth", "05ACE4:Skyrim.esm", 3) == 1, "trade applied");
        std::vector<std::byte> manifest;
        Check(NativeEconomy::CopyCaravanManifest(manifest) && !manifest.empty(), "caravan manifest copied");
        std::vector<std::byte> saved;
        Check(NativeEconomy::CopyState(saved), "state serialized");
        Check(NativeEconomy::Reset() == 0, "state reset");
        Check(NativeEconomy::LoadState(saved) == 5, "state restored");
        Check(NativeEconomy::ApplyTrade(1, "markarth", "05ACE4:Skyrim.esm", 3) == 0, "trade replay blocked");
        Check(NativeEconomy::SyncWorldDay(201) == 6, "world day after state restore");
        std::cout << "PASS C++ bridge: load, price, world day, trade, state roundtrip\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL " << error.what() << ": " << NativeEconomy::LastError() << '\n';
        return 1;
    }
}
