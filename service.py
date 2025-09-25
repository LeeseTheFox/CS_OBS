import json
import subprocess
import time
import psutil
import os
import platform
import shlex
import shutil

# Get the absolute path of the directory containing the script
script_dir = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(script_dir, 'config.json')
LOG_PATH = os.path.join(script_dir, 'actions.log')

# Hardcoded polling interval in seconds
POLL_INTERVAL = 5

# Clear log file on startup
try:
    open(LOG_PATH, 'w').close()
except IOError as e:
    print(f"Error clearing log file on startup: {e}")

def log_action(message):
    """Writes a message to the action log file, overwriting the previous one."""
    try:
        with open(LOG_PATH, 'w') as f:
            f.write(message)
    except IOError as e:
        print(f"Error writing to log file: {e}")

def detect_obs_path():
    """Detects the OBS path based on OS and available installations."""
    system = platform.system()
    
    if system == "Windows":
        # Windows paths (keep existing logic)
        possible_paths = [
            "C:\\Program Files\\obs-studio\\bin\\64bit\\obs64.exe",
            "C:\\Program Files (x86)\\obs-studio\\bin\\32bit\\obs32.exe"
        ]
        for path in possible_paths:
            if os.path.exists(path):
                return path
        return "obs"  # fallback
    
    else:  # Linux
        # Check for Flatpak OBS first
        try:
            subprocess.run(['flatpak', 'info', 'com.obsproject.Studio'], 
                         check=True, capture_output=True)
            return "flatpak run com.obsproject.Studio"
        except (subprocess.CalledProcessError, FileNotFoundError):
            pass
        
        # Check for system OBS
        try:
            subprocess.run(['which', 'obs'], check=True, capture_output=True)
            return "obs"
        except (subprocess.CalledProcessError, FileNotFoundError):
            pass
        
        return "obs"  # fallback

def load_config():
    """Loads the configuration from config.json."""
    try:
        with open(CONFIG_PATH, 'r') as f:
            return json.load(f)
    except FileNotFoundError:
        print(f"Error: {CONFIG_PATH} not found.")
        # Create a default config file based on OS
        obs_path = detect_obs_path()
        default_config = {
            "obs_path": obs_path,
            "whitelisted_games": []
        }
        
        try:
            with open(CONFIG_PATH, 'w') as f:
                json.dump(default_config, f, indent=4)
            
            print(f"Created default config file at {CONFIG_PATH}")
            return default_config
        except IOError as e:
            print(f"Error creating default config file: {e}")
            return None
    except json.JSONDecodeError:
        print(f"Error: Could not decode {CONFIG_PATH}.")
        return None

def is_process_running(identifier):
    """
    Checks if a process whose name or command-line contains `identifier` is running.
    """
    target = identifier.lower()

    for proc in psutil.process_iter(['pid', 'name', 'cmdline']):
        try:
            # 1) Match on the process name
            name = proc.info.get('name') or ""
            if target in name.lower():
                return True

            # 2) Match on cleaned command-line
            cmd = proc.info.get('cmdline') or []
            # filter out empty strings for clarity
            args = [arg for arg in cmd if arg.strip()]
            cmdline_str = " ".join(args).lower().replace("\\", "/")
            if target in cmdline_str:
                return True

        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue

def is_obs_running():
    """Checks if OBS is running (including Flatpak version)."""
    for proc in psutil.process_iter(['name', 'cmdline']):
        proc_name = proc.info['name'].lower()
        
        # Check for regular OBS process
        if proc_name == 'obs':
            return True
        
        # Check for Flatpak OBS process
        if proc.info['cmdline']:
            cmdline_str = ' '.join(proc.info['cmdline']).lower()
            if 'com.obsproject.studio' in cmdline_str:
                return True
    
    return False

def get_obs_process():
    """Returns the OBS process if it's running, None otherwise."""
    for proc in psutil.process_iter(['pid', 'name', 'cmdline']):
        try:
            proc_name = proc.info['name'].lower()
            cmdline = proc.info['cmdline'] or []
            cmdline_str = ' '.join(cmdline).lower()
            
            # Check for regular OBS process
            if proc_name == 'obs':
                return proc
            
            # Check for Flatpak OBS process
            if 'com.obsproject.studio' in cmdline_str:
                return proc
                
        except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
            continue
    
    return None

