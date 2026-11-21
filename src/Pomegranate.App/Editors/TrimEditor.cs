using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pomegranate.App.Infrastructure;
using Pomegranate.Core.Tools;
using Pomegranate.Engines.Tools;

namespace Pomegranate.App.Editors;

/// <summary>
/// Trim (audio and video) and Bleep (audio), spec §7.3. Audio shows a waveform and can play; video shows a
/// strip of frames and a large preview of the frame at the handle being moved.
/// </summary>
internal sealed class TrimEditor : EditorWindow
{
    private const int FrameCount = 10;

    private readonly MediaPreview _media;
    private readonly bool _isVideo;
    private readonly bool _bleep;
    private readonly RangeBar _bar;
    private readonly TextBlock _times = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 6), Opacity = 0.8 };
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _previewDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private ImageStage? _stage;
    private Button? _play;
    private bool _playing;
    private double _stopAt;
    private bool _silence;
    private double _previewAt;
    private bool _previewBusy;

    public TrimEditor(string path, MediaPreview media, bool isVideo, bool bleep)
        : base(L.Get(bleep ? "Tool_Bleep" : "Tool_Trim"), path)
    {
        _media = media;
        _isVideo = isVideo;
        _bleep = bleep;
        _bar = new RangeBar { Marking = bleep, Height = isVideo ? 72 : 180, Margin = new Thickness(12, 8, 12, 0) };
        AutomationName(_bar, L.Get(bleep ? "Bleep_Ranges" : "Trim_Range"));
        _bar.Changed += UpdateTimes;
        _bar.HandleMoved += t =>
        {
            if (_isVideo)
            {
                _previewAt = t;
                _previewDelay.Stop();
                _previewDelay.Start();
            }
        };
        _previewDelay.Tick += async (_, _) =>
        {
            _previewDelay.Stop();
            await ShowFrameAsync(_previewAt);
        };
        _clock.Tick += (_, _) => Tick();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && !_isVideo && Keyboard.FocusedElement is not TextBox)
            {
                TogglePlay();
                e.Handled = true;
            }
        };
        Closed += (_, _) =>
        {
            _clock.Stop();
            _player.Close();
        };
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        var info = await _media.ProbeAsync(SourcePath, cancellationToken);
        var duration = info.Duration?.TotalSeconds ?? throw new InvalidOperationException(L.Get("Trim_NoDuration"));
        _bar.Duration = duration;
        _bar.Start = 0;
        _bar.End = duration;

        var body = new DockPanel();
        if (_isVideo)
        {
            _stage = new ImageStage();
            var first = Path.Combine(Temp, "preview.png");
            await _media.FrameAsync(SourcePath, 0, 1600, first, cancellationToken);
            _stage.Show(ImageStage.LoadBitmap(first), info.DisplayWidth, info.DisplayHeight);
            _bar.Frames = await FramesAsync(duration, cancellationToken);
            DockPanel.SetDock(_bar, Dock.Bottom);
            body.Children.Add(_bar);
            body.Children.Add(_stage);
        }
        else
        {
            _bar.Peaks = await _media.PeaksAsync(SourcePath, 2000, Temp, cancellationToken);
            var wav = Path.Combine(Temp, "play.wav");
            await _media.PlayableWavAsync(SourcePath, wav, cancellationToken);
            _player.Open(new Uri(wav));
            _bar.VerticalAlignment = VerticalAlignment.Center;
            body.Children.Add(_bar);

            _play = IconButton("", L.Get("Trim_Play"), (_, _) => TogglePlay(), L.Get("Trim_PlayTip"));
            Toolbar.Children.Add(_play);
        }

        if (_bleep)
        {
            Toolbar.Children.Add(Choice<bool>(
                [("", L.Get("Bleep_Tone"), false), ("", L.Get("Bleep_Silence"), true)],
                false,
                silence => _silence = silence));
            Toolbar.Children.Add(IconButton("", L.Get("Bleep_Remove"), (_, _) => _bar.RemoveSelectedMark()));
        }

        Toolbar.Children.Add(_times);
        Body = body;
        Hint = L.Get(_bleep ? "Bleep_Hint" : _isVideo ? "Trim_HintVideo" : "Trim_Hint");
        UpdateTimes();
        _bar.Focus();
    }

    /// <summary>A strip of frames spread over the clip, for finding your way along the timeline.</summary>
    private async Task<IReadOnlyList<BitmapSource>> FramesAsync(double duration, CancellationToken cancellationToken)
    {
        var frames = new List<BitmapSource>();
        for (var i = 0; i < FrameCount; i++)
        {
            var file = Path.Combine(Temp, $"strip-{i}.png");
            var t = duration * (i + 0.5) / FrameCount;
            try
            {
                await _media.FrameAsync(SourcePath, t, 160, file, cancellationToken);
                frames.Add(ImageStage.LoadBitmap(file));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"No frame at {t}s for the timeline.", ex);
            }
        }

        return frames;
    }

    private async Task ShowFrameAsync(double seconds)
    {
        if (_stage is null || _previewBusy)
        {
            return;
        }

        _previewBusy = true;
        try
        {
            var file = Path.Combine(Temp, "preview.png");
            await _media.FrameAsync(SourcePath, Math.Min(seconds, Math.Max(0, _bar.Duration - 0.05)), 1600, file, ClosingToken);
            _stage.Replace(ImageStage.LoadBitmap(file));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"Couldn't show the frame at {seconds}s.", ex);
        }
        finally
        {
            _previewBusy = false;
        }
    }

    private void TogglePlay()
    {
        if (_play is null)
        {
            return;
        }

        if (_playing)
        {
            StopPlaying();
            return;
        }

        // Trim plays what will be kept; Bleep plays the selected range (or everything) with a second of lead-in.
        double from, to;
        if (!_bleep)
        {
            from = _bar.Start;
            to = _bar.End;
        }
        else if (_bar.SelectedMark >= 0)
        {
            var mark = _bar.Marks[_bar.SelectedMark];
            from = Math.Max(0, mark.Start - 1);
            to = Math.Min(_bar.Duration, mark.End + 1);
        }
        else
        {
            from = 0;
            to = _bar.Duration;
        }

        _stopAt = to;
        _player.Position = TimeSpan.FromSeconds(from);
        _player.Play();
        _playing = true;
        _play.Content = IconLabel("", L.Get("Trim_Stop"));
        _clock.Start();
    }

    private void StopPlaying()
    {
        _player.Pause();
        _playing = false;
        _clock.Stop();
        _bar.Playhead = null;
        _bar.InvalidateVisual();
        if (_play is not null)
        {
            _play.Content = IconLabel("", L.Get("Trim_Play"));
        }
    }

    private void Tick()
    {
        var position = _player.Position.TotalSeconds;
        if (position >= _stopAt)
        {
            StopPlaying();
            return;
        }

        // Bleep preview: mute the marked ranges while playing so you hear what the result will be like.
        if (_bleep)
        {
            _player.IsMuted = _bar.Marks.Any(m => position >= m.Start && position <= m.End);
        }

        _bar.Playhead = position;
        _bar.InvalidateVisual();
    }

    private void UpdateTimes()
    {
        if (_bleep)
        {
            var total = _bar.Marks.Sum(m => m.Length);
            _times.Text = L.Format("Bleep_Summary", _bar.Marks.Count, RangeBar.Format(total));
        }
        else
        {
            _times.Text = L.Format("Trim_Summary", RangeBar.Format(_bar.Start), RangeBar.Format(_bar.End), RangeBar.Format(_bar.End - _bar.Start));
        }
    }

    protected override ToolEdit? BuildEdit()
    {
        if (_bleep)
        {
            if (_bar.Marks.Count == 0)
            {
                Hint = L.Get("Bleep_NothingYet");
                return null;
            }

            return new BleepEdit(_bar.Marks.OrderBy(m => m.Start).ToArray(), _silence);
        }

        if (_bar.Start <= 0.01 && _bar.End >= _bar.Duration - 0.01)
        {
            Hint = L.Get("Trim_NothingYet");
            return null;
        }

        return new TrimEdit(new TimeRange(_bar.Start, _bar.End));
    }
}
