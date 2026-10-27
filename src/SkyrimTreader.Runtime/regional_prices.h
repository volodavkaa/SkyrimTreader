// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include <cstdint>
#include <string>

namespace RegionalPrices {
std::string Initialize();
std::string Update();
void Restore();
const char* CurrentRegion();
const char* TrackedFormKey(std::uint32_t formId);
}
