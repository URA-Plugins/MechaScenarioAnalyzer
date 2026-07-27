using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;

namespace MechaScenarioAnalyzer;

public sealed class MechaScenarioAnalyzer : IPlugin
{
    IDisposable? analyzerRegistration;
    ILiveDisplayOutput? liveDisplay;
    LiveDisplayWorkspace? workspace;
    bool hasPublishedTrainingPanel;

    public string Name => "赛博杯剧本解析器";

    public string Author => "UmamusumeResponseAnalyzer";

    public string[] Targets => ["Cygames", "Komoe"];

    public void Initialize(IPluginContext context)
    {
        analyzerRegistration = context.Analyzers.RegisterResponse<
            GameApi.SingleModeMecha.CheckEvent,
            SingleModeMechaCheckEventResponse>(Analyze, priority: 1);
        liveDisplay = context.LiveDisplay;
        hasPublishedTrainingPanel = false;
    }

    public void Dispose()
    {
        analyzerRegistration?.Dispose();
        analyzerRegistration = null;

        if (liveDisplay is not null && workspace is not null)
            liveDisplay.RemoveWorkspace(workspace);

        liveDisplay = null;
        workspace = null;
        hasPublishedTrainingPanel = false;
    }

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

        var content = Handler.ParseMechaCommandInfo(response);
        LiveDisplay.SetPanel(
            Workspace,
            "training",
            "训练分析",
            content,
            switchToWorkspace: !hasPublishedTrainingPanel);
        hasPublishedTrainingPanel = true;
        return ValueTask.CompletedTask;
    }

    ILiveDisplayOutput LiveDisplay => liveDisplay
        ?? throw new InvalidOperationException("MechaScenarioAnalyzer 尚未初始化 LiveDisplay。");

    LiveDisplayWorkspace Workspace => workspace
        ??= LiveDisplay.CreateWorkspace("MechaScenarioAnalyzer");
}
