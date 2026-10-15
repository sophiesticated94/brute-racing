using Game.Core;
using Game.DirectoryApi;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=32768);
builder.Services.AddSingleton(GameCatalog.Load(f=>File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"data",f))));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MatchQueue>();
builder.Services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.MaxDepth=16);
var limits=builder.Configuration.GetSection("RequestLimits").Get<RequestLimits>()??new();
if(limits.Capacity<1||limits.EnqueueCapacity<1||!double.IsFinite(limits.RefillPerSecond)||limits.RefillPerSecond<=0||!double.IsFinite(limits.EnqueueRefillPerSecond)||limits.EnqueueRefillPerSecond<=0)throw new InvalidOperationException("Nieprawidłowe RequestLimits.");
builder.Services.AddSingleton(limits);builder.Services.AddSingleton<RequestLimiter>();
builder.Services.AddHostedService<QueueJanitor>();
var app=builder.Build();
app.Use(async(context,next)=>
{
    context.Response.Headers.CacheControl="no-store";
    string ip=context.Connection.RemoteIpAddress?.ToString()??"unknown";
    bool enqueue=context.Request.Method=="POST"&&context.Request.Path=="/queue";
    if(!context.RequestServices.GetRequiredService<RequestLimiter>().Accept(ip,enqueue,out int retry))
    {context.Response.StatusCode=429;context.Response.Headers.RetryAfter=retry.ToString();await context.Response.WriteAsJsonAsync(new{error="Limit żądań."});return;}
    try
    {
        if(context.Request.ContentLength>32768)throw new BadHttpRequestException("Limit body 32 KiB.",413);
        if(context.Request.Method=="POST" && (context.Request.ContentLength is >0 || context.Request.Headers.TransferEncoding.Count>0))
        {
            using var buffer=new MemoryStream();byte[] chunk=new byte[4096];int count;
            while((count=await context.Request.Body.ReadAsync(chunk))>0)
            {if(buffer.Length+count>32768)throw new BadHttpRequestException("Limit body 32 KiB.",413);buffer.Write(chunk,0,count);}
            try{using var document=System.Text.Json.JsonDocument.Parse(buffer.ToArray(),new(){MaxDepth=16});}
            catch(System.Text.Json.JsonException e){throw new BadHttpRequestException("Nieprawidłowy JSON lub głębokość większa niż 16.",e.Message.Contains("depth",StringComparison.OrdinalIgnoreCase)?413:400);}
            context.Request.Body=new MemoryStream(buffer.ToArray());
        }
        await next();
    }
    catch(QueueConflictException e){context.Response.StatusCode=409;context.Response.Headers.RetryAfter="60";await context.Response.WriteAsJsonAsync(new{error=e.Message});}
    catch(QueueCapacityException e){context.Response.StatusCode=429;context.Response.Headers.RetryAfter="5";await context.Response.WriteAsJsonAsync(new{error=e.Message});}
    catch(BadHttpRequestException e){context.Response.StatusCode=e.StatusCode;context.Response.Headers.RetryAfter="1";await context.Response.WriteAsJsonAsync(new{error=e.Message});}
    catch(KeyNotFoundException e){context.Response.StatusCode=404;await context.Response.WriteAsJsonAsync(new{error=e.Message});}
    catch(Exception e) when(e is ArgumentException or InvalidOperationException or InvalidDataException)
    {context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error=e.Message});}
});
app.MapGet("/health",(MatchQueue queue)=>new{status="ok",protocol=4,contentHash=queue.ContentHash});
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
