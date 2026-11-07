# Firmware Synapse v2 για ESP32-S3

Το firmware συνδυάζει τρεις λειτουργίες:

- μέτρηση της χωρητικής αφής,
- οδήγηση της RGBWC ταινίας μέσω ESP-IDF RMT TX,
- αποστολή σταθερής τηλεμετρίας στην εφαρμογή Windows.

Η λογική φωτισμού προέρχεται από τη δοκιμασμένη v1. Η εφαρμογή αποφασίζει το
πραγματικό επίπεδο συμμετοχής από το `SMOOTH` και το ενεργό calibration
profile. Το `LEVEL` του firmware παραμένει μόνο διαγνωστική ένδειξη.

## Αρχεία

- `SynapseV2.ino`: κύριος κώδικας.
- `HardwareProfile.h`: pins και χαρακτηριστικά των υποστηριζόμενων πλακετών.

Οι ρυθμίσεις της πλακέτας διατηρούνται χωριστά από τη λογική του firmware,
ώστε η αλλαγή hardware να μην απαιτεί επεμβάσεις στον υπόλοιπο κώδικα.

## Hardware profiles

Η δοκιμαστική πλακέτα και η πλακέτα παρουσίασης δεν θεωρούνται ίδιες. Η
πλακέτα παρουσίασης είναι η προεπιλογή στο `HardwareProfile.h`.

Το `SYNAPSE_PROFILE_BENCH_ESP32S3` παραμένει διαθέσιμο μόνο για το παλιό
δοκιμαστικό hardware:

- generic ESP32-S3 board profile,
- touch input: GPIO 6,
- RGBWC data: GPIO 4,
- 24 addressable sections,
- Serial: 115200 baud.

Το προεπιλεγμένο `SYNAPSE_PROFILE_PRESENTATION_WAVESHARE` αφορά πλέον την
πραγματική πλακέτα της εγκατάστασης που αναγνωρίστηκε τοπικά:

- ESP32-S3 revision 0.2,
- 32 MB octal flash στα 1.8 V,
- 16 MB OPI PSRAM,
- touch input: GPIO 1, όπως στον δοκιμασμένο κώδικα v1,
- RGBWC data: GPIO 47,
- 24 addressable sections,
- Serial: 115200 baud μέσω CH343/UART0.

Το GPIO 47 επιλέχθηκε για την ταινία της εγκατάστασης ώστε η γραμμή data να
μην επηρεάζεται από τα δεδομένα εκκίνησης του UART TX κατά το reset.

## Upload με Arduino IDE

1. Εγκαταστήστε το ESP32 board package και επιλέξτε **ESP32S3 Dev Module**.
2. Ανοίξτε το `SynapseV2.ino`.
3. Επιβεβαιώστε το hardware profile στο `HardwareProfile.h`.
4. Για την πλακέτα παρουσίασης χρησιμοποιήστε:
   - Flash Size: `32MB`
   - Flash Mode: `OPI`
   - PSRAM: `OPI PSRAM`
   - USB Mode: `Hardware CDC and JTAG`
   - USB CDC On Boot: προεπιλεγμένη ρύθμιση
   - Upload Mode: προεπιλεγμένη UART/USB ρύθμιση
5. Επιλέξτε τη σωστή θύρα COM.
6. Κλείστε την εφαρμογή Synapse και κάθε Serial Monitor που χρησιμοποιεί τη
   θύρα.
7. Πατήστε **Verify** και μετά **Upload**.

Αν το upload δεν ξεκινά, χρησιμοποιήστε καλώδιο USB δεδομένων και θέστε την
πλακέτα σε download mode. Ξεκινήστε με χαμηλή upload speed. Δεν αλλάζουμε
flash mode ή pins για να παρακάμψουμε ένα σφάλμα χωρίς πρώτα να ελέγξουμε το
ακριβές hardware.

Μετά το upload ανοίγουμε προσωρινά το Serial Monitor στα `115200 baud` και
επιβεβαιώνουμε ότι εμφανίζονται το startup banner και έγκυρα packets. Το
Serial Monitor κλείνει πριν συνδεθεί η εφαρμογή.

## Serial packet

```text
LEVEL,<0-7>,VALUE,<raw>,SMOOTH,<smooth>,SEQ,<seq>,UPTIME,<ms>
```

Μετά το startup banner δεν εκπέμπονται ελεύθερα debug ή HTTP μηνύματα, ώστε
να παραμένει αυστηρό το input stream της εφαρμογής.

- `VALUE`: η άμεση τιμή του touch sensor.
- `SMOOTH`: εξομαλυμένη τιμή με φίλτρο `alpha = 0.16`.
- `LEVEL`: προσωρινή διαγνωστική ταξινόμηση του firmware.
- `SEQ`: αύξων αριθμός packet.
- `UPTIME`: χρόνος λειτουργίας του ESP32 σε milliseconds.

## Φωτισμός

Ο φωτισμός χρησιμοποιεί δικό του φίλτρο και τις σταθερές
`TOUCH_MIN_RAW`–`TOUCH_MAX_RAW`. Η βαθμονόμηση της εφαρμογής δεν αλλάζει
αυτές τις τιμές. Επομένως ήχος και καταγραφή μπορούν να είναι σωστά
βαθμονομημένα ενώ ο φωτισμός χρειάζεται ξεχωριστή ρύθμιση.

Πριν συνδεθούν τα LED ελέγχονται η κοινή γείωση, η τάση, η πολικότητα, η
κατεύθυνση data, το GPIO 47, το όριο ρεύματος και ο πραγματικός αριθμός
addressable sections. Η πρώτη δοκιμή γίνεται με χαμηλή φωτεινότητα και
διαθέσιμο τρόπο άμεσης διακοπής της τροφοδοσίας.

## Wi-Fi / HTTP

Το Wi-Fi event posting του v1 δεν περιλαμβάνεται σε αυτό το πρώτο v2
milestone. Η καταγραφή συμμετοχών γίνεται τοπικά από την Windows εφαρμογή και
το blocking HTTP request θα μπορούσε να επηρεάσει LED timing και heartbeat.
Μπορεί να επανεξεταστεί ως ξεχωριστό, μη blocking module μετά το hardware
validation.

## Έλεγχοι πριν από παρουσίαση

Το επιτυχημένο compile δεν αποδεικνύει σωστή ηλεκτρική λειτουργία. Απαιτούνται:

1. επιβεβαίωση πλακέτας, profile και pins,
2. έλεγχος `raw`, `smooth`, `SEQ` και `UPTIME`,
3. νέα βαθμονόμηση με την τελική επιφάνεια,
4. φυσικός έλεγχος όλων των χρωμάτων και όλου του μήκους των LED,
5. έλεγχος ομαλής ανόδου και πτώσης του φωτισμού,
6. δοκιμή σύνδεσης, αποσύνδεσης και επανασύνδεσης USB,
7. κοινή δοκιμή firmware, εφαρμογής και audio output.
