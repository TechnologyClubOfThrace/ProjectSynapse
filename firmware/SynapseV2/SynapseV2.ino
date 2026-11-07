/*
  SYNAPSE v2 — Συλλογική αφή, φωτισμός RGBWC και τηλεμετρία εφαρμογής

  Πλατφόρμα: ESP32-S3
  Οδήγηση LED: ESP-IDF RMT TX
  Πρωτόκολλο επικοινωνίας με την εφαρμογή:
    LEVEL,<0-7>,VALUE,<raw>,SMOOTH,<smooth>,SEQ,<seq>,UPTIME,<ms>

  Το LEVEL είναι μόνο διαγνωστική ένδειξη του firmware. Η εφαρμογή Windows
  διαβάζει την τιμή SMOOTH, εφαρμόζει το ενεργό profile βαθμονόμησης και
  αποφασίζει εκείνη ποιο μουσικό επίπεδο θα ενεργοποιηθεί.
*/

#include <Arduino.h>
#include "driver/rmt_tx.h"
#include "HardwareProfile.h"

// =============================================================================
// ΡΥΘΜΙΣΕΙΣ ΣΥΜΠΕΡΙΦΟΡΑΣ
// Οι ρυθμίσεις της συγκεκριμένης πλακέτας βρίσκονται στο HardwareProfile.h,
// ώστε να μην μπερδέψουμε τη bench πλακέτα με εκείνη της παρουσίασης.
// =============================================================================

constexpr uint32_t TOUCH_MIN_RAW = 52000;
constexpr uint32_t TOUCH_MAX_RAW = 350000;

constexpr float LED_TOUCH_SMOOTHING = 0.06f;
constexpr float ENERGY_SPEED = 0.009f;
constexpr float ENERGY_DECAY = 0.003f;

constexpr uint8_t IDLE_BRIGHTNESS = 5;
constexpr uint8_t MAX_BRIGHTNESS = 150;
constexpr float FADE_SECTIONS = 6.5f;

constexpr uint8_t YELLOW_R = 255;
constexpr uint8_t YELLOW_G = 128;
constexpr uint8_t YELLOW_B = 0;
constexpr uint8_t MID_R = 230;
constexpr uint8_t MID_G = 200;
constexpr uint8_t MID_B = 15;
constexpr uint8_t GREEN_R = 200;
constexpr uint8_t GREEN_G = 255;
constexpr uint8_t GREEN_B = 30;
constexpr uint8_t WHITE_1 = 0;
constexpr uint8_t WHITE_2 = 0;

constexpr uint32_t SAMPLE_INTERVAL_MS = 20;
constexpr uint32_t HEARTBEAT_INTERVAL_MS = 1000;
constexpr uint32_t LOOP_DELAY_MS = 10;
constexpr float TELEMETRY_FILTER_ALPHA = 0.16f;

// Προσωρινά διαγνωστικά όρια. Μας βοηθούν να βλέπουμε μια ένδειξη LEVEL στο
// Serial, αλλά δεν ελέγχουν τον ήχο. Η τελική απόφαση ανήκει στην εφαρμογή.
constexpr uint32_t DIAGNOSTIC_THRESHOLDS[7] = {
  240000, 440000, 470000, 500000, 535000, 575000, 610000
};
constexpr uint32_t DIAGNOSTIC_HYSTERESIS = 7000;

static_assert(TOUCH_MAX_RAW > TOUCH_MIN_RAW, "Touch range must be increasing");
static_assert(IDLE_BRIGHTNESS <= MAX_BRIGHTNESS, "Brightness range is invalid");

// =============================================================================
// ΧΡΟΝΙΣΜΟΙ RGBWC / RMT
// Οι τιμές προέρχονται από τον λειτουργικό κώδικα v1 και δεν αλλάζουν χωρίς
// μέτρηση πάνω στην πραγματική ταινία LED.
// =============================================================================

constexpr uint32_t RMT_RESOLUTION_HZ = 40000000;
constexpr uint16_t T0H_TICKS = 14;
constexpr uint16_t T0L_TICKS = 32;
constexpr uint16_t T1H_TICKS = 28;
constexpr uint16_t T1L_TICKS = 24;
constexpr uint16_t RESET_US = 300;

