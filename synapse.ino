#include <Arduino.h>
#include "driver/rmt_tx.h"
#include <WiFi.h>
#include <HTTPClient.h>

//Version 5v2

// =====================================================
// WIFI & SERVER CONFIGURATION
// =====================================================
const char* WIFI_SSID     = "yourSSID";
const char* WIFI_PASSWORD = "yourWifiPass";
const char* SERVER_URL    = "yourAPIendpoint";

// =================
//      Config
// =================

#define LED_PIN          4
#define TOUCH_PIN        1 // θέλουμε ακόμη 3 analog  touch
#define NUM_LEDS         24 // Αριθμός συνολικών addressable sections (1 section = 8.3 cm) μέγιστο 10 μέτρα

// RAW TOUCH RANGE: 0% Φωτισμός → ΑΝΑΛΟΓΙΚΟ ΕΝΔΙΑΜΕΣΑ → 100% Φωτισμός
#define TOUCH_MIN_RAW    52000
#define TOUCH_MAX_RAW    350000

// TOUCH SMOOTHING: 0.03 = πολύ smooth → 0.50 = πολύ άμεσο
#define TOUCH_SMOOTHING  0.06f

// ENERGY / MOVEMENT SPEED (Ταχύτητα ανόδου και πτώσης φωτισμού)
#define ENERGY_SPEED     0.009f
#define ENERGY_DECAY     0.003f

// BRIGHTNESS (Φωτεινότητα) 0 εως 255
#define IDLE_BRIGHTNESS  5
#define MAX_BRIGHTNESS   150

// -----------------------------
// COLORS (ΧΡΩΜΑΤΑ ΑΝΑ ΖΩΝΗ ΜΗΚΟΥΣ)
// -----------------------------
// 1. Θερμό κίτρινο (0% έως 65%)
#define YELLOW_R   255
#define YELLOW_G   128
#define YELLOW_B   0

// 2. Ενδιάμεσο κιτρινοπράσινο / Lime (65% έως 75%)
#define MID_R      230
#define MID_G      200
#define MID_B      15

// 3. Πράσινο (75% έως 100%)
#define GREEN_R    200
#define GREEN_G    255
#define GREEN_B    30

// WHITE CHANNELS (Για την ώρα είναι απλά off, δεν χρησιμοποιούνται)
#define WHITE_1   0
#define WHITE_2   0

// END FADE:  0 = καθόλου fade 3.0 = πολύ απαλό : Πόσα sections χρησιμοποιούνται για το ομαλό τελείωμα της φωτεινής περιοχής.
#define FADE_SECTIONS   6.5f

// LOOP SPEED
#define LOOP_DELAY_MS   10  // Γενικό delay στην λούπα

// -----------------------------
// SYNAPSE PARTICIPATION LOGIC
// -----------------------------
#define STEP_PER_PERSON   10000   // 10.000 raw μονάδες ανά άτομο/επίπεδο
#define MAX_PEOPLE        7       // Έως 7 άτομα
#define MIN_DURATION_MS   500     // Ελάχιστος χρόνος παραμονής για καταγραφή συμβάντος

// =====================================================
// RMT
// =====================================================

rmt_channel_handle_t txChannel = NULL;
rmt_encoder_handle_t copyEncoder = NULL;


// =====================================================
// WS2812B TIMING
// =====================================================

#define T0H_TICKS  14
#define T0L_TICKS  32

#define T1H_TICKS  28
#define T1L_TICKS  24

#define RESET_US   300


// =====================================================
// TOUCH / ENERGY
// =====================================================

float touchFiltered = 0.0f;
float energy = 0.0f;

int32_t lastRawTouch = 0;

// Μεταβλητές καταγραφής συμβάντων ανά ζώνη
int activeZone = 0;
unsigned long zoneStartTime = 0;
int32_t zoneRawSum = 0;
int zoneSampleCount = 0;

// =====================================================
// SEND BYTE
// =====================================================

void encodeByte(uint8_t value, rmt_symbol_word_t *symbols)
{
  for (int bit = 7; bit >= 0; bit--)
  {
    bool one = value & (1 << bit);
    int index = 7 - bit;

    if (one)
    {
      symbols[index].level0 = 1;
      symbols[index].duration0 = T1H_TICKS;
      symbols[index].level1 = 0;
      symbols[index].duration1 = T1L_TICKS;
    }
    else
    {
      symbols[index].level0 = 1;
      symbols[index].duration0 = T0H_TICKS;
      symbols[index].level1 = 0;
      symbols[index].duration1 = T0L_TICKS;
    }
  }
}


