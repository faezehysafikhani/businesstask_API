using BusinessTask.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddBusinessTaskHost();

var app = builder.Build();
app.UseBusinessTaskHost();
await app.InitializeBusinessTaskHostAsync();
app.Run();
