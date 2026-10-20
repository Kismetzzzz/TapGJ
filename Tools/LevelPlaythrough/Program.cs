using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TapGJ.Playthrough
{
    public sealed class RunSummary
    {
        public int Run;
        public int SeedBase;
        public PlayStyle Style;
        public List<LevelResult> Results = new List<LevelResult>();
    }

    public static class Program
    {
        const int Runs = 10;
        const int SeedBase = 20240607;

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var root = args.Length > 0 ? args[0] : FindRoot();
            var assets = Path.Combine(root, "Assets");

            Console.WriteLine($"工程根目录：{root}");
            Console.WriteLine($"资源目录  ：{assets}");
            Console.WriteLine();

            var levels = AssetLoader.LoadAll(assets);
            if (levels.Count == 0)
            {
                Console.WriteLine("没有读到任何关卡。");
                return 1;
            }

            Console.WriteLine($"读到 {levels.Count} 关；每轮采用不同策略，共跑 {Runs} 轮 / {Runs * levels.Count} 局。\n");

            var allRuns = new List<RunSummary>();
            for (int run = 0; run < Runs; run++)
            {
                var style = PlayStyles.All[run % PlayStyles.All.Length];
                int runSeedBase = SeedBase + run * 7919;
                var batch = new RunSummary { Run = run + 1, SeedBase = runSeedBase, Style = style };

                foreach (var level in levels)
                {
                    // 固定种子关是作者配置的谜题：尊重资产固定局面。其余关按轮次和关卡改变随机种子。
                    int seed = level.UseFixedSeed
                        ? level.FixedSeed
                        : runSeedBase + levels.IndexOf(level) * 101;
                    var policy = new Policy(level, seed, style, runSeedBase);
                    batch.Results.Add(policy.Play());
                }

                allRuns.Add(batch);
                int wins = batch.Results.Count(r => r.Victory);
                Console.WriteLine($"第 {batch.Run,2} 轮 [{PlayStyles.Name(style)}]：{wins}/{levels.Count} 关通关（策略随机种子 {runSeedBase}）");
            }

            var report = BuildReport(allRuns, levels);
            var outPath = Path.Combine(root, "docs", "playthrough-10-runs.md");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, report, new UTF8Encoding(false));

            Console.WriteLine();
            Console.WriteLine("按策略汇总：");
            foreach (var run in allRuns)
                Console.WriteLine($"  第 {run.Run,2} 轮 [{PlayStyles.Name(run.Style),-10}] " +
                                  $"{run.Results.Count(r => r.Victory),2}/{levels.Count} 关");

            int totalWins = allRuns.Sum(r => r.Results.Count(x => x.Victory));
            Console.WriteLine($"\n总计 {totalWins}/{Runs * levels.Count} 局通关；报告：{outPath}");
            return 0;
        }

        static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "ProjectSettings")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }

        static string BuildReport(List<RunSummary> runs, List<LevelData> levels)
        {
            var sb = new StringBuilder();
            int total = runs.Sum(r => r.Results.Count);
            int wins = runs.Sum(r => r.Results.Count(x => x.Victory));

            sb.AppendLine("# 全关卡10轮策略试玩记录");
            sb.AppendLine();
            sb.AppendLine($"> 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
            int fixedCount = levels.Count(level => level.UseFixedSeed);
            sb.AppendLine($"> 范围：{runs.Count} 轮 × {levels.Count} 关 = {total} 局；使用 `Assets/Scripts/Core` 规则引擎自动模拟，机制与事件按项目资产参数复刻。");
            sb.AppendLine($"> 总体：{wins}/{total} 局通关（{(total == 0 ? 0 : wins * 100.0 / total):F1}%）。");
            sb.AppendLine();
            sb.AppendLine("## 10种试玩方式");
            sb.AppendLine();
            sb.AppendLine("| 轮次 | 策略 | 做法差异 | 策略随机种子 |");
            sb.AppendLine("|---:|---|---|---:|");
            foreach (var run in runs)
            {
                sb.AppendLine($"| {run.Run} | {PlayStyles.Name(run.Style)} | {StyleDescription(run.Style)} | {run.SeedBase} |");
            }
            sb.AppendLine();
            sb.AppendLine(fixedCount == levels.Count
                ? $"> 本次 {fixedCount}/{levels.Count} 关都配置了固定种子：10轮保持每关开局局面一致，变化的是玩家策略及其随机扰动种子，用来对比不同打法。"
                : $"> {fixedCount}/{levels.Count} 关遵从资产固定种子以保持谜题局面；其余关按轮次和关卡编号更换局面种子。10轮策略及随机扰动种子也不同。");
            sb.AppendLine();
            sb.AppendLine("> 重要限制：这不是 Unity 窗口的人工点击测试。各轮是不同的自动决策策略，但仍属于启发式策略，不是穷举搜索；“败”代表该策略在对应种子下未找到解，不证明关卡无解。");
            sb.AppendLine();

            sb.AppendLine("## 每关10轮结果");
            sb.AppendLine();
            sb.Append("| 关卡 | 名称 | ");
            foreach (var run in runs) sb.Append($"第{run.Run}轮 | ");
            sb.AppendLine("合计 | 最好回合 |");
            sb.Append("|---|---|");
            foreach (var _ in runs) sb.Append("---:|");
            sb.AppendLine("---:|---:|");

            foreach (var level in levels)
            {
                var levelRuns = runs.Select(r => r.Results.First(x => x.Level.LevelId == level.LevelId)).ToList();
                int levelWins = levelRuns.Count(r => r.Victory);
                int bestRound = levelRuns.Where(r => r.Victory).Select(r => r.Rounds).DefaultIfEmpty(0).Min();
                sb.Append($"| {level.LevelId} | {level.DisplayName} | ");
                foreach (var r in levelRuns)
                    sb.Append($"{(r.Victory ? "胜" : "败")} ({r.Rounds}) | ");
                sb.AppendLine($"{levelWins}/10 | {(bestRound == 0 ? "—" : bestRound.ToString())} |");
            }
            sb.AppendLine();

            sb.AppendLine("## 按策略汇总");
            sb.AppendLine();
            sb.AppendLine("| 轮次 | 策略 | 通关 | 失败关卡 |");
            sb.AppendLine("|---:|---|---:|---|");
            foreach (var run in runs)
            {
                var failed = run.Results.Where(r => !r.Victory).Select(r => r.Level.LevelId).ToList();
                sb.AppendLine($"| {run.Run} | {PlayStyles.Name(run.Style)} | {run.Results.Count(r => r.Victory)}/{levels.Count} | {(failed.Count == 0 ? "无" : string.Join("、", failed))} |");
            }
            sb.AppendLine();

            sb.AppendLine("## 未通关的局面与动作");
            sb.AppendLine();
            foreach (var run in runs)
            {
                foreach (var r in run.Results.Where(x => !x.Victory))
                {
                    sb.AppendLine($"### 第 {run.Run} 轮「{PlayStyles.Name(run.Style)}」— {r.Level.LevelId} {r.Level.DisplayName}");
                    sb.AppendLine();
                    sb.AppendLine($"- 种子：{r.Seed}；回合：{r.Rounds}/{r.Level.Rules.RoundLimit}；终局：{r.FinalBoard}");
                    if (r.GoalProgress.Count > 0)
                        sb.AppendLine($"- 目标进度：{string.Join("；", r.GoalProgress)}");
                    sb.AppendLine($"- 动作：{(r.Trace.Count == 0 ? "没有打出有效动作" : string.Join(" / ", r.Trace))}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("## 试玩方式说明");
            sb.AppendLine();
            foreach (var style in PlayStyles.All)
                sb.AppendLine($"- **{PlayStyles.Name(style)}**：{StyleDescription(style)}");
            sb.AppendLine();
            sb.AppendLine("逐关终局和目标条件来自 Core 引擎；关卡 MonoBehaviour 机制与 ScriptableObject 事件通过工具里的纯 C# 适配实现。建议把失败次数高的关卡作为下一轮人工试玩重点。");
            return sb.ToString();
        }

        static string StyleDescription(PlayStyle style) => style switch
        {
            PlayStyle.ObjectiveGreedy => "严格按关卡目标进度选最优动作。",
            PlayStyle.Aggressive => "额外偏好相克消灭，并允许很小的短期分数回撤。",
            PlayStyle.Conservative => "减少召唤与秘术牌消耗，只接受明显有利的动作。",
            PlayStyle.SummonFocused => "额外奖励空格召唤，用召唤调整棋盘与配对。",
            PlayStyle.ReactionFocused => "偏好打在元素上触发反应，而非只看静态目标分。",
            PlayStyle.CompositeFocused => "明显偏好融合、分解，推动复合元素循环。",
            PlayStyle.BottleFocused => "优先增加元素瓶进度与瓶子产牌。",
            PlayStyle.BalanceFocused => "额外关注均衡目标的差距与达成状态。",
            PlayStyle.RandomizedNearBest => "在近优动作中随机选择，保留可复现随机种子。",
            PlayStyle.Exploratory => "随机探索候选动作，并容忍短期回撤，增加路线差异。",
            _ => style.ToString(),
        };
    }
}
