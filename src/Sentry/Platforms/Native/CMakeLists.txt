cmake_minimum_required(VERSION 3.18)
# enables support for CMAKE_MSVC_RUNTIME_LIBRARY
cmake_policy(SET CMP0091 NEW)

project(sentry LANGUAGES C CXX)

if (WIN32)
  # Include debug info in the static library itself. See https://github.com/getsentry/sentry-native/issues/895 for context.
  set(CMAKE_C_FLAGS_RELWITHDEBINFO "/Z7 /O2 /Ob1 /DNDEBUG" CACHE STRING "C Flags for RelWithDebInfo" FORCE)
  set(CMAKE_CXX_FLAGS_RELWITHDEBINFO "/Z7 /O2 /Ob1 /DNDEBUG" CACHE STRING "CXX Flags for RelWithDebInfo" FORCE)
endif()

if(APPLE)
  set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "Architectures to build for on macOS" FORCE)
  set(CMAKE_OSX_DEPLOYMENT_TARGET "12.0" CACHE STRING "Minimum macOS version to support" FORCE)
endif()

set(SENTRY_SDK_NAME "sentry.native.dotnet" CACHE STRING "The SDK name to report when sending events." FORCE)
set(SENTRY_BUILD_SHARED_LIBS OFF CACHE BOOL "Build shared libraries")
set(SENTRY_BACKEND "inproc" CACHE STRING "The sentry backend responsible for reporting crashes." FORCE)
set(SENTRY_TRANSPORT "none" CACHE STRING "The HTTP transport that sentry uses to submit events to the sentry server" FORCE)

add_subdirectory(
  "${CMAKE_CURRENT_LIST_DIR}/../../../../modules/sentry-native"
  "${CMAKE_BINARY_DIR}/sentry-native"
)

target_sources(sentry PRIVATE
  sentry_dotnet_logger.c
  sentry_dotnet_logger.h
)