rmt_channel_handle_t txChannel = nullptr;
rmt_encoder_handle_t copyEncoder = nullptr;

// =============================================================================
// ΚΑΤΑΣΤΑΣΗ ΑΦΗΣ, ΦΩΤΙΣΜΟΥ ΚΑΙ ΤΗΛΕΜΕΤΡΙΑΣ
// Κρατάμε διαφορετικό φίλτρο για τα LED και διαφορετικό για την εφαρμογή.
// Έτσι ο φωτισμός παραμένει ομαλός χωρίς να αλλοιώνονται οι τιμές βαθμονόμησης.
// =============================================================================

uint32_t rawTouch = 0;
float telemetrySmooth = 0.0f;
bool telemetryFilterReady = false;
float ledTouchFiltered = 0.0f;
float energy = 0.0f;

int diagnosticLevel = 0;
uint32_t messageSequence = 0;
uint32_t lastSampleAt = 0;
uint32_t lastMessageAt = 0;

// =============================================================================
// ΕΞΟΔΟΣ RGBWC
// =============================================================================

void encodeByte(uint8_t value, rmt_symbol_word_t* symbols) {
  // Μετατρέπουμε κάθε bit στους δύο παλμούς που περιμένει η addressable ταινία.
  for (int bit = 7; bit >= 0; --bit) {
    const bool one = (value & (1U << bit)) != 0;
    const int index = 7 - bit;
    symbols[index].level0 = 1;
    symbols[index].duration0 = one ? T1H_TICKS : T0H_TICKS;
    symbols[index].level1 = 0;
    symbols[index].duration1 = one ? T1L_TICKS : T0L_TICKS;
  }
}

void sendSegment(
  uint8_t red,
  uint8_t green,
  uint8_t blue,
  uint8_t white1,
  uint8_t white2
) {
  // Κάθε section λαμβάνει έξι bytes: RGB, δύο λευκά κανάλια και ένα μηδενικό
  // έκτο byte, όπως ακριβώς στον δοκιμασμένο controller της v1.
  rmt_symbol_word_t symbols[48];
  encodeByte(red, &symbols[0]);
  encodeByte(green, &symbols[8]);
  encodeByte(blue, &symbols[16]);
  encodeByte(white1, &symbols[24]);
  encodeByte(white2, &symbols[32]);
  encodeByte(0, &symbols[40]);

  rmt_transmit_config_t transmitConfig = {};
  transmitConfig.loop_count = 0;
  ESP_ERROR_CHECK(rmt_transmit(
    txChannel,
    copyEncoder,
    symbols,
    sizeof(symbols),
    &transmitConfig));
  ESP_ERROR_CHECK(rmt_tx_wait_all_done(txChannel, portMAX_DELAY));
}

