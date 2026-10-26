// SPDX-License-Identifier: GPL-3.0-or-later
#include "regional_prices.h"
#include "native_economy_bridge.h"

#include <Windows.h>

#include <charconv>
#include <cstdint>
#include <string>
#include <vector>

using UInt8 = std::uint8_t;
using UInt16 = std::uint16_t;
using UInt32 = std::uint32_t;
using UInt64 = std::uint64_t;
using SInt8 = std::int8_t;
using SInt16 = std::int16_t;
using SInt32 = std::int32_t;
using SInt64 = std::int64_t;
#define STATIC_ASSERT(condition) static_assert(condition)
#define ASSERT(condition) static_assert(condition)
#define _MESSAGE(...) ((void)0)
#undef InterlockedDecrement
#define InterlockedDecrement(pointer) _InterlockedDecrement(reinterpret_cast<volatile long*>(pointer))
#include <skse64/GameForms.h>
#include <skse64/GameObjects.h>
#include <skse64/GameReferences.h>

namespace {
struct Item
{
    TESForm* form = nullptr;
    std::string key;
    int kind = 0;
    int original = 0;
    int applied = 0;
    bool enabled = false;
};

std::vector<Item> items;

std::uintptr_t GameBase() { return reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)); }

TESForm* LookupForm(UInt32 formId)
{
    // Verified against the installed SKSE 2.3.1 source for Skyrim 1.7.104.
    const auto lookup = reinterpret_cast<_LookupFormByID>(GameBase() + 0x001E5860);
    return lookup(formId);
}

bool ParseVanillaFormKey(const std::string& key, UInt32& formId)
{
    if (key.size() != 17 || key[6] != ':' || _stricmp(key.c_str() + 7, "Skyrim.esm") != 0) return false;
    UInt32 value = 0;
    const auto result = std::from_chars(key.data(), key.data() + 6, value, 16);
    if (result.ec != std::errc{} || result.ptr != key.data() + 6) return false;
    formId = value;
    return true;
}

int ReadValue(const Item& item)
{
    if (!item.form) return -1;
    switch (item.kind) {
    case 1:
        if (item.form->formType == kFormType_Misc)
            return static_cast<int>(static_cast<TESObjectMISC*>(item.form)->value.value);
        break;
    case 2:
        if (item.form->formType == kFormType_Potion) {
            auto* food = static_cast<AlchemyItem*>(item.form);
            if (food->itemData.flags & AlchemyItem::kFlag_ManualCalc)
                return static_cast<int>(food->itemData.value);
        }
        break;
    case 3:
        if (item.form->formType == kFormType_Weapon)
            return static_cast<int>(static_cast<TESObjectWEAP*>(item.form)->value.value);
        break;
    case 4:
        if (item.form->formType == kFormType_Armor)
            return static_cast<int>(static_cast<TESObjectARMO*>(item.form)->value.value);
        break;
    }
    return -1;
}

void WriteValue(const Item& item, int value)
{
    const auto safe = static_cast<UInt32>(value);
    switch (item.kind) {
    case 1: static_cast<TESObjectMISC*>(item.form)->value.value = safe; break;
    case 2: static_cast<AlchemyItem*>(item.form)->itemData.value = safe; break;
    case 3: static_cast<TESObjectWEAP*>(item.form)->value.value = safe; break;
    case 4: static_cast<TESObjectARMO*>(item.form)->value.value = safe; break;
    }
}

const char* CurrentRegion()
{
    if (!GameBase()) return nullptr;
    // g_thePlayer in SKSE 2.3.1 GameAPI.cpp. The city hierarchy is in GameForms.h.
    auto* player = *reinterpret_cast<PlayerCharacter**>(GameBase() + 0x03230778);
    if (!player) return nullptr;
    auto* location = player->locationAC8;
    for (int depth = 0; location && depth < 8; ++depth) {
        if (location->formType != kFormType_Location) return nullptr;
        switch (location->formID) {
        case 0x00018A59: return "markarth";
        case 0x00018A56: return "whiterun";
        case 0x00018A58: return "riften";
        }
        location = location->parentLoc;
    }
    return nullptr;
}
}

namespace RegionalPrices {
std::string Initialize()
{
    items.clear();
    if (!NativeEconomy::Ready() || !GameBase()) return "Regional prices unavailable.";
    std::vector<NativeEconomy::TrackedItem> catalog;
    if (!NativeEconomy::GetTrackedItems(catalog)) return "Regional catalog unavailable: " + NativeEconomy::LastError();
    int enabled = 0;
    for (const auto& entry : catalog) {
        UInt32 formId = 0;
        if (!ParseVanillaFormKey(entry.formKey, formId)) continue;
        Item item{ LookupForm(formId), entry.formKey, entry.kind, entry.baseValue, entry.baseValue, false };
        const int live = ReadValue(item);
        item.enabled = live == item.original;
        if (item.enabled) ++enabled;
        items.push_back(std::move(item));
    }
    return "Regional forms enabled " + std::to_string(enabled) + "/" + std::to_string(catalog.size()) + ".";
}

std::string Update()
{
    if (!NativeEconomy::Ready()) return {};
    const char* region = CurrentRegion();
    std::string changes;
    for (auto& item : items) {
        if (!item.enabled) continue;
        const int live = ReadValue(item);
        if (live != item.applied && live != item.original) {
            item.enabled = false;
            changes += "Skipped externally changed " + item.key + ". ";
            continue;
        }
        const int desired = region ? NativeEconomy::GetPrice(region, item.key.c_str()) : item.original;
        if (desired < 0) {
            item.enabled = false;
            changes += "Price lookup failed for " + item.key + ". ";
            continue;
        }
        if (live != desired) {
            WriteValue(item, desired);
            changes += item.key + " " + std::to_string(live) + "->" + std::to_string(desired) + ". ";
        }
        item.applied = desired;
    }
    return changes;
}

void Restore()
{
    for (auto& item : items) {
        if (item.enabled && ReadValue(item) == item.applied)
            WriteValue(item, item.original);
        item.applied = item.original;
    }
}
}