// =====================================================
// SEND RGBWC SEGMENT
// =====================================================

void sendSegment(
  uint8_t r,
  uint8_t g,
  uint8_t b,
  uint8_t w1,
  uint8_t w2
)
{
  rmt_symbol_word_t symbols[48];

  encodeByte(r,  &symbols[0]);
  encodeByte(g,  &symbols[8]);
  encodeByte(b,  &symbols[16]);
  encodeByte(w1, &symbols[24]);
  encodeByte(w2, &symbols[32]);

  // 6ο byte = 0
  encodeByte(0, &symbols[40]);

  rmt_transmit_config_t tx_config = {};

  tx_config.loop_count = 0;

  ESP_ERROR_CHECK(
    rmt_transmit(
      txChannel,
      copyEncoder,
      symbols,
      sizeof(symbols),
      &tx_config
    )
  );


  ESP_ERROR_CHECK(
    rmt_tx_wait_all_done(
      txChannel,
      portMAX_DELAY
    )
  );
}


// =====================================================
// SHOW STRIP
// =====================================================

void showStrip()
{
  float energyPos = energy * NUM_LEDS;

  // Όρια ζωνών ανάλογα με το μήκος
  float split65 = NUM_LEDS * 0.65f;
  float split75 = NUM_LEDS * 0.75f;

  for (int i = 0; i < NUM_LEDS; i++)
  {
    float intensity = 0.0f;
    float activeBrightness = IDLE_BRIGHTNESS;

    if (energy > 0.0001f)
    {
      if (i < floor(energyPos))
      {
        intensity = 1.0f;
      }
      else if (i == floor(energyPos))
      {
        intensity = energyPos - floor(energyPos);
      }

      if (FADE_SECTIONS > 0.0f)
      {
        float distanceFromEnd = energyPos - i;
        if (distanceFromEnd > 0.0f && distanceFromEnd < FADE_SECTIONS)
        {
          float fadeFactor = distanceFromEnd / FADE_SECTIONS;
          intensity *= fadeFactor;
        }
      }

      intensity = constrain(intensity, 0.0f, 1.0f);
      activeBrightness = IDLE_BRIGHTNESS + intensity * (MAX_BRIGHTNESS - IDLE_BRIGHTNESS);
    }

    uint8_t brightness = constrain(activeBrightness, 0.0f, 255.0f);

    // =================================================
    // ΕΠΙΛΟΓΗ ΧΡΩΜΑΤΟΣ ΒΑΣΕΙ ΘΕΣΗΣ (3 ΖΩΝΕΣ)
    // =================================================
    uint8_t baseR, baseG, baseB;

    if (i >= split75)
    {
      // Ζώνη 3: 75% - 100% (Πράσινο)
      baseR = GREEN_R;
      baseG = GREEN_G;
      baseB = GREEN_B;
    }
    else if (i >= split65)
    {
      // Ζώνη 2: 65% - 75% (Ενδιάμεσο Κιτρινοπράσινο)
      baseR = MID_R;
      baseG = MID_G;
      baseB = MID_B;
    }
    else
    {
      // Ζώνη 1: 0% - 65% (Θερμό Κίτρινο)
      baseR = YELLOW_R;
      baseG = YELLOW_G;
      baseB = YELLOW_B;
    }

    // Κλιμάκωση φωτεινότητας
    uint8_t r = ((uint16_t)baseR * brightness) / 255;
    uint8_t g = ((uint16_t)baseG * brightness) / 255;
    uint8_t b = ((uint16_t)baseB * brightness) / 255;

    sendSegment(r, g, b, WHITE_1, WHITE_2);
  }

  gpio_set_level((gpio_num_t)LED_PIN, 0);
  delayMicroseconds(RESET_US);
}


// =====================================================
// READ RAW TOUCH
// =====================================================

int32_t readRawTouch()
{
  return touchRead(TOUCH_PIN);
}

// =====================================================
// RAW → 0.0 - 1.0
// =====================================================

