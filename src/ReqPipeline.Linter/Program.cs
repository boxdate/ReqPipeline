using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReqPipeline.Core.Application;
using ReqPipeline.Core.Infrastructure;
using ReqPipeline.Core.Interfaces;
using ReqPipeline.Core.KnowledgeBase;
using ReqPipeline.Core.Models;
using ReqPipeline.Core.StaticAnalysis;
using ReqPipeline.Core.Export;

if (args.Length < 2)
{
    Console.WriteLine("使い方: dotnet run -- <要求JSON> <用語集JSON>");
    return;
}

try
{
    // ========================================================
    // 1. DI (依存性の注入)
    // ========================================================
    var reqProvider = new JsonRequirementProvider();
    var glosProvider = new JsonGlossaryProvider();
    var llmClient = new OllamaLlmClient("qwen2.5:7b");
    var knowledgeBase = new MarkdownKnowledgeBase("KnowledgeBase");

    var orchestrator = new PipelineOrchestrator(
        new StructureVerifier(),
        new GlossaryVerifier(),
        new SemanticValidator(llmClient, knowledgeBase));

    // ========================================================
    // 2. ユースケースの実行
    // ========================================================
    Console.WriteLine("🚀 パイプラインを実行中...\n");
    var nodes = reqProvider.Load(args[0]);
    var glossary = glosProvider.Load(args[1]);
    var context = new PipelineContext(nodes, glossary);
    await orchestrator.RunPipelineAsync(context);

    // ========================================================
    // 3. プレゼンテーション (UI表示)
    // Webアプリ化する時は、ここが HTML(Razor/React) の描画処理に置き換わります！
    // ========================================================
    if (context.HasFatalError())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("🚨 Linterエラーのため処理を中断しました。");
        Console.ResetColor();
    }
    else
    {
        Console.WriteLine("✨ パイプライン完走！ファイル出力も完了しました。\n");
    }

    foreach (var issue in context.Issues)
    {
        var color = issue.Severity == Severity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
        Console.ForegroundColor = color;
        Console.WriteLine($"[{issue.Severity}] {issue.Message}");
        Console.ResetColor();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Error] {ex.Message}");
}