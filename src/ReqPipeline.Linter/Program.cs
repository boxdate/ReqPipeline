using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReqPipeline.Core.Application;
using ReqPipeline.Core.Data;
using ReqPipeline.Core.Infrastructure;
using ReqPipeline.Core.Interfaces;
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

    // バリデーターのリスト（実行順序が重要！）
    var validators = new List<IRequirementStaticAnalysis> 
    { 
        // --- Verification (機械的検証) ---
        new StructureVerifier(), // NEW: USDM構造とEARS必須チェック
        new GlossaryVerifier(),  // リネーム済: 用語と構文チェック
        
        // --- Validation (意味論的妥当性確認) ---
        new SemanticValidator(llmClient) 
    };
    
    // ... 以下略 ...
    var exporters = new List<IRequirementExporter> { new KiroMarkdownExporter(), new UsdmCsvExporter() };

    var orchestrator = new PipelineOrchestrator(reqProvider, glosProvider, validators, exporters);

    // ========================================================
    // 2. ユースケースの実行
    // Webアプリ化する時は、コントローラーからこの1行を呼ぶだけになります！
    // ========================================================
    Console.WriteLine("🚀 パイプラインを実行中...\n");
    var currentDir = Directory.GetCurrentDirectory(); 
    var context = await orchestrator.RunPipelineAsync(args[0], args[1], currentDir);

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