namespace Outatime.App.Platform;

/// Open at Login. Packaged (the Store build): the MSIX startup task, which the user can also switch off in Windows
/// Settings → Apps → Startup. Unpackaged: the Run key. Elsewhere it isn't offered.
public static class Startup
{
    const string TaskId = "OutatimeStartup";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Supported => OperatingSystem.IsWindows();

    public static bool IsPackaged
    {
        get
        {
#if WINDOWS
            try { return Windows.ApplicationModel.Package.Current != null; } catch (Exception) { return false; }
#else
            return false;
#endif
        }
    }

    public static async Task<bool> IsEnabled()
    {
#if WINDOWS
        if (IsPackaged)
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
                return task.State is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            catch (Exception) { return false; }
        }
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("Outatime") != null;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    /// Returns whether it's on afterwards: the user (or a policy) can refuse.
    public static async Task<bool> SetEnabled(bool on)
    {
#if WINDOWS
        if (IsPackaged)
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
                if (on) await task.RequestEnableAsync(); else task.Disable();
            }
            catch (Exception) { }
            return await IsEnabled();
        }
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue("Outatime", $"\"{Environment.ProcessPath}\""); else key.DeleteValue("Outatime", false);
        return await IsEnabled();
#else
        await Task.CompletedTask;
        return false;
#endif
    }
}
