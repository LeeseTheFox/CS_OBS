# CS_OBS: Automatic OBS Launcher

CS_OBS is a lightweight, cross-platform utility that automatically starts and stops OBS with the Replay Buffer enabled when you start playing a game. It's designed to be simple, efficient, and easy to configure.

## Features

- **Automatic OBS Control:** Automatically launches OBS with the Replay Buffer when a whitelisted game starts, and closes it when the game stops.
- **Whitelist System:** You have full control over which games trigger OBS to launch.
- **Process Picker:** A user-friendly process picker to easily add running games to your whitelist.
- **Manual Control:** Add and remove games from the whitelist manually.
- **System Tray Integration:** The application runs in the system tray for easy access and minimal intrusion.
- **Cross-Platform:** Works on both Windows and Linux.

## Getting Started

### Prerequisites

- **Python 3:** Make sure you have Python 3 installed on your system.
- **OBS Studio:** The application requires OBS Studio to be installed.
- **Python Dependencies:** Install the required Python packages:
  ```sh
  pip install -r requirements.txt
  ```
- **PyGObject:** Required for system tray functionality on Linux with Wayland. On some systems, you may need to install additional system packages:
  ```sh
  # Fedora
  sudo dnf install python3-gobject gtk3
  # Ubuntu/Debian
  sudo apt install python3-gi python3-gi-cairo gir1.2-gtk-3.0
  ```

## Usage

1.  **Run the application:**
    ```sh
    python3 cs_obs.py
    ```
2.  **Configure the application:**
    - The first time you run the application, it will create a `config.json` file.
    - Add the games you want to be whitelisted using the "Process picker" or by adding them manually.
    - Click "Start the service" to begin monitoring for game launches.
3.  **System Tray:**
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

-   `obs_path`: The absolute path to your OBS executable.
    -   **Windows Example:** `"C:\\Program Files\\obs-studio\\bin\\64bit\\obs64.exe"`
    -   **Linux Example:** `"obs"` (if it's in your system's PATH) or `/usr/bin/obs`
-   `whitelisted_games`: A list of game process names that will trigger the scene switch.
    -   On **Windows**, this should be the executable name (e.g., `"Discovery.exe"`).
    -   On **Linux**, if we're dealing with a native Linux game, typically the process name has no pre-defined extension (e.g., `"Crab Game.x86_64"`). If the game is running via Proton, you should include the .exe extension.