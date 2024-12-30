using System;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace CS_OBS3
{
    public class TaskSchedulerUtil
    {
        private const string TaskName = "CS_OBS Autostart";

        public static bool CreateStartupTask(string executablePath)
        {
            try
            {
                using TaskService ts = new();
                TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = "Starts CS_OBS with admin privileges on system startup";

                td.Principal.RunLevel = TaskRunLevel.Highest;

                td.Triggers.Add(new LogonTrigger());

                td.Actions.Add(new ExecAction(executablePath));

                td.Settings.StartWhenAvailable = true;
                td.Settings.DisallowStartIfOnBatteries = false;
                td.Settings.StopIfGoingOnBatteries = false;

                ts.RootFolder.RegisterTaskDefinition(TaskName, td);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating startup task: {ex.Message}");
                return false;
            }
        }

        public static bool DeleteStartupTask()
        {
            try
            {
                using TaskService ts = new();
                ts.RootFolder.DeleteTask(TaskName, false);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting startup task: {ex.Message}");
                return false;
            }
        }

        public static bool IsTaskExists()
        {
            using TaskService ts = new();
            return ts.GetTask(TaskName) != null;
        }
    }
}