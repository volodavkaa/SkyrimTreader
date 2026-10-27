// SPDX-License-Identifier: GPL-3.0-or-later
#include "../../src/SkyrimTreader.Runtime/trade_capture_logic.h"

#include <iostream>
#include <stdexcept>

void Check(bool actual, const char* description)
{
    if (!actual) throw std::runtime_error(description);
}

int main()
{
    using TradeCapture::NetSoldToMerchant;
    constexpr auto player = TradeCapture::kPlayerFormId;
    constexpr std::uint32_t vendor = 0x1234;
    Check(NetSoldToMerchant(true, true, true, player, vendor, 3) == 3, "sale increases stock");
    Check(NetSoldToMerchant(true, true, true, vendor, player, 2) == -2, "purchase decreases stock");
    Check(NetSoldToMerchant(false, true, true, player, vendor, 3) == 0, "outside barter ignored");
    Check(NetSoldToMerchant(true, false, true, player, vendor, 3) == 0, "load transition ignored");
    Check(NetSoldToMerchant(true, true, false, player, vendor, 3) == 0, "untracked item ignored");
    Check(NetSoldToMerchant(true, true, true, 0, player, 3) == 0, "unowned source ignored");
    Check(NetSoldToMerchant(true, true, true, player, 0, 3) == 0, "unowned target ignored");
    Check(NetSoldToMerchant(true, true, true, vendor, 0x4567, 3) == 0, "NPC transfer ignored");
    Check(NetSoldToMerchant(true, true, true, player, vendor, 0) == 0, "empty transfer ignored");
    Check(NetSoldToMerchant(true, true, true, player, vendor, 0x80000000u) == 0, "overflow ignored");
    TradeCapture::Session session;
    Check(session.NetSoldToMerchant(true, true, player, vendor, 1) == 0, "closed session ignored");
    session.SetBarterOpen(true);
    Check(session.NetSoldToMerchant(false, true, player, vendor, 1) == 0, "loading does not bind vendor");
    Check(session.NetSoldToMerchant(true, true, player, vendor, 1) == 1, "first sale binds vendor");
    Check(session.NetSoldToMerchant(true, true, vendor, player, 2) == -2, "purchase from bound vendor");
    Check(session.NetSoldToMerchant(true, true, 0x4567, player, 1) == 0, "other vendor ignored");
    session.SetBarterOpen(false);
    session.SetBarterOpen(true);
    Check(session.NetSoldToMerchant(true, true, 0x4567, player, 1) == -1, "new menu binds new vendor");
    std::cout << "PASS trade capture classification\n";
}