def cleanup_obs_sentinel():
    """Removes the .sentinel directory from OBS config directory to prevent shutdown warnings."""
    system = platform.system()
    config_dirs = []
    
    if system == "Linux":
        # Check both regular and Flatpak OBS locations
        config_dirs = [
            os.path.expanduser("~/.config/obs-studio"),
            os.path.expanduser("~/.var/app/com.obsproject.Studio/config/obs-studio")
        ]
    elif system == "Windows":
        config_dirs = [os.path.expanduser("~/AppData/Roaming/obs-studio")]
    elif system == "Darwin":  # macOS
        config_dirs = [os.path.expanduser("~/Library/Application Support/obs-studio")]
    
    for config_dir in config_dirs:
        if config_dir and os.path.exists(config_dir):
            sentinel_path = os.path.join(config_dir, ".sentinel")
            if os.path.exists(sentinel_path):
                try:
                    if os.path.isdir(sentinel_path):
                        shutil.rmtree(sentinel_path)
                        print(f"Removed OBS .sentinel directory from {config_dir}")
                    else:
                        os.remove(sentinel_path)
                        print(f"Removed OBS .sentinel file from {config_dir}")
                except OSError as e:
                    print(f"Warning: Could not remove .sentinel from {config_dir}: {e}")

def start_obs(obs_path):
    """Starts OBS and returns the OBS process object."""
    print("Starting OBS...")
    
    # Clean up sentinel file to prevent shutdown warnings (OBS 32.0+ compatibility)
    cleanup_obs_sentinel()
    
    try:
        # Handle Flatpak commands properly by splitting the command
        if obs_path.startswith('flatpak run'):
            command = shlex.split(obs_path) + [
                '--startreplaybuffer',
                '--minimize-to-tray'
            ]
        else:
            command = [
                obs_path,
                '--startreplaybuffer',
                '--minimize-to-tray'
            ]
        
        # Start the process
        subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        
        # Wait a moment for OBS to start, then find its process
        time.sleep(2)
        obs_process = get_obs_process()
        
        if obs_process:
            print(f"OBS started by script with PID: {obs_process.pid}")
            return obs_process
        else:
            print("Warning: Started OBS but couldn't find its process")
            return None
            
    except FileNotFoundError:
        print(f"Error: Could not find OBS executable at '{obs_path}'.")
        return None
    except Exception as e:
        print(f"Error starting OBS: {e}")
        return None

def stop_obs(obs_process):
    """Stops the OBS process."""
    if obs_process is None:
        return

    try:
        # Check if the process still exists and is still OBS
        if obs_process.is_running():
            proc_name = obs_process.name().lower()
            cmdline = obs_process.cmdline() or []
            cmdline_str = ' '.join(cmdline).lower()
            
            is_obs_process = (proc_name == 'obs' or 
                             'com.obsproject.studio' in cmdline_str or
                             'obs' in proc_name)
            
            if is_obs_process:
                print(f"Stopping OBS process with PID: {obs_process.pid}")
                obs_process.terminate()
                # Wait for the process to terminate
                try:
                    obs_process.wait(timeout=3)
                    print("OBS terminated successfully")
                except psutil.TimeoutExpired:
                    print(f"OBS process {obs_process.pid} did not terminate gracefully, killing it.")
                    obs_process.kill()
            else:
                print(f"Process with PID {obs_process.pid} is no longer an OBS process. Won't stop it.")
        else:
            print("OBS process is no longer running")
            
    except psutil.NoSuchProcess:
        print("OBS process already terminated")
    except Exception as e:
        print(f"Error stopping OBS process: {e}")


