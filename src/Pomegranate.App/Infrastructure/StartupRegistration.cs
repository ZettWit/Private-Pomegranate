using Windows.ApplicationModel;

namespace Pomegranate.App.Infrastructure;

/// <summary>
/// "Start when Windows starts", through the MSIX package's startup task. Development builds that run
/// unpackaged leave the PC's startup programs alone, so trying the app never changes the registry.
/// </summary>
internal static class StartupRegistration
{
    private const string TaskId = "PomegranateStartup";

    public static async Task ApplyAsync(bool enabled)
    {
        if (!AppInfo.IsPackaged)
        {
            Log.Info($"Start with Windows = {enabled} (not applied: development build, not installed).");
            return;
        }

        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (enabled && task.State == StartupTaskState.Disabled)
            {
                await task.RequestEnableAsync();
            }
            else if (!enabled && task.State == StartupTaskState.Enabled)
            {
                task.Disable();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't change the start-with-Windows setting.", ex);
        }
    }
}
