using Game.Core;
using Game.DirectoryApi;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=32768);
builder.Services.AddSingleton(GameCatalog.Load(f=>File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"data",f))));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MatchQueue>();
builder.Services.AddHostedService<QueueJanitor>();
var app=builder.Build();
app.Use(async(context,next)=>
{
    context.Response.Headers.CacheControl="no-store";
    try{await next();}
    catch(KeyNotFoundException e){context.Response.StatusCode=404;await context.Response.WriteAsJsonAsync(new{error=e.Message});}
    catch(Exception e) when(e is ArgumentException or InvalidOperationException or InvalidDataException)
    {context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error=e.Message});}
});
app.MapGet("/health",(MatchQueue queue)=>new{status="ok",protocol=3,contentHash=queue.ContentHash});
app.MapGet("/address",(HttpContext context)=>new{address=context.Connection.RemoteIpAddress?.MapToIPv4().ToString()});
app.MapPost("/queue",(QueueRequest request,HttpContext context,MatchQueue queue)=>queue.Enqueue(request,context.Connection.RemoteIpAddress?.ToString()??""));
app.MapGet("/queue/{id:guid}",(Guid id,HttpContext context,MatchQueue queue)=>queue.Poll(id,Secret(context)));
app.MapPost("/queue/{id:guid}/ready",(Guid id,Guid? offer,HttpContext context,MatchQueue queue)=>{queue.Ready(id,Secret(context),offer);return Results.NoContent();});
app.MapPost("/queue/{id:guid}/fail",(Guid id,Guid? offer,HttpContext context,MatchQueue queue)=>{queue.Fail(id,Secret(context),offer);return Results.NoContent();});
app.MapDelete("/queue/{id:guid}",(Guid id,HttpContext context,MatchQueue queue)=>{queue.Cancel(id,Secret(context));return Results.NoContent();});
app.Run();
static string Secret(HttpContext context)=>context.Request.Headers["X-Queue-Secret"].ToString();

public partial class Program;
sealed class QueueJanitor(MatchQueue queue):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        try{while(await timer.WaitForNextTickAsync(stoppingToken))queue.Sweep();}
        catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}
    }
}
