# Project Synapse

<p align="center">
  <img src="src/Synapse.App/Assets/synapse-logo-gr.png" alt="Synapse - Σύλλογος Τεχνολογίας Θράκης" width="520">
</p>

Το Synapse είναι μια διαδραστική εγκατάσταση συλλογικής αφής, φωτισμού και
μουσικής. Μια κοινή επιφάνεια αφής συνδέεται με ESP32-S3. Το firmware μετρά τη
χωρητική μεταβολή και οδηγεί τον φωτισμό του δέντρου, ενώ η εφαρμογή Windows
εκτιμά το επίπεδο συμμετοχής και ελέγχει τα μουσικά layers.

Το έργο αναπτύσσεται από τον Σύλλογο Τεχνολογίας Θράκης.

- Οπτικοποίηση δεδομένων: <https://synapse-network-steth.vercel.app/>

## Περιεχόμενο repository

- `firmware/SynapseV2/`: firmware αφής, τηλεμετρίας και RGBWC φωτισμού.
- `src/Synapse.App/`: εφαρμογή παρουσίασης για Windows 10/11.
- `src/Synapse.Audio/`: audio engine πέντε συγχρονισμένων layers.
- `src/Synapse.Input/`: Serial επικοινωνία, βαθμονόμηση και ταξινόμηση αφής.
- `tests/`: αυτοματοποιημένοι έλεγχοι.
- `tools/`: έλεγχος audio assets και δημιουργία portable πακέτου.
- `docs/`: τεχνική τεκμηρίωση και checklist ελέγχων.

Οι μουσικές συνθέσεις, οι βαθμονομήσεις, οι καταγραφές και τα portable builds
δεν αποθηκεύονται στο Git. Παραμένουν τοπικά ώστε το repository να είναι μικρό
και να μην περιέχει δεδομένα της εγκατάστασης.

Η αρχική έκδοση διατηρείται στο tag `v1.0.0`.

## Απαιτήσεις εφαρμογής

- Windows 10 ή 11 x64
- .NET 8 SDK
- συμβατή συσκευή ήχου
- ESP32-S3 με το firmware του φακέλου `firmware/SynapseV2`

## Τοπικά δεδομένα

Δημιουργήστε τοπικά, χωρίς να τα προσθέσετε στο Git, την ακόλουθη δομή:

```text
local-data/
├── compositions/
│   └── <composition>/
│       ├── manifest.json
│       ├── <five WAV layers>
│       └── <reference mix WAV>
├── calibrations/
│   └── active-calibration.json
└── runtime/
```

Κάθε σύνθεση χρειάζεται έγκυρο manifest και πέντε συγχρονισμένα WAV αρχεία.
Οι λεπτομέρειες της μορφής περιγράφονται στο `docs/AUDIO_ENGINE.md`.

## Build και έλεγχοι

```powershell
dotnet build .\src\Synapse.App\Synapse.App.csproj --configuration Release

dotnet run --project .\tests\Synapse.Audio.Tests\Synapse.Audio.Tests.csproj `
  --configuration Release -- `
  .\local-data\compositions
```

## Εκκίνηση από τον πηγαίο κώδικα

```powershell
dotnet run --project .\src\Synapse.App\Synapse.App.csproj `
  --configuration Release -- `
  --compositions .\local-data\compositions `
  --calibrations .\local-data\calibrations `
  --runtime .\local-data\runtime
```

Η εφαρμογή δεν ξεκινά αυτόματα την αναπαραγωγή. Ο χειριστής επιλέγει συσκευή
εξόδου, εκτελεί την προετοιμασία ήχου και στη συνέχεια συνδέει το ESP32.

## Firmware

Το presentation profile χρησιμοποιεί:

- touch input: `GPIO 1`
- RGBWC data: `GPIO 47`
- Serial: `115200 baud`

Οι πλήρεις ρυθμίσεις πλακέτας και οι οδηγίες upload βρίσκονται στο
`firmware/SynapseV2/README.md`.

## Portable πακέτο

```powershell
.\tools\Build-Portable.ps1 `
  -AssetsRoot .\local-data `
  -PackageName "Synapse-v2-portable-win-x64"
```

Το παραγόμενο πακέτο αποθηκεύεται στο αγνοούμενο `artifacts/portable` και
περιλαμβάνει checksum SHA-256. Πριν από διανομή απαιτείται ο έλεγχος του
`docs/V2_RELEASE_CHECKLIST.md`.

## Όρια επαλήθευσης

Το επιτυχημένο build και οι αυτοματοποιημένοι έλεγχοι δεν αντικαθιστούν τη
δοκιμή με την τελική επιφάνεια αφής, τα LED, τα τροφοδοτικά, τη συσκευή ήχου
και τα ηχεία της εγκατάστασης.

## Άδεια

Copyright (C) 2026 Technology Club of Thrace.

Ο πηγαίος κώδικας του Project Synapse διατίθεται σύμφωνα με την GNU General
Public License v3.0 only (`GPL-3.0-only`). Δείτε το αρχείο `LICENSE`.

Οι μουσικές συνθέσεις και τα δεδομένα της εγκατάστασης δεν περιλαμβάνονται
στο repository και δεν καλύπτονται από την παρούσα άδεια.
