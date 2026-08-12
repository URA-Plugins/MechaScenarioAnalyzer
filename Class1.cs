using System.Text.Json;
using System.Text.Json.Serialization;
using Gallop;
using Gallop.Endpoints;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace MechaScenarioAnalyzer;

public sealed class MechaScenarioAnalyzer : IPlugin
{
    const string InternalName = "MechaScenarioAnalyzer";
    const string TrainingPanelKey = "training";
    const int DefaultHistoryLimit = 100;
    const int MaximumHistoryLimit = 1000;

    static readonly JsonSerializerOptions SettingsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };

    readonly object historyGate = new();
    readonly List<HistoryEntry> history = [];

    IApplication? application;
    Workspace? workspace;
    WorkspaceContent? panelContent;
    WorkspaceContent? liveSnapshot;
    HistoryPanelView? historyView;
    int historyLimit = DefaultHistoryLimit;
    int selectedIndex = -1;
    bool hasPublishedTrainingPanel;
    bool hasUnread;
    volatile bool disposed;

    static string SettingsDirectory => Path.Combine("PluginData", InternalName);

    static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public void Initialize(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = LoadSettings();
        lock (historyGate)
        {
            history.Clear();
            liveSnapshot = null;
            selectedIndex = -1;
            hasUnread = false;
            historyLimit = settings.HistoryLimit;
            application = context.Application;
            disposed = false;
            hasPublishedTrainingPanel = false;
        }
    }

    public void Dispose()
    {
        HistoryPanelView? view;
        Workspace? target;
        var removePanel = false;
        lock (historyGate)
        {
            if (disposed)
                return;
            disposed = true;
            history.Clear();
            liveSnapshot = null;
            selectedIndex = -1;
            hasUnread = false;
            view = historyView;
            historyView = null;
            panelContent = null;
            target = workspace;
            removePanel = hasPublishedTrainingPanel;
            hasPublishedTrainingPanel = false;
        }
        view?.DetachKeyboard();

        if (removePanel)
            target!.RemovePanel(TrainingPanelKey);
    }

    [ResponseAnalyzer<GameApi.SingleModeMecha.CheckEvent>(1)]
    public ValueTask Analyze(SingleModeMechaCheckEventResponse response)
    {
        var data = response.data;
        if (data.home_info.command_info_array is null || data.chara_info.state is 2 or 3)
            return ValueTask.CompletedTask;
        if (data.chara_info.playing_state != 26
            && ((data.unchecked_event_array is { Length: > 0 }) || data.race_start_info is not null))
        {
            return ValueTask.CompletedTask;
        }

        var key = new HistoryKey(data.chara_info.single_mode_chara_id, data.chara_info.turn);
        var content = Handler.ParseMechaCommandInfo(response);
        Publish(key, content);
        return ValueTask.CompletedTask;
    }

    public async Task ConfigPromptAsync(
        IApplication application,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        cancellationToken.ThrowIfCancellationRequested();
        if (application.TopRunnable is null &&
            Environment.CurrentManagedThreadId != application.MainThreadId)
        {
            throw new InvalidOperationException(
                "MechaScenarioAnalyzer 无法从非 UI thread 启动配置：Terminal.Gui 当前没有正在运行的 session。");
        }

        var draft = LoadSettings();
        HistorySettings saved;
        if (Environment.CurrentManagedThreadId == application.MainThreadId)
        {
            saved = RunConfigDialog(application, draft, cancellationToken);
        }
        else
        {
            var completion = new TaskCompletionSource<HistorySettings>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            application.Invoke(() =>
            {
                try
                {
                    completion.SetResult(RunConfigDialog(application, draft, cancellationToken));
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
            saved = await completion.Task;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(saved, SettingsJson));
        ApplyHistoryLimit(saved.HistoryLimit);
    }

    static HistorySettings RunConfigDialog(
        IApplication application,
        HistorySettings draft,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new Dialog
        {
            Title = "MechaScenarioAnalyzer 配置",
            Width = 58,
            Height = 12,
        };
        var historyLimit = new NumericUpDown<int>
        {
            X = 1,
            Y = 2,
            Width = 18,
            Value = draft.HistoryLimit,
            Increment = 1,
        };
        var validation = new Label
        {
            X = 1,
            Y = 5,
            Width = Dim.Fill(1),
            Height = 2,
            Text = string.Empty,
        };
        dialog.Add(
            new Label { X = 1, Y = 1, Text = "History 保存上限（0 表示关闭）" },
            historyLimit,
            new Label { X = 21, Y = 2, Text = "范围：0–1000" },
            validation);

        var accepted = false;
        var save = new Button { Text = "保存", IsDefault = true };
        save.Accepting += (_, e) =>
        {
            if (historyLimit.Value is < 0 or > MaximumHistoryLimit)
            {
                validation.Text = "History 上限必须是 0 到 1000。";
                e.Handled = true;
                return;
            }

            accepted = true;
            application.RequestStop(dialog);
            e.Handled = true;
        };
        var cancel = new Button { Text = "取消" };
        cancel.Accepting += (_, e) =>
        {
            application.RequestStop(dialog);
            e.Handled = true;
        };
        dialog.AddButton(cancel);
        dialog.AddButton(save);
        historyLimit.SetFocus();

        using (cancellationToken.Register(
                   () => application.Invoke(() => application.RequestStop(dialog))))
            application.Run(dialog);
        cancellationToken.ThrowIfCancellationRequested();
        if (!accepted)
        {
            throw new OperationCanceledException(
                "MechaScenarioAnalyzer 配置已取消。",
                cancellationToken);
        }

        return new(historyLimit.Value);
    }

    static HistorySettings LoadSettings()
    {
        if (!File.Exists(SettingsPath))
            return new(DefaultHistoryLimit);

        HistorySettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<HistorySettings>(
                    File.ReadAllText(SettingsPath),
                    SettingsJson)
                ?? throw new JsonException("配置内容不能是 null。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"MechaScenarioAnalyzer 配置文件无效: {SettingsPath}。{ex.Message}",
                ex);
        }

        ValidateHistoryLimit(settings.HistoryLimit);
        return settings;
    }

    static void ValidateHistoryLimit(int value)
    {
        if (value is < 0 or > MaximumHistoryLimit)
        {
            throw new InvalidDataException(
                $"MechaScenarioAnalyzer historyLimit 必须在 0 到 {MaximumHistoryLimit} 之间，当前值: {value}。配置文件: {SettingsPath}");
        }
    }

    void Publish(HistoryKey key, WorkspaceContent content)
    {
        var refresh = false;
        var notifyUnread = false;
        Workspace? target;
        lock (historyGate)
        {
            if (disposed)
                return;

            target = workspace ??= Workspace.Create(InternalName);
            if (!hasPublishedTrainingPanel)
            {
                panelContent = new(CreateHistoryView);
                target.SetPanel(
                    TrainingPanelKey,
                    "训练分析",
                    panelContent,
                    switchToWorkspace: true);
                hasPublishedTrainingPanel = true;
            }
            else
            {
                target.SetPanel(
                    TrainingPanelKey,
                    "训练分析",
                    panelContent!,
                    switchToWorkspace: false);
            }

            liveSnapshot = content;
            if (historyLimit == 0)
            {
                history.Clear();
                selectedIndex = -1;
                hasUnread = false;
                refresh = true;
            }
            else
            {
                var existingIndex = history.FindIndex(entry => entry.Key == key);
                if (existingIndex >= 0)
                {
                    history[existingIndex] = new(key, content);
                    refresh = existingIndex == selectedIndex;
                }
                else
                {
                    var wasFollowingLatest = selectedIndex < 0 || selectedIndex == history.Count - 1;
                    history.Add(new(key, content));
                    if (wasFollowingLatest)
                    {
                        selectedIndex = history.Count - 1;
                        hasUnread = false;
                        refresh = true;
                    }
                    else if (!hasUnread)
                    {
                        hasUnread = true;
                        notifyUnread = true;
                    }

                    if (TrimHistoryLocked())
                    {
                        refresh = true;
                        notifyUnread = false;
                    }
                }
            }
        }

        if (refresh)
            RefreshHistoryView();
        if (notifyUnread)
            NotifyIfActive(target, "有新的训练分析记录。按 → 查看最新。");
    }

    void ApplyHistoryLimit(int value)
    {
        ValidateHistoryLimit(value);
        lock (historyGate)
        {
            historyLimit = value;
            if (value == 0)
            {
                history.Clear();
                selectedIndex = -1;
                hasUnread = false;
            }
            else
            {
                TrimHistoryLocked();
            }
        }
        RefreshHistoryView();
    }

    bool TrimHistoryLocked()
    {
        var overflow = history.Count - historyLimit;
        if (overflow <= 0)
            return false;

        if (selectedIndex < overflow)
        {
            history.RemoveRange(0, overflow);
            selectedIndex = history.Count - 1;
            hasUnread = false;
            return true;
        }

        history.RemoveRange(0, overflow);
        selectedIndex -= overflow;
        return false;
    }

    bool Navigate(KeyCode keyCode)
    {
        var refresh = false;
        int position;
        int count;
        lock (historyGate)
        {
            if (disposed || historyLimit == 0 || history.Count == 0)
                return false;

            var previousIndex = selectedIndex;
            selectedIndex = keyCode switch
            {
                KeyCode.CursorUp => Math.Max(0, selectedIndex - 1),
                KeyCode.CursorDown => Math.Min(history.Count - 1, selectedIndex + 1),
                KeyCode.CursorLeft => 0,
                KeyCode.CursorRight => history.Count - 1,
                _ => selectedIndex,
            };
            if (selectedIndex == history.Count - 1)
                hasUnread = false;

            refresh = selectedIndex != previousIndex;
            position = selectedIndex + 1;
            count = history.Count;
        }

        if (refresh)
            RefreshHistoryView();
        NotifyIfActive(workspace, $"训练分析历史 {position}/{count}");
        return true;
    }

    WorkspaceContent? SelectedContentLocked()
        => historyLimit > 0 && selectedIndex >= 0 && selectedIndex < history.Count
            ? history[selectedIndex].Content
            : liveSnapshot;

    View CreateHistoryView()
    {
        lock (historyGate)
        {
            if (disposed)
            {
                return new View
                {
                    Width = Dim.Fill(),
                    Height = Dim.Auto(),
                };
            }

            var app = application
                ?? throw new InvalidOperationException("MechaScenarioAnalyzer 尚未初始化 IApplication。");
            var view = new HistoryPanelView(app, this);
            historyView = view;
            view.Show(SelectedContentLocked() ?? WorkspaceContent.Text(string.Empty));
            return view;
        }
    }

    void RefreshHistoryView()
    {
        HistoryPanelView? view;
        IApplication? app;
        lock (historyGate)
        {
            view = historyView;
            app = application;
        }
        if (view is null || app is null)
            return;

        if (Environment.CurrentManagedThreadId == app.MainThreadId)
        {
            ShowIfActive(view);
            return;
        }
        app.Invoke(() => ShowIfActive(view));
    }

    void ShowIfActive(HistoryPanelView view)
    {
        lock (historyGate)
        {
            if (disposed || !ReferenceEquals(historyView, view) ||
                !hasPublishedTrainingPanel || workspace is null || panelContent is null)
            {
                return;
            }

            var content = SelectedContentLocked();
            if (content is null)
                return;
            view.Show(content);
            workspace.SetPanel(
                TrainingPanelKey,
                "训练分析",
                panelContent,
                switchToWorkspace: false);
        }
    }

    void NotifyIfActive(Workspace? target, string text)
    {
        lock (historyGate)
        {
            if (disposed || target is null || !ReferenceEquals(workspace, target))
                return;
            target.Notify(text, UiSeverity.Info);
        }
    }

    void ReleaseHistoryView(HistoryPanelView view)
    {
        lock (historyGate)
        {
            if (ReferenceEquals(historyView, view))
                historyView = null;
        }
    }

    readonly record struct HistoryKey(int SingleModeCharaId, int Turn);

    sealed record HistoryEntry(HistoryKey Key, WorkspaceContent Content);

    sealed record HistorySettings(int HistoryLimit);

    sealed class HistoryPanelView : View
    {
        readonly IApplication application;
        readonly MechaScenarioAnalyzer owner;
        bool keyboardAttached = true;
        bool viewDisposed;

        internal HistoryPanelView(IApplication application, MechaScenarioAnalyzer owner)
        {
            this.application = application;
            this.owner = owner;
            Width = Dim.Fill();
            Height = Dim.Auto();
            CanFocus = true;
            TabStop = TabBehavior.TabStop;
            application.Keyboard.KeyDown += ApplicationKeyDown;
        }

        internal void Show(WorkspaceContent content)
        {
            if (viewDisposed)
                return;

            var next = content.CreateView();
            next.X = 0;
            next.Y = 0;
            next.Width = Dim.Fill();
            next.Height = Dim.Auto();
            next.CanFocus = false;

            var previous = SubViews.FirstOrDefault();
            if (previous is not null)
            {
                Remove(previous);
                previous.Dispose();
            }
            Add(next);
            SetNeedsLayout();
            SetNeedsDraw();
        }

        internal void DetachKeyboard()
        {
            if (!keyboardAttached)
                return;
            keyboardAttached = false;
            application.Keyboard.KeyDown -= ApplicationKeyDown;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !viewDisposed)
            {
                viewDisposed = true;
                DetachKeyboard();
                owner.ReleaseHistoryView(this);
            }
            base.Dispose(disposing);
        }

        void ApplicationKeyDown(object? sender, Key key)
        {
            if (key.Handled || key.IsCtrl || key.IsAlt || key.IsShift ||
                !ReferenceEquals(Workspace.Current, owner.workspace) ||
                !ContainsFocus())
            {
                return;
            }

            if (key.KeyCode is not (
                    KeyCode.CursorUp or
                    KeyCode.CursorDown or
                    KeyCode.CursorLeft or
                    KeyCode.CursorRight))
            {
                return;
            }

            if (owner.Navigate(key.KeyCode))
                key.Handled = true;
        }

        bool ContainsFocus()
        {
            for (var view = application.TopRunnableView?.MostFocused;
                 view is not null;
                 view = view.SuperView)
            {
                if (ReferenceEquals(view, this))
                    return true;
            }
            return false;
        }
    }
}
