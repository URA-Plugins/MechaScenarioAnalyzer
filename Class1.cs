using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;

namespace MechaScenarioAnalyzer;

public sealed class MechaScenarioAnalyzer : IPlugin
{
    IDisposable? analyzerRegistration;
    Workspace? workspace;
    bool hasPublishedTrainingPanel;

    public string Name => "赛博杯剧本解析器";

    public string Author => "UmamusumeResponseAnalyzer";

    public string[] Targets => ["Cygames", "Komoe"];

    public void Initialize(IPluginContext context)
    {
        analyzerRegistration = context.Analyzers.RegisterResponse<
            GameApi.SingleModeMecha.CheckEvent,
            SingleModeMechaCheckEventResponse>(Analyze, priority: 1);
        hasPublishedTrainingPanel = false;
    }

    public void Dispose()
    {
        var registration = analyzerRegistration;
        analyzerRegistration = null;
        registration?.Dispose();

        if (!hasPublishedTrainingPanel)
            return;

        workspace!.RemovePanel("training");
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
        var workspace = this.workspace ??= Workspace.Create("MechaScenarioAnalyzer");
        workspace.SetPanel(
            "training",
            "训练分析",
            content,
            switchToWorkspace: !hasPublishedTrainingPanel);
        hasPublishedTrainingPanel = true;
        return ValueTask.CompletedTask;
    }
}
