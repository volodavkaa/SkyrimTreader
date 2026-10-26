cmake_minimum_required(VERSION 3.24)
project(STRNativeBridgeChecks LANGUAGES CXX)
add_executable(STRNativeBridgeChecks
    main.cpp
    ../../src/SkyrimTreader.Runtime/native_economy_bridge.cpp)
target_compile_features(STRNativeBridgeChecks PRIVATE cxx_std_20)
target_compile_definitions(STRNativeBridgeChecks PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)

