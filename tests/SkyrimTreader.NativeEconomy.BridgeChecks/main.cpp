// SPDX-License-Identifier: GPL-3.0-or-later
#include "../../src/SkyrimTreader.Runtime/native_economy_bridge.h"

#include <algorithm>
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
        Check(NativeEconomy::GetTrackedItems(tracked) && tracked.size() >= 4, "tracked catalog items");
        const auto ingot = std::find_if(tracked.begin(), tracked.end(), [](const auto& item) {
            return item.formKey == "05ACE4:Skyrim.esm";
        });
        Check(ingot != tracked.end() && ingot->baseValue == 7 && ingot->kind == 1, "iron ingot metadata");
        Check(NativeEconomy::GetPrice("markarth", "05ACE4:Skyrim.esm") == 7, "initial ingot price");
        Check(NativeEconomy::AdvanceToDay(5) == 5, "day advancement");
        Check(NativeEconomy::GetPrice("markarth", "05ACE4:Skyrim.esm") == 6, "regional price");
        for (const auto* region : { "markarth", "whiterun", "riften" })
            for (const auto& item : tracked)
                Check(NativeEconomy::GetPrice(region, item.formKey.c_str()) > 0,
                    "every tracked item has a regional price");
        Check(NativeEconomy::SyncWorldDay(200) == 5, "world day origin");
        Check(NativeEconomy::ApplyTrade(1, "markarth", "05ACE4:Skyrim.esm", 3) == 1, "trade applied");
        Check(NativeEconomy::ApplyTradeAuto("markarth", "05ACE4:Skyrim.esm", 1) == 2,
            "automatic trade sequence");
        std::vector<std::byte> manifest;
        Check(NativeEconomy::CopyCaravanManifest(manifest) && !manifest.empty(), "caravan manifest copied");
        std::vector<std::byte> status;
        Check(NativeEconomy::CopyStatus(status) && !status.empty(), "economy status copied");
        std::vector<std::byte> visiblePlan;
        Check(NativeEconomy::CopyVisibleCaravanPlan("whiterun", visiblePlan) && visiblePlan.size() > 4,
            "visible caravan plan copied");
        std::vector<std::byte> physicalPlan;
        Check(NativeEconomy::CopyPhysicalCaravanPlan("whiterun", physicalPlan) &&
            std::string(reinterpret_cast<const char*>(physicalPlan.data()), physicalPlan.size()) == "null",
            "unverified physical roads cannot spawn a caravan");
        std::vector<std::byte> saved;
        Check(NativeEconomy::CopyState(saved), "state serialized");
        Check(NativeEconomy::Reset() == 0, "state reset");
        Check(NativeEconomy::LoadState(saved) == 5, "state restored");
        Check(NativeEconomy::ApplyTrade(1, "markarth", "05ACE4:Skyrim.esm", 3) == 0, "trade replay blocked");
        Check(NativeEconomy::ApplyTradeAuto("markarth", "05ACE4:Skyrim.esm", 1) == 3,
            "automatic trade sequence survives restore");
        Check(NativeEconomy::SyncWorldDay(201) == 6, "world day after state restore");
        std::cout << "PASS C++ bridge: load, price, world day, trade, state roundtrip\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL " << error.what() << ": " << NativeEconomy::LastError() << '\n';
        return 1;
    }
}
