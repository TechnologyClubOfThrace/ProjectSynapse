import sqlite3
import datetime
from flask import Flask, request, jsonify

'''
A simple Flask server to recieve data from ESP.
Store the data to a simple SQLite database
Build for SYNAPSE event.
'''

DB_NAME = "synapse_data.db"
SERVER_PORT = 5000

app = Flask(__name__)

# =====================================================
# ΑΡΧΙΚΟΠΟΙΗΣΗ ΒΑΣΗΣ ΔΕΔΟΜΕΝΩΝ (Initialise SQLITE)
# =====================================================
def init_db():
    conn = sqlite3.connect(DB_NAME)
    cursor = conn.cursor()
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS touch_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
            raw_touch INTEGER NOT NULL,
            target_energy REAL NOT NULL,
            people INTEGER NOT NULL,
            duration_ms INTEGER NOT NULL
        )
    ''')
    conn.commit()
    conn.close()

# =====================================================
# API ENDPOINT for ESP32
# =====================================================
@app.route('/api/event', methods=['POST'])
def record_event():
    # Λήψη JSON payload από το ESP32
    data = request.get_json(force=True)
    
    if not data:
        return jsonify({"status": "error", "message": "Missing JSON payload"}), 400

    raw_touch = data.get("raw")
    target_energy = data.get("target")
    people = data.get("people")
    duration_ms = data.get("duration_ms")

    # Έλεγχος ότι υπάρχουν τα απαραίτητα πεδία
    if raw_touch is None or target_energy is None or people is None or duration_ms is None:
        return jsonify({"status": "error", "message": "Incomplete data fields"}), 400

    # Αποθήκευση στη βάση SQLite
    try:
        conn = sqlite3.connect(DB_NAME)
        cursor = conn.cursor()
        cursor.execute('''
            INSERT INTO touch_events (raw_touch, target_energy, people, duration_ms)
            VALUES (?, ?, ?, ?)
        ''', (raw_touch, float(target_energy), int(people), int(duration_ms)))
        
        event_id = cursor.lastrowid
        conn.commit()
        conn.close()

        # Τερματικό log για άμεσο monitoring
        now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{now}] ID: {event_id:<4} | People: {people:<2} | Target: {float(target_energy)*100:>5.1f}% | Raw: {raw_touch:<6} | Duration: {duration_ms}ms")

        return jsonify({"status": "success", "id": event_id}), 201

    except Exception as e:
        print(f"Database error: {e}")
        return jsonify({"status": "error", "message": str(e)}), 500

# =====================================================
# MAIN
# =====================================================
if __name__ == '__main__':
    init_db()
    print("==================================================")
    print("      SYNAPSE - SQLITE TOUCH DATA LOGGER          ")
    print("==================================================")
    print(f"Βάση δεδομένων: {DB_NAME}")
    print(f"Πίνακας       : touch_events")
    print(f"Server Port   : {SERVER_PORT}")
    print("Αναμονή δεδομένων από το ESP32 στο /api/event...")
    print("==================================================")
    
    # host='0.0.0.0' ώστε να δέχεται κλήσεις από το ESP32 στο τοπικό Wi-Fi
    app.run(host='0.0.0.0', port=SERVER_PORT, debug=False)
