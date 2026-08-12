# MechaScenarioAnalyzer

赛博杯训练分析插件。成功发布的训练结果在当前插件进程内保留 history；重启插件后 history 为空。

Scenario history 使用 `(single_mode_chara_id, turn)` 作为记录键。同一育成角色同一回合的后续输出原位更新，不增加记录，也不改变记录顺序。

配置文件为 `PluginData/MechaScenarioAnalyzer/settings.json`：

```json
{
  "historyLimit": 100
}
```

`historyLimit` 默认为 `100`，有效范围为 `0` 到 `1000`。保存较小的上限会立即删除最旧记录；提高上限不会恢复已删除的记录。设为 `0` 时不保留 history，界面继续显示最近一次成功输出。

训练分析面板获得焦点时，使用 `↑` / `↓` 查看较旧 / 较新的记录，使用 `←` / `→` 跳到最旧 / 最新记录。正文滚动使用 `PageUp`、`PageDown`、`Home`、`End` 或鼠标滚轮。
