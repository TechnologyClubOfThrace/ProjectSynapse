# Synapse v2 portable build

Η portable έκδοση στοχεύει Windows 10/11 x64 και είναι self-contained. Ο τελικός
υπολογιστής δεν χρειάζεται εγκατεστημένο .NET, Internet ή δικαιώματα διαχειριστή.

## Δημιουργία

Από τη ρίζα του project:

```powershell
dotnet restore .\src\Synapse.App\Synapse.App.csproj `
  -r win-x64 `
  --force-evaluate `
  --packages .\.packages `
  --source https://api.nuget.org/v3/index.json

.\tools\Build-Portable.ps1 `
  -AssetsRoot .\local-data `
  -PackageName "Synapse-v2-portable-win-x64"
```

Το restore χρειάζεται δίκτυο μόνο για τα επίσημα NuGet packages και runtime
packs. Το packaging μετά το restore λειτουργεί χωρίς δίκτυο.

Ο φάκελος που δίνεται στο `AssetsRoot` πρέπει να περιέχει τους υποφακέλους
`compositions` και `calibrations`. Τα αρχεία αυτά παραμένουν τοπικά και δεν
περιλαμβάνονται στο repository.

Το script σταματά αν υπάρχει ήδη φάκελος ή ZIP με το ίδιο όνομα. Δεν διαγράφει
ούτε αντικαθιστά προηγούμενο build.

## Περιεχόμενο

- self-contained εφαρμογή `win-x64`, χωρίς trimming ή single-file bundling,
- η βιβλιοθήκη `compositions`,
- μόνο το `active-calibration.json`,
- κενός χώρος `data\runtime` για τις νέες τοπικές καταγραφές,
- οδηγίες εκκίνησης και SHA-256 manifest,
- ZIP και ξεχωριστό SHA-256 του ZIP.

Το υπάρχον `participation-events.jsonl` δεν αντιγράφεται ποτέ.

## Υποχρεωτικοί έλεγχοι πριν από διανομή

1. Release build χωρίς warnings/errors.
2. Εκτέλεση του `tests\Synapse.Audio.Tests`.
3. Εκτέλεση του `tools\Verify-AudioAssets.ps1`.
4. Έλεγχος όλων των εγγραφών του `FILE_HASHES_SHA256.txt`.
5. Εκκίνηση του `Synapse.App.exe` απευθείας από τον portable φάκελο.
6. Πραγματική δοκιμή ήχου, ESP32, αποσύνδεσης USB και βαθμονόμησης στον τελικό
   υπολογιστή της εγκατάστασης.

## Γνωστά όρια της τρέχουσας δοκιμαστικής έκδοσης

- Η ενεργή βαθμονόμηση περιλαμβάνει επιβεβαιωμένα επίπεδα 0-2, όχι ακόμη 0-7.
- Το presentation profile χρησιμοποιεί GPIO 1 για αφή και GPIO 47 για LED data.
- Παραμένει υποχρεωτική η τελική δοκιμή reset με ολόκληρη την εγκατάσταση.
- Τα όρια φωτισμού LED είναι προσωρινές σταθερές του firmware και δεν
  επηρεάζονται από τη βαθμονόμηση της εφαρμογής.
- Απαιτείται δοκιμή σε καθαρό Windows 10/11 x64 σύστημα πριν χαρακτηριστεί το
  πακέτο έκδοση παρουσίασης.