float rawToEnergy(
  int32_t raw
)
{
  // ---------------------------------------------------
  // Κάτω από MIN
  // ---------------------------------------------------
  if (raw <= TOUCH_MIN_RAW)
  {
    return 0.0f;
  }

  // ---------------------------------------------------
  // Πάνω από MAX
  // ---------------------------------------------------
  if (raw >= TOUCH_MAX_RAW)
  {
    return 1.0f;
  }

  // ---------------------------------------------------
  // ΑΝΑΛΟΓΙΚΗ ΜΕΤΑΤΡΟΠΗ
  // ---------------------------------------------------
  float value =(float)(raw - TOUCH_MIN_RAW) / (float)(TOUCH_MAX_RAW - TOUCH_MIN_RAW);
  return constrain(value, 0.0f, 1.0f);
}

// =====================================================
// DATA LOGGING & HTTP POST (ΜΕ TIMEOUT & FLUSH)
// =====================================================
void sendEventToServer(int32_t raw, float target, int people, unsigned long duration)
{
  if (WiFi.status() != WL_CONNECTED) {
    return;
  }

  HTTPClient http;
  http.begin(SERVER_URL);
  http.setTimeout(150); // Σημαντικό: Μην μπλοκάρεις τη loop αν αργήσει η απόκριση
  http.addHeader("Content-Type", "application/json");

  String jsonPayload = "{";
  jsonPayload += "\"raw\":" + String(raw) + ",";
  jsonPayload += "\"target\":" + String(target, 3) + ",";
  jsonPayload += "\"people\":" + String(people) + ",";
  jsonPayload += "\"duration_ms\":" + String(duration);
  jsonPayload += "}";

  int httpResponseCode = http.POST(jsonPayload);

  // Serial logging του HTTP με άμεσο άδειασμα buffer (flush)
  if (httpResponseCode > 0) {
    Serial.printf("[HTTP] POST OK (%d): %s\n", httpResponseCode, jsonPayload.c_str());
  } else {
    Serial.printf("[HTTP] Error: %s\n", http.errorToString(httpResponseCode).c_str());
  }

  http.end();
}

void processTouchLogging(int32_t raw, float target)
{
  int currentZone = 0;
  if (raw > TOUCH_MIN_RAW) {
    currentZone = ((raw - TOUCH_MIN_RAW) / STEP_PER_PERSON) + 1;
    currentZone = constrain(currentZone, 1, MAX_PEOPLE);
  }

  unsigned long now = millis();

  // Αν η τιμή παραμένει στο ίδιο επίπεδο/ζώνη
  if (currentZone == activeZone && activeZone > 0) {
    zoneRawSum += raw;
    zoneSampleCount++;
    return;
  }

  // Αν άλλαξε επίπεδο (έληξε η παραμονή στην προηγούμενη ζώνη)
  if (activeZone > 0) {
    unsigned long duration = now - zoneStartTime;

    if (duration >= MIN_DURATION_MS && zoneSampleCount > 0) {
      int32_t avgRaw = zoneRawSum / zoneSampleCount;
      sendEventToServer(avgRaw, target, activeZone, duration);
    }
  }

  // Έναρξη νέας ζώνης
  activeZone = currentZone;
  zoneStartTime = now;
  zoneRawSum = (currentZone > 0) ? raw : 0;
  zoneSampleCount = (currentZone > 0) ? 1 : 0;
}

// =====================================================
// SETUP
// =====================================================

