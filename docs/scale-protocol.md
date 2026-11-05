# Bluetooth scale protocol

BambuMan (Android) can fill in a spool's weight from a Bluetooth LE scale: put the spool on the scale, hold its tag to the
phone, and the weight field of the spool card is filled from the scale. The weight stays editable.

There is no common protocol for kitchen scales — each maker uses its own — so BambuMan supports two:

| Protocol | For | Service | Weight characteristic |
|---|---|---|---|
| **BambuMan scale protocol** | DIY scales (ESP32 + HX711 load cell amplifier, …) | `d9a40001-3bc4-40b0-839e-ca2ccb948710` | `d9a40002-3bc4-40b0-839e-ca2ccb948710` (notify) |
| **Weight Scale Service** (Bluetooth SIG) | The few scales that implement the standard, mostly body scales | `0x181D` | Weight Measurement `0x2A9D` (indicate) |

A scale is found by the service it **advertises**, so it must put the service UUID in its advertising data.

## What weight to send

Send the weight of **everything on the scale**: for a spool, the filament *and* the empty spool. That is what the weight
field holds for every inventory backend (Spoolman, Bambuddy and No backend): the app takes the spool's *Empty weight*
off itself to get the filament left.

So **tare the scale empty** (nothing on it). Don't tare it with an empty spool on it — the app would then subtract the
empty spool a second time.

## BambuMan scale protocol (v1)

### GATT

| | UUID | Properties | |
|---|---|---|---|
| Service | `d9a40001-3bc4-40b0-839e-ca2ccb948710` | primary, **advertised** | |
| Weight | `d9a40002-3bc4-40b0-839e-ca2ccb948710` | notify (+ read) | required |
| Control | `d9a40003-3bc4-40b0-839e-ca2ccb948710` | write / write without response | optional |

### Weight value

At least 5 bytes, little-endian:

| Bytes | Type | Meaning |
|---|---|---|
| 0–3 | `int32` | Weight in **0.1 g** (signed: a tared scale may read slightly below zero) |
| 4 | `uint8` | Flags: bit 0 = **stable** (the weight has settled), bit 1 = **error** (overload, sensor fault — the weight is ignored), bits 2–7 reserved, send 0 |

Bytes after byte 4 are reserved for later versions; the app ignores them. Keep the value at 20 bytes or less (the
default ATT MTU).

Examples:

| Bytes (hex) | Weight |
|---|---|
| `39 30 00 00 01` | 1234.5 g, stable |
| `39 30 00 00 00` | 1234.5 g, still settling |
| `E7 FF FF FF 01` | −2.5 g, stable |
| `00 00 00 00 02` | error, ignored |

Notify:

- whenever the weight or the stable flag changes, and at least once a second while connected,
- no more than about 10 times a second.

Set the **stable** flag once the weight stays within about ±0.5 g for a second. The app fills the weight from live
readings while the scale settles, and keeps the stable weight once there is one: live readings after it don't change
the field, only the next stable weight does.

### Control value

One command byte:

| Byte | Command |
|---|---|
| `01` | Tare — zero the scale |

Other values are reserved; ignore them. When the characteristic is there, the app's settings show a **Tare** button.

### Advertising

- Advertise the service UUID `d9a40001-3bc4-40b0-839e-ca2ccb948710` — the app scans for it.
- Give the scale a name (in the scan response is fine); it is shown when picking the scale.
- Allow one connection, and advertise again after a disconnect: the app reconnects by itself when the scale comes
  back.

## How the app uses the readings

- When a spool's tag is read, the weight is filled from the latest reading if the scale shows more than 20 g, and the
  reading is fresh: up to 60 seconds old when stable, 3 seconds when still settling.
- While the spool card is open, new readings keep filling the weight (stable preferred, see above) until you type in
  the weight field yourself.
- Readings below 20 g are an empty scale: lifting the spool off doesn't empty the field.
- The weight goes in as grams, to 0.1 g.

## Weight Scale Service

