cmake_minimum_required(VERSION 3.16)
project(joyveyor CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release)
endif()

# Core simulation library (engine-agnostic)
file(GLOB_RECURSE JV_CORE_SOURCES CONFIGURE_DEPENDS core/*.cpp)
add_library(joyveyor_core STATIC ${JV_CORE_SOURCES})
target_include_directories(joyveyor_core PUBLIC core)

# Unit tests: one executable per tests/<name>.cpp
enable_testing()
file(GLOB JV_TEST_SOURCES CONFIGURE_DEPENDS tests/*.cpp)
foreach(test_src ${JV_TEST_SOURCES})
  get_filename_component(test_name ${test_src} NAME_WE)
  add_executable(${test_name} ${test_src})
  target_link_libraries(${test_name} PRIVATE joyveyor_core)
  # The session-bridge test drives the C API (unity/jv_unity_bridge.cpp).
  if(test_name STREQUAL "test_bridge_session")
    target_sources(${test_name} PRIVATE unity/jv_unity_bridge.cpp)
    target_include_directories(${test_name} PRIVATE . unity)
  endif()
  add_test(NAME ${test_name} COMMAND ${test_name})
endforeach()

# Benchmarks: one executable per bench/<name>.cpp
file(GLOB JV_BENCH_SOURCES CONFIGURE_DEPENDS bench/*.cpp)
foreach(bench_src ${JV_BENCH_SOURCES})
  get_filename_component(bench_name ${bench_src} NAME_WE)
  add_executable(${bench_name} ${bench_src})
  target_link_libraries(${bench_name} PRIVATE joyveyor_core)
endforeach()