void setup()
{
  Serial.begin(115200);

  delay(500);


  // ---------------------------------------------------
  // DATA PIN
  // ---------------------------------------------------

  pinMode(
    LED_PIN,
    OUTPUT
  );

  digitalWrite(
    LED_PIN,
    LOW
  );


  // ---------------------------------------------------
  // RMT
  // ---------------------------------------------------

  rmt_tx_channel_config_t tx_config = {};

  tx_config.gpio_num =
    (gpio_num_t)LED_PIN;

  tx_config.clk_src =
    RMT_CLK_SRC_DEFAULT;

  tx_config.resolution_hz =
    40000000;

  tx_config.mem_block_symbols =
    64;

  tx_config.trans_queue_depth =
    4;

  tx_config.flags.invert_out =
    false;

  tx_config.flags.with_dma =
    false;


  ESP_ERROR_CHECK(
    rmt_new_tx_channel(
      &tx_config,
      &txChannel
    )
  );


  // ---------------------------------------------------
  // COPY ENCODER
  // ---------------------------------------------------

  rmt_copy_encoder_config_t encoder_config = {};


  ESP_ERROR_CHECK(
    rmt_new_copy_encoder(
      &encoder_config,
      &copyEncoder
    )
  );


  // ---------------------------------------------------
  // ENABLE
  // ---------------------------------------------------

  ESP_ERROR_CHECK(
    rmt_enable(
      txChannel
    )
  );

   // WIFI SETUP
  Serial.println();
  Serial.print("[WiFi] Σύνδεση στο ");
  Serial.println(WIFI_SSID);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  int retries = 0;
  while (WiFi.status() != WL_CONNECTED && retries < 20) {
    delay(500);
    Serial.print(".");
    retries++;
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.println("\n[WiFi] Συνδέθηκε επιτυχώς!");
    Serial.print("[WiFi] IP: ");
    Serial.println(WiFi.localIP());
  } else {
    Serial.println("\n[WiFi] Offline mode.");
  }



  delay(100);


  // ---------------------------------------------------
  // START
  // ---------------------------------------------------

  energy = 0.0f;

  showStrip();


  // ---------------------------------------------------
  // SERIAL INFO
  // ---------------------------------------------------
  Serial.println("========================================");
  Serial.println("       SYNAPSE TOUCH CONTROLLER         ");
  Serial.println("========================================");
  Serial.println();
  Serial.println("========================================");
  Serial.println("       RGBWC ANALOG TOUCH CONTROL");
  Serial.println("========================================");

  Serial.print("Sections          : ");
  Serial.println(NUM_LEDS);

  Serial.print("Touch MIN RAW     : ");
  Serial.println(TOUCH_MIN_RAW);

  Serial.print("Touch MAX RAW     : ");
  Serial.println(TOUCH_MAX_RAW);

  Serial.print("Idle brightness   : ");
  Serial.println(IDLE_BRIGHTNESS);

  Serial.print("Max brightness    : ");
  Serial.println(MAX_BRIGHTNESS);

  Serial.print("Smoothing         : ");
  Serial.println(TOUCH_SMOOTHING);

  Serial.print("Energy speed      : ");
  Serial.println(ENERGY_SPEED);

  Serial.print("Energy decay      : ");
  Serial.println(ENERGY_DECAY);

  Serial.print("Fade sections     : ");
  Serial.println(FADE_SECTIONS);

  Serial.println("========================================");
}


// =====================================================
// LOOP
// =====================================================
void loop()
{
  // ===================================================
  // 1. READ RAW - Ανάγνωση του touch Sensor
  // ===================================================
  lastRawTouch = readRawTouch();

  // ===================================================
  // 2. RAW → TARGET ENERGY
  // ===================================================
  float targetEnergy = rawToEnergy(lastRawTouch);

  // ===================================================
  // 3. SMOOTH TOUCH - Εξομάλυνση
  // ===================================================
  touchFiltered +=
    (targetEnergy - touchFiltered)
    * TOUCH_SMOOTHING;

  // ===================================================
  // 4. ENERGY MOVEMENT - Κίνηση της ενέργειας
  // ===================================================

  if (energy < touchFiltered)
  {
    energy += ENERGY_SPEED;

    if (energy > touchFiltered)
      energy = touchFiltered;
  }
  else
  {
    energy -= ENERGY_DECAY;

    if (energy < touchFiltered)
      energy = touchFiltered;
  }


  // ===================================================
  // 5. DISPLAY - Σχεδιασμός στις LED ταινίες
  // ===================================================
  showStrip();

  // ===================================================
  // 6. Καταγραφή διαρκείας & αποστολή στη βάση SQLite
  // ===================================================
  processTouchLogging(lastRawTouch, targetEnergy);

  // ===================================================
  // 7. SERIAL DEBUG
  // ===================================================

  static unsigned long lastPrint = 0;

  if (millis() - lastPrint > 150)
  {
    lastPrint = millis();
    Serial.print("Raw: ");
    Serial.print(lastRawTouch);
    Serial.print(" | Target: ");
    Serial.print(targetEnergy * 100, 1);
    Serial.print("% | Energy: ");
    Serial.print(energy * 100, 1);
    Serial.print("% | Sections: ");
    Serial.println(energy * NUM_LEDS, 1);
  }
  delay(LOOP_DELAY_MS);
}