A scale with the standard [Weight Scale Service](https://www.bluetooth.com/specifications/specs/weight-scale-service-1-0/)
(`0x181D`) sends Weight Measurements (`0x2A9D`) by indication: a flags byte (bit 0 unit: 0 = kg, 1 = lb; bit 1 time
stamp present; bit 2 user id present; bit 3 BMI and height present), the weight as `uint16` in steps of 0.005 kg or
0.01 lb, then the optional fields. A measurement is final, so the app takes every one as stable. `0xFFFF`
("measurement unsuccessful") is ignored.

Mind that the standard's resolution is 5 g, and that body scales often only measure from a few kilograms up — a
spool may not register at all. For spools, a DIY scale with the BambuMan protocol works better.

## Example: ESP32 + HX711

A minimal scale for the Arduino IDE with the ESP32 board package (2.x or 3.x) and the
[HX711 library](https://github.com/bogde/HX711) by Bogdan Necula (`HX711 Arduino Library` in the library manager).

Wiring: load cell → HX711 (E+, E−, A+, A−), HX711 `DT` → GPIO 4, `SCK` → GPIO 5, `VCC` → 3V3, `GND` → GND.

```cpp
// BambuMan scale protocol v1 — see docs/scale-protocol.md
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLE2902.h>
#include <HX711.h>

#define SERVICE_UUID "d9a40001-3bc4-40b0-839e-ca2ccb948710"
#define WEIGHT_UUID  "d9a40002-3bc4-40b0-839e-ca2ccb948710"
#define CONTROL_UUID "d9a40003-3bc4-40b0-839e-ca2ccb948710"

const int HX711_DT = 4;
const int HX711_SCK = 5;

// Raw HX711 units per gram. Calibrate: set it to 1, tare, put a known weight on, and divide the weight shown by its grams.
const float CALIBRATION = 420.0f;

const float STABLE_RANGE = 0.5f;          // g
const unsigned long STABLE_TIME = 1000;   // ms
const unsigned long NOTIFY_INTERVAL = 200; // ms, at most 5 notifications a second
const unsigned long KEEPALIVE = 1000;      // ms, at least one a second

HX711 hx711;
BLECharacteristic *weightCharacteristic;

volatile bool connected = false;
volatile bool tareRequested = false;

float weight = 0;
float settledFrom = 0;
unsigned long settledSince = 0;
bool stable = false;

int32_t sentTenths = INT32_MIN;
bool sentStable = false;
unsigned long lastNotify = 0;

class ServerCallbacks : public BLEServerCallbacks {
  void onConnect(BLEServer *server) override { connected = true; }

  void onDisconnect(BLEServer *server) override {
    connected = false;
    sentTenths = INT32_MIN;
    BLEDevice::startAdvertising(); // the app reconnects by itself
  }
};

class ControlCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic *characteristic) override {
    auto value = characteristic->getValue(); // std::string on core 2.x, String on 3.x
    if (value.length() > 0 && (uint8_t)value[0] == 0x01) tareRequested = true;
  }
};

void sendWeight(int32_t tenths, bool isStable) {
  uint8_t data[5] = {
    (uint8_t)(tenths & 0xFF),
    (uint8_t)((tenths >> 8) & 0xFF),
    (uint8_t)((tenths >> 16) & 0xFF),
    (uint8_t)((tenths >> 24) & 0xFF),
    (uint8_t)(isStable ? 0x01 : 0x00)
  };

  weightCharacteristic->setValue(data, sizeof(data));
  weightCharacteristic->notify();
}

void setup() {
  hx711.begin(HX711_DT, HX711_SCK);
  hx711.set_scale(CALIBRATION);
  hx711.tare(20); // start empty

  BLEDevice::init("BambuMan Scale");
  BLEServer *server = BLEDevice::createServer();
  server->setCallbacks(new ServerCallbacks());

  BLEService *service = server->createService(SERVICE_UUID);

  weightCharacteristic = service->createCharacteristic(WEIGHT_UUID, BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY);
  weightCharacteristic->addDescriptor(new BLE2902());

  BLECharacteristic *control = service->createCharacteristic(CONTROL_UUID, BLECharacteristic::PROPERTY_WRITE | BLECharacteristic::PROPERTY_WRITE_NR);
  control->setCallbacks(new ControlCallbacks());

  service->start();

  BLEAdvertising *advertising = BLEDevice::getAdvertising();
  advertising->addServiceUUID(SERVICE_UUID);
  advertising->setScanResponse(true); // the name goes in the scan response, the 128-bit uuid in the advertisement
  BLEDevice::startAdvertising();
}

void loop() {
  if (tareRequested) {
    tareRequested = false;
    hx711.tare(10);
    weight = 0;
    stable = false;
  }

  if (!hx711.is_ready()) {
    delay(5);
    return;
  }

  // A little smoothing; the HX711 reads 10 times a second at its default rate.
  weight = weight * 0.7f + hx711.get_units(1) * 0.3f;

  unsigned long now = millis();

  if (fabsf(weight - settledFrom) > STABLE_RANGE) {
    settledFrom = weight;
    settledSince = now;
    stable = false;
  } else if (now - settledSince >= STABLE_TIME) {
    stable = true;
  }

  if (!connected) return;

  int32_t tenths = (int32_t)lroundf(weight * 10.0f);
  bool changed = tenths != sentTenths || stable != sentStable;

  if ((changed && now - lastNotify >= NOTIFY_INTERVAL) || now - lastNotify >= KEEPALIVE) {
    sendWeight(tenths, stable);
    sentTenths = tenths;
    sentStable = stable;
    lastNotify = now;
  }
}
```

Then in BambuMan: **Settings** → **Bluetooth scale** → turn on **Weigh with a Bluetooth scale** → **Find scale** →
**Use** next to *BambuMan Scale*. The scale's live weight shows under its name.

## Platform support

Android only. The Windows desktop app has no Bluetooth scale support.
