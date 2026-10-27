// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include <cstdint>
#include <limits>

namespace TradeCapture {
constexpr std::uint32_t kPlayerFormId = 0x14;

// Positive quantity adds stock to the merchant's region; negative removes it.
// Zero means the transfer cannot safely be attributed to a barter transaction.
inline int NetSoldToMerchant(bool barterOpen, bool gameLoaded, bool trackedItem,
    std::uint32_t fromFormId, std::uint32_t toFormId, std::uint32_t count)
{
    if (!barterOpen || !gameLoaded || !trackedItem || count == 0 ||
        count > static_cast<std::uint32_t>(std::numeric_limits<int>::max()) ||
        fromFormId == 0 || toFormId == 0 || fromFormId == toFormId)
        return 0;
    if (fromFormId == kPlayerFormId) return static_cast<int>(count);
    if (toFormId == kPlayerFormId) return -static_cast<int>(count);
    return 0;
}

class Session
{
public:
    void SetBarterOpen(bool open)
    {
        if (_open != open) _merchantFormId = 0;
        _open = open;
    }

    int NetSoldToMerchant(bool gameLoaded, bool trackedItem,
        std::uint32_t fromFormId, std::uint32_t toFormId, std::uint32_t count)
    {
        const int delta = TradeCapture::NetSoldToMerchant(_open, gameLoaded, trackedItem,
            fromFormId, toFormId, count);
        if (delta == 0) return 0;
        const auto merchant = fromFormId == kPlayerFormId ? toFormId : fromFormId;
        if (_merchantFormId != 0 && _merchantFormId != merchant) return 0;
        _merchantFormId = merchant;
        return delta;
    }

private:
    bool _open = false;
    std::uint32_t _merchantFormId = 0;
};
}
