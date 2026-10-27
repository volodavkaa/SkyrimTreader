cmake_minimum_required(VERSION 3.24)
project(STRTradeCaptureChecks LANGUAGES CXX)
add_executable(STRTradeCaptureChecks main.cpp)
target_compile_features(STRTradeCaptureChecks PRIVATE cxx_std_20)