def main():
    """Main function to run the monitoring loop."""
    config = load_config()
    if not config:
        return

    try:
        last_mod_time = os.path.getmtime(CONFIG_PATH)
    except FileNotFoundError:
        print(f"Error: {CONFIG_PATH} not found on startup. Exiting.")
        return

    obs_path = config.get('obs_path', detect_obs_path())
    whitelisted_games = config.get('whitelisted_games', [])

    if not whitelisted_games:
        print("No whitelisted games found in config.json. Exiting.")
        return

    print("Starting monitoring...")
    print(f"Whitelisted games: {whitelisted_games}")
    print(f"OBS path: {obs_path}")

    script_obs_process = None
    last_running_game = None

    try:
        while True:
            try:
                # Check for configuration changes
                try:
                    current_mod_time = os.path.getmtime(CONFIG_PATH)
                    if current_mod_time != last_mod_time:
                        print("Configuration file changed, reloading...")
                        last_mod_time = current_mod_time
                        new_config = load_config()
                        if new_config:
                            config = new_config # Update the main config object
                            # Reload all config-dependent variables
                            obs_path = config.get('obs_path', detect_obs_path())
                            new_whitelisted_games = config.get('whitelisted_games', [])

                            # Only print if the list has actually changed
                            if set(whitelisted_games) != set(new_whitelisted_games):
                                print(f"Whitelist updated: {new_whitelisted_games}")
                                whitelisted_games = new_whitelisted_games
                            
                            if not whitelisted_games:
                                print("Whitelist is now empty. Stopping service.")
                                if script_obs_process:
                                    stop_obs(script_obs_process)
                                    script_obs_process = None
                                return # Exit the main function, stopping the script
                except FileNotFoundError:
                    print(f"Error: {CONFIG_PATH} was not found during a check. Stopping service.")
                    if script_obs_process:
                        stop_obs(script_obs_process)
                    return # Exit

                # Check for running games
                game_running = False
                running_game_name = None
                for game in whitelisted_games:
                    if is_process_running(game):
                        print(f"Found running game: {game}")
                        running_game_name = game
                        game_running = True
                        break

                # Check if our OBS process is still running
                script_obs_is_running = False
                if script_obs_process:
                    try:
                        if script_obs_process.is_running():
                            proc_name = script_obs_process.name().lower()
                            cmdline = script_obs_process.cmdline() or []
                            cmdline_str = ' '.join(cmdline).lower()
                            
                            # Check if it's still an OBS process (including Flatpak)
                            is_obs_process = (proc_name == 'obs' or 
                                             'com.obsproject.studio' in cmdline_str or
                                             'obs' in proc_name)
                            
                            if is_obs_process:
                                script_obs_is_running = True
                            else:
                                # Process changed, no longer OBS
                                print("Tracked process is no longer OBS")
                                script_obs_process = None
                        else:
                            # Process is no longer running
                            print("Tracked OBS process is no longer running")
                            script_obs_process = None
                    except (psutil.NoSuchProcess, psutil.AccessDenied):
                        # Process is gone or inaccessible
                        print("Tracked OBS process is gone")
                        script_obs_process = None

                # Debug output
                print(f"Game running: {game_running}, OBS running: {script_obs_is_running}")

                if game_running:
                    if not script_obs_is_running:
                        # Start OBS only if no other instance is running
                        if not is_obs_running():
                            log_action(f"{running_game_name} process detected, launching OBS...")
                            script_obs_process = start_obs(obs_path)
                            last_running_game = running_game_name
                        else:
                            print("OBS is already running (started externally)")
                elif script_obs_is_running:
                    # Game is not running, stop our instance of OBS
                    if last_running_game:
                        log_action(f"{last_running_game} process no longer present, closing OBS...")
                    else:
                        log_action("Whitelisted game process no longer present, closing OBS...")
                    stop_obs(script_obs_process)
                    script_obs_process = None
                    last_running_game = None

            except Exception as e:
                print(f"An error occurred in the monitoring loop: {e}")
            
            time.sleep(POLL_INTERVAL)
    except KeyboardInterrupt:
        print("\nStopping monitoring.")
        # On exit, only stop OBS if we started it
        if script_obs_process:
            stop_obs(script_obs_process)

if __name__ == "__main__":
    main()
