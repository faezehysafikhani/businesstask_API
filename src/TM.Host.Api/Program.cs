using TM.Host.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddTmHost();

var app = builder.Build();
app.UseTmHost();
await app.InitializeTmHostAsync();
app.Run();
