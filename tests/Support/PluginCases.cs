using UmamusumeResponseAnalyzer.PluginTesting;
using System.IO.Compression;
using System.Text;
using Gallop;
using Gallop.Endpoints;
using Newtonsoft.Json;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using HostSkillData = UmamusumeResponseAnalyzer.Entities.SkillData;

static partial class PluginCases
{
    static readonly int[] BaseTrainingIds = [101, 105, 102, 103, 106];

    public static PluginCase Case { get; } =
        new(
            "MechaScenarioAnalyzer",
            static () => new MechaScenarioAnalyzer.MechaScenarioAnalyzer(),
            new(
                static () => CreateMechaTrainingResponse(),
                "MechaScenarioAnalyzer",
                "training",
                "训练分析",
                FullBleed: true,
                VisibleText: "总Lv:",
                CreateUpdatedResponse: static () => CreateMechaTrainingResponse(73),
                InitialUpdateText: "80/100",
                UpdatedVisibleText: "73/100",
                History: new(
                    static (vital, charaId, turn) => CreateMechaTrainingResponse(vital, charaId, turn))));

    static SingleModeMechaCheckEventResponse CreateMechaTrainingResponse(
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1)
    {
        int[] trainIds = [901, 105, 902, 103, 906];
        var response = (SingleModeMechaCheckEventResponse)RandomDtoGenerator.Create(
            typeof(SingleModeMechaCheckEventResponse),
            "MechaScenarioAnalyzer:valid", RandomDtoProfile.Analyzer);
        response.data.chara_info = CreateChara(
            9,
            trainIds,
            vital,
            singleModeCharaId,
            turn);
        response.data.home_info = CreateHomeInfo(trainIds);
        response.data.unchecked_event_array = [];
        response.data.select_index_info_array = [];
        response.data.race_start_info = null!;
        response.data.mecha_data_set = new()
        {
            command_info_array =
            [
                .. trainIds.Select((commandId, index) => new SingleModeMechaCommandInfo
                {
                    command_type = 1,
                    command_id = commandId,
                    params_inc_dec_info_array = CreateParams(index + 1),
                    point_up_info_array = [new() { status_type = index + 1, value = 10 }],
                    is_recommend = index == 0,
                    energy_num = 1
                })
            ],
            tuning_point = 1,
            rival_info = new()
            {
                speed = 10,
                stamina = 10,
                power = 10,
                guts = 10,
                wiz = 10,
                speed_limit = 200,
                stamina_limit = 200,
                power_limit = 200,
                guts_limit = 200,
                wiz_limit = 200,
                progress_rate = 10
            },
            overdrive_info = new()
            {
                energy_num = 1,
                remain_num = 1,
                over_drive_state = 0
            },
            board_info_array =
            [
                .. Enumerable.Range(1, 3).Select(boardId => new SingleModeMechaBoardInfo
                {
                    board_id = boardId,
                    chip_info_array = [new() { chip_id = 2000 + boardId, point = 1 }]
                })
            ],
            upgrade_race_result_array = []
        };
        return response;
    }

    static SingleModeChara CreateChara(
        int scenarioId,
        int[]? trainIds = null,
        int vital = 80,
        int singleModeCharaId = 1001,
        int turn = 1) => new()
    {
        single_mode_chara_id = singleModeCharaId,
        card_id = 100100,
        state = 1,
        playing_state = 1,
        scenario_id = scenarioId,
        turn = turn,
        motivation = 5,
        vital = vital,
        max_vital = 100,
        speed = 500,
        stamina = 500,
        power = 500,
        guts = 500,
        wiz = 500,
        max_speed = 1200,
        max_stamina = 1200,
        max_power = 1200,
        max_guts = 1200,
        max_wiz = 1200,
        skill_point = 1200,
        skill_array = [],
        skill_tips_array = [],
        support_card_array = [],
        evaluation_info_array = [],
        training_level_info_array =
        [
            .. (trainIds ?? BaseTrainingIds).Select(commandId => new TrainingLevelInfo
            {
                command_id = commandId,
                level = 1
            })
        ],
        chara_effect_id_array = [],
        proper_distance_short = 1,
        proper_distance_mile = 1,
        proper_distance_middle = 1,
        proper_distance_long = 1,
        proper_running_style_nige = 1,
        proper_running_style_senko = 1,
        proper_running_style_sashi = 1,
        proper_running_style_oikomi = 1,
        proper_ground_turf = 1,
        proper_ground_dirt = 1
    };

    static SingleModeHomeInfo CreateHomeInfo(int[]? trainIds = null) => new()
    {
        command_info_array =
        [
            .. (trainIds ?? BaseTrainingIds).Select((commandId, index) => new SingleModeCommandInfo
            {
                command_type = 1,
                command_id = commandId,
                is_enable = 1,
                failure_rate = index * 5,
                training_partner_array = [],
                tips_event_partner_array = [],
                sub_command_partner_array = [],
                params_inc_dec_info_array = CreateParams(index + 1)
            })
        ]
    };

    static SingleModeParamsIncDecInfo[] CreateParams(int value) =>
    [
        new() { target_type = 1, value = value },
        new() { target_type = 2, value = value },
        new() { target_type = 3, value = value },
        new() { target_type = 4, value = value },
        new() { target_type = 5, value = value },
        new() { target_type = 30, value = value },
        new() { target_type = 10, value = -10 }
    ];}
