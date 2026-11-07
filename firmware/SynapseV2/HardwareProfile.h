#pragma once

#include <Arduino.h>

#define SYNAPSE_PROFILE_BENCH_ESP32S3 1
#define SYNAPSE_PROFILE_PRESENTATION_WAVESHARE 2

// Select the hardware explicitly at compile time. The verified presentation
// controller is the default profile.
#ifndef SYNAPSE_HARDWARE_PROFILE
#define SYNAPSE_HARDWARE_PROFILE SYNAPSE_PROFILE_PRESENTATION_WAVESHARE
#endif

#if SYNAPSE_HARDWARE_PROFILE == SYNAPSE_PROFILE_BENCH_ESP32S3

constexpr const char* HARDWARE_PROFILE_NAME = "bench-esp32s3";
constexpr gpio_num_t LED_PIN = GPIO_NUM_4;
constexpr uint8_t TOUCH_PIN = 6;
constexpr uint16_t NUM_LEDS = 24;

#elif SYNAPSE_HARDWARE_PROFILE == SYNAPSE_PROFILE_PRESENTATION_WAVESHARE

// Final controller: ESP32-S3 rev. 0.2, 32 MB octal flash and 16 MB OPI PSRAM.
// GPIO 47 keeps the LED data line separate from the UART TX boot output.
constexpr const char* HARDWARE_PROFILE_NAME = "presentation-esp32s3-gpio47";
constexpr gpio_num_t LED_PIN = GPIO_NUM_47;
constexpr uint8_t TOUCH_PIN = 1;
constexpr uint16_t NUM_LEDS = 24;

#else

#error "Unknown SYNAPSE_HARDWARE_PROFILE"

#endif

static_assert(NUM_LEDS > 0, "NUM_LEDS must be positive");
static_assert(LED_PIN != static_cast<gpio_num_t>(TOUCH_PIN),
              "Touch and LED data pins must be different");
