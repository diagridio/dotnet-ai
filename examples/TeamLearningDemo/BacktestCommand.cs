// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
//
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2030
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using System.Text.Json;
using TeamLearningDemo.Market;

namespace TeamLearningDemo;

/// <summary>
/// The <c>--backtest</c> command, without any agent:
/// <code>
///   dotnet run -- --backtest                                        score the published skill
///   dotnet run -- --backtest --propose support '{"min_score": 2}'   judge a proposal like the workflow does
/// </code>
/// </summary>
public static class BacktestCommand
{
    /// <returns>The process exit code: 2 for a usage error or an invalid proposal, else 0.</returns>
    public static async Task<int> RunAsync(string[] args, CandleStore store, TeamPaths paths)
    {
        var published = await SkillParams.LoadAsync(paths.ParamsFile);
        var backtest = await Backtest.LoadAsync(store, DateTimeOffset.UtcNow);
        Console.WriteLine($"skill v{published.Version}; {backtest.Range}");
        var propose = Array.IndexOf(args, "--propose");
        if (propose >= 0)
        {
            if (propose + 2 >= args.Length)
            {
                Console.Error.WriteLine("usage: --backtest --propose <support|resistance> '<changes JSON>'");
                return 2;
            }
            var verdict = await ProposalJudge.JudgeAsync(published, args[propose + 1], args[propose + 2], () => Task.FromResult(backtest));
            Console.WriteLine($"valid {verdict.Valid}, promote {verdict.Promote}: {verdict.ResultJson}");
            return verdict.Valid ? 0 : 2;
        }
        foreach (var side in Levels.Sides)
        {
            Console.WriteLine($"{side.Name()}: {JsonSerializer.Serialize(backtest.Verdict(side, published.For(side), baseline: null))}");
        }
        return 0;
    }
}
