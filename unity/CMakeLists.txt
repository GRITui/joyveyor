cmake_minimum_required(VERSION 3.16)
project(jv_unity CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release)
endif()

file(GLOB_RECURSE JV_CORE_SOURCES CONFIGURE_DEPENDS ../core/*.cpp)
add_library(jv_unity SHARED jv_unity_bridge.cpp ${JV_CORE_SOURCES})
target_include_directories(jv_unity PRIVATE ..)
set_target_properties(jv_unity PROPERTIES PREFIX "lib")
