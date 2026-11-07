# Synapse v2 - ESP32-S3 hardware validation

## Επιβεβαιωμένο bench hardware

- Chip: ESP32-S3, revision 0.2
- Flash: 16 MB, quad mode, 3.3 V
- Embedded PSRAM: 8 MB
- USB/UART bridge: CH343
- Arduino core: `esp32:esp32` 3.3.12
- Board profile: `esp32:esp32:esp32s3`

Τα παραπάνω αφορούν αποκλειστικά τη δοκιμαστική πλακέτα.

Οι επιλογές build που χρησιμοποιήθηκαν στη bench πλακέτα είναι:

```text
FlashSize=16M
PSRAM=opi
PartitionScheme=app3M_fat9M_16MB
UploadSpeed=921600
USBMode=hwcdc
CDCOnBoot=default
UploadMode=default
```

## Επιβεβαιωμένο hardware παρουσίασης

- Chip: ESP32-S3, revision 0.2
- Flash: 32 MB, octal mode, 1.8 V
- Embedded PSRAM: 16 MB, OPI, 1.8 V
- USB/UART bridge: CH343 στο COM9 κατά την αναγνώριση
- Native USB-Serial/JTAG: COM8 κατά την αναγνώριση
- Arduino core: `esp32:esp32` 3.3.12
- Board profile: `esp32:esp32:esp32s3`
- Touch input: GPIO 1 από την καλωδίωση v1
- RGBWC data: GPIO 47, ώστε να αποφεύγονται παλμοί του UART TX κατά το reset
- Addressable sections: 24

Οι αριθμοί COM είναι προσωρινοί και μπορεί να αλλάξουν σε άλλο Windows PC.
Το chip και η μνήμη επιβεβαιώθηκαν read-only με esptool 5.3.1. Και τα δύο
USB interfaces ολοκλήρωσαν ROM handshake, αλλά η φόρτωση του προσωρινού RAM
stub έδωσε αρχικά write-timeout. Η αιτία ήταν ασταθές USB data cable: μετά
την αντικατάστασή του ο stub φορτώθηκε και εκτελέστηκε κανονικά. Το
`flash-id` είχε ολοκληρωθεί με ασφάλεια σε `--no-stub` mode.

Οι επιλογές build για την πλακέτα παρουσίασης είναι:

```text
FlashSize=32M
FlashMode=opi
PSRAM=opi
PartitionScheme=app5M_fat24M_32MB
UploadSpeed=115200 για την πρώτη ελεγχόμενη δοκιμή
USBMode=hwcdc
CDCOnBoot=default
UploadMode=default
```

## Πρώτο presentation upload

Το πρώτο upload ολοκληρώθηκε στο CH343/UART0 μέσω COM9, με esptool 5.3.1,
115200 baud και ενεργό RAM stub. Γράφτηκαν μόνο:

- bootloader στο `0x0000`,
- partition table στο `0x8000`,
- boot application στο `0xE000`,
- Synapse v2 application στο `0x10000`.

Και τα τέσσερα τμήματα ολοκλήρωσαν `Hash of data verified`. Δεν έγινε
`erase-all` και δεν άλλαξαν eFuses, Secure Boot ή Flash Encryption. Το ακριβές
deployment snapshot και τα binaries βρίσκονται στο
`artifacts/firmware/20261029-first-presentation-flash`.

Το production serial preflight έλαβε τρία συνεχόμενα έγκυρα packets στο
COM9, με αυξανόμενα `SEQ` και `UPTIME` και κανένα απορριφθέν μήνυμα. Με το
GPIO1 ασύνδετο η προσωρινή αδρανής ένδειξη ήταν περίπου 14.700 raw units· δεν
αποτελεί baseline βαθμονόμησης της τελικής επιφάνειας.

Η θύρα COM είναι runtime ιδιότητα των Windows και δεν πρέπει να αποθηκεύεται
ως μόνιμη ρύθμιση firmware ή repository.

## Compile και upload της δοκιμής

Το firmware μεταγλωττίστηκε με Arduino CLI και δημιουργήθηκε merged binary
16 MB. Το upload ολοκληρώθηκε με επιβεβαίωση hash για bootloader, partition
table, boot application και sketch.

Πριν από το upload δημιουργήθηκε πλήρες flash backup 16 MB στον τοπικό,
git-ignored φάκελο `.backup`. Το backup πρέπει να αντιμετωπίζεται ως
εμπιστευτικό, επειδή ένα πλήρες flash image μπορεί να περιέχει παλαιότερες
ρυθμίσεις ή δεδομένα συσκευής.

## Serial verification

Το production `SerialTouchClient` έλαβε τρία διαδοχικά heartbeat packets:

- level εντός `0–7`,
- έγκυρα `raw` και `smooth`,
- αυξανόμενο `SEQ`,
- αυξανόμενο `UPTIME`,
- κανένα απορριφθέν μήνυμα.

Η αδρανής μέτρηση μετά το upload ήταν περίπου 54.200 raw units. Αυτό είναι
μόνο bench measurement και δεν αντικαθιστά βαθμονόμηση στην τελική μεταλλική
κατασκευή.

## Εκκρεμείς φυσικές δοκιμές

- μετρήσεις για κάθε ζώνη συμμετοχής 1–7,
- επαλήθευση hysteresis κοντά σε κάθε threshold,
- αποσύνδεση USB κατά τη διάρκεια συνεδρίας,
- επανασύνδεση και αναγνώριση device restart,
- πραγματική ακουστική δοκιμή όλων των transitions,
- soak test με το τελικό τροφοδοτικό, κάρτα ήχου και ηχεία.
