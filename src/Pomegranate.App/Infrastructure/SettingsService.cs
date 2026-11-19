using System.IO;
using Pomegranate.Core.Settings;

namespace Pomegranate.App.Infrastructure;

/// <summary>The current settings. Every change is saved at once and announced to the rest of the app.</summary>
internal sealed class SettingsService(SettingsStore store)
{
    private PomegranateSettings _current = store.Load();

    public event EventHandler<PomegranateSettings>? Changed;

    public PomegranateSettings Current => _current;

    public void Update(Func<PomegranateSettings, PomegranateSettings> change)
    {
        var next = change(_current).Normalize();
        if (next == _current)
        {
            return;
        }

        _current = next;
        try
        {
            store.Save(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Couldn't save settings.", ex);
        }

        Changed?.Invoke(this, next);
    }
}
