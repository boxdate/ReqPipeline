using ReqPipeline.Web.Components;
using ReqPipeline.Core.Application;
using ReqPipeline.Core.StaticAnalysis;
using ReqPipeline.Core.Interfaces;
using ReqPipeline.Core.KnowledgeBase;
using ReqPipeline.Core.Models;
using ReqPipeline.Core.Infrastructure;
using ReqPipeline.Core.Export;
// using ReqPipeline.Core.Data;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ========================================================
// 【DIの移植】コアエンジンの部品をWebのコンテナに登録する
// ========================================================
// 1. データプロバイダ
builder.Services.AddScoped<IRequirementProvider, JsonRequirementProvider>();
builder.Services.AddScoped<IGlossaryProvider, JsonGlossaryProvider>();

// 2. インフラ（Ollamaクライアント）
// インスタンス化の際にモデル名を渡すようファクトリ形式で登録します
// builder.Services.AddScoped<ILlmClient>(sp => new OllamaLlmClient("qwen2.5:7b"));
// appsettings.json から "OllamaSettings:ModelName" の値を取得する
// （もし設定ファイルに書き忘れていた場合の保険として、フォールバック値も入れておきます）
var modelName = builder.Configuration.GetValue<string>("OllamaSettings:ModelName") ?? "qwen2.5:7b";

// 取得したモデル名を使ってインスタンス化！
builder.Services.AddScoped<ILlmClient>(sp => new OllamaLlmClient(modelName));

// 3. ナレッジベースの登録（MarkdownKnowledgeBaseを実体として使う）
// 実行ファイルのディレクトリにある "KnowledgeBase" フォルダを指定して生成する
builder.Services.AddScoped<IKnowledgeBase>(sp => 
{
    var kbPath = Path.Combine(AppContext.BaseDirectory, "KnowledgeBase");
    return new MarkdownKnowledgeBase(kbPath);
});

// 4. バリデーター群 (順番が重要！)
// ※ASP.NET CoreのDIは、同じインターフェースで複数登録すると IEnumerable<T> としてまとめて注入してくれます
// 4. バリデーター群 (それぞれ正しいインターフェース名で登録する！)
builder.Services.AddScoped<StructureVerifier, StructureVerifier>();
builder.Services.AddScoped<GlossaryVerifier, GlossaryVerifier>();
builder.Services.AddScoped<IRequirementStaticAnalysis, SemanticValidator>();

// 5. エクスポーター群
builder.Services.AddScoped<IRequirementExporter, KiroMarkdownExporter>();
builder.Services.AddScoped<IRequirementExporter, UsdmCsvExporter>();

// 6. すべてを束ねるオーケストレーター
builder.Services.AddScoped<PipelineOrchestrator>();

// 7. QA用
builder.Services.AddScoped<ReqPipeline.Core.QaIntegration.AiQaPerspectiveGenerator>();
// ========================================================

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
