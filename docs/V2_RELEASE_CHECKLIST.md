# Synapse v2 - Release checklist

Το checklist συμπληρώνεται για το ακριβές build και deployment package που θα
χρησιμοποιηθεί στην παρουσίαση. Τα P0 και P1 σημεία είναι υποχρεωτικά.

## A. Baseline και έκδοση

- [ ] Καταγράφηκε το v1 baseline commit `5926a9f`.
- [ ] Το `ProjectSynapse/main` παρέμεινε αμετάβλητο κατά την ανάπτυξη.
- [ ] Καταγράφηκε η έκδοση ESP32 board package/Arduino Core.
- [ ] Καταγράφηκε η έκδοση της Windows εφαρμογής και της audio engine.
- [ ] Το release package έχει μοναδικό version και SHA-256 checksum.
- [ ] Υπάρχει δοκιμασμένο rollback package της v1.

## B. Firmware και hardware - P0

- [ ] Επιβεβαιώθηκε το ακριβές ESP32-S3 board.
- [ ] Επιβεβαιώθηκαν LED, touch και λοιπά GPIO.
- [ ] Το firmware κάνει compile χωρίς error.
- [ ] Ελέγχθηκε ασφαλές boot και restart.
- [ ] Ελέγχθηκε ο φωτισμός της v1 χωρίς regression.
- [ ] Τα white channels παραμένουν στην εγκεκριμένη κατάσταση.
- [ ] Επιβεβαιώθηκε το ανώτατο brightness/power limit.
- [ ] Έγινε calibration στο τελικό ηλεκτρόδιο και περιβάλλον.
- [ ] Ελέγχθηκαν όλα τα επίπεδα 0-5 με hysteresis.
- [ ] Ελέγχθηκε Serial heartbeat και sequence handling.

## C. Audio files - P0

- [ ] Το runtime folder περιέχει ακριβώς πέντε stems.
- [ ] Το `tree instalation1.wav` δεν βρίσκεται στο runtime folder.
- [ ] Τα hashes συμφωνούν με το εγκεκριμένο manifest.
- [ ] Όλα τα stems είναι stereo PCM 16-bit/44.1 kHz.
- [ ] Όλα τα stems έχουν ίδια διάρκεια και κοινό start point.
- [ ] Επιβεβαιώθηκε η αντιστοίχιση επιπέδου 1-5 προς όργανο.
- [ ] Μετρήθηκαν LUFS-I, short-term LUFS και true peak.
- [ ] Η πλήρης μίξη συγκρίθηκε ακουστικά με το reference.
- [ ] Δεν ακούγονται clicks σε ενεργοποίηση, fade, pause ή resume.
- [ ] Υπάρχει ασφαλές master gain και limiter για το τελικό PA.

## D. Audio engine - P0/P1

- [ ] Χρησιμοποιείται ένα output device και ένα κοινό audio clock.
- [ ] Τα stems είναι preloaded ή επαρκώς buffered πριν την έναρξη.
- [ ] Δεν εμφανίζεται drift στα 60, 120 και 180 δευτερόλεπτα.
- [ ] Δεν εμφανίζεται buffer underrun σε τετράωρο soak test.
- [ ] Serial loss προκαλεί ασφαλές fade-to-silence.
- [ ] Audio device loss ανιχνεύεται και εμφανίζεται στον χειριστή.
- [ ] Ελέγχθηκε reconnect ή τεκμηριώθηκε ασφαλές restart.
- [ ] Η εφαρμογή αποτυγχάνει καθαρά αν το output device δεν ανοίξει.
- [ ] Δεν υπάρχουν κενά `catch` που κρύβουν κρίσιμα audio errors.

## E. Serial και εφαρμογή - P0/P1

