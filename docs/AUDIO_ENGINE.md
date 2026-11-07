# Synapse v2 - Audio engine

## Στόχος

Το `Synapse.Audio` αντικαθιστά τα πέντε ανεξάρτητα Windows Media Player COM
instances με μία ενιαία audio engine:

- ένα WASAPI shared output,
- ένα `MixingSampleProvider`,
- πέντε `WaveFileReader` inputs,
- ένα κοινό callback/sample clock,
- ανεξάρτητο time-based gain ramp ανά layer,
- master gain και τελικό safety ceiling,
- ανίχνευση σφάλματος/απώλειας audio device.

Η engine είναι συνδεδεμένη με τη νέα εφαρμογή `Synapse.App`, με simulator
συμμετοχής 0–7 και με USB/Serial transport για το ESP32. Οι επτά ζώνες
συμμετοχής αντιστοιχίζονται σε πέντε μουσικά layers πριν φτάσουν στην engine.

## Κλειδωμένες εκδόσεις

- Target: `net8.0-windows`
- `NAudio.Core`: `2.4.0`
- `NAudio.Wasapi`: `2.4.0`
- Output mode: WASAPI shared
- Default requested latency: 100 ms

Οι εκδόσεις βρίσκονται στο `packages.lock.json`. Δεν χρησιμοποιούνται floating
package versions.

## Device policy

1. Αν δεν υπάρχει `deviceId`, επιλέγεται το Windows default multimedia render
   endpoint.
2. Αν υπάρχει `deviceId`, πρέπει να αντιστοιχεί σε ενεργό output endpoint.
3. Mono endpoint απορρίπτεται.
4. Διαφορετικό device mix rate επιτρέπεται σε shared mode και εμφανίζεται ως
   warning.
5. Το preflight αρχικοποιεί WASAPI με silence, αλλά δεν καλεί `Play`.
6. Απώλεια device κατά την αναπαραγωγή μεταφέρει την engine σε `Faulted` και
   ενεργοποιεί το `Faulted` event.
7. Δεν γίνεται σιωπηλή αλλαγή σε διαφορετική συσκευή κατά τη διάρκεια session.

## Sample policy

Το `manifest.json` είναι η πηγή αλήθειας για:

- σειρά levels 1-5,
- filenames,
- SHA-256,
- PCM format,
- διάρκεια,
- layer gains,
- master gain,
- limiter ceiling,
- interaction duration και tail behavior.

Η εφαρμογή αρνείται να ανοίξει την engine αν λείπει ή αλλάζει κάποιο runtime
asset. Το reference mix επαληθεύεται αλλά δεν προστίθεται στον mixer.

## Gain staging

Η αριθμητική σύγκριση με το reference mix έδειξε unity gain (`0 dB`) για όλα τα
stems. Το reference ανακατασκευάζεται από το γραμμικό τους άθροισμα με residual
περίπου `-68 dB`.

Το master safety ceiling βρίσκεται στα `-0,18 dBFS`. Ο μετρητής
`LimitedSampleCount` πρέπει να παραμένει μηδενικός στην κανονική λειτουργία. Αν
αυξηθεί, το γεγονός θα καταγράφεται ως audio fault/gain-staging warning.

## Fades και pause

Τα fades υπολογίζονται σε audio frames και όχι σε UI timer ticks. Έτσι δεν
επηρεάζονται από προσωρινή καθυστέρηση του γραφικού περιβάλλοντος.

Οι προεπιλεγμένες διάρκειες είναι `900 ms` για fade-in και `1.500 ms` για
fade-out. Οι τιμές διατηρούν την αφή άμεση, αλλά αποφεύγουν το αντιληπτό
απότομο κόψιμο του ήχου σε δημόσια παρουσίαση.

Το `PauseAfterFadeAsync`:

1. θέτει όλα τα target gains στο μηδέν,
2. περιμένει μέχρι ο audio callback να ολοκληρώσει πραγματικά το ramp,
3. ακυρώνεται αν στο μεταξύ δοθεί νέο level/resume command,
4. κάνει pause μόνο μετά τη σιωπή.

## Preflight

Από τη ρίζα του repository:

```powershell
dotnet run --project .\src\Synapse.Audio.Preflight\Synapse.Audio.Preflight.csproj `
  --configuration Release -- `
  .\local-data\compositions\<composition>
```

Το preflight:

- επαληθεύει manifest και πέντε WAV,
- ελέγχει hashes, format και διάρκεια,
- εντοπίζει το επιλεγμένο Windows endpoint,
- εμφανίζει το device mix format,
- αρχικοποιεί WASAPI με silence,
- δεν παίζει ήχο.

## Tests

```powershell
dotnet run --project .\tests\Synapse.Audio.Tests\Synapse.Audio.Tests.csproj `
  --configuration Release -- `
  .\local-data\compositions
```

Τα σημερινά automated tests καλύπτουν:

- πέντε providers σε κοινό mixer clock,
- frame-aligned stereo gain ramps,
- master ceiling και limited-sample counter,
- validation του πραγματικού manifest και των WAV.

## Εκκρεμείς δοκιμές

- Πραγματική αναπαραγωγή των πέντε stems.
- Ακουστική σύγκριση με το reference mix.
- Start skew/drift measurement στα 60/120/180 seconds.
- Device unplug/replug ενώ λειτουργεί η engine.
- Τετράωρο soak test.
- Self-contained publish και δοκιμή σε καθαρό Windows 10/11 σύστημα.
- Σύνδεση του δοκιμασμένου Serial protocol του ESP32 με το υπάρχον session
  state machine του presentation UI.
