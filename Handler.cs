using Gallop;
using System.Text;
using UmamusumeResponseAnalyzer.TerminalGui;
using static MechaScenarioAnalyzer.i18n.Game;

namespace MechaScenarioAnalyzer;

public static class Handler
{
    static int currentTurn;

    public static WorkspaceContent ParseMechaCommandInfo(SingleModeMechaCheckEventResponse @event)
    {
        var criticalInfo = new List<string>();
        var turn = new TurnInfoMecha(@event.data);
        var dataset = @event.data.mecha_data_set;

        if (currentTurn != turn.Turn - 1
            && currentTurn != turn.Turn
            && turn.Turn != 1)
        {
            criticalInfo.Add(string.Format(I18N_WrongTurnAlert, currentTurn, turn.Turn));
        }

        if (@event.data.chara_info.playing_state != 1)
            criticalInfo.Add(I18N_RepeatTurn);
        else
            currentTurn = turn.Turn;

        var trainItems = new Dictionary<int, SingleModeCommandInfo>
        {
            [GameGlobal.TrainIdsMecha[0]] = @event.data.home_info.command_info_array[0],
            [GameGlobal.TrainIdsMecha[1]] = @event.data.home_info.command_info_array[1],
            [GameGlobal.TrainIdsMecha[2]] = @event.data.home_info.command_info_array[2],
            [GameGlobal.TrainIdsMecha[3]] = @event.data.home_info.command_info_array[3],
            [GameGlobal.TrainIdsMecha[4]] = @event.data.home_info.command_info_array[4]
        };
        var trainStats = new TrainStats[5];

        foreach (var (trainId, index) in GameGlobal.TrainIdsMecha.Select((trainId, index) => (trainId, index)))
        {
            var trainItem = trainItems[trainId];
            var trainParams = new Dictionary<int, int>
            {
                [1] = 0,
                [2] = 0,
                [3] = 0,
                [4] = 0,
                [5] = 0,
                [30] = 0,
                [10] = 0
            };
            foreach (var trainParam in trainItem.params_inc_dec_info_array)
                trainParams[trainParam.target_type] += trainParam.value;

            var stats = new TrainStats
            {
                FailureRate = trainItem.failure_rate,
                VitalGain = trainParams[10],
                FiveValueGain = [trainParams[1], trainParams[2], trainParams[3], trainParams[4], trainParams[5]],
                PtGain = trainParams[30]
            };
            stats.VitalGain = Math.Clamp(stats.VitalGain, -turn.Vital, turn.MaxVital - turn.Vital);

            var upperGain = dataset.command_info_array
                .FirstOrDefault(x => x.command_id == trainItem.command_id
                    || x.command_id == GameGlobal.XiahesuIds[GameGlobal.ToTrainId[trainId]])
                ?.params_inc_dec_info_array;
            if (upperGain is not null)
            {
                foreach (var item in upperGain)
                {
                    if (item.target_type == 30)
                        stats.PtGain += item.value;
                    else if (item.target_type <= 5)
                        stats.FiveValueGain[item.target_type - 1] += item.value;
                }
            }

            for (var statIndex = 0; statIndex < 5; statIndex++)
            {
                stats.FiveValueGain[statIndex] =
                    ScoreUtils.ReviseOver1200(turn.Stats[statIndex] + stats.FiveValueGain[statIndex])
                    - ScoreUtils.ReviseOver1200(turn.Stats[statIndex]);
            }

            trainStats[index] = stats;
        }

        var totalValue = turn.StatsRevised.Sum();
        var rivalInfo = dataset.rival_info;
        var mechaLevels = new[] { rivalInfo.speed, rivalInfo.stamina, rivalInfo.power, rivalInfo.guts, rivalInfo.wiz };
        var mechaLevelLimits = new[] { rivalInfo.speed_limit, rivalInfo.stamina_limit, rivalInfo.power_limit, rivalInfo.guts_limit, rivalInfo.wiz_limit };
        var overdriveInfo = dataset.overdrive_info;
        var gearText = overdriveInfo.remain_num switch
        {
            0 or 1 => $"{overdriveInfo.remain_num} (+{overdriveInfo.energy_num})",
            2 => "2",
            _ => throw new InvalidOperationException($"未知齿轮槽数量: {overdriveInfo.remain_num}")
        };
        var totalEnergy = dataset.board_info_array.Sum(x => x.chip_info_array.First(x => x.chip_id > 2000).point)
            + dataset.tuning_point;
        var headEnergy = dataset.board_info_array.First(x => x.board_id == 1).chip_info_array.First(x => x.chip_id > 2000).point;
        var bodyEnergy = dataset.board_info_array.First(x => x.board_id == 2).chip_info_array.First(x => x.chip_id > 2000).point;
        var legEnergy = dataset.board_info_array.First(x => x.board_id == 3).chip_info_array.First(x => x.chip_id > 2000).point;
        var builder = new StringBuilder()
            .Append(turn.Year).Append(I18N_Year).Append(' ')
            .Append(turn.Month).Append(I18N_Month).Append(turn.HalfMonth)
            .Append(" | 总属性: ").Append(totalValue)
            .Append(" | Pt: ").Append(@event.data.chara_info.skill_point)
            .Append(" | ").Append(I18N_Vital).Append(": ").Append(turn.Vital).Append('/').Append(turn.MaxVital)
            .Append(" | 干劲: ").Append(MotivationText(@event.data.chara_info.motivation))
            .AppendLine()
            .Append("总Lv: ").Append(mechaLevels.Sum()).Append(" (").Append(rivalInfo.progress_rate).Append("%)")
            .Append(" | 总EN: ").Append(totalEnergy)
            .Append(" | EN分配: 头").Append(headEnergy).Append(" 胸").Append(bodyEnergy).Append(" 脚").Append(legEnergy)
            .Append(" | 齿轮: ").Append(gearText);
        if (overdriveInfo.over_drive_state > 0)
            builder.Append(" 已启动");

        foreach (var info in criticalInfo)
            builder.AppendLine().Append("! ").Append(info);

        var bestScore = trainStats.Max(x => x.FiveValueGain.Sum());
        foreach (var command in turn.CommandInfoArray)
        {
            var index = command.TrainIndex - 1;
            var stats = trainStats[index];
            var currentStat = turn.StatsRevised[index];
            var statUpToMax = turn.MaxStatsRevised[index] - currentStat;
            var afterVital = stats.VitalGain + turn.Vital;
            var score = stats.FiveValueGain.Sum();
            var mechaLevel = mechaLevels[index];
            var mechaLevelLimit = mechaLevelLimits[index];
            var levelText = (mechaLevelLimit - mechaLevel) switch
            {
                0 => "MAX",
                < 0 => "ERR{mechaLv}",
                _ => mechaLevel.ToString()
            };
            var pointUp = command.PointUpInfoArray.Sum(x => x.Value);
            var states = new List<string>();
            if (command.IsRecommend)
                states.Add("齿轮");
            if (command.TrainingPartners.Any(x => x.Shining))
                states.Add("闪耀");
            if (overdriveInfo.over_drive_state > 0)
                states.Add("OD");

            builder.AppendLine().AppendLine()
                .Append("【").Append(GameGlobal.TrainNames[GameGlobal.TrainIds[index]]).Append("】")
                .Append(" 失败率 ").Append(stats.FailureRate).Append('%')
                .Append(" | 当前/余量 ").Append(currentStat).Append('/').Append(statUpToMax)
                .Append(" | 体力 ").Append(afterVital).Append('/').Append(turn.MaxVital)
                .Append(" | Lv").Append(turn.Turn is >= 37 and <= 40 or >= 61 and <= 64 ? "5(夏合宿)" : command.TrainLevel.ToString())
                .AppendLine()
                .Append(score == bestScore ? "★" : " ")
                .Append("属性 +").Append(score).Append(" | Pt +").Append(stats.PtGain)
                .Append(" | 研究 ").Append(levelText).Append('/').Append(mechaLevelLimit)
                .Append(" (+").Append(pointUp).Append(')');

            if (states.Count > 0)
                builder.Append(" | ").AppendJoin(' ', states);
            if (command.TrainingPartners.Count > 0)
                builder.AppendLine().Append("伙伴: ").AppendJoin(' ', command.TrainingPartners.Select(x => x.Name));
        }

        return WorkspaceContent.Text(builder.ToString());
    }

    static string MotivationText(int motivation)
        => motivation switch
        {
            5 => I18N_MotivationBest,
            4 => I18N_MotivationGood,
            3 => I18N_MotivationNormal,
            2 => I18N_MotivationBad,
            1 => I18N_MotivationWorst,
            _ => throw new InvalidOperationException($"未知干劲值: {motivation}")
        };
}
