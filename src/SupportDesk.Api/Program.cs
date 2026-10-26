using Microsoft.AspNetCore.Mvc;
using SupportDesk.Api.Errors;
using SupportDesk.Core.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options => options.SuppressAsyncSuffixInActionNames = false);
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Framework binding errors can include raw JSON and internal type names.
    options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Invalid request",
        Detail = "Provide the required JSON fields with text strings, named priority/status values, and an integer ticket ID where required."
    });
});
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSupportDesk(builder.Configuration.GetConnectionString("SupportDesk"), builder.Configuration["AI:Provider"],
    builder.Configuration["AI:Model"], builder.Configuration["AI:ApiKey"],
    builder.Configuration.GetValue<int?>("AI:TimeoutSeconds") ?? 30, builder.Configuration["AI:ThinkingLevel"]);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