- [ ] Απορρίπτονται malformed και out-of-range packets.
- [ ] Partial line/timeout δεν αλλάζει την τρέχουσα κατάσταση.
- [ ] Η απώλεια heartbeat σταματά contact time και ήχο.
- [ ] Έγινε unplug/replug του USB κατά τη λειτουργία.
- [ ] Έγινε restart εφαρμογής χωρίς restart του ESP32.
- [ ] Έγινε restart ESP32 χωρίς restart εφαρμογής.
- [ ] Η UI παραμένει responsive υπό συνεχές Serial traffic.
- [ ] Το άνοιγμα αποθηκευμένου calibration profile δεν το ενεργοποιεί πριν από Αποθήκευση.
- [ ] Η σίγαση και επαναφορά μουσικής έχουν ομαλό fade χωρίς αλλαγή του session clock.
- [ ] Κατά τη σίγαση συνεχίζουν κανονικά Συνάψεις, καταγραφή και LEDs.
- [ ] Τα logs έχουν όριο και δεν γεμίζουν μνήμη ή δίσκο.
- [ ] Η έξοδος από την εφαρμογή απελευθερώνει Serial και audio resources.

## F. Wi-Fi, server και SQLite - P1

- [ ] Ο φωτισμός και ο ήχος λειτουργούν χωρίς Wi-Fi/server.
- [ ] HTTP timeout δεν επηρεάζει touch sampling ή Serial heartbeat.
- [ ] Το API κάνει type/range validation.
- [ ] Το API δεν επιστρέφει exception details.
- [ ] Η SQLite λειτουργεί σε WAL mode με κατάλληλο timeout.
- [ ] Ελέγχθηκε ασφαλές κλείσιμο και επανεκκίνηση server.
- [ ] Έγινε backup και δοκιμή ανάκτησης της βάσης.
- [ ] Δεν αποθηκεύονται προσωπικά δεδομένα.
- [ ] Το bind/firewall περιορίζεται στο απολύτως αναγκαίο deployment scope.

## G. Ασφάλεια και repository - P0/P1

- [ ] Δεν υπάρχουν Wi-Fi passwords, tokens ή πραγματικά endpoints στο Git.
- [ ] Υπάρχουν μόνο example configuration files.
- [ ] Database, WAL/SHM, logs και temp files βρίσκονται στο `.gitignore`.
- [ ] Ελέγχθηκαν οι άδειες όλων των dependencies και media assets.
- [x] Τα WAV και τα αρχεία μουσικής παραμένουν εκτός Git repository.
- [ ] Το reference mix και τα πρόχειρα αρχεία δεν μπήκαν στο runtime package.
- [ ] Το package εγκαθίσταται χωρίς Internet.
- [ ] Δεν απαιτεί απενεργοποίηση antivirus, firewall ή certificate validation.

## H. Πρόβα εγκατάστασης - P0

- [ ] Έγινε cold boot με όλο τον τελικό εξοπλισμό.
- [ ] Εκτελέστηκαν επανειλημμένα πλήρεις τρίλεπτες συνεδρίες.
- [ ] Έγινε τετράωρο soak test.
- [ ] Έγινε δοκιμή διάρκειας αντίστοιχης με την πραγματική εκδήλωση.
- [ ] Ελέγχθηκαν τροφοδοτικά, θερμοκρασίες και καλωδιώσεις.
- [ ] Ελέγχθηκε touch stability με ενεργά LEDs και ηχητικό σύστημα.
- [ ] Μετρήθηκε ασφαλές SPL στον χώρο.
- [ ] Υπάρχει spare USB cable, τροφοδοσία και γνωστό recovery procedure.
- [ ] Ο χειριστής μπορεί να κάνει restart χωρίς developer εργαλεία.

## I. GitHub integration

- [ ] Δημιουργήθηκε τοπικά το `feature/v2-integration`.
- [ ] Ελέγχθηκε ολόκληρο το diff έναντι της v1.
- [ ] Τα commits είναι μικρά και θεματικά.
- [ ] Build και smoke test έγιναν από καθαρό checkout/package.
- [ ] Το Pull Request περιλαμβάνει test evidence και rollback plan.
- [ ] Δόθηκε ρητή έγκριση πριν από κάθε push/release ενέργεια.
- [ ] Δοκιμάστηκε το ακριβές `v2.0.0-rc.1` package.
- [ ] Δεν παραμένει ανοικτό P0 ή P1 εύρημα πριν από το `v2.0.0`.

## Τελική έγκριση

- Build/version:
- Ημερομηνία ελέγχου:
- Hardware configuration:
- Audio device/driver:
- Υπεύθυνος τεχνικού ελέγχου:
- Υπεύθυνος λειτουργίας εκδήλωσης:
- Ανοικτές P2 παρατηρήσεις:
- Απόφαση: `GO` / `NO-GO`
