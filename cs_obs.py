import tkinter as tk
from tkinter import messagebox
import json
import os
import subprocess
import sys
import psutil
from PIL import Image, ImageDraw
import pystray
import threading
import warnings
import platform

# --- Configuration ---
script_dir = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(script_dir, 'config.json')
MONITOR_SCRIPT_PATH = os.path.join(script_dir, 'service.py')
LOG_PATH = os.path.join(script_dir, 'actions.log')

# Clear log file on startup
try:
    open(LOG_PATH, 'w').close()
except IOError as e:
    print(f"Error clearing log file on startup: {e}")


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


class ConfigManagerApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.withdraw() # Start the window in a hidden state to prevent flickering
        self.title("CS_OBS")
        self.geometry("480x500")
        self.resizable(False, False)

        self.config = self.load_config()
        self.monitor_process = None
        self.tray_icon = None

        # --- UI Elements ---
        # Frame for game list
        games_frame = tk.LabelFrame(self, text="Whitelisted games", padx=10, pady=10)
        games_frame.pack(padx=10, pady=10, fill="both", expand=True)

        list_container = tk.Frame(games_frame)
        list_container.pack(pady=5, fill="both", expand=True)

        scrollbar = tk.Scrollbar(list_container, orient=tk.VERTICAL)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)

        self.games_listbox = tk.Listbox(list_container, yscrollcommand=scrollbar.set)
        self.games_listbox.pack(side=tk.LEFT, fill="both", expand=True)

        scrollbar.config(command=self.games_listbox.yview)

        add_frame = tk.Frame(games_frame)
        add_frame.pack(pady=5, fill="x")

        self.new_game_entry = tk.Entry(add_frame)
        self.new_game_entry.pack(side="left", fill="x", expand=True, padx=(0, 5))
        self.new_game_entry.bind("<Return>", self.add_game)

        self.add_button = tk.Button(add_frame, text="Add manually", command=self.add_game)
        self.add_button.pack(side="left")

        self.pick_process_button = tk.Button(add_frame, text="Process picker", command=self.show_process_picker)
        self.pick_process_button.pack(side="left", padx=(5, 0))

        self.remove_button = tk.Button(games_frame, text="Remove the selected game", command=self.remove_game)
        self.remove_button.pack(pady=5, fill="x")

        # Frame for actions
        action_frame = tk.Frame(self)
        action_frame.pack(padx=10, pady=10, fill="x")

        self.start_on_boot_var = tk.BooleanVar()
        self.start_on_boot_check = tk.Checkbutton(action_frame, text="Start on boot", variable=self.start_on_boot_var, command=self.toggle_start_on_boot)
        self.start_on_boot_check.pack(side="left")

        self.toggle_monitor_button = tk.Button(action_frame, text="Start the service", command=self.toggle_monitor)
        self.toggle_monitor_button.pack(side="right")

        self.status_label = tk.Label(self, text="Status: Unknown", bd=1, relief=tk.SUNKEN, anchor=tk.W)
        self.status_label.pack(side=tk.BOTTOM, fill=tk.X)

        self.log_label = tk.Label(self, text="", bd=1, relief=tk.SUNKEN, anchor=tk.W, fg="blue")
        self.log_label.pack(side=tk.BOTTOM, fill=tk.X)

        self.populate_ui()
        
        # --- Startup sequence ---
        self.monitor_process = self.find_monitor_process() # Get initial status

        has_games = bool(self.config.get('whitelisted_games'))

        # On Linux, check and update autostart service if needed
        if platform.system() == "Linux":
            # Check if autostart service exists and is outdated
            if self.is_systemd_service_active() and self.is_autostart_outdated():
                self.check_and_update_autostart()
            self.start_on_boot_var.set(self.is_systemd_service_active())
        else:
            self.start_on_boot_check.config(state=tk.DISABLED)

        # 1. Auto-start service if games are configured and it's not running
        if has_games and not self.monitor_process:
            self.start_monitor(show_messages=False)

        # 2. Start periodic status checks
        self.check_monitor_status()
        self.update_log_display()

        self.protocol("WM_DELETE_WINDOW", self.hide_window)

        # 3. Setup persistent tray icon
        self.setup_tray_icon()
        
        # 4. Show window only if no games are configured
        if not has_games:
            self.show_window()

    def show_process_picker(self):
        process_picker = ProcessPicker(self, self.add_game_from_picker)
        process_picker.grab_set()

    def add_game_from_picker(self, process_name):
        if process_name:
            current_games = self.games_listbox.get(0, tk.END)
            if process_name not in current_games:
                self.games_listbox.insert(tk.END, process_name)
                self._save_config()
                if not self.monitor_process:
                    self.start_monitor()
            else:
                messagebox.showinfo("Info", f"'{process_name}' is already in the whitlist.")
    def create_icon_image(self):
        """Creates a PIL image for the tray icon."""
        # Try to load the custom icon file first
        icon_path = os.path.join(script_dir, 'icon.ico')
        
        try:
            if os.path.exists(icon_path):
                # Temporarily ignore the UserWarning from the IcoImagePlugin
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    icon = Image.open(icon_path)

                # Use integer value 3 for BICUBIC resampling (works in all Pillow versions)
                return icon.resize((32, 32), 3)
        except Exception as e:
            print(f"Error loading icon: {e}")
        
        # Fall back to the checkerboard pattern if the icon file can't be loaded
        width = 32  # Changed to match the resize above
        height = 32
        color1 = 'black'
        color2 = 'white'
        image = Image.new('RGB', (width, height), color1)
        dc = ImageDraw.Draw(image)
        dc.rectangle((width // 2, 0, width, height // 2), fill=color2)
        dc.rectangle((0, height // 2, width // 2, height), fill=color2)
        return image

    def setup_tray_icon(self):
        """Creates and runs the persistent system tray icon."""
        image = self.create_icon_image()
        menu = pystray.Menu(
            pystray.MenuItem('Show / Hide', self.toggle_window_visibility, default=True),
            pystray.MenuItem('Quit', self.quit_application_from_tray)
        )
        self.tray_icon = pystray.Icon("cs-obs", image, "CS_OBS", menu)
        # Run the icon in a separate thread
        threading.Thread(target=self.tray_icon.run, daemon=True).start()

    def toggle_window_visibility(self):
        """Shows or hides the main window."""
        if self.state() == 'normal':
            self.hide_window()
        else:
            self.show_window()

    def show_window(self):
        """Shows and focuses the main window."""
        self.after(0, self.deiconify)
        self.after(10, self.lift) # Bring to front
        self.after(20, self.focus_force)

    def hide_window(self):
        """Hides the main window."""
        self.withdraw()

    def quit_application_from_tray(self):
        """A wrapper to call quit_application from the tray menu."""
        # This ensures the call is scheduled for the main tkinter thread
        self.after(0, self.quit_application)

    def quit_application(self):
        """Handles the logic of properly quitting the application."""
        self.monitor_process = self.find_monitor_process() # Get current status
        if self.monitor_process:
            self.stop_monitor(show_messages=False) # Stop the service silently
        
        if self.tray_icon:
            self.tray_icon.stop()
        
        # Clean up the log file on exit
        if os.path.exists(LOG_PATH):
            try:
                os.remove(LOG_PATH)
            except OSError as e:
                print(f"Error removing log file: {e}")

        # Check and update autostart service if needed before exit
        if platform.system() == "Linux" and self.is_systemd_service_active() and self.is_autostart_outdated():
            self.check_and_update_autostart()

        self.destroy()

    def _save_config(self):
        """Saves the current UI state to the config file."""
        updated_config = self.config.copy()
        updated_config['whitelisted_games'] = list(self.games_listbox.get(0, tk.END))

        try:
            with open(CONFIG_PATH, 'w') as f:
                json.dump(updated_config, f, indent=4)
            self.config = updated_config # update internal config state
        except IOError as e:
            messagebox.showerror("Error", f"Failed to save config file:\n{e}")

    def load_config(self):
        """Loads configuration from config.json or creates and returns defaults."""
        try:
            with open(CONFIG_PATH, 'r') as f:
                config_data = json.load(f)
                # Ensure all keys are present
                config_data.setdefault('whitelisted_games', [])
                config_data.setdefault('obs_path', detect_obs_path())
                return config_data
        except (FileNotFoundError, json.JSONDecodeError):
            # Create a default config file (without comments)
            obs_path = detect_obs_path()
            default_config = {
                "obs_path": obs_path,
                "whitelisted_games": []
            }
            
            # Create the config file
            try:
                with open(CONFIG_PATH, 'w') as f:
                    json.dump(default_config, f, indent=4)
                
                # Create a README file with examples if it doesn't exist
                readme_path = os.path.join(script_dir, 'README.md')
                if not os.path.exists(readme_path):
                    with open(readme_path, 'w') as f:
                        f.write("""# CS_OBS Configuration Guide

## Configuration Examples

### For Windows games
Include the .exe extension:
```json
{
    "obs_path": "C:\\Program Files\\obs-studio\\bin\\64bit\\obs64.exe",
    "whitelisted_games": [
        "csgo.exe",
        "Discovery.exe",
        "valorant.exe"
    ]
}
```

### For Linux games (System OBS)
Use the executable name without extension:
```json
{
    "obs_path": "obs",
    "whitelisted_games": [
        "csgo",
        "valorant",
        "dota2"
    ]
}
```

### For Linux games (Flatpak OBS)
The script will auto-detect Flatpak OBS:
```json
{
    "obs_path": "flatpak run com.obsproject.Studio",
    "whitelisted_games": [
        "csgo",
        "valorant",
        "dota2"
    ]
}
```
""")
                
                print(f"Created default config file at {CONFIG_PATH}")
                
                show_message = "A default configuration file has been created.\nPlease add your games to the whitelist.\nSee README.md for examples."
                if "flatpak" in obs_path.lower():
                    show_message += f"\n\nDetected Flatpak OBS installation:\n{obs_path}"
                
                messagebox.showinfo("Configuration", show_message)
            except IOError as e:
                print(f"Error creating default config file: {e}")
                messagebox.showerror("Error", f"Failed to create default config file:\n{e}")
            
            return default_config

    def toggle_start_on_boot(self):
        """Handles the 'Start on boot' checkbox toggle."""
        if platform.system() != "Linux":
            return

        if self.start_on_boot_var.get():
            self.create_systemd_service()
        else:
            self.delete_systemd_service()

    def get_service_file_path(self):
        """Returns the path for the systemd service file."""
        return os.path.expanduser("~/.config/systemd/user/cs_obs.service")

    def is_systemd_service_active(self):
        """Checks if the systemd service is enabled."""
        service_path = self.get_service_file_path()
        if not os.path.exists(service_path):
            return False
        
        try:
            # Check if the service is enabled (will link to multi-user.target)
            status = subprocess.check_output(['systemctl', '--user', 'is-enabled', 'cs_obs.service'], text=True, stderr=subprocess.DEVNULL).strip()
            return status == 'enabled'
        except (subprocess.CalledProcessError, FileNotFoundError):
            return False

    def create_systemd_service(self):
        """Creates and enables the systemd service file."""
        service_path = self.get_service_file_path()
        service_dir = os.path.dirname(service_path)

        if not os.path.exists(service_dir):
            os.makedirs(service_dir)
            
        python_executable = sys.executable
        script_path = os.path.abspath(__file__)
        working_dir = os.path.dirname(script_path)

        service_content = f"""[Unit]
Description=CS_OBS: Automatic OBS Launcher
After=graphical-session.target

[Service]
ExecStart={python_executable} {script_path}
WorkingDirectory={working_dir}
Restart=on-failure

[Install]
WantedBy=graphical-session.target
"""
        try:
            with open(service_path, 'w') as f:
                f.write(service_content)

            # Reload systemd, enable and start the service
            subprocess.run(['systemctl', '--user', 'daemon-reload'], check=True)
            subprocess.run(['systemctl', '--user', 'enable', 'cs_obs.service'], check=True)

        except (IOError, subprocess.CalledProcessError) as e:
            messagebox.showerror("Error", f"Failed to create or enable service: {e}")

    def delete_systemd_service(self):
        """Disables and deletes the systemd service file."""
        service_path = self.get_service_file_path()

        try:
            # Stop and disable the service
            subprocess.run(['systemctl', '--user', 'stop', 'cs_obs.service'], check=False, stderr=subprocess.DEVNULL)
            subprocess.run(['systemctl', '--user', 'disable', 'cs_obs.service'], check=False, stderr=subprocess.DEVNULL)

            if os.path.exists(service_path):
                os.remove(service_path)
            
            # Reload systemd
            subprocess.run(['systemctl', '--user', 'daemon-reload'], check=True)

        except (IOError, subprocess.CalledProcessError) as e:
            messagebox.showerror("Error", f"Failed to disable or remove service: {e}")

    def populate_ui(self):
        """Populates UI elements with data from the loaded config."""
        self.games_listbox.delete(0, tk.END)
        for game in self.config.get('whitelisted_games', []):
            self.games_listbox.insert(tk.END, game)

    def add_game(self, event=None):
        """Adds a new game to the list."""
        new_game = self.new_game_entry.get().strip()
        if new_game and new_game not in self.games_listbox.get(0, tk.END):
            self.games_listbox.insert(tk.END, new_game)
            self.new_game_entry.delete(0, tk.END)
            self._save_config()
        elif not new_game:
            messagebox.showwarning("Warning", "Game name cannot be empty.")
        else:
            messagebox.showwarning("Warning", f"'{new_game}' is already in the list.")

    def remove_game(self):
        """Removes the selected game from the list."""
        selected_indices = self.games_listbox.curselection()
        if not selected_indices:
            messagebox.showwarning("Warning", "Please select a game to remove.")
            return
        # We delete from the bottom up to avoid index shifting issues
        for i in sorted(selected_indices, reverse=True):
            self.games_listbox.delete(i)
        self._save_config()

    def start_monitor(self, show_messages=True):
        """Starts the monitoring script."""
        if not self.config.get('whitelisted_games'):
            if show_messages:
                messagebox.showwarning("Warning", "Cannot start the service: no whitelisted games defined.")
            return

        # Silently exit if process is already running
        if self.find_monitor_process():
            return

        try:
            # Use DEVNULL to detach the process from the GUI's console
            subprocess.Popen(['python3', MONITOR_SCRIPT_PATH], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if show_messages:
                messagebox.showinfo("Monitor", "The service started.")
        except FileNotFoundError:
            if show_messages:
                messagebox.showerror("Error", "Could not find python3. Please ensure it's in your PATH.")
        except Exception as e:
            if show_messages:
                messagebox.showerror("Error", f"Failed to start the service: {e}")
        
        # Immediately update status after action
        self.check_monitor_status()

    def stop_monitor(self, show_messages=True):
        """Stops the monitoring script."""
        process_to_stop = self.find_monitor_process()
        if not process_to_stop:
            return # Silently exit, nothing to stop

        try:
            process_to_stop.terminate()
            try:
                process_to_stop.wait(timeout=3)
            except psutil.TimeoutExpired:
                process_to_stop.kill()
            if show_messages:
                messagebox.showinfo("Monitor", "The service stopped.")
        except psutil.NoSuchProcess:
            pass  # Already gone
        except Exception as e:
            if show_messages:
                messagebox.showerror("Error", "Could not stop the service: {e}")

        self.monitor_process = None
        # Immediately update status after action
        self.check_monitor_status()

    def find_monitor_process(self):
        """Finds if the monitor script process is running."""
        for proc in psutil.process_iter(['pid', 'name', 'cmdline']):
            if proc.info['cmdline'] and 'python' in proc.name().lower():
                cmdline = ' '.join(proc.info['cmdline'])
                if MONITOR_SCRIPT_PATH in cmdline:
                    return proc
        return None

    def check_monitor_status(self):
        """Checks and updates the monitor status label and button."""
        self.monitor_process = self.find_monitor_process()
        if self.monitor_process:
            self.status_label.config(text=f"Status: the service is RUNNING (PID: {self.monitor_process.pid})", fg="green")
            self.toggle_monitor_button.config(text="Stop the service")
        else:
            self.status_label.config(text="Status: the service is NOT RUNNING", fg="red")
            self.toggle_monitor_button.config(text="Start the service")
        self.after(2000, self.check_monitor_status) # Periodically check

    def update_log_display(self):
        """Checks for and displays the latest action from the log file."""
        log_message = ""
        if os.path.exists(LOG_PATH):
            try:
                with open(LOG_PATH, 'r') as f:
                    log_message = f.read().strip()
            except IOError:
                log_message = "Error reading log file."
        
        if log_message:
            self.log_label.config(text=f"Logs: {log_message}")
            if not self.log_label.winfo_viewable():
                self.log_label.pack(side=tk.BOTTOM, fill=tk.X, after=self.status_label)
        else:
            if self.log_label.winfo_viewable():
                self.log_label.pack_forget()

        self.after(2000, self.update_log_display) # Periodically check

    def toggle_monitor(self):
        """Starts or stops the monitoring script."""
        # Use find_monitor_process to get the most up-to-date status
        if self.find_monitor_process():
            self.stop_monitor()
        else:
            self.start_monitor()

    def is_autostart_outdated(self):
        """Checks if the autostart service file exists and if its paths match the current script location."""
        if platform.system() != "Linux":
            return False
            
        service_path = self.get_service_file_path()
        if not os.path.exists(service_path):
            return False
            
        # Get current script paths
        python_executable = sys.executable
        script_path = os.path.abspath(__file__)
        working_dir = os.path.dirname(script_path)
        
        try:
            # Read the existing service file
            with open(service_path, 'r') as f:
                service_content = f.read()
                
            # Check if paths in the service file match current paths
            return (f"ExecStart={python_executable} {script_path}" not in service_content or
                    f"WorkingDirectory={working_dir}" not in service_content)
                
        except IOError as e:
            print(f"Error checking autostart service: {e}")
            return False

    def check_and_update_autostart(self):
        """Checks if the systemd service file exists and if its paths match the current script location.
        If not, it updates the service file."""
        if platform.system() != "Linux":
            return False
            
        service_path = self.get_service_file_path()
        if not os.path.exists(service_path):
            return False
            
        # Get current script paths
        python_executable = sys.executable
        script_path = os.path.abspath(__file__)
        working_dir = os.path.dirname(script_path)
        
        try:
            # Read the existing service file
            with open(service_path, 'r') as f:
                service_content = f.read()
                
            # Check if paths in the service file match current paths
            if (f"ExecStart={python_executable} {script_path}" not in service_content or
                f"WorkingDirectory={working_dir}" not in service_content):
                
                # Paths don't match, update the service file
                new_service_content = f"""[Unit]
Description=CS_OBS: Automatic OBS Launcher
After=graphical-session.target

[Service]
ExecStart={python_executable} {script_path}
WorkingDirectory={working_dir}
Restart=on-failure

[Install]
WantedBy=graphical-session.target
"""
                with open(service_path, 'w') as f:
                    f.write(new_service_content)
                    
                # Reload systemd
                subprocess.run(['systemctl', '--user', 'daemon-reload'], check=True)
                print("Updated autostart service with new script location.")
                
                # Show a notification to the user
                messagebox.showinfo(
                    "Autostart Updated", 
                    "The autostart service has been updated with the new location of the script."
                )
                
                return True  # Return True if service was updated
                
        except (IOError, subprocess.CalledProcessError) as e:
            print(f"Error checking or updating autostart service: {e}")
            messagebox.showerror(
                "Autostart Error", 
                f"Failed to update autostart service with new location:\n{e}"
            )
            
        return False  # Return False if no update was needed or if it failed

class ProcessPicker(tk.Toplevel):
    def __init__(self, master, callback):
        super().__init__(master)
        self.title("Process picker")
        self.minsize(300, 400)
        self.callback = callback

        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", self.filter_list)
        search_entry = tk.Entry(self, textvariable=self.search_var)
        search_entry.pack(fill=tk.X, padx=5, pady=5)

        self.process_listbox = tk.Listbox(self)
        self.process_listbox.pack(fill=tk.BOTH, expand=True, padx=5, pady=5)

        select_button = tk.Button(self, text="Select a process", command=self.on_select)
        select_button.pack(pady=5)

        self.processes = self.get_process_list()
        self.populate_listbox(self.processes)

        self.process_listbox.bind("<Double-Button-1>", self.on_select)

    def get_process_list(self):
        processes = []
        for p in psutil.process_iter(['name', 'exe']):
            try:
                # We need a special consideration for Linux as some processes might not have an exe.
                # Also, on Linux, game executables often don't have a file extension.
                if p.info['exe'] or platform.system() != 'Windows':
                     processes.append(p.info['name'])
            except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
                pass
        return sorted(list(set(processes)), key=str.lower)

    def populate_listbox(self, processes):
        self.process_listbox.delete(0, tk.END)
        for process_name in processes:
            self.process_listbox.insert(tk.END, process_name)

    def filter_list(self, *args):
        search_term = self.search_var.get().lower()
        if not search_term:
            self.populate_listbox(self.processes)
        else:
            filtered_processes = [p for p in self.processes if search_term in p.lower()]
            self.populate_listbox(filtered_processes)
    
    def on_select(self, event=None):
        selected_indices = self.process_listbox.curselection()
        if selected_indices:
            selected_process = self.process_listbox.get(selected_indices[0])
            self.callback(selected_process)
            self.destroy()

if __name__ == "__main__":
    app = ConfigManagerApp()
    app.mainloop()