void showStrip() {
  // Η energy εκφράζει πόσο μήκος της κατασκευής έχει «γεμίσει» με φως.
  // Στο άκρο εφαρμόζεται σταδιακό σβήσιμο, ώστε η μετάβαση να μη φαίνεται σκληρή.
  const float energyPosition = energy * NUM_LEDS;
  const float split65 = NUM_LEDS * 0.65f;
  const float split75 = NUM_LEDS * 0.75f;

  for (uint16_t index = 0; index < NUM_LEDS; ++index) {
    float intensity = 0.0f;
    float activeBrightness = IDLE_BRIGHTNESS;

    if (energy > 0.0001f) {
      if (index < floorf(energyPosition)) {
        intensity = 1.0f;
      } else if (index == floorf(energyPosition)) {
        intensity = energyPosition - floorf(energyPosition);
      }

      if (FADE_SECTIONS > 0.0f) {
        const float distanceFromEnd = energyPosition - index;
        if (distanceFromEnd > 0.0f && distanceFromEnd < FADE_SECTIONS) {
          intensity *= distanceFromEnd / FADE_SECTIONS;
        }
      }

      intensity = constrain(intensity, 0.0f, 1.0f);
      activeBrightness = IDLE_BRIGHTNESS +
        intensity * (MAX_BRIGHTNESS - IDLE_BRIGHTNESS);
    }

    const uint8_t brightness = static_cast<uint8_t>(
      constrain(activeBrightness, 0.0f, 255.0f));

    uint8_t baseRed;
    uint8_t baseGreen;
    uint8_t baseBlue;
    // Το χρώμα εξαρτάται από τη θέση πάνω στην εγκατάσταση και όχι από τον
    // αριθμό των ατόμων. Έτσι διατηρείται η οπτική αφήγηση της v1.
    if (index >= split75) {
      baseRed = GREEN_R;
      baseGreen = GREEN_G;
      baseBlue = GREEN_B;
    } else if (index >= split65) {
      baseRed = MID_R;
      baseGreen = MID_G;
      baseBlue = MID_B;
    } else {
      baseRed = YELLOW_R;
      baseGreen = YELLOW_G;
      baseBlue = YELLOW_B;
    }

    const uint8_t red = (static_cast<uint16_t>(baseRed) * brightness) / 255;
    const uint8_t green = (static_cast<uint16_t>(baseGreen) * brightness) / 255;
    const uint8_t blue = (static_cast<uint16_t>(baseBlue) * brightness) / 255;
    sendSegment(red, green, blue, WHITE_1, WHITE_2);
  }

  gpio_set_level(LED_PIN, 0);
  // Το χαμηλό διάστημα στο τέλος ενημερώνει την ταινία ότι ολοκληρώθηκε frame.
  delayMicroseconds(RESET_US);
}

void initializeLedTransport() {
  // Δημιουργούμε ένα RMT TX channel και έναν copy encoder. Η αρχικοποίηση
  // γίνεται μία φορά στο setup· η loop στέλνει μόνο έτοιμα σύμβολα.
  pinMode(static_cast<uint8_t>(LED_PIN), OUTPUT);
  digitalWrite(static_cast<uint8_t>(LED_PIN), LOW);

  rmt_tx_channel_config_t channelConfig = {};
  channelConfig.gpio_num = LED_PIN;
  channelConfig.clk_src = RMT_CLK_SRC_DEFAULT;
  channelConfig.resolution_hz = RMT_RESOLUTION_HZ;
  channelConfig.mem_block_symbols = 64;
  channelConfig.trans_queue_depth = 4;
  channelConfig.flags.invert_out = false;
  channelConfig.flags.with_dma = false;
  ESP_ERROR_CHECK(rmt_new_tx_channel(&channelConfig, &txChannel));

  rmt_copy_encoder_config_t encoderConfig = {};
  ESP_ERROR_CHECK(rmt_new_copy_encoder(&encoderConfig, &copyEncoder));
  ESP_ERROR_CHECK(rmt_enable(txChannel));
}

// =============================================================================
// ΑΦΗ ΚΑΙ ΤΗΛΕΜΕΤΡΙΑ
// =============================================================================

float rawToEnergy(uint32_t rawValue) {
  // Μετατρέπουμε τη raw χωρητική μέτρηση σε αναλογική τιμή 0.0–1.0 αποκλειστικά
  // για την κίνηση των LED. Δεν είναι ταξινόμηση αριθμού συμμετεχόντων.
  if (rawValue <= TOUCH_MIN_RAW) {
    return 0.0f;
  }
  if (rawValue >= TOUCH_MAX_RAW) {
    return 1.0f;
  }
  const float value = static_cast<float>(rawValue - TOUCH_MIN_RAW) /
    static_cast<float>(TOUCH_MAX_RAW - TOUCH_MIN_RAW);
  return constrain(value, 0.0f, 1.0f);
}

int updateDiagnosticLevel(uint32_t smoothValue) {
  // Το hysteresis ζητά διαφορετικό όριο για άνοδο και κάθοδο. Έτσι η ένδειξη
  // δεν πηγαινοέρχεται γρήγορα όταν η μέτρηση βρίσκεται κοντά σε ένα σύνορο.
  int level = diagnosticLevel;
  while (level < 7 &&
         smoothValue >= DIAGNOSTIC_THRESHOLDS[level] + DIAGNOSTIC_HYSTERESIS) {
    ++level;
  }
  while (level > 0 &&
         smoothValue < DIAGNOSTIC_THRESHOLDS[level - 1] - DIAGNOSTIC_HYSTERESIS) {
    --level;
  }
  return level;
}

