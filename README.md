# CS_OBS: Automatic OBS Launcher

CS_OBS is a lightweight Linux utility that automatically starts and stops OBS with the Replay Buffer enabled when you start playing a game. It serves as a flexible replacement for Nvidia ShadowPlay, giving you the power and customization of OBS Studio with automatic game detection. The application is designed to be simple, efficient, and easy to configure.

**Note:** While Windows support exists in the code, this application is primarily developed and tested on Linux. Windows functionality is untested.

## Features

- **ShadowPlay Replacement:** A flexible alternative to Nvidia ShadowPlay that works with any graphics card and gives you the full power of OBS Studio.
- **Automatic OBS Control:** Automatically launches OBS with the Replay Buffer when a whitelisted game starts, and closes it when the game stops.
- **Whitelist System:** You have full control over which games trigger OBS to launch.
- **Process Picker:** A user-friendly process picker to easily add running games to your whitelist.
- **Manual Control:** Add and remove games from the whitelist manually.
- **System Tray Integration:** The application runs in the system tray for easy access and minimal intrusion.
- **Linux-Focused:** Developed and tested primarily on Linux, with automatic detection of Flatpak and system OBS installations.

## Installation

1. **Download the repository:** Either download as ZIP from GitHub or clone with `git clone https://github.com/LeeseTheFox/CS_OBS.git`

2. **Install system dependencies:**
   ```bash
   # Fedora
   sudo dnf install python3-pip python3-gobject gtk3 python3-tkinter
   
   # Ubuntu/Debian  
   sudo apt install python3-pip python3-gi python3-gi-cairo gir1.2-gtk-3.0 python3-tkinter
   ```

3. **Install Python dependencies:**
   ```bash
   pip install -r requirements.txt
   ```

4. **Install OBS Studio** (if not already installed)

5. **Run the application:**
   ```bash
   python3 cs_obs.py
   ```

## Usage

1.  **Configure the application:**
    - The first time you run the application, it will create a `config.json` file.
    - Add the games you want to be whitelisted using the "Process picker" or by adding them manually.
    - Click "Start the service" to begin monitoring for game launches.

2.  **System Tray:**
    - The application will run in the system tray.
    - Right-click the tray icon to show/hide the main window or quit the application.

## Configuration

The `config.json` file is automatically created in the same directory as the application. Here's a detailed explanation of the configuration options:

```json
{
    "obs_path": "path/to/your/obs/executable",
    "whitelisted_games": [
        "game1.exe",
        "game2"
    ]
}
```

-   `obs_path`: The path to your OBS executable.
    -   **Linux (System):** `"obs"` (if it's in your system's PATH) or `/usr/bin/obs`
    -   **Linux (Flatpak):** `"flatpak run com.obsproject.Studio"` (auto-detected)
    -   **Windows (untested):** `"C:\\Program Files\\obs-studio\\bin\\64bit\\obs64.exe"`
-   `whitelisted_games`: A list of game process names that will trigger OBS to launch.
    -   **Native Linux games:** Typically no extension (e.g., `"csgo"`, `"Crab Game.x86_64"`)
    -   **Proton/Wine games:** Include the .exe extension (e.g., `"Discovery.exe"`)
    -   **Windows games (untested):** Executable name with .exe extension