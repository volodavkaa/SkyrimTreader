// SPDX-License-Identifier: GPL-3.0-or-later
#include "trade_events.h"
#include "trade_capture_logic.h"
#include "regional_prices.h"
#include "native_economy_bridge.h"

#include <Windows.h>

#include <cstdint>
#include <string>

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
#include <skse64/GameEvents.h>

namespace {
const bool* loaded = nullptr;
void (*writeLog)(const std::string&) = nullptr;
void (*saveStatus)() = nullptr;
TradeCapture::Session session;
bool registered = false;

class MenuSink final : public BSTEventSink<MenuOpenCloseEvent>
{
public:
    EventResult ReceiveEvent(MenuOpenCloseEvent* event, EventDispatcher<MenuOpenCloseEvent>*) override
    {
        if (event && _stricmp(event->menuName.c_str(), "BarterMenu") == 0)
            session.SetBarterOpen(event->opening);
        return kEvent_Continue;
    }
};

class ContainerSink final : public BSTEventSink<TESContainerChangedEvent>
{
public:
    EventResult ReceiveEvent(TESContainerChangedEvent* event, EventDispatcher<TESContainerChangedEvent>*) override
    {
        if (!event || !loaded || !*loaded || !NativeEconomy::Ready())
            return kEvent_Continue;
        const char* key = RegionalPrices::TrackedFormKey(event->itemFormId);
        const char* region = RegionalPrices::CurrentRegion();
        if (!region) return kEvent_Continue;
        const int delta = session.NetSoldToMerchant(*loaded, key != nullptr,
            event->fromFormId, event->toFormId, event->count);
        if (delta == 0) return kEvent_Continue;
        const auto sequence = NativeEconomy::ApplyTradeAuto(region, key, delta);
        if (writeLog) {
            if (sequence < 0)
                writeLog("Barter transfer rejected: " + NativeEconomy::LastError());
            else
                writeLog("Barter transfer #" + std::to_string(sequence) + " " + region + " " + key +
                    " delta=" + std::to_string(delta) + ".");
        }
        if (sequence >= 0 && saveStatus) saveStatus();
        return kEvent_Continue;
    }
};

MenuSink menuSink;
ContainerSink containerSink;
}

namespace TradeEvents {
bool Register(const bool* gameLoaded, void (*log)(const std::string&), void (*writeStatus)())
{
    if (registered) return true;
    const auto base = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    if (!base || !gameLoaded) return false;
    // Offsets from the installed SKSE 2.3.1 source, for Skyrim 1.7.104 only.
    auto* menu = *reinterpret_cast<void**>(base + 0x0219E5C0);
    using GetDispatchers = EventDispatcherList* (*)();
    auto* events = reinterpret_cast<GetDispatchers>(base + 0x001DA8F0)();
    if (!menu || !events) return false;
    using AddSink = void (*)(void*, void*);
    const auto addSink = reinterpret_cast<AddSink>(base + 0x005EB480);
    loaded = gameLoaded;
    writeLog = log;
    saveStatus = writeStatus;
    addSink(reinterpret_cast<char*>(menu) + 0x8, &menuSink);
    addSink(&events->unk370, &containerSink);
    registered = true;
    return true;
}

void ResetSession() { session.SetBarterOpen(false); }
}