void sendState(uint32_t now, bool levelChanged) {
  // Στέλνουμε packet όταν αλλάξει το διαγνωστικό LEVEL ή όταν έρθει η ώρα του
  // heartbeat. Το heartbeat επιτρέπει στην εφαρμογή να ανιχνεύσει αποσύνδεση.
  const bool heartbeatDue = now - lastMessageAt >= HEARTBEAT_INTERVAL_MS;
  if (!levelChanged && !heartbeatDue) {
    return;
  }

  Serial.print("LEVEL,");
  Serial.print(diagnosticLevel);
  Serial.print(",VALUE,");
  Serial.print(rawTouch);
  Serial.print(",SMOOTH,");
  Serial.print(static_cast<uint32_t>(telemetrySmooth));
  Serial.print(",SEQ,");
  Serial.print(messageSequence++);
  Serial.print(",UPTIME,");
  Serial.println(now);
  lastMessageAt = now;
}

void sampleTouchAndSendTelemetry(uint32_t now) {
  // Η δειγματοληψία γίνεται με βάση το millis() και όχι με μεγάλο blocking
  // delay, ώστε να εξυπηρετούνται σταθερά τόσο τα LED όσο και το Serial.
  if (now - lastSampleAt < SAMPLE_INTERVAL_MS) {
    return;
  }
  lastSampleAt = now;

  rawTouch = touchRead(TOUCH_PIN);
  if (!telemetryFilterReady) {
    telemetrySmooth = rawTouch;
    telemetryFilterReady = true;
  } else {
    telemetrySmooth += TELEMETRY_FILTER_ALPHA *
      (static_cast<float>(rawTouch) - telemetrySmooth);
  }

  const float targetEnergy = rawToEnergy(rawTouch);
  // Το φίλτρο των LED είναι πιο αργό και υπηρετεί την αισθητική κίνηση φωτός.
  ledTouchFiltered += (targetEnergy - ledTouchFiltered) * LED_TOUCH_SMOOTHING;

  const int nextDiagnosticLevel = updateDiagnosticLevel(
    static_cast<uint32_t>(telemetrySmooth));
  const bool levelChanged = nextDiagnosticLevel != diagnosticLevel;
  diagnosticLevel = nextDiagnosticLevel;
  sendState(now, levelChanged);
}

void updateEnergy() {
  // Η άνοδος είναι γρηγορότερη από την πτώση. Η κατασκευή ανταποκρίνεται άμεσα
  // στην αφή και η φωτεινή ενέργεια υποχωρεί πιο ήρεμα όταν η επαφή σταματήσει.
  if (energy < ledTouchFiltered) {
    energy = min(energy + ENERGY_SPEED, ledTouchFiltered);
  } else {
    energy = max(energy - ENERGY_DECAY, ledTouchFiltered);
  }
}

// =============================================================================
// ΚΥΚΛΟΣ ΖΩΗΣ ARDUINO
// =============================================================================

void setup() {
  Serial.begin(115200);
  delay(500);

  initializeLedTransport();
  energy = 0.0f;
  showStrip();

  // Τα δύο ενημερωτικά μηνύματα προηγούνται του πρώτου έγκυρου packet. Η
  // εφαρμογή τα αγνοεί κατά την εκκίνηση και μετά λαμβάνει μόνο τηλεμετρία.
  Serial.println("SYNAPSE_V2 ESP32-S3 RGBWC TOUCH TELEMETRY");
  Serial.print("HARDWARE_PROFILE ");
  Serial.println(HARDWARE_PROFILE_NAME);
}

void loop() {
  // Κάθε κύκλος κάνει τρία καθαρά βήματα: μέτρηση, ενημέρωση ενέργειας και
  // σχεδίαση φωτισμού. Η μικρή καθυστέρηση κρατά προβλέψιμη τη συμπεριφορά.
  const uint32_t now = millis();
  sampleTouchAndSendTelemetry(now);
  updateEnergy();
  showStrip();
  delay(LOOP_DELAY_MS);
}
