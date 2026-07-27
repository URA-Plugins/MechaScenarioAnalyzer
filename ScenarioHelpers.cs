using Gallop;

namespace MechaScenarioAnalyzer;

public class CommandInfo
{
    public CommandInfo(SingleModeMechaCheckEventResponse.CommonResponse response, int commandId)
    {
        CommandId = commandId;
        if (GameGlobal.ToTrainIndex.TryGetValue(commandId, out var trainIndex))
            TrainIndex = trainIndex + 1;

        var training = response.chara_info.training_level_info_array.FirstOrDefault(x => x.command_id == CommandId);
        TrainLevel = training is null ? 0 : training.level;

        var normalCommand = response.home_info.command_info_array.First(x => x.command_id == CommandId);
        TrainingPartners = normalCommand.training_partner_array
            .Select(x => new TrainingPartner(response, x, normalCommand))
            .OrderBy(x => x.Priority)
            .ToArray();
    }

    public int CommandId { get; }
    public int TrainIndex { get; }
    public int TrainLevel { get; }
    public IReadOnlyList<TrainingPartner> TrainingPartners { get; }
}

public sealed class MechaCommandInfo : CommandInfo
{
    public MechaCommandInfo(SingleModeMechaCheckEventResponse.CommonResponse response, int commandId) : base(response, commandId)
    {
        var command = response.mecha_data_set.command_info_array.First(x => x.command_id == commandId);
        PointUpInfoArray = command.point_up_info_array.Select(x => (x.status_type, x.value)).ToArray();
        IsRecommend = command.is_recommend;
    }

    public IReadOnlyList<(int StatusType, int Value)> PointUpInfoArray { get; }
    public bool IsRecommend { get; }
}

public sealed class TrainingPartner
{
    public TrainingPartner(SingleModeMechaCheckEventResponse.CommonResponse response, int position, SingleModeCommandInfo command)
    {
        var rawName = position is >= 1 and <= 6
            ? $"支援{response.chara_info.support_card_array.First(x => x.position == position).support_card_id}"
            : $"角色{position}";
        var friendship = response.chara_info.evaluation_info_array.FirstOrDefault(x => x.target_id == position)?.evaluation ?? 0;

        Priority = position is >= 1 and <= 6 ? 0 : 1;
        Shining = position is >= 1 and <= 6 && friendship >= 80;
        Name = $"{rawName}{(friendship is > 0 and < 100 ? $"({friendship})" : string.Empty)}";
        if (command.tips_event_partner_array.Intersect(command.training_partner_array).Contains(position))
            Name = $"!{Name}";
    }

    public int Priority { get; }
    public string Name { get; }
    public bool Shining { get; }
}

public sealed class TrainStats
{
    public int[] FiveValueGain = [];
    public int PtGain;
    public int VitalGain;
    public int FailureRate;
}

public static class ScoreUtils
{
    public static int ReviseOver1200(int value) => value > 1200 ? value * 2 - 1200 : value;
}

public sealed class TurnInfoMecha
{
    readonly SingleModeMechaCheckEventResponse.CommonResponse response;

    public TurnInfoMecha(SingleModeMechaCheckEventResponse.CommonResponse response)
    {
        this.response = response;
        CommandInfoArray = response.mecha_data_set.command_info_array
            .Select(x => new MechaCommandInfo(response, x.command_id))
            .Where(x => x.TrainIndex != 0)
            .ToArray();
    }

    public int Turn => response.chara_info.turn;
    public int Year => (Turn - 1) / 24 + 1;
    public int Month => ((Turn - 1) % 24) / 2 + 1;
    public string HalfMonth => Turn % 2 == 0 ? "后半" : "前半";
    public int Vital => response.chara_info.vital;
    public int MaxVital => response.chara_info.max_vital;
    public int[] Stats => [response.chara_info.speed, response.chara_info.stamina, response.chara_info.power, response.chara_info.guts, response.chara_info.wiz];
    public int[] StatsRevised => [.. Stats.Select(ScoreUtils.ReviseOver1200)];
    public int[] MaxStatsRevised =>
    [
        ScoreUtils.ReviseOver1200(response.chara_info.max_speed),
        ScoreUtils.ReviseOver1200(response.chara_info.max_stamina),
        ScoreUtils.ReviseOver1200(response.chara_info.max_power),
        ScoreUtils.ReviseOver1200(response.chara_info.max_guts),
        ScoreUtils.ReviseOver1200(response.chara_info.max_wiz)
    ];
    public IReadOnlyList<MechaCommandInfo> CommandInfoArray { get; }
}
