using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using BentleyFormsService.Data;
using BentleyFormsService.Repositories;
using BentleyFormsService.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Context (SQLite)
var dbPath = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=forms.db";
builder.Services.AddDbContext<FormsDbContext>(options =>
    options.UseSqlite(dbPath));

// 2. Repositories
builder.Services.AddScoped<IFormRepository, FormRepository>();
builder.Services.AddScoped<IFormDefinitionRepository, FormDefinitionRepository>();
builder.Services.AddScoped<IWorkflowRepository, WorkflowRepository>();

// 3. Services
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<IFormService, FormService>();
builder.Services.AddScoped<IAiFormAgent, AiFormAgent>();
builder.Services.AddHttpClient();

// 4. Controllers & JSON Options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// 5. Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Bentley Cloud Platform - AI Forms API",
        Version = "v2",
        Description = "Next-generation, AI-enabled Forms Service for Bentley Cloud Platform. Implements Bentley Forms API contracts with a Workflow State Machine and AI Agent field extraction."
    });
});

// Enable CORS for flexibility
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Auto-migrate and seed initial project data on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
    await DbInitializer.InitializeAsync(db);
}

app.UseCors("AllowAll");

// Serve static files from wwwroot (Field-Side UI)
app.UseDefaultFiles();
app.UseStaticFiles();

// Configure Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Bentley Forms API v2");
    c.RoutePrefix = "swagger";
});

app.UseAuthorization();
app.MapControllers();

app.Run();
