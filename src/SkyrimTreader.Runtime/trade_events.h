// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include <string>

namespace TradeEvents {
bool Register(const bool* gameLoaded, void (*log)(const std::string&), void (*writeStatus)());
void ResetSession();
}
